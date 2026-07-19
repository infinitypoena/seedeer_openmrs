using OpenmrsSeeder.Models.Catalogs;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Decide QUÉ laboratorios se ordenan en una visita — seam puro estático (sin red ni estado), extraído
/// de <see cref="Seeders.LabOrderSeeder"/> para que la selección sea testeable (la lógica inline en un
/// método async es el mismo antipatrón que dejó pasar el bug del obs-group).
/// <para>
/// Regla central (coherencia dx↔examen): si un diagnóstico de la visita tiene examen confirmatorio
/// (índice inverso <see cref="CatalogLoader.LabsConfirmatorios"/>, derivado de <c>res_trigger_dx</c>),
/// ese examen se ordena SIEMPRE — el médico que sospecha dengue pide el NS1, no lo echa a los dados.
/// Los labs aleatorios de categoría quedan como acompañantes. Sin confirmatorio, el comportamiento
/// histórico se conserva intacto.
/// </para>
/// </summary>
public static class LabOrderSelector
{
    /// <summary>Prob. de ordenar algún lab cuando el dx marca <c>requiere_lab=true</c> (histórica).</summary>
    public const double ProbConRequiereLab = 0.80;
    /// <summary>Prob. del segundo lab (histórica: 2 labs el 40 % de las veces con orden).</summary>
    public const double ProbSegundoLab = 0.40;

    /// <summary>
    /// Selección completa de la visita. <paramref name="dxConfirmables"/> son los diagnósticos sobre los
    /// que se fuerza el confirmatorio — el orquestador pasa los <c>DxsEvaluables</c> (en un control
    /// post-alta el episodio resuelto no re-ordena su examen, ley L9). <paramref name="probBase"/> es
    /// <c>ReferralProbabilities.LabOrder</c>.
    /// </summary>
    public static List<LaboratorioEntry> Seleccionar(
        IReadOnlyList<LaboratorioEntry> catalogo,
        IReadOnlyDictionary<string, IReadOnlyList<LaboratorioEntry>> confirmatorios,
        IReadOnlyCollection<string> categoriasPaciente,
        IReadOnlyCollection<string> dxConfirmables,
        bool requiereLab,
        double probBase,
        IReadOnlyDictionary<string, DateOnly> ordenesVigentes,
        DateOnly fechaVisita,
        Random rng)
    {
        var dirigidos = Confirmatorios(confirmatorios, dxConfirmables, ordenesVigentes, fechaVisita);

        // Con un confirmatorio pendiente, la orden no se sortea: se pide. Sin él, la tirada histórica.
        var debeOrden = dirigidos.Count > 0
            || rng.NextDouble() < (requiereLab ? ProbConRequiereLab : probBase);
        if (!debeOrden) return [];

        var yaElegidos = dirigidos.Select(l => l.CielUuid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidatos = catalogo
            .Where(l => !yaElegidos.Contains(l.CielUuid))
            .Where(l => categoriasPaciente.Any(c => AplicaCategoria(l, c)))
            .Where(l => !OrderVigencia.EstaActivo(ordenesVigentes, l.CielUuid, fechaVisita))
            .ToList();

        // Con confirmatorio: 0-1 acompañante de categoría. Sin él: 1-2 labs, la distribución histórica.
        var extras = dirigidos.Count > 0
            ? (rng.NextDouble() < ProbSegundoLab ? 1 : 0)
            : (rng.NextDouble() < ProbSegundoLab ? 2 : 1);

        return dirigidos
            .Concat(candidatos.OrderBy(_ => rng.Next()).Take(extras))
            .ToList();
    }

    /// <summary>
    /// Exámenes confirmatorios pendientes de los diagnósticos dados: los del índice inverso, dedupeados
    /// (dos dx pueden compartir examen) y sin orden aún vigente (<see cref="OrderVigencia"/>).
    /// </summary>
    public static List<LaboratorioEntry> Confirmatorios(
        IReadOnlyDictionary<string, IReadOnlyList<LaboratorioEntry>> indice,
        IReadOnlyCollection<string> dxUuids,
        IReadOnlyDictionary<string, DateOnly> ordenesVigentes,
        DateOnly fechaVisita)
    {
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resultado = new List<LaboratorioEntry>();
        foreach (var dx in dxUuids)
        {
            if (!indice.TryGetValue(dx, out var labs)) continue;
            foreach (var lab in labs)
                if (vistos.Add(lab.CielUuid) &&
                    !OrderVigencia.EstaActivo(ordenesVigentes, lab.CielUuid, fechaVisita))
                    resultado.Add(lab);
        }
        return resultado;
    }

    /// <summary>
    /// Labs de una visita de CHEQUEO voluntario: entre <paramref name="minLabs"/> y
    /// <paramref name="maxLabs"/> del pool <c>chequeo=true</c>, al azar, sin órdenes vigentes.
    /// El chequeo no se sortea (el paciente vino expresamente a hacerse exámenes).
    /// </summary>
    public static List<LaboratorioEntry> SeleccionarChequeo(
        IReadOnlyList<LaboratorioEntry> catalogo,
        IReadOnlyDictionary<string, DateOnly> ordenesVigentes,
        DateOnly fechaVisita,
        int minLabs,
        int maxLabs,
        Random rng)
    {
        var pool = catalogo
            .Where(l => l.EsChequeo)
            .Where(l => !OrderVigencia.EstaActivo(ordenesVigentes, l.CielUuid, fechaVisita))
            .ToList();
        if (pool.Count == 0) return [];

        var cantidad = Math.Min(pool.Count, rng.Next(minLabs, maxLabs + 1));
        return pool.OrderBy(_ => rng.Next()).Take(cantidad).ToList();
    }

    /// <summary>
    /// Examen adicional que un paciente ENFERMO pide por su cuenta ("ya que estoy, chéquenme la
    /// sangre"): un lab del pool <c>chequeo=true</c> que no esté ya elegido ni vigente, o null.
    /// La tirada de probabilidad la hace el llamador (RNG de chequeo, no el del seeder).
    /// </summary>
    public static LaboratorioEntry? ExamenAPeticion(
        IReadOnlyList<LaboratorioEntry> catalogo,
        IReadOnlyCollection<string> yaElegidos,
        IReadOnlyDictionary<string, DateOnly> ordenesVigentes,
        DateOnly fechaVisita,
        Random rng)
    {
        var elegidos = yaElegidos as ISet<string>
            ?? new HashSet<string>(yaElegidos, StringComparer.OrdinalIgnoreCase);
        var pool = catalogo
            .Where(l => l.EsChequeo)
            .Where(l => !elegidos.Contains(l.CielUuid))
            .Where(l => !OrderVigencia.EstaActivo(ordenesVigentes, l.CielUuid, fechaVisita))
            .ToList();
        return pool.Count == 0 ? null : pool[rng.Next(pool.Count)];
    }

    /// <summary>La fila del catálogo aplica a la categoría clínica dada (columnas <c>aplica_*</c>).</summary>
    public static bool AplicaCategoria(LaboratorioEntry l, string cat) => cat switch
    {
        "respiratorio"    => l.AplicaRespiratorio,
        "cardiovascular"  => l.AplicaCardiovascular,
        "diabetes"        => l.AplicaDiabetes,
        "digestivo"       => l.AplicaDigestivo,
        "osteomuscular"   => l.AplicaOsteomuscular,
        "urologico"       => l.AplicaUrologico,
        "infeccioso"      => l.AplicaInfeccioso,
        "endocrino"       => l.AplicaEndocrino,
        "neurologico"     => l.AplicaNeurologico,
        "dermatologico"   => l.AplicaDermatologico,
        "salud_mental"    => l.AplicaSaludMental,
        "ginecoobstetrico"=> l.AplicaGinecoobstetrico,
        "trauma"          => l.AplicaTrauma,
        _ => false
    };
}
