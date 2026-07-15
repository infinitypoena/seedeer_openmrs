using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Catalogs;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Decide, de forma pura, la probabilidad de que una consulta termine en cita de control, condicionada
/// a la gravedad clínica del cuadro: al paciente que se refiere al hospital se le cita casi siempre (para
/// verlo tras el alta); una condición crónica casi siempre agenda control; un cuadro grave con
/// frecuencia; el resto según la probabilidad base. Sustituye la moneda plana que daba el mismo 30 % a
/// una faringitis y a una insuficiencia cardíaca.
/// </summary>
public static class SeguimientoPolicy
{
    /// <param name="referido">
    /// El paciente se va al hospital (ver <see cref="ReferenciaPolicy"/>): se le cita para el control
    /// post-alta, que es la visita en la que la clínica retoma su seguimiento.
    /// </param>
    public static double Probabilidad(
        IEnumerable<DiagnosticoEntry> dxs, ReferralProbabilitiesSettings rp, bool referido = false)
    {
        if (referido) return rp.FollowUpReferido;

        var lista = dxs as ICollection<DiagnosticoEntry> ?? dxs.ToList();
        if (lista.Any(d => d.EsCronica))              return rp.FollowUpCronico;
        if (lista.Any(d => d.Severidad == "grave"))   return rp.FollowUpGrave;
        return rp.FollowUp;
    }
}
