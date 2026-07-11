using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests;

public class PatientProfileGeneratorTests
{
    /// <summary>CatalogLoader vacío → el generador cae al fallback de Bogus (comportamiento previo).</summary>
    private static CatalogLoader EmptyCatalogs()
    {
        var c = new CatalogLoader();
        c.LoadFromLists([], [], [], [], [], [], []);
        return c;
    }

    /// <summary>CatalogLoader con pools de nombres/apellidos representativos.</summary>
    private static CatalogLoader NameCatalogs()
    {
        var nombres = new List<NombreEntry>();
        for (int i = 0; i < 60; i++) nombres.Add(new NombreEntry { Nombre = $"NomM{i}", Genero = "M" });
        for (int i = 0; i < 60; i++) nombres.Add(new NombreEntry { Nombre = $"NomF{i}", Genero = "F" });
        var apellidos = Enumerable.Range(0, 80).Select(i => $"Ape{i}").ToList();

        var c = new CatalogLoader();
        c.LoadFromLists([], [], [], [], [], [], [], nombres: nombres, apellidos: apellidos);
        return c;
    }

    private static PatientProfileGenerator CreateGen(int seed = 42) =>
        new(new SimulationSettings { RandomSeed = seed }, EmptyCatalogs());

    private static int EdadEnMeses(DateOnly birth, DateOnly reference)
    {
        var meses = (reference.Year - birth.Year) * 12 + (reference.Month - birth.Month);
        if (reference.Day < birth.Day) meses--;
        return meses;
    }

    [Fact]
    public void GenerateNew_IdentificadorConPrefixSIM()
    {
        var gen = CreateGen();
        var p   = gen.GenerateNew();
        Assert.StartsWith("SIM-", p.Identifier);
        Assert.Equal(12, p.Identifier.Length); // "SIM-" + 8 hex
    }

    [Fact]
    public void GenerateNew_GeneroEsMoF()
    {
        var gen = CreateGen();
        for (int i = 0; i < 20; i++)
        {
            var p = gen.GenerateNew();
            Assert.Contains(p.Gender, new[] { "M", "F" });
        }
    }

    [Fact]
    public void GenerateNew_GrupoEtarioValido()
    {
        var grupos = new[] { "0-14", "15-29", "30-44", "45-64", "65+" };
        var gen    = CreateGen();
        for (int i = 0; i < 30; i++)
        {
            var p = gen.GenerateNew();
            Assert.Contains(p.AgeGroup, grupos);
        }
    }

    [Fact]
    public void GenerateNew_EdadConsistenteConGrupo()
    {
        var gen = CreateGen();
        for (int i = 0; i < 50; i++)
        {
            var p   = gen.GenerateNew();
            var hoy = DateOnly.FromDateTime(DateTime.Today);
            var edad = hoy.Year - p.BirthDate.Year;
            if (p.BirthDate > hoy.AddYears(-edad)) edad--;

            var (min, max) = p.AgeGroup switch
            {
                "0-14"  => (0, 14),
                "15-29" => (15, 29),
                "30-44" => (30, 44),
                "45-64" => (45, 64),
                "65+"   => (65, 90),
                _       => (0, 120)
            };
            Assert.InRange(edad, min, max);
        }
    }

    [Fact]
    public void GenerateNew_NombreYApellidoNoVacios()
    {
        var gen = CreateGen();
        for (int i = 0; i < 10; i++)
        {
            var p = gen.GenerateNew();
            Assert.NotEmpty(p.GivenName);
            Assert.NotEmpty(p.FamilyName);
        }
    }

    [Fact]
    public void GenerateNew_EsNuevoPorDefecto()
    {
        var p = CreateGen().GenerateNew();
        Assert.True(p.EsNuevo);
    }

    [Fact]
    public void GenerateNew_ConFechaPasada_NacimientoNuncaDespuesDeLaVisita()
    {
        var gen = CreateGen();
        var refDate = new DateOnly(2023, 6, 1);
        for (int i = 0; i < 200; i++)
        {
            var p = gen.GenerateNew(refDate);
            Assert.True(p.BirthDate <= refDate,
                $"Nacimiento {p.BirthDate} no debe ser posterior a la visita {refDate}");
        }
    }

    [Fact]
    public void GenerateNew_RespetaEdadMinima_6Meses()
    {
        var gen = CreateGen();
        var refDate = new DateOnly(2023, 6, 1);
        for (int i = 0; i < 300; i++)
        {
            var p = gen.GenerateNew(refDate);
            Assert.True(EdadEnMeses(p.BirthDate, refDate) >= 6,
                $"Edad < 6 meses: nacimiento {p.BirthDate}, grupo {p.AgeGroup}");
        }
    }

    [Fact]
    public void GenerateNew_ModoPediatrico_PermiteDesde1Mes()
    {
        var settings = new SimulationSettings { RandomSeed = 7 };
        settings.DemographicProfile.PediatricClinic = true;
        // Forzar solo el grupo 0-14 para ejercitar el mínimo pediátrico
        settings.DemographicProfile.AgeGroups = [new() { Label = "0-14", Weight = 100 }];
        var gen = new PatientProfileGenerator(settings, EmptyCatalogs());
        var refDate = new DateOnly(2023, 6, 1);

        bool huboLactanteMenor6 = false;
        for (int i = 0; i < 400; i++)
        {
            var p = gen.GenerateNew(refDate);
            var meses = EdadEnMeses(p.BirthDate, refDate);
            Assert.True(meses >= 1, $"En pediatría el mínimo es 1 mes; obtuve {meses}");
            if (meses < 6) huboLactanteMenor6 = true;
        }
        Assert.True(huboLactanteMenor6, "En modo pediátrico deberían aparecer lactantes < 6 meses");
    }

    [Fact]
    public void GenerateNew_ConCatalogo_NombreCompletoCasiSiempreUnico()
    {
        // Con 60 nombres/género × 2 posiciones × 80 apellidos × 2 posiciones el espacio es enorme:
        // las colisiones de nombre completo deben ser una fracción mínima.
        var gen = new PatientProfileGenerator(new SimulationSettings { RandomSeed = 42 }, NameCatalogs());
        var nombres = new List<string>();
        const int n = 1000;
        for (int i = 0; i < n; i++)
        {
            var p = gen.GenerateNew();
            Assert.NotEmpty(p.SecondGivenName);
            Assert.NotEmpty(p.SecondFamilyName);
            Assert.NotEqual(p.GivenName, p.SecondGivenName);
            Assert.NotEqual(p.FamilyName, p.SecondFamilyName);
            nombres.Add($"{p.GivenName}|{p.SecondGivenName}|{p.FamilyName}|{p.SecondFamilyName}");
        }
        var distintos = nombres.Distinct().Count();
        Assert.True(distintos >= 985, $"Se esperaban ~únicos; hubo {n - distintos} colisiones de nombre completo");
    }

    [Fact]
    public void GenerateNew_SinCatalogo_FallbackBogus_SegundoNombreVacio()
    {
        var gen = CreateGen(); // catálogos vacíos
        var p = gen.GenerateNew();
        Assert.NotEmpty(p.GivenName);
        Assert.NotEmpty(p.FamilyName);
        Assert.Empty(p.SecondGivenName);
        Assert.Empty(p.SecondFamilyName);
    }

    [Fact]
    public void GenerateNew_DistribucionGeneroAproximada()
    {
        // GenderRatio default: M=48, F=52 → ~48% masculino
        var gen = CreateGen(seed: 1);
        int masculinos = 0;
        const int n = 500;
        for (int i = 0; i < n; i++)
            if (gen.GenerateNew().Gender == "M") masculinos++;

        Assert.InRange(masculinos, 200, 300); // 40%-60%
    }

    // ---- Direcciones salvadoreñas (direcciones.csv) ----

    private static PatientProfileGenerator GenConDirecciones(params DireccionEntry[] direcciones)
    {
        var c = new CatalogLoader();
        c.LoadFromLists([], [], [], [], [], [], [], direcciones: direcciones);
        return new PatientProfileGenerator(new SimulationSettings { RandomSeed = 42 }, c);
    }

    [Fact]
    public void GenerateNew_ConCatalogoDeDirecciones_DireccionSalvadorenaCoherente()
    {
        var gen = GenConDirecciones(
            new DireccionEntry { Departamento = "San Salvador", Municipio = "Mejicanos", Zona = "Colonia Zacamil", Peso = 5 });

        var p = gen.GenerateNew();

        Assert.Equal("El Salvador", p.Country);
        Assert.Equal("San Salvador", p.StateProvince);
        Assert.Equal("Mejicanos", p.City);
        Assert.StartsWith("Colonia Zacamil", p.Address1);
        Assert.Contains("casa #", p.Address1); // zona urbana → detalle de casa
    }

    [Fact]
    public void GenerateNew_ZonaRural_CantonSinNumeroDeCasa()
    {
        var gen = GenConDirecciones(
            new DireccionEntry { Departamento = "San Salvador", Municipio = "Apopa", Zona = "Cantón Joya Galana", Peso = 1 });

        var p = gen.GenerateNew();

        Assert.Equal("Cantón Joya Galana", p.Address1);
    }

    [Fact]
    public void GenerateNew_ElPesoConcentraLosMunicipios()
    {
        var gen = GenConDirecciones(
            new DireccionEntry { Departamento = "San Salvador", Municipio = "Cercano", Zona = "Colonia A", Peso = 20 },
            new DireccionEntry { Departamento = "Usulután",     Municipio = "Lejano",  Zona = "Barrio B",  Peso = 1 });

        int cercanos = 0;
        for (int i = 0; i < 200; i++)
            if (gen.GenerateNew().City == "Cercano") cercanos++;

        Assert.True(cercanos > 160, $"El municipio de peso 20 debería dominar; salió {cercanos}/200");
    }

    [Fact]
    public void GenerateNew_CatalogoConDuplicados_NoSeCuelga()
    {
        // Antes, un pool con >1 elementos todos idénticos colgaba PickDistinctPair (do/while infinito)
        var nombres = new List<NombreEntry>
        {
            new() { Nombre = "María", Genero = "F" },
            new() { Nombre = "María", Genero = "F" },
            new() { Nombre = "María", Genero = "F" },
            new() { Nombre = "José",  Genero = "M" },
            new() { Nombre = "José",  Genero = "M" },
        };
        var apellidos = new List<string> { "Pérez", "Pérez", "Pérez" };
        var c = new CatalogLoader();
        c.LoadFromLists([], [], [], [], [], [], [], nombres: nombres, apellidos: apellidos);
        var gen = new PatientProfileGenerator(new SimulationSettings { RandomSeed = 42 }, c);

        for (int i = 0; i < 50; i++)
        {
            var p = gen.GenerateNew(); // no debe colgarse
            Assert.NotEmpty(p.GivenName);
            Assert.NotEmpty(p.FamilyName);
            Assert.Equal("", p.SecondFamilyName); // pool deduplicado a 1 → sin segundo apellido
        }
    }

    [Fact]
    public void GenerateNew_SinCatalogo_FallbackBogusYPaisVacio()
    {
        var gen = CreateGen(); // catálogos vacíos

        var p = gen.GenerateNew();

        Assert.False(string.IsNullOrWhiteSpace(p.Address1)); // Bogus sigue dando dirección
        Assert.False(string.IsNullOrWhiteSpace(p.City));
        Assert.Equal("", p.Country);        // vacío → PatientSeeder usa el histórico "España"
        Assert.Equal("", p.StateProvince);
    }

    // ── Atributos de persona (teléfono + estado civil) ────────────────────────

    [Fact]
    public void GenerarTelefono_FormatoSalvadoreno()
    {
        var rng = new Random(20);
        int moviles = 0;
        const int N = 1000;
        for (int i = 0; i < N; i++)
        {
            var tel = PatientProfileGenerator.GenerarTelefono(rng);
            Assert.Matches(@"^[27]\d{3}-\d{4}$", tel);
            if (tel[0] == '7') moviles++;
        }
        Assert.InRange(moviles / (double)N, 0.72, 0.88); // ~80% móvil
    }

    [Fact]
    public void GenerarEstadoCivil_MenoresSiempreSolteros()
    {
        var rng = new Random(21);
        for (int edad = 0; edad < 18; edad++)
            Assert.Equal(PatientProfileGenerator.EstadoCivilSoltero,
                PatientProfileGenerator.GenerarEstadoCivil(edad, rng));
    }

    [Fact]
    public void GenerarEstadoCivil_AdultosMayoriaCasadoOAcompanado_Y_ViudezEnMayores()
    {
        var rng = new Random(22);
        const int N = 2000;

        int enPareja45 = 0, viudos70 = 0, viudos25 = 0;
        for (int i = 0; i < N; i++)
        {
            var e45 = PatientProfileGenerator.GenerarEstadoCivil(50, rng);
            if (e45 == PatientProfileGenerator.EstadoCivilCasado ||
                e45 == PatientProfileGenerator.EstadoCivilAcompanado) enPareja45++;
            if (PatientProfileGenerator.GenerarEstadoCivil(70, rng) == PatientProfileGenerator.EstadoCivilViudo) viudos70++;
            if (PatientProfileGenerator.GenerarEstadoCivil(25, rng) == PatientProfileGenerator.EstadoCivilViudo) viudos25++;
        }

        Assert.InRange(enPareja45 / (double)N, 0.55, 0.75); // 45-64: mayoría en pareja (0.64 config.)
        Assert.InRange(viudos70 / (double)N, 0.25, 0.42);   // 65+: viudez visible (0.33 config.)
        Assert.Equal(0, viudos25);                          // <30: sin viudos (peso 0)
    }

    [Fact]
    public void GenerateNew_TelefonoYEstadoCivilPresentes()
    {
        var gen = CreateGen();
        var p   = gen.GenerateNew(new DateOnly(2025, 6, 15));
        Assert.Matches(@"^[27]\d{3}-\d{4}$", p.Telefono);
        Assert.False(string.IsNullOrEmpty(p.EstadoCivilUuid));
        // Coherencia dura: si el paciente es menor, el estado civil es soltero
        var edad = PatientProfileGenerator.EdadEnAnios(p.BirthDate, new DateOnly(2025, 6, 15));
        if (edad < 18) Assert.Equal(PatientProfileGenerator.EstadoCivilSoltero, p.EstadoCivilUuid);
    }

    [Fact]
    public void EdadEnAnios_CumpleaniosExactoYPrevio()
    {
        var nacimiento = new DateOnly(2000, 6, 15);
        Assert.Equal(25, PatientProfileGenerator.EdadEnAnios(nacimiento, new DateOnly(2025, 6, 15))); // cumple hoy
        Assert.Equal(24, PatientProfileGenerator.EdadEnAnios(nacimiento, new DateOnly(2025, 6, 14))); // aún no cumple
    }
}
