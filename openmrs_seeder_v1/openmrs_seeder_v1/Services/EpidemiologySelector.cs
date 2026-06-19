using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Catalogs;

namespace OpenmrsSeeder.Services;

public class EpidemiologySelector
{
    private readonly CatalogLoader _catalogs;
    private readonly Random _rng;

    public EpidemiologySelector(CatalogLoader catalogs, SimulationSettings settings)
    {
        _catalogs = catalogs;
        _rng = new Random(settings.RandomSeed + 3);
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
