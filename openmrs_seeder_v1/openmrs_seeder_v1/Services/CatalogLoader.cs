using OpenmrsSeeder.Models.Catalogs;

namespace OpenmrsSeeder.Services;

public class CatalogLoader
{
    public IReadOnlyList<EpidemiologyEntry> EpidemiologyProfile { get; private set; } = [];
    public IReadOnlyList<DiagnosticoEntry> Diagnosticos { get; private set; } = [];
    public IReadOnlyList<MedicamentoEntry> Medicamentos { get; private set; } = [];
    public IReadOnlyList<LaboratorioEntry> Laboratorios { get; private set; } = [];
    public IReadOnlyList<ExamenClinicoEntry> ExamenesClinicos { get; private set; } = [];
    public IReadOnlyList<AlergenoEntry> Alergenos { get; private set; } = [];
    public IReadOnlyList<MotivoConsultaEntry> MotivosConsulta { get; private set; } = [];
    public IReadOnlyList<ClimaEntry> Clima { get; private set; } = [];
    public IReadOnlyList<ConsultorioEntry> Consultorios { get; private set; } = [];
    public IReadOnlyList<AfinidadEntry> Afinidades { get; private set; } = [];
    public IReadOnlyList<NombreEntry> Nombres { get; private set; } = [];
    public IReadOnlyList<string> Apellidos { get; private set; } = [];
    public IReadOnlyList<ProgramaEntry> Programas { get; private set; } = [];
    public IReadOnlyList<DireccionEntry> Direcciones { get; private set; } = [];
    public IReadOnlyList<PanelComponenteEntry> Paneles { get; private set; } = [];
    public IReadOnlyList<PersonalLaboratorioEntry> PersonalLaboratorio { get; private set; } = [];

    /// <summary>Carga directa desde listas — usado en tests unitarios.</summary>
    public void LoadFromLists(
        IEnumerable<Models.Catalogs.EpidemiologyEntry> epidemiology,
        IEnumerable<Models.Catalogs.DiagnosticoEntry> diagnosticos,
        IEnumerable<Models.Catalogs.MedicamentoEntry> medicamentos,
        IEnumerable<Models.Catalogs.LaboratorioEntry> laboratorios,
        IEnumerable<Models.Catalogs.ExamenClinicoEntry> examenesClinicos,
        IEnumerable<Models.Catalogs.AlergenoEntry> alergenos,
        IEnumerable<Models.Catalogs.MotivoConsultaEntry> motivosConsulta,
        IEnumerable<Models.Catalogs.ClimaEntry>? clima = null,
        IEnumerable<Models.Catalogs.ConsultorioEntry>? consultorios = null,
        IEnumerable<Models.Catalogs.AfinidadEntry>? afinidades = null,
        IEnumerable<Models.Catalogs.NombreEntry>? nombres = null,
        IEnumerable<string>? apellidos = null,
        IEnumerable<Models.Catalogs.ProgramaEntry>? programas = null,
        IEnumerable<Models.Catalogs.DireccionEntry>? direcciones = null,
        IEnumerable<Models.Catalogs.PanelComponenteEntry>? paneles = null,
        IEnumerable<Models.Catalogs.PersonalLaboratorioEntry>? personalLaboratorio = null)
    {
        EpidemiologyProfile = epidemiology.ToList().AsReadOnly();
        Diagnosticos        = diagnosticos.ToList().AsReadOnly();
        Medicamentos        = medicamentos.ToList().AsReadOnly();
        Laboratorios        = laboratorios.ToList().AsReadOnly();
        ExamenesClinicos    = examenesClinicos.ToList().AsReadOnly();
        Alergenos           = alergenos.ToList().AsReadOnly();
        MotivosConsulta     = motivosConsulta.ToList().AsReadOnly();
        Clima               = (clima ?? []).ToList().AsReadOnly();
        Consultorios        = (consultorios ?? []).ToList().AsReadOnly();
        Afinidades          = (afinidades ?? []).ToList().AsReadOnly();
        Nombres             = (nombres ?? []).ToList().AsReadOnly();
        Apellidos           = (apellidos ?? []).ToList().AsReadOnly();
        Programas           = (programas ?? []).ToList().AsReadOnly();
        Direcciones         = (direcciones ?? []).ToList().AsReadOnly();
        Paneles             = (paneles ?? []).ToList().AsReadOnly();
        PersonalLaboratorio = (personalLaboratorio ?? []).ToList().AsReadOnly();
    }

    public void Load(string catalogsPath)
    {
        EpidemiologyProfile = LoadCsv(Path.Combine(catalogsPath, "epidemiology-profile.csv"), ParseEpidemiology);
        Diagnosticos        = LoadCsv(Path.Combine(catalogsPath, "diagnosticos.csv"),          ParseDiagnostico);
        Medicamentos        = LoadCsv(Path.Combine(catalogsPath, "medicamentos.csv"),           ParseMedicamento);
        Laboratorios        = LoadCsv(Path.Combine(catalogsPath, "laboratorios.csv"),           ParseLaboratorio);
        ExamenesClinicos    = LoadCsv(Path.Combine(catalogsPath, "examenes_clinicos.csv"),      ParseExamenClinico);
        Alergenos           = LoadCsv(Path.Combine(catalogsPath, "alergenos.csv"),              ParseAlergeno);
        MotivosConsulta     = LoadCsv(Path.Combine(catalogsPath, "motivos_consulta.csv"),       ParseMotivoConsulta);
        Clima               = LoadCsv(Path.Combine(catalogsPath, "clima.csv"),                  ParseClima);
        Consultorios        = LoadCsv(Path.Combine(catalogsPath, "consultorios.csv"),           ParseConsultorio);
        Afinidades          = LoadCsv(Path.Combine(catalogsPath, "comorbilidad_afinidades.csv"), ParseAfinidad);
        Nombres             = LoadCsv(Path.Combine(catalogsPath, "nombres.csv"),                 ParseNombre);
        Apellidos           = LoadCsv(Path.Combine(catalogsPath, "apellidos.csv"),               ParseApellido)
                                  .Where(a => !string.IsNullOrWhiteSpace(a)).ToList().AsReadOnly();
        Programas           = LoadCsv(Path.Combine(catalogsPath, "programas.csv"),               ParsePrograma);
        Direcciones         = LoadCsv(Path.Combine(catalogsPath, "direcciones.csv"),             ParseDireccion);
        Paneles             = LoadCsv(Path.Combine(catalogsPath, "paneles.csv"),                 ParsePanelComponente);
        PersonalLaboratorio = LoadCsv(Path.Combine(catalogsPath, "personal_laboratorio.csv"),    ParsePersonalLaboratorio);
    }

    private static IReadOnlyList<T> LoadCsv<T>(string path, Func<Dictionary<string, string>, T?> parser)
    {
        if (!File.Exists(path)) return [];
        var lines = File.ReadAllLines(path, System.Text.Encoding.UTF8);
        if (lines.Length < 2) return [];

        var headers = SplitLine(lines[0]);
        var result = new List<T>();

        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var values = SplitLine(line);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length && i < values.Length; i++)
                row[headers[i]] = values[i];
            var entry = parser(row);
            if (entry is not null) result.Add(entry);
        }

        return result.AsReadOnly();
    }

    private static string[] SplitLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')      { inQuotes = !inQuotes; }
            else if (c == ',' && !inQuotes) { result.Add(current.ToString().Trim()); current.Clear(); }
            else               { current.Append(c); }
        }
        result.Add(current.ToString().Trim());
        return result.ToArray();
    }

    private static bool   B(Dictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var v) && v.Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Booleano con valor por defecto cuando la columna falta o está vacía (columnas nuevas retrocompatibles).</summary>
    private static bool   B(Dictionary<string, string> row, string key, bool porDefecto) =>
        row.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v.Equals("true", StringComparison.OrdinalIgnoreCase)
            : porDefecto;

    private static int    I(Dictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : 0;

    private static string S(Dictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var v) ? v : "";

    private static double D(Dictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var v) &&
        double.TryParse(v, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static EpidemiologyEntry ParseEpidemiology(Dictionary<string, string> row) => new()
    {
        Categoria = S(row, "categoria"),
        GrupoEdad = S(row, "grupo_edad"),
        Genero    = S(row, "genero"),
        Peso      = I(row, "peso")
    };

    private static DiagnosticoEntry ParseDiagnostico(Dictionary<string, string> row) => new()
    {
        CielUuid              = S(row, "ciel_uuid"),
        NombreEs              = S(row, "nombre_es"),
        Categoria             = S(row, "categoria"),
        Severidad             = S(row, "severidad"),
        Aplica0_14            = B(row, "aplica_0_14"),
        Aplica15_29           = B(row, "aplica_15_29"),
        Aplica30_44           = B(row, "aplica_30_44"),
        Aplica45_64           = B(row, "aplica_45_64"),
        Aplica65mas           = B(row, "aplica_65mas"),
        PesoM                 = I(row, "peso_M"),
        PesoF                 = I(row, "peso_F"),
        RequiereLab           = B(row, "requiere_lab"),
        RequiereRx            = B(row, "requiere_rx"),
        RequiereExamenClinico = B(row, "requiere_examen_clinico"),
        Clima                 = S(row, "clima")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .ToHashSet(),
        EsCronica             = B(row, "cronica"),
        EsComun               = B(row, "comun"),
        VitalFiebre           = B(row, "vital_fiebre"),
        VitalImc              = S(row, "vital_imc").ToLowerInvariant(),
        VitalPa               = S(row, "vital_pa").ToLowerInvariant(),
        VitalFc               = S(row, "vital_fc").ToLowerInvariant(),
        VitalSpo2             = S(row, "vital_spo2").ToLowerInvariant(),
        Sexo                  = S(row, "sexo").Trim().ToUpperInvariant() is "M" or "F" ? S(row, "sexo").Trim().ToUpperInvariant() : ""
    };

    private static ClimaEntry ParseClima(Dictionary<string, string> row) => new()
    {
        Semana        = I(row, "semana"),
        Estacion      = S(row, "estacion").ToLowerInvariant(),
        TempPromedioC = D(row, "temp_promedio_c")
    };

    private static MedicamentoEntry ParseMedicamento(Dictionary<string, string> row) => new()
    {
        DrugUuid            = S(row, "drug_uuid"),
        ConceptUuid         = S(row, "concept_uuid"),
        NombreGenerico      = S(row, "nombre_generico"),
        Strength            = S(row, "strength"),
        ViaUuid             = S(row, "via_uuid"),
        Dosis               = D(row, "dosis"),
        UnidadDosisUuid     = S(row, "unidad_dosis_uuid"),
        FrecuenciaUuid      = S(row, "frecuencia_uuid"),
        DiasTratamiento     = I(row, "dias_tratamiento"),
        AplicaRespiratorio  = B(row, "aplica_respiratorio"),
        AplicaCardiovascular= B(row, "aplica_cardiovascular"),
        AplicaDiabetes      = B(row, "aplica_diabetes"),
        AplicaDigestivo     = B(row, "aplica_digestivo"),
        AplicaOsteomuscular = B(row, "aplica_osteomuscular"),
        AplicaUrologico     = B(row, "aplica_urologico"),
        AplicaInfeccioso    = B(row, "aplica_infeccioso"),
        AplicaEndocrino     = B(row, "aplica_endocrino"),
        AplicaNeurologico     = B(row, "aplica_neurologico"),
        AplicaDermatologico   = B(row, "aplica_dermatologico"),
        AplicaSaludMental     = B(row, "aplica_salud_mental"),
        AplicaGinecoobstetrico= B(row, "aplica_ginecoobstetrico"),
        AplicaTrauma          = B(row, "aplica_trauma")
    };

    private static LaboratorioEntry ParseLaboratorio(Dictionary<string, string> row) => new()
    {
        CielUuid            = S(row, "ciel_uuid"),
        NombreEs            = S(row, "nombre_es"),
        AplicaRespiratorio  = B(row, "aplica_respiratorio"),
        AplicaCardiovascular= B(row, "aplica_cardiovascular"),
        AplicaDiabetes      = B(row, "aplica_diabetes"),
        AplicaDigestivo     = B(row, "aplica_digestivo"),
        AplicaOsteomuscular = B(row, "aplica_osteomuscular"),
        AplicaUrologico     = B(row, "aplica_urologico"),
        AplicaInfeccioso    = B(row, "aplica_infeccioso"),
        AplicaEndocrino     = B(row, "aplica_endocrino"),
        AplicaNeurologico     = B(row, "aplica_neurologico"),
        AplicaDermatologico   = B(row, "aplica_dermatologico"),
        AplicaSaludMental     = B(row, "aplica_salud_mental"),
        AplicaGinecoobstetrico= B(row, "aplica_ginecoobstetrico"),
        AplicaTrauma          = B(row, "aplica_trauma"),
        // Columnas de resultado (opcionales; ausentes = sin resultado)
        Datatype            = S(row, "datatype").Trim().ToLowerInvariant(),
        ResMin              = D(row, "res_min"),
        ResMax              = D(row, "res_max"),
        ResMinAnormal       = D(row, "res_min_anormal"),
        ResMaxAnormal       = D(row, "res_max_anormal"),
        ResNormalUuid       = S(row, "res_normal_uuid"),
        ResAnormalUuid      = S(row, "res_anormal_uuid"),
        ResTrigger          = Pipe(row, "res_trigger"),
        ResTriggerDx        = Pipe(row, "res_trigger_dx"),
        // Dónde se procesa y cuánto tarda (columnas nuevas; ausentes = en la clínica, mismo día)
        SeRealizaEnClinica  = B(row, "se_realiza_en_clinica", porDefecto: true),
        DiasEntregaMin      = I(row, "dias_entrega_min"),
        DiasEntregaMax      = I(row, "dias_entrega_max")
    };

    /// <summary>Lista separada por '|' (vacío = lista vacía).</summary>
    private static List<string> Pipe(Dictionary<string, string> row, string key) =>
        S(row, key).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static ExamenClinicoEntry ParseExamenClinico(Dictionary<string, string> row) => new()
    {
        CielUuid            = S(row, "ciel_uuid"),
        NombreEs            = S(row, "nombre_es"),
        TipoResultado       = S(row, "tipo_resultado"),
        ResMin              = D(row, "res_min"),
        ResMax              = D(row, "res_max"),
        AplicaRespiratorio  = B(row, "aplica_respiratorio"),
        AplicaCardiovascular= B(row, "aplica_cardiovascular"),
        AplicaDiabetes      = B(row, "aplica_diabetes"),
        AplicaDigestivo     = B(row, "aplica_digestivo"),
        AplicaOsteomuscular = B(row, "aplica_osteomuscular"),
        AplicaUrologico     = B(row, "aplica_urologico"),
        AplicaInfeccioso    = B(row, "aplica_infeccioso"),
        AplicaEndocrino     = B(row, "aplica_endocrino"),
        AplicaNeurologico     = B(row, "aplica_neurologico"),
        AplicaDermatologico   = B(row, "aplica_dermatologico"),
        AplicaSaludMental     = B(row, "aplica_salud_mental"),
        AplicaGinecoobstetrico= B(row, "aplica_ginecoobstetrico"),
        AplicaTrauma          = B(row, "aplica_trauma")
    };

    private static AlergenoEntry ParseAlergeno(Dictionary<string, string> row) => new()
    {
        ConceptUuid     = S(row, "concept_uuid"),
        NombreEs        = S(row, "nombre_es"),
        TipoAlergeno    = S(row, "tipo_alergeno"),
        SeveridadTipica = S(row, "severidad_tipica")
    };

    private static MotivoConsultaEntry ParseMotivoConsulta(Dictionary<string, string> row) => new()
    {
        Categoria = S(row, "categoria"),
        Texto     = S(row, "texto")
    };

    private static ConsultorioEntry ParseConsultorio(Dictionary<string, string> row) => new()
    {
        LocationUuid     = S(row, "location_uuid"),
        MedicoIdentifier = S(row, "medico_identifier"),
        MedicoNombre     = S(row, "medico_nombre"),
        MedicoGenero     = S(row, "medico_genero").Equals("F", StringComparison.OrdinalIgnoreCase) ? "F" : "M"
    };

    private static NombreEntry? ParseNombre(Dictionary<string, string> row)
    {
        var nombre = S(row, "nombre");
        if (string.IsNullOrWhiteSpace(nombre)) return null;
        var genero = S(row, "genero").Equals("F", StringComparison.OrdinalIgnoreCase) ? "F" : "M";
        return new NombreEntry { Nombre = nombre, Genero = genero };
    }

    private static string ParseApellido(Dictionary<string, string> row) => S(row, "apellido");

    private static ProgramaEntry? ParsePrograma(Dictionary<string, string> row)
    {
        var uuid = S(row, "program_uuid");
        if (string.IsNullOrWhiteSpace(uuid)) return null;
        return new ProgramaEntry
        {
            ProgramUuid       = uuid,
            Nombre            = S(row, "program_nombre"),
            TriggerDx         = Pipe(row, "trigger_dx"),
            TriggerCategoria  = Pipe(row, "trigger_categoria"),
            EstadoInicialUuid = S(row, "estado_inicial_uuid")
        };
    }

    private static DireccionEntry? ParseDireccion(Dictionary<string, string> row)
    {
        var municipio = S(row, "municipio");
        if (string.IsNullOrWhiteSpace(municipio)) return null;
        return new DireccionEntry
        {
            Departamento = S(row, "departamento"),
            Municipio    = municipio,
            Zona         = S(row, "zona"),
            Peso         = Math.Max(1, I(row, "peso"))
        };
    }

    private static PanelComponenteEntry? ParsePanelComponente(Dictionary<string, string> row)
    {
        var panel = S(row, "panel_uuid");
        var comp  = S(row, "componente_uuid");
        if (string.IsNullOrWhiteSpace(panel) || string.IsNullOrWhiteSpace(comp)) return null;
        return new PanelComponenteEntry
        {
            PanelUuid      = panel,
            ComponenteUuid = comp,
            Nombre         = S(row, "nombre"),
            ResMin         = D(row, "res_min"),
            ResMax         = D(row, "res_max"),
            ResMinAnormal  = D(row, "res_min_anormal"),
            ResMaxAnormal  = D(row, "res_max_anormal"),
            ResTrigger     = Pipe(row, "res_trigger")
        };
    }

    private static PersonalLaboratorioEntry? ParsePersonalLaboratorio(Dictionary<string, string> row)
    {
        var identifier = S(row, "identifier");
        if (string.IsNullOrWhiteSpace(identifier)) return null;
        return new PersonalLaboratorioEntry
        {
            Identifier = identifier,
            Nombre     = S(row, "nombre"),
            Genero     = S(row, "genero").Equals("F", StringComparison.OrdinalIgnoreCase) ? "F" : "M",
            Rol        = S(row, "rol").Trim().ToLowerInvariant()
        };
    }

    private static AfinidadEntry ParseAfinidad(Dictionary<string, string> row) => new()
    {
        Categoria = S(row, "categoria"),
        Afines    = S(row, "afines")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList()
    };
}
