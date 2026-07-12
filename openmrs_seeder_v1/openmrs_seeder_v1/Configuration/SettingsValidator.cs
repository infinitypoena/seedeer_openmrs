using System.Collections;
using Microsoft.Extensions.Configuration;

namespace OpenmrsSeeder.Configuration;

/// <summary>
/// Validación fail-fast de la configuración al arranque. Dos responsabilidades:
/// (1) <see cref="Validate"/> — reglas de rango/coherencia sobre los settings ya bindeados; cualquier
///     violación impide arrancar (una probabilidad 1.5 o StartDate &gt; EndDate solo se manifestarían
///     como comportamiento raro a mitad de una corrida de horas).
/// (2) <see cref="FindUnknownKeys"/> — claves presentes en el JSON que no corresponden a ninguna
///     propiedad del POCO: el binding de .NET las ignora EN SILENCIO (así sobrevivió meses la clave
///     obsoleta ReferralProbabilities:AllergyOnNew). Se reportan como warning, no como error.
/// Clase estática pura (sin red ni DI) para poder testearla de forma determinista.
/// </summary>
public static class SettingsValidator
{
    public static List<string> Validate(SimulationSettings sim, OpenMrsSettings omrs)
    {
        var violaciones = new List<string>();

        void Prob(double valor, string nombre)
        {
            if (valor is < 0 or > 1)
                violaciones.Add($"{nombre} debe estar entre 0 y 1 (valor: {valor})");
        }

        void Banda(double min, double max, string nombreMin, string nombreMax)
        {
            if (min > max)
                violaciones.Add($"{nombreMin} ({min}) no puede ser mayor que {nombreMax} ({max})");
        }

        void NoNegativo(double valor, string nombre)
        {
            if (valor < 0)
                violaciones.Add($"{nombre} no puede ser negativo (valor: {valor})");
        }

        // Ventana y volumen
        if (sim.StartDate > sim.EndDate)
            violaciones.Add($"StartDate ({sim.StartDate:yyyy-MM-dd}) no puede ser posterior a EndDate ({sim.EndDate:yyyy-MM-dd})");
        if (sim.PacientesPorDiaMedio <= 0)
            violaciones.Add($"PacientesPorDiaMedio debe ser mayor que 0 (valor: {sim.PacientesPorDiaMedio})");
        if (sim.PorcentajeRecurrentes is < 0 or > 100)
            violaciones.Add($"PorcentajeRecurrentes debe estar entre 0 y 100 (valor: {sim.PorcentajeRecurrentes})");

        // Probabilidades simples
        Prob(sim.SeguimientoCronicoProb, "SeguimientoCronicoProb");
        Prob(sim.SeguimientoAgudoProb, "SeguimientoAgudoProb");
        NoNegativo(sim.VentanaSeguimientoAgudoDias, "VentanaSeguimientoAgudoDias");

        var rp = sim.ReferralProbabilities;
        Prob(rp.LabOrder, "ReferralProbabilities.LabOrder");
        Prob(rp.ClinicalExam, "ReferralProbabilities.ClinicalExam");
        Prob(rp.DrugOrder, "ReferralProbabilities.DrugOrder");
        Prob(rp.Urgent, "ReferralProbabilities.Urgent");
        Prob(rp.FollowUp, "ReferralProbabilities.FollowUp");
        Prob(rp.FollowUpCronico, "ReferralProbabilities.FollowUpCronico");
        Prob(rp.FollowUpGrave, "ReferralProbabilities.FollowUpGrave");
        Prob(rp.LabResult, "ReferralProbabilities.LabResult");

        // Bandas sorteadas por corrida
        Prob(sim.CommonProbMin, "CommonProbMin");
        Prob(sim.CommonProbMax, "CommonProbMax");
        Banda(sim.CommonProbMin, sim.CommonProbMax, "CommonProbMin", "CommonProbMax");
        Prob(sim.MedicoCabeceraProbMin, "MedicoCabeceraProbMin");
        Prob(sim.MedicoCabeceraProbMax, "MedicoCabeceraProbMax");
        Banda(sim.MedicoCabeceraProbMin, sim.MedicoCabeceraProbMax, "MedicoCabeceraProbMin", "MedicoCabeceraProbMax");

        // Roster de médicos
        if (sim.MinMedicosPorDia < 1)
            violaciones.Add($"MinMedicosPorDia debe ser al menos 1 (valor: {sim.MinMedicosPorDia})");
        Banda(sim.MinMedicosPorDia, sim.MaxMedicosPorDia, "MinMedicosPorDia", "MaxMedicosPorDia");

        // Alergias
        var al = sim.Allergy;
        Prob(al.BaseProbabilityMin, "Allergy.BaseProbabilityMin");
        Prob(al.BaseProbabilityMax, "Allergy.BaseProbabilityMax");
        Banda(al.BaseProbabilityMin, al.BaseProbabilityMax, "Allergy.BaseProbabilityMin", "Allergy.BaseProbabilityMax");
        Prob(al.SecondAllergyProbability, "Allergy.SecondAllergyProbability");
        Prob(al.ThirdAllergyProbability, "Allergy.ThirdAllergyProbability");
        NoNegativo(al.MaxAllergies, "Allergy.MaxAllergies");

        // Comorbilidad
        var co = sim.Comorbidity;
        Prob(co.BaseProbability, "Comorbidity.BaseProbability");
        Prob(co.SecondExtraProbability, "Comorbidity.SecondExtraProbability");
        NoNegativo(co.MaxAdditional, "Comorbidity.MaxAdditional");
        NoNegativo(co.AffinityBoost, "Comorbidity.AffinityBoost");
        foreach (var (grupo, factor) in co.AgeScaling)
            NoNegativo(factor, $"Comorbidity.AgeScaling[{grupo}]");

        // Recurrencia (espaciamiento entre visitas)
        var re = sim.Recurrence;
        NoNegativo(re.MinDiasAgudo, "Recurrence.MinDiasAgudo");
        NoNegativo(re.MinDiasCronico, "Recurrence.MinDiasCronico");
        Banda(re.MinDiasAgudo, re.MaxDiasAgudo, "Recurrence.MinDiasAgudo", "Recurrence.MaxDiasAgudo");
        Banda(re.MinDiasCronico, re.MaxDiasCronico, "Recurrence.MinDiasCronico", "Recurrence.MaxDiasCronico");

        // Citas
        NoNegativo(sim.Appointments.ToleranciaDias, "Appointments.ToleranciaDias");
        Prob(sim.Appointments.AsistenciaProb, "Appointments.AsistenciaProb");

        // Variedad
        NoNegativo(sim.Variedad.RepeticionDamping, "Variedad.RepeticionDamping");

        // Órdenes
        if (sim.Orders.LabVigenciaDias < 1)
            violaciones.Add($"Orders.LabVigenciaDias debe ser al menos 1 (valor: {sim.Orders.LabVigenciaDias})");

        // Pesos por día de semana
        var w = sim.WeekdayWeights;
        NoNegativo(w.Monday, "WeekdayWeights.Monday");
        NoNegativo(w.Tuesday, "WeekdayWeights.Tuesday");
        NoNegativo(w.Wednesday, "WeekdayWeights.Wednesday");
        NoNegativo(w.Thursday, "WeekdayWeights.Thursday");
        NoNegativo(w.Friday, "WeekdayWeights.Friday");
        NoNegativo(w.Saturday, "WeekdayWeights.Saturday");
        NoNegativo(w.Sunday, "WeekdayWeights.Sunday");

        // Demografía
        var dp = sim.DemographicProfile;
        if (dp.AgeGroups.Count == 0)
            violaciones.Add("DemographicProfile.AgeGroups no puede estar vacío");
        else if (dp.AgeGroups.All(g => g.Weight <= 0))
            violaciones.Add("DemographicProfile.AgeGroups debe tener al menos un grupo con peso mayor que 0");
        foreach (var g in dp.AgeGroups)
            NoNegativo(g.Weight, $"DemographicProfile.AgeGroups[{g.Label}].Weight");
        NoNegativo(dp.GenderRatio.M, "DemographicProfile.GenderRatio.M");
        NoNegativo(dp.GenderRatio.F, "DemographicProfile.GenderRatio.F");
        if (dp.GenderRatio.M + dp.GenderRatio.F <= 0)
            violaciones.Add("DemographicProfile.GenderRatio: M + F debe ser mayor que 0");

        // Conexión
        if (string.IsNullOrWhiteSpace(omrs.RestApi.BaseUrl))
            violaciones.Add("OpenMRS.RestApi.BaseUrl no puede estar vacío");

        return violaciones;
    }

    /// <summary>
    /// Claves del JSON que no existen como propiedad en el POCO destino (el binding las ignora en
    /// silencio). No desciende en propiedades de tipo diccionario o colección (sus claves son libres,
    /// p.ej. Comorbidity:AgeScaling:0-14). Devuelve rutas completas tipo
    /// "Simulation:ReferralProbabilities:AllergyOnNew".
    /// </summary>
    public static List<string> FindUnknownKeys(IConfigurationSection section, Type settingsType)
    {
        var desconocidas = new List<string>();
        Walk(section, settingsType, desconocidas);
        return desconocidas;
    }

    private static void Walk(IConfigurationSection section, Type type, List<string> desconocidas)
    {
        foreach (var child in section.GetChildren())
        {
            var prop = type.GetProperties()
                .FirstOrDefault(p => string.Equals(p.Name, child.Key, StringComparison.OrdinalIgnoreCase));

            if (prop is null)
            {
                desconocidas.Add(child.Path);
                continue;
            }

            if (EsComplejo(prop.PropertyType))
                Walk(child, prop.PropertyType, desconocidas);
        }
    }

    /// <summary>POCO anidado en el que vale la pena descender (no primitivo, no colección/diccionario).</summary>
    private static bool EsComplejo(Type t) =>
        t.IsClass
        && t != typeof(string)
        && !typeof(IEnumerable).IsAssignableFrom(t);
}
