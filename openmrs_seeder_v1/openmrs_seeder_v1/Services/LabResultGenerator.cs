using OpenmrsSeeder.Models.Catalogs;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Genera el resultado de un examen de laboratorio de forma pura y testeable (sin red), coherente
/// con la(s) enfermedad(es) del paciente. Numéricos → valor en banda normal o anormal; codificados
/// → UUID de la respuesta normal/anormal. Paneles, imágenes y filas sin <c>datatype</c> no producen
/// resultado (la orden queda sin valor). Mismo patrón que <see cref="VitalsSeeder.ComputeVitals"/>.
/// </summary>
public static class LabResultGenerator
{
    /// <summary>Prob. de que el resultado sea ANORMAL cuando la enfermedad del paciente dispara el examen.</summary>
    public const double ProbAnormalSiTrigger = 0.80;

    public enum TipoResultado { Ninguno, Numerico, Codificado }

    public readonly record struct LabResult(TipoResultado Tipo, double? Numerico, string? CodedUuid)
    {
        public static readonly LabResult Ninguno = new(TipoResultado.Ninguno, null, null);
    }

    /// <summary>
    /// Decide el resultado de <paramref name="lab"/> para un paciente con las categorías y diagnósticos
    /// dados. El disparador de anormalidad es por categoría (<c>ResTrigger</c>, típico de numéricos) o
    /// por diagnóstico específico (<c>ResTriggerDx</c>, típico de codificados disease-specific como NS1).
    /// </summary>
    public static LabResult Generar(
        LaboratorioEntry lab,
        ISet<string> categoriasPaciente,
        ISet<string> diagnosticosPaciente,
        Random rng)
    {
        var disparado = lab.ResTrigger.Any(categoriasPaciente.Contains)
                     || lab.ResTriggerDx.Any(diagnosticosPaciente.Contains);
        var anormal = disparado && rng.NextDouble() < ProbAnormalSiTrigger;

        switch (lab.Datatype)
        {
            case "numeric":
            {
                var (min, max) = anormal ? (lab.ResMinAnormal, lab.ResMaxAnormal) : (lab.ResMin, lab.ResMax);
                if (max < min) (min, max) = (max, min);
                // Banda con límites enteros → valor entero: conceptos con allow_decimal=false (p.ej.
                // ASAT, amilasa) rechazan decimales con Obs.error.precision, y un entero es válido
                // aunque el concepto sí admita decimales. Bandas decimales (HbA1c) conservan 1 decimal.
                var decimales = double.IsInteger(min) && double.IsInteger(max) ? 0 : 1;
                var valor = Math.Round(rng.NextDouble() * (max - min) + min, decimales);
                return new LabResult(TipoResultado.Numerico, valor, null);
            }
            case "coded":
            {
                var uuid = anormal ? lab.ResAnormalUuid : lab.ResNormalUuid;
                return string.IsNullOrWhiteSpace(uuid)
                    ? LabResult.Ninguno
                    : new LabResult(TipoResultado.Codificado, null, uuid);
            }
            default:
                return LabResult.Ninguno; // panel | imagen | ""
        }
    }
}
