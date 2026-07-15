using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests.Modelo;

public class SeguimientoPolicyTests
{
    private static readonly ReferralProbabilitiesSettings Rp = new(); // FollowUp .30 / Grave .80 / Cronico .90

    private static DiagnosticoEntry Dx(bool cronica = false, string sev = "leve") =>
        new() { EsCronica = cronica, Severidad = sev };

    [Fact]
    public void Cronico_UsaFollowUpCronico()
    {
        var p = SeguimientoPolicy.Probabilidad([Dx(cronica: true, sev: "leve")], Rp);
        Assert.Equal(Rp.FollowUpCronico, p);
    }

    [Fact]
    public void GraveNoCronico_UsaFollowUpGrave()
    {
        var p = SeguimientoPolicy.Probabilidad([Dx(cronica: false, sev: "grave")], Rp);
        Assert.Equal(Rp.FollowUpGrave, p);
    }

    [Fact]
    public void LeveNoCronico_UsaFollowUpBase()
    {
        var p = SeguimientoPolicy.Probabilidad([Dx(cronica: false, sev: "leve")], Rp);
        Assert.Equal(Rp.FollowUp, p);
    }

    [Fact]
    public void CronicoGanaAGrave_CuandoCoexisten()
    {
        // Primario grave + comorbilidad crónica → domina la banda crónica.
        var p = SeguimientoPolicy.Probabilidad([Dx(sev: "grave"), Dx(cronica: true)], Rp);
        Assert.Equal(Rp.FollowUpCronico, p);
    }

    [Fact]
    public void SinDiagnosticos_UsaFollowUpBase()
    {
        var p = SeguimientoPolicy.Probabilidad([], Rp);
        Assert.Equal(Rp.FollowUp, p);
    }
}
