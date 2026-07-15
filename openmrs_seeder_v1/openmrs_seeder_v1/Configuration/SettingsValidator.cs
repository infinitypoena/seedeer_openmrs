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
        Prob(rp.FollowUpReferido, "ReferralProbabilities.FollowUpReferido");

        // Ciclo de vida de la orden de laboratorio
        var lb = sim.Laboratorio;
        Prob(lb.ProbRechazo, "Laboratorio.ProbRechazo");
        Prob(lb.ProbResultadoLlega, "Laboratorio.ProbResultadoLlega");
        NoNegativo(lb.MinutosHastaTomaMin, "Laboratorio.MinutosHastaTomaMin");
        Banda(lb.MinutosHastaTomaMin, lb.MinutosHastaTomaMax,
            "Laboratorio.MinutosHastaTomaMin", "Laboratorio.MinutosHastaTomaMax");

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
        NoNegativo(re.MinDiasPostReferencia, "Recurrence.MinDiasPostReferencia");
        Banda(re.MinDiasPostReferencia, re.MaxDiasPostReferencia,
            "Recurrence.MinDiasPostReferencia", "Recurrence.MaxDiasPostReferencia");
        NoNegativo(re.VisitasEspontaneasPorPacienteAno, "Recurrence.VisitasEspontaneasPorPacienteAno");

        // Citas
        NoNegativo(sim.Appointments.ToleranciaDias, "Appointments.ToleranciaDias");
        Prob(sim.Appointments.AsistenciaProb, "Appointments.AsistenciaProb");

        // Variedad
        NoNegativo(sim.Variedad.RepeticionDamping, "Variedad.RepeticionDamping");

        // Satisfacción (la nota 1-5 de cada visita)
        var sa = sim.Satisfaccion;
        if (sa.MediaBase is < 1 or > 5)
            violaciones.Add($"Satisfaccion.MediaBase debe estar en la escala 1-5 (valor: {sa.MediaBase})");
        if (sa.UmbralSatisfaccion is < 1 or > 5)
            violaciones.Add($"Satisfaccion.UmbralSatisfaccion debe estar en la escala 1-5 (valor: {sa.UmbralSatisfaccion})");
        NoNegativo(sa.Desviacion, "Satisfaccion.Desviacion");
        NoNegativo(sa.BonusMedicoCabecera, "Satisfaccion.BonusMedicoCabecera");
        NoNegativo(sa.BonusCitaCumplida, "Satisfaccion.BonusCitaCumplida");
        NoNegativo(sa.PenalizacionCuadroGrave, "Satisfaccion.PenalizacionCuadroGrave");
        NoNegativo(sa.PenalizacionSaturacion, "Satisfaccion.PenalizacionSaturacion");
        if (sa.CapacidadComodaPorDia < 1)
            violaciones.Add($"Satisfaccion.CapacidadComodaPorDia debe ser al menos 1 (valor: {sa.CapacidadComodaPorDia})");

        // Crecimiento (difusión de Bass). Los tres mandos de la curva: dónde arranca
        // (PacientesPorDiaMedio), dónde acaba (PacientesPorDiaObjetivo) y cuánto tarda
        // (VentanaActividadDias). El techo es solo una red de seguridad y debe quedar por encima de ambos.
        var cr = sim.Crecimiento;
        if (cr.PoblacionCaptacion < 1)
            violaciones.Add($"Crecimiento.PoblacionCaptacion debe ser al menos 1 (valor: {cr.PoblacionCaptacion})");
        if (cr.RecurrentesPorPacienteExtra < 0)
            violaciones.Add(
                $"Crecimiento.RecurrentesPorPacienteExtra no puede ser negativo (valor: {cr.RecurrentesPorPacienteExtra}); " +
                "0 = derivar el boca a boca del objetivo (lo normal)");
        if (cr.MinVisitasRecurrente < 1)
            violaciones.Add($"Crecimiento.MinVisitasRecurrente debe ser al menos 1 (valor: {cr.MinVisitasRecurrente})");
        if (cr.VentanaActividadDias < 1)
            violaciones.Add($"Crecimiento.VentanaActividadDias debe ser al menos 1 (valor: {cr.VentanaActividadDias})");
        if (cr.PacientesPorDiaObjetivo < 1)
            violaciones.Add($"Crecimiento.PacientesPorDiaObjetivo debe ser al menos 1 (valor: {cr.PacientesPorDiaObjetivo})");
        if (cr.PacientesPorDiaMax < sim.PacientesPorDiaMedio)
            violaciones.Add(
                $"Crecimiento.PacientesPorDiaMax ({cr.PacientesPorDiaMax}) no puede ser menor que " +
                $"PacientesPorDiaMedio ({sim.PacientesPorDiaMedio}): el techo dejaría a la clínica por debajo de su volumen de arranque");
        if (cr.PacientesPorDiaMax < cr.PacientesPorDiaObjetivo)
            violaciones.Add(
                $"Crecimiento.PacientesPorDiaMax ({cr.PacientesPorDiaMax}) no puede ser menor que " +
                $"PacientesPorDiaObjetivo ({cr.PacientesPorDiaObjetivo}): el techo impediría alcanzar el objetivo");
        // El objetivo ES el aforo de la consulta, y PacientesPorDiaMax solo una red de seguridad POR ENCIMA.
        // Si van pegados, quien gobierna la clínica acaba siendo el techo (33 de los 42 meses de la corrida
        // de 3,5 años se pasaron clavados en él) y la ley L5 no tiene margen para distinguirlo.
        if (cr.Enabled && cr.PacientesPorDiaMax < cr.PacientesPorDiaObjetivo * 1.3)
            violaciones.Add(
                $"Crecimiento.PacientesPorDiaMax ({cr.PacientesPorDiaMax}) debe quedar holgado por encima de " +
                $"PacientesPorDiaObjetivo ({cr.PacientesPorDiaObjetivo}, al menos ×1,3 = " +
                $"{(int)Math.Ceiling(cr.PacientesPorDiaObjetivo * 1.3)}): el techo es una red de seguridad, no el aforo");
        // El aforo (el objetivo) y la capacidad cómoda tienen que ser el mismo número: si la clínica trabaja
        // sistemáticamente por encima de lo que puede atender con holgura, las notas se hunden por diseño y
        // el churn se dispara sin que eso signifique nada.
        if (cr.Enabled && sim.Satisfaccion.Enabled &&
            sim.Satisfaccion.CapacidadComodaPorDia < cr.PacientesPorDiaObjetivo)
            violaciones.Add(
                $"Satisfaccion.CapacidadComodaPorDia ({sim.Satisfaccion.CapacidadComodaPorDia}) no puede ser menor que " +
                $"Crecimiento.PacientesPorDiaObjetivo ({cr.PacientesPorDiaObjetivo}): la clínica viviría saturada en su " +
                "propia meseta y las calificaciones se hundirían por construcción");
        Prob(cr.AsistenciaProbInsatisfecho, "Crecimiento.AsistenciaProbInsatisfecho");
        // El coeficiente de innovación se deriva (p = PacientesPorDiaMedio / M): si el mercado es más
        // pequeño que las altas de un solo día, p > 1 y el modelo pierde sentido.
        if (cr.Enabled && cr.PoblacionCaptacion >= 1 && sim.PacientesPorDiaMedio > cr.PoblacionCaptacion)
            violaciones.Add(
                $"Crecimiento.PoblacionCaptacion ({cr.PoblacionCaptacion}) es menor que las altas de un solo día: " +
                "el área de influencia se agotaría el primer día");

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

        // Corrección de fechas de auditoría (acceso directo a MariaDB)
        var db = omrs.Database;
        if (db.CorregirFechas && string.IsNullOrWhiteSpace(db.ConnectionString))
            violaciones.Add("OpenMRS.Database.ConnectionString no puede estar vacío si CorregirFechas es true");
        if (db.TamanoLote < 1)
            violaciones.Add($"OpenMRS.Database.TamanoLote debe ser al menos 1 (valor: {db.TamanoLote})");
        if (string.IsNullOrWhiteSpace(db.PrefijoPaciente))
            violaciones.Add("OpenMRS.Database.PrefijoPaciente no puede estar vacío (acota qué filas puede tocar el proceso)");

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
