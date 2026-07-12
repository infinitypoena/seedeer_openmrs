using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Catalogs;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Decide, de forma pura, la probabilidad de que una consulta termine en cita de control, condicionada
/// a la gravedad clínica del cuadro: una condición crónica casi siempre agenda control; un cuadro grave
/// con frecuencia; el resto según la probabilidad base de seguimiento. Sustituye la moneda plana que
/// daba el mismo 30 % a una faringitis y a una insuficiencia cardíaca.
/// </summary>
public static class SeguimientoPolicy
{
    public static double Probabilidad(IEnumerable<DiagnosticoEntry> dxs, ReferralProbabilitiesSettings rp)
    {
        var lista = dxs as ICollection<DiagnosticoEntry> ?? dxs.ToList();
        if (lista.Any(d => d.EsCronica))              return rp.FollowUpCronico;
        if (lista.Any(d => d.Severidad == "grave"))   return rp.FollowUpGrave;
        return rp.FollowUp;
    }
}
