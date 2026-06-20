using Bogus;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Services;

public class PatientProfileGenerator
{
    private readonly SimulationSettings _settings;
    private readonly Faker _faker;
    private readonly Random _rng;

    public PatientProfileGenerator(SimulationSettings settings)
    {
        _settings = settings;
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

        return new SimulatedPatient
        {
            Identifier = $"SIM-{Guid.NewGuid().ToString("N")[..8].ToUpper()}",
            GivenName  = gender == "M"
                ? _faker.Name.FirstName(Bogus.DataSets.Name.Gender.Male)
                : _faker.Name.FirstName(Bogus.DataSets.Name.Gender.Female),
            FamilyName = _faker.Name.LastName(),
            Gender     = gender,
            BirthDate  = GenerateBirthDate(ageGroup, refDate),
            AgeGroup   = ageGroup,
            Address1   = _faker.Address.StreetAddress(),
            City       = _faker.Address.City(),
            EsNuevo    = true
        };
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
