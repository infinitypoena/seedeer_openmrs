using Bogus;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Services;

public class PatientProfileGenerator
{
    private readonly SimulationSettings _settings;
    private readonly CatalogLoader _catalogs;
    private readonly Faker _faker;
    private readonly Random _rng;

    // Pools de nombres del catálogo (lazy: el CatalogLoader se carga después del constructor).
    private List<string>? _nombresM;
    private List<string>? _nombresF;
    private List<string>? _apellidos;

    public PatientProfileGenerator(SimulationSettings settings, CatalogLoader catalogs)
    {
        _settings = settings;
        _catalogs = catalogs;
        _rng = new Random(settings.RandomSeed + 1);
        _faker = new Faker(settings.Locale) { Random = new Randomizer(settings.RandomSeed + 2) };
    }

    /// <summary>
    /// Genera un paciente nuevo. La fecha de nacimiento se ancla a <paramref name="referenceDate"/>
    /// (la fecha de la visita/creación) para que la edad sea válida y ≥ al mínimo configurado en
    /// esa fecha. Si se omite, se usa la fecha de hoy.
    /// </summary>
    public SimulatedPatient GenerateNew(DateOnly? referenceDate = null)
    {
        var refDate  = referenceDate ?? DateOnly.FromDateTime(DateTime.Today);
        var gender   = PickGender();
        var ageGroup = PickAgeGroup();
        var (given, secondGiven, family, secondFamily) = GenerateName(gender);
        var (address1, city, departamento, pais) = GenerateAddress();
        var birthDate = GenerateBirthDate(ageGroup, refDate);
        var edad      = EdadEnAnios(birthDate, refDate);

        return new SimulatedPatient
        {
            Identifier       = $"SIM-{Guid.NewGuid().ToString("N")[..8].ToUpper()}",
            GivenName        = given,
            SecondGivenName  = secondGiven,
            FamilyName       = family,
            SecondFamilyName = secondFamily,
            Gender           = gender,
            BirthDate        = birthDate,
            AgeGroup         = ageGroup,
            Telefono         = GenerarTelefono(_rng),
            EstadoCivilUuid  = GenerarEstadoCivil(edad, _rng),
            Address1         = address1,
            City             = city,
            StateProvince    = departamento,
            Country          = pais,
            EsNuevo          = true
        };
    }

    // ── Atributos de persona (teléfono + estado civil) ────────────────────────

    // Answers del concepto CIEL "Estado civil" (1054), verificados en esta instancia.
    public const string EstadoCivilSoltero    = "1057AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"; // No casado anteriormente
    public const string EstadoCivilCasado     = "5555AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    public const string EstadoCivilAcompanado = "1060AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"; // Vive con su pareja
    public const string EstadoCivilViudo      = "1059AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    public const string EstadoCivilDivorciado = "1058AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    public const string EstadoCivilSeparado   = "1056AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    /// <summary>Teléfono salvadoreño sintético: móvil 7###-#### (~80 %) o fijo 2###-#### (~20 %).</summary>
    public static string GenerarTelefono(Random rng)
    {
        var prefijo = rng.NextDouble() < 0.80 ? 7 : 2;
        return $"{prefijo}{rng.Next(0, 1000):000}-{rng.Next(0, 10000):0000}";
    }

    /// <summary>
    /// Estado civil coherente con la edad (seam puro): menores siempre solteros; con la edad crece
    /// casado/acompañado y en 65+ aparece viudez. Devuelve el UUID del answer del concepto 1054.
    /// </summary>
    public static string GenerarEstadoCivil(int edad, Random rng)
    {
        if (edad < 18) return EstadoCivilSoltero;

        // (uuid, peso) por franja — pesos relativos, no necesitan sumar 1.
        (string Uuid, double Peso)[] pesos = edad switch
        {
            < 30 => [(EstadoCivilSoltero, 0.62), (EstadoCivilCasado, 0.18), (EstadoCivilAcompanado, 0.18),
                     (EstadoCivilSeparado, 0.02)],
            < 45 => [(EstadoCivilSoltero, 0.25), (EstadoCivilCasado, 0.42), (EstadoCivilAcompanado, 0.20),
                     (EstadoCivilDivorciado, 0.06), (EstadoCivilSeparado, 0.06), (EstadoCivilViudo, 0.01)],
            < 65 => [(EstadoCivilSoltero, 0.14), (EstadoCivilCasado, 0.50), (EstadoCivilAcompanado, 0.14),
                     (EstadoCivilDivorciado, 0.08), (EstadoCivilSeparado, 0.08), (EstadoCivilViudo, 0.06)],
            _    => [(EstadoCivilSoltero, 0.08), (EstadoCivilCasado, 0.44), (EstadoCivilAcompanado, 0.05),
                     (EstadoCivilDivorciado, 0.05), (EstadoCivilSeparado, 0.05), (EstadoCivilViudo, 0.33)]
        };

        var pick = rng.NextDouble() * pesos.Sum(p => p.Peso);
        double acumulado = 0;
        foreach (var (uuid, peso) in pesos)
        {
            acumulado += peso;
            if (pick <= acumulado) return uuid;
        }
        return pesos[^1].Uuid;
    }

    /// <summary>Edad cumplida en años a la fecha de referencia.</summary>
    public static int EdadEnAnios(DateOnly nacimiento, DateOnly referencia)
    {
        var edad = referencia.Year - nacimiento.Year;
        if (referencia < nacimiento.AddYears(edad)) edad--;
        return Math.Max(0, edad);
    }

    /// <summary>Edad cumplida en meses a la fecha de referencia (para la talla pediátrica por edad).</summary>
    public static int EdadEnMeses(DateOnly nacimiento, DateOnly referencia)
    {
        var meses = (referencia.Year - nacimiento.Year) * 12 + referencia.Month - nacimiento.Month;
        if (referencia.Day < nacimiento.Day) meses--;
        return Math.Max(0, meses);
    }

    /// <summary>
    /// Seam puro: grupo de edad a la fecha de la visita, recalculado desde la fecha de nacimiento (no
    /// copiado de visitas previas). Así la edad del paciente avanza con el tiempo simulado y un niño
    /// puede cruzar de franja entre controles. Las fronteras coinciden con <c>GenerateBirthDate</c>.
    /// </summary>
    public static string GrupoEdad(DateOnly birthDate, DateOnly fechaVisita)
    {
        var edad = EdadEnAnios(birthDate, fechaVisita);
        return edad <= 14 ? "0-14"
             : edad <= 29 ? "15-29"
             : edad <= 44 ? "30-44"
             : edad <= 64 ? "45-64"
             :              "65+";
    }

    /// <summary>
    /// Dirección salvadoreña coherente desde <c>direcciones.csv</c>: zona (colonia/barrio/cantón)
    /// elegida por peso — la mayoría cerca de la clínica (área metropolitana), cola de municipios
    /// lejanos — con detalle de casa/pasaje para las zonas urbanas. Catálogo vacío = fallback Bogus
    /// (comportamiento histórico, país vacío → "España" en PatientSeeder).
    /// </summary>
    private (string address1, string city, string departamento, string pais) GenerateAddress()
    {
        var direcciones = _catalogs.Direcciones;
        if (direcciones.Count == 0)
            return (_faker.Address.StreetAddress(), _faker.Address.City(), "", "");

        var total = direcciones.Sum(d => d.Peso);
        var pick = _rng.NextDouble() * total;
        double acumulado = 0;
        var elegida = direcciones[^1];
        foreach (var d in direcciones)
        {
            acumulado += d.Peso;
            if (pick <= acumulado) { elegida = d; break; }
        }

        // Los cantones (rurales) no llevan numeración de casa; las zonas urbanas sí
        var address1 = elegida.Zona.StartsWith("Cantón", StringComparison.OrdinalIgnoreCase)
            ? elegida.Zona
            : _rng.NextDouble() < 0.30
                ? $"{elegida.Zona}, pasaje {(char)('A' + _rng.Next(0, 8))}, casa #{_rng.Next(1, 61)}"
                : $"{elegida.Zona}, casa #{_rng.Next(1, 121)}";

        return (address1, elegida.Municipio, elegida.Departamento, "El Salvador");
    }

    /// <summary>
    /// Genera un nombre completo (primer + segundo nombre, primer + segundo apellido) a partir de los
    /// catálogos <c>nombres.csv</c>/<c>apellidos.csv</c>. Si el catálogo está vacío cae al comportamiento
    /// previo con Bogus (un solo nombre y un apellido), manteniendo retrocompatibilidad.
    /// </summary>
    private (string given, string secondGiven, string family, string secondFamily) GenerateName(string gender)
    {
        var nombres   = PoolNombres(gender);
        var apellidos = _apellidos ??= _catalogs.Apellidos.Distinct().ToList();

        if (nombres.Count == 0 || apellidos.Count == 0)
        {
            var fallbackGiven = gender == "M"
                ? _faker.Name.FirstName(Bogus.DataSets.Name.Gender.Male)
                : _faker.Name.FirstName(Bogus.DataSets.Name.Gender.Female);
            return (fallbackGiven, "", _faker.Name.LastName(), "");
        }

        var (given, secondGiven) = PickDistinctPair(nombres);
        var (family, secondFamily) = PickDistinctPair(apellidos);
        return (given, secondGiven, family, secondFamily);
    }

    /// <summary>Dos elementos distintos del pool (el segundo vacío si el pool tiene un solo elemento).</summary>
    private (string first, string second) PickDistinctPair(IReadOnlyList<string> pool)
    {
        var first = pool[_rng.Next(pool.Count)];
        if (pool.Count == 1) return (first, "");
        // Tope de intentos: un pool con valores repetidos no debe poder colgar el generador
        // (los pools se deduplican al cargar, pero este guard lo hace imposible por construcción).
        for (int intento = 0; intento < 20; intento++)
        {
            var second = pool[_rng.Next(pool.Count)];
            if (second != first) return (first, second);
        }
        return (first, "");
    }

    private List<string> PoolNombres(string gender)
    {
        // Distinct: un nombre duplicado en el CSV no debe sesgar la selección ni romper el par distinto
        _nombresM ??= _catalogs.Nombres.Where(n => n.Genero == "M").Select(n => n.Nombre).Distinct().ToList();
        _nombresF ??= _catalogs.Nombres.Where(n => n.Genero == "F").Select(n => n.Nombre).Distinct().ToList();
        return gender == "M" ? _nombresM : _nombresF;
    }

    private string PickGender()
    {
        var ratio = _settings.DemographicProfile.GenderRatio;
        return _rng.NextDouble() * (ratio.M + ratio.F) < ratio.M ? "M" : "F";
    }

    private string PickAgeGroup()
    {
        var groups = _settings.DemographicProfile.AgeGroups;
        var total = groups.Sum(g => g.Weight);
        var pick = _rng.NextDouble() * total;
        double cumulative = 0;
        foreach (var g in groups)
        {
            cumulative += g.Weight;
            if (pick <= cumulative) return g.Label;
        }
        return groups.Last().Label;
    }

    private DateOnly GenerateBirthDate(string ageGroup, DateOnly refDate)
    {
        // Grupo 0-14: edad en meses con piso mínimo (6 meses, o 1 mes en consultorio pediátrico).
        if (ageGroup == "0-14")
        {
            var profile   = _settings.DemographicProfile;
            var minMonths = profile.PediatricClinic ? profile.PediatricMinAgeMonths : profile.MinPatientAgeMonths;
            minMonths     = Math.Max(1, minMonths);
            var ageMonths = _rng.Next(minMonths, 14 * 12 + 1);
            // Restar días solo envejece (mueve el nacimiento más atrás) → nunca baja del mínimo.
            return refDate.AddMonths(-ageMonths).AddDays(-_rng.Next(0, 28));
        }

        var (min, max) = ageGroup switch
        {
            "15-29" => (15, 29),
            "30-44" => (30, 44),
            "45-64" => (45, 64),
            "65+"   => (65, 85),
            _       => (18, 60)
        };
        var age = _rng.Next(min, max + 1);
        var dayOffset = _rng.Next(0, 365);
        return refDate.AddYears(-age).AddDays(-dayOffset);
    }
}
