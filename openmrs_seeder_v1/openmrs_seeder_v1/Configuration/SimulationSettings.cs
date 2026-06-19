namespace OpenmrsSeeder.Configuration;

public class SimulationSettings
{
    public DateTime StartDate { get; set; } = new DateTime(2023, 1, 1);
    public DateTime EndDate { get; set; } = new DateTime(2024, 12, 31);
    public int PacientesPorDiaMedio { get; set; } = 40;
    public int PorcentajeRecurrentes { get; set; } = 30;
    public string Locale { get; set; } = "es";
    public int RandomSeed { get; set; } = 42;
    public string ClinicType { get; set; } = "ConsultaExterna";
    public HorarioAtencionSettings HorarioAtencion { get; set; } = new();
    public DemographicProfileSettings DemographicProfile { get; set; } = new();
    public ReferralProbabilitiesSettings ReferralProbabilities { get; set; } = new();
    public WeekdayWeightsSettings WeekdayWeights { get; set; } = new();
}

public class HorarioAtencionSettings
{
    public BloquePico PicoAM { get; set; } = new() { Inicio = "08:00", Fin = "10:00", Peso = 40 };
    public BloquePico PicoPM { get; set; } = new() { Inicio = "14:00", Fin = "16:00", Peso = 30 };
}

public class BloquePico
{
    public string Inicio { get; set; } = "";
    public string Fin { get; set; } = "";
    public int Peso { get; set; }
}

public class DemographicProfileSettings
{
    public List<AgeGroupWeight> AgeGroups { get; set; } =
    [
        new() { Label = "0-14",  Weight = 20 },
        new() { Label = "15-29", Weight = 18 },
        new() { Label = "30-44", Weight = 25 },
        new() { Label = "45-64", Weight = 25 },
        new() { Label = "65+",   Weight = 12 },
    ];

    public GenderRatioSettings GenderRatio { get; set; } = new();
}

public class AgeGroupWeight
{
    public string Label { get; set; } = "";
    public int Weight { get; set; }
}

public class GenderRatioSettings
{
    public int M { get; set; } = 48;
    public int F { get; set; } = 52;
}

public class ReferralProbabilitiesSettings
{
    public double LabOrder { get; set; } = 0.40;
    public double ClinicalExam { get; set; } = 0.35;
    public double DrugOrder { get; set; } = 0.65;
    public double Urgent { get; set; } = 0.20;
    public double FollowUp { get; set; } = 0.30;
    public double AllergyOnNew { get; set; } = 0.15;
}

public class WeekdayWeightsSettings
{
    public double Monday { get; set; } = 1.20;
    public double Tuesday { get; set; } = 1.20;
    public double Wednesday { get; set; } = 1.00;
    public double Thursday { get; set; } = 1.00;
    public double Friday { get; set; } = 0.90;
    public double Saturday { get; set; } = 0.50;
    public double Sunday { get; set; } = 0.00;
}
