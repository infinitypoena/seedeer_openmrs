using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Catalogs;

namespace OpenmrsSeeder.Services;

public class EpidemiologySelector
{
    private readonly CatalogLoader _catalogs;
    private readonly Random _rng;
    private readonly ComorbiditySettings _comorbidity;

    public EpidemiologySelector(CatalogLoader catalogs, SimulationSettings settings)
    {
        _catalogs = catalogs;
        _rng = new Random(settings.RandomSeed + 3);
        _comorbidity = settings.Comorbidity;
    }

    public string SelectCategoria(string ageGroup, string gender)
    {
        var candidates = _catalogs.EpidemiologyProfile
            .Where(e => e.GrupoEdad == ageGroup && (e.Genero == gender || e.Genero == "Ambos"))
            .ToList();

        if (candidates.Count == 0) return "infeccioso";

        var total = candidates.Sum(e => e.Peso);
        var pick = _rng.NextDouble() * total;
        double cumulative = 0;
        foreach (var entry in candidates)
        {
            cumulative += entry.Peso;
            if (pick <= cumulative) return entry.Categoria;
        }
        return candidates.Last().Categoria;
    }

    public DiagnosticoEntry? SelectDiagnostico(string categoria, string ageGroup, string gender)
    {
        var candidates = _catalogs.Diagnosticos
            .Where(d => d.Categoria == categoria && AplicaAGrupo(d, ageGroup))
            .ToList();

        if (candidates.Count == 0) return null;

        var total = candidates.Sum(d => gender == "M" ? d.PesoM : d.PesoF);
        if (total == 0) return candidates[_rng.Next(candidates.Count)];

        var pick = _rng.NextDouble() * total;
        double cumulative = 0;
        foreach (var dx in candidates)
        {
            cumulative += gender == "M" ? dx.PesoM : dx.PesoF;
            if (pick <= cumulative) return dx;
        }
        return candidates.Last();
    }

    /// <summary>
    /// Selecciona 0..N diagnósticos adicionales (comorbilidad) detectados en la misma visita.
    /// La probabilidad escala con la edad y las categorías afines al primario reciben más peso.
    /// </summary>
    public List<DiagnosticoEntry> SelectComorbilidades(DiagnosticoEntry primario, string ageGroup, string gender)
    {
        var resultado = new List<DiagnosticoEntry>();
        if (_comorbidity.MaxAdditional <= 0) return resultado;

        var ageMult = _comorbidity.AgeScaling.GetValueOrDefault(ageGroup, 1.0);
        var effProb = Math.Min(_comorbidity.BaseProbability * ageMult, 0.95);
        if (_rng.NextDouble() >= effProb) return resultado;

        var extras = 1;
        if (_comorbidity.MaxAdditional >= 2 && _rng.NextDouble() < _comorbidity.SecondExtraProbability)
            extras = 2;
        extras = Math.Min(extras, _comorbidity.MaxAdditional);

        var usadas = new HashSet<string> { primario.Categoria };
        var uuids  = new HashSet<string> { primario.CielUuid };

        for (int i = 0; i < extras; i++)
        {
            var categoria = PickCategoriaPonderada(ageGroup, gender, usadas);
            if (categoria is null) break;

            var dx = SelectDiagnostico(categoria, ageGroup, gender);
            if (dx is not null && uuids.Add(dx.CielUuid))
            {
                resultado.Add(dx);
                usadas.Add(dx.Categoria);
            }
            else
            {
                usadas.Add(categoria); // evita reintentar una categoría sin Dx válido
            }
        }

        return resultado;
    }

    /// <summary>
    /// Elige una categoría por ruleta ponderada (pesos del perfil epidemiológico),
    /// excluyendo las ya usadas y aumentando el peso de las categorías afines a ellas.
    /// </summary>
    private string? PickCategoriaPonderada(string ageGroup, string gender, HashSet<string> excluir)
    {
        var afines = new HashSet<string>();
        foreach (var cat in excluir)
            if (_comorbidity.Affinities.TryGetValue(cat, out var lista))
                foreach (var a in lista) afines.Add(a);

        var pesos = _catalogs.EpidemiologyProfile
            .Where(e => e.GrupoEdad == ageGroup && (e.Genero == gender || e.Genero == "Ambos"))
            .GroupBy(e => e.Categoria)
            .Where(g => !excluir.Contains(g.Key))
            .Select(g => new
            {
                Categoria = g.Key,
                Peso = g.Sum(e => e.Peso) * (afines.Contains(g.Key) ? _comorbidity.AffinityBoost : 1.0)
            })
            .Where(x => x.Peso > 0)
            .ToList();

        if (pesos.Count == 0) return null;

        var total = pesos.Sum(x => x.Peso);
        var pick = _rng.NextDouble() * total;
        double cumulative = 0;
        foreach (var x in pesos)
        {
            cumulative += x.Peso;
            if (pick <= cumulative) return x.Categoria;
        }
        return pesos[^1].Categoria;
    }

    private static bool AplicaAGrupo(DiagnosticoEntry d, string ageGroup) => ageGroup switch
    {
        "0-14"  => d.Aplica0_14,
        "15-29" => d.Aplica15_29,
        "30-44" => d.Aplica30_44,
        "45-64" => d.Aplica45_64,
        "65+"   => d.Aplica65mas,
        _       => false
    };
}
