using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Services;

namespace openmrs_seeder_v1.Tests;

/// <summary>
/// El CatalogLoader es mudo (CSV ausente → lista vacía, booleano mal escrito → false): estas pruebas
/// fijan lo que el CatalogValidator sí debe cazar antes de que la corrida toque OpenMRS.
/// </summary>
public class CatalogValidatorTests
{
    // ── Fixture: el catálogo mínimo VÁLIDO. Cada prueba rompe una sola cosa. ──

    private static EpidemiologyEntry Epi(string cat = "respiratorio", string grupo = "0-14", int peso = 10) =>
        new() { Categoria = cat, GrupoEdad = grupo, Genero = "Ambos", Peso = peso };

    private static DiagnosticoEntry Dx() => new()
    {
        CielUuid = "dx-uuid", NombreEs = "Gripe", Categoria = "respiratorio", Severidad = "leve",
        Aplica0_14 = true, Aplica15_29 = true, Aplica30_44 = true, Aplica45_64 = true, Aplica65mas = true,
        PesoM = 10, PesoF = 10
    };

    private static MedicamentoEntry Med() => new()
    {
        DrugUuid = "drug-uuid", ConceptUuid = "concept-uuid", NombreGenerico = "Amoxicilina",
        AplicaRespiratorio = true
    };

    private static LaboratorioEntry Lab() => new()
    {
        CielUuid = "lab-uuid", NombreEs = "Hemograma", AplicaRespiratorio = true,
        Datatype = "numeric", ResMin = 4, ResMax = 10
    };

    private static ExamenClinicoEntry Examen() => new()
    {
        CielUuid = "ex-uuid", NombreEs = "Flujo pico", TipoResultado = "numerico",
        ResMin = 200, ResMax = 550, AplicaRespiratorio = true
    };

    /// <summary>Catálogo válido; <paramref name="mutar"/> rompe una regla concreta.</summary>
    private static (List<string> Errores, List<string> Advertencias) Validar(Action<Fixture>? mutar = null)
    {
        var f = new Fixture();
        mutar?.Invoke(f);

        var loader = new CatalogLoader();
        loader.LoadFromLists(
            f.Epidemiologia, f.Diagnosticos, f.Medicamentos, f.Laboratorios, f.Examenes,
            f.Alergenos, f.Motivos,
            clima: f.Clima, consultorios: f.Consultorios, afinidades: f.Afinidades,
            nombres: f.Nombres, apellidos: f.Apellidos, programas: f.Programas,
            direcciones: f.Direcciones, paneles: f.Paneles);

        return CatalogValidator.Validate(loader);
    }

    private sealed class Fixture
    {
        // Perfil con peso para los 5 grupos de edad (ambos géneros) — si no, falla la cobertura.
        public List<EpidemiologyEntry> Epidemiologia { get; set; } =
            [.. CatalogValidator.GruposEdad.Select(g => Epi(grupo: g))];
        public List<DiagnosticoEntry> Diagnosticos { get; set; } = [Dx()];
        public List<MedicamentoEntry> Medicamentos { get; set; } = [Med()];
        public List<LaboratorioEntry> Laboratorios { get; set; } = [Lab()];
        public List<ExamenClinicoEntry> Examenes { get; set; } = [Examen()];
        public List<AlergenoEntry> Alergenos { get; set; } =
            [new() { ConceptUuid = "alg-uuid", NombreEs = "Penicilina", TipoAlergeno = "DRUG" }];
        public List<MotivoConsultaEntry> Motivos { get; set; } =
            [new() { Categoria = "respiratorio", Texto = "tos y fiebre" }];
        public List<NombreEntry> Nombres { get; set; } =
            [new() { Nombre = "José", Genero = "M" }, new() { Nombre = "María", Genero = "F" }];
        public List<string> Apellidos { get; set; } = ["Martínez"];

        // Opcionales: vacíos por defecto (feature desactivada = uso legítimo).
        public List<ClimaEntry> Clima { get; set; } = [];
        public List<ConsultorioEntry> Consultorios { get; set; } = [];
        public List<AfinidadEntry> Afinidades { get; set; } = [];
        public List<ProgramaEntry> Programas { get; set; } = [];
        public List<DireccionEntry> Direcciones { get; set; } = [];
        public List<PanelComponenteEntry> Paneles { get; set; } = [];
    }

    // ── Caso base ─────────────────────────────────────────────────────────────

    [Fact]
    public void CatalogoValido_SinErrores()
    {
        var (errores, _) = Validar();
        Assert.Empty(errores);
    }

    [Fact]
    public void OpcionalesVacios_NoSonError()
    {
        // clima, consultorios, programas, direcciones, paneles y afinidades vacíos = features apagadas
        var (errores, _) = Validar();
        Assert.Empty(errores);
    }

    // ── Presencia de los obligatorios ─────────────────────────────────────────

    [Fact]
    public void CatalogoObligatorioVacio_EsError()
    {
        var (errores, _) = Validar(f => f.Medicamentos = []);
        Assert.Contains(errores, e => e.Contains("medicamentos.csv") && e.Contains("obligatorio"));
    }

    // ── Diagnósticos ──────────────────────────────────────────────────────────

    [Fact]
    public void CategoriaInexistente_EsError()
    {
        var (errores, _) = Validar(f => f.Diagnosticos = [new DiagnosticoEntry
        {
            CielUuid = "x", NombreEs = "Dx raro", Categoria = "cardiologia", Severidad = "leve",
            Aplica0_14 = true, PesoM = 5, PesoF = 5
        }]);
        Assert.Contains(errores, e => e.Contains("cardiologia") && e.Contains("13 categorías"));
    }

    [Fact]
    public void DiagnosticoSinGrupoDeEdad_EsError()
    {
        var (errores, _) = Validar(f =>
        {
            var dx = Dx();
            dx.Aplica0_14 = dx.Aplica15_29 = dx.Aplica30_44 = dx.Aplica45_64 = dx.Aplica65mas = false;
            f.Diagnosticos = [dx];
        });
        Assert.Contains(errores, e => e.Contains("ningún grupo de edad"));
    }

    [Fact]
    public void DiagnosticoConAmbosPesosCero_EsError()
    {
        var (errores, _) = Validar(f =>
        {
            var dx = Dx();
            dx.PesoM = dx.PesoF = 0;
            f.Diagnosticos = [dx];
        });
        Assert.Contains(errores, e => e.Contains("nunca podría elegirse"));
    }

    [Fact]
    public void VitalOSexoConValorNoValido_EsError()
    {
        var (errores, _) = Validar(f =>
        {
            var dx = Dx();
            dx.Sexo = "X";
            dx.VitalImc = "altisimo";
            f.Diagnosticos = [dx];
        });
        Assert.Contains(errores, e => e.Contains("sexo='X'"));
        Assert.Contains(errores, e => e.Contains("vital_imc='altisimo'"));
    }

    [Fact]
    public void EstacionInvalidaEnClimaDelDx_EsError()
    {
        var (errores, _) = Validar(f =>
        {
            var dx = Dx();
            dx.Clima = ["otoño"];
            f.Diagnosticos = [dx];
        });
        Assert.Contains(errores, e => e.Contains("clima='otoño'"));
    }

    // ── Cruce perfil ↔ diagnósticos ───────────────────────────────────────────

    [Fact]
    public void CategoriaDelPerfilSinDiagnosticos_EsError()
    {
        // El selector elegiría 'diabetes' y no encontraría ningún dx → visita sin diagnóstico
        var (errores, _) = Validar(f => f.Epidemiologia.Add(Epi(cat: "diabetes", grupo: "45-64")));
        Assert.Contains(errores, e => e.Contains("'diabetes'") && e.Contains("sin diagnóstico"));
    }

    [Fact]
    public void DiagnosticoDeCategoriaAusenteDelPerfil_EsError()
    {
        var (errores, _) = Validar(f =>
        {
            var dx = Dx();
            dx.Categoria = "trauma";
            f.Diagnosticos.Add(dx);
        });
        Assert.Contains(errores, e => e.Contains("'trauma'") && e.Contains("inalcanzables"));
    }

    [Fact]
    public void PerfilSinCoberturaDeUnGrupoEdadGenero_EsError()
    {
        var (errores, _) = Validar(f =>
            f.Epidemiologia = [.. f.Epidemiologia.Where(e => e.GrupoEdad != "65+")]);
        Assert.Contains(errores, e => e.Contains("(65+, M)"));
        Assert.Contains(errores, e => e.Contains("(65+, F)"));
    }

    // ── Laboratorios / paneles ────────────────────────────────────────────────

    [Fact]
    public void BandaInvertida_EsError()
    {
        var (errores, _) = Validar(f =>
        {
            var lab = Lab();
            (lab.ResMin, lab.ResMax) = (10, 4);
            f.Laboratorios = [lab];
        });
        Assert.Contains(errores, e => e.Contains("res_min") && e.Contains("no puede ser mayor"));
    }

    [Fact]
    public void LabCodedSinRespuestas_EsError()
    {
        var (errores, _) = Validar(f =>
        {
            var lab = Lab();
            lab.Datatype = "coded";
            f.Laboratorios = [lab];
        });
        Assert.Contains(errores, e => e.Contains("coded exige"));
    }

    [Fact]
    public void DatatypeDesconocido_EsError()
    {
        var (errores, _) = Validar(f =>
        {
            var lab = Lab();
            lab.Datatype = "texto";
            f.Laboratorios = [lab];
        });
        Assert.Contains(errores, e => e.Contains("datatype='texto'"));
    }

    [Fact]
    public void LabSinNingunaCategoria_EsError()
    {
        var (errores, _) = Validar(f =>
        {
            var lab = Lab();
            lab.AplicaRespiratorio = false;
            f.Laboratorios = [lab];
        });
        Assert.Contains(errores, e => e.Contains("nunca podría ordenarse"));
    }

    [Fact]
    public void PanelColgado_SinLaboratorioQueLoRespalde_EsError()
    {
        var (errores, _) = Validar(f => f.Paneles =
            [new() { PanelUuid = "no-existe", ComponenteUuid = "comp", Nombre = "Hemoglobina", ResMin = 12, ResMax = 16 }]);
        Assert.Contains(errores, e => e.Contains("no existe en laboratorios.csv"));
    }

    [Fact]
    public void LabPanelSinComponentes_EsAdvertenciaNoError()
    {
        var (errores, avisos) = Validar(f =>
        {
            var lab = Lab();
            lab.Datatype = "panel";
            f.Laboratorios = [lab];
        });
        Assert.Empty(errores);
        Assert.Contains(avisos, a => a.Contains("sin componentes"));
    }

    // ── Exámenes clínicos ─────────────────────────────────────────────────────

    [Fact]
    public void ExamenNumericoSinBanda_EsError()
    {
        // Ya no hay fallback por unidad: sin banda, el examen no tendría de dónde sacar el valor
        var (errores, _) = Validar(f =>
        {
            var ex = Examen();
            ex.ResMin = ex.ResMax = 0;
            f.Examenes = [ex];
        });
        Assert.Contains(errores, e => e.Contains("numerico exige una banda"));
    }

    [Fact]
    public void ExamenCategorico_NoNecesitaBanda()
    {
        var (errores, _) = Validar(f =>
        {
            var ex = Examen();
            ex.TipoResultado = "categorico";
            ex.ResMin = ex.ResMax = 0;
            f.Examenes = [ex];
        });
        Assert.Empty(errores);
    }

    // ── Resto de catálogos ────────────────────────────────────────────────────

    [Fact]
    public void MedicamentoSinUuid_EsError()
    {
        var (errores, _) = Validar(f =>
        {
            var med = Med();
            med.DrugUuid = "";
            f.Medicamentos = [med];
        });
        Assert.Contains(errores, e => e.Contains("drug_uuid vacío"));
    }

    [Fact]
    public void TipoDeAlergenoNoValido_EsError()
    {
        var (errores, _) = Validar(f => f.Alergenos =
            [new() { ConceptUuid = "a", NombreEs = "Polen", TipoAlergeno = "PLANT" }]);
        Assert.Contains(errores, e => e.Contains("tipo_alergeno='PLANT'"));
    }

    [Fact]
    public void ProgramaSinTrigger_EsError()
    {
        var (errores, _) = Validar(f => f.Programas =
            [new() { ProgramUuid = "prog", Nombre = "Diabetes" }]);
        Assert.Contains(errores, e => e.Contains("sin trigger_dx ni trigger_categoria"));
    }

    [Fact]
    public void MedicoDuplicadoEnConsultorios_EsError()
    {
        var (errores, _) = Validar(f => f.Consultorios =
        [
            new() { LocationUuid = "loc-1", MedicoIdentifier = "SIM-MED-C1", MedicoNombre = "Ana" },
            new() { LocationUuid = "loc-2", MedicoIdentifier = "SIM-MED-C1", MedicoNombre = "Luis" }
        ]);
        Assert.Contains(errores, e => e.Contains("duplicado"));
    }

    [Fact]
    public void SemanaDeClimaFueraDeRango_EsError()
    {
        var (errores, _) = Validar(f => f.Clima =
            [new() { Semana = 60, Estacion = "verano", TempPromedioC = 30 }]);
        Assert.Contains(errores, e => e.Contains("semana=60"));
    }

    [Fact]
    public void NombresSinUnGenero_EsError()
    {
        var (errores, _) = Validar(f => f.Nombres = [new() { Nombre = "José", Genero = "M" }]);
        Assert.Contains(errores, e => e.Contains("género 'F'"));
    }

    [Fact]
    public void CategoriaSinMotivosDeConsulta_EsAdvertenciaNoError()
    {
        var (errores, avisos) = Validar();   // el fixture solo tiene motivos de 'respiratorio'
        Assert.Empty(errores);
        Assert.Contains(avisos, a => a.Contains("motivos_consulta.csv") && a.Contains("'trauma'"));
    }

    // ── Red de seguridad: los CSV reales del repo deben validar limpios ───────

    [Fact]
    public void CatalogosRealesDelRepo_ValidanSinErrores()
    {
        var loader = new CatalogLoader();
        loader.Load(RutaCatalogos());

        var (errores, _) = CatalogValidator.Validate(loader);

        Assert.True(errores.Count == 0,
            "Los catálogos del repo no pasan la validación:\n - " + string.Join("\n - ", errores));
    }

    /// <summary>Sube desde el bin de los tests hasta la carpeta de catálogos del proyecto.</summary>
    private static string RutaCatalogos()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidata = Path.Combine(dir.FullName, "openmrs_seeder_v1", "catalogs");
            if (Directory.Exists(candidata)) return candidata;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("No se encontró la carpeta catalogs/ del proyecto");
    }
}
