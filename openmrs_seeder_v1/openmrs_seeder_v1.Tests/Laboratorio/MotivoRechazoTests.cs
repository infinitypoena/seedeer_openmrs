using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests.Laboratorio;

/// <summary>
/// El motivo con el que el laboratorio rechaza una muestra (<c>DECLINED</c> + <c>fulfillerComment</c>):
/// texto clínicamente plausible, elegido con la tirada inyectada.
/// </summary>
public class MotivoRechazoTests
{
    [Fact]
    public void SiempreDevuelveUnMotivoNoVacio()
    {
        Assert.False(string.IsNullOrWhiteSpace(LabWorkflow.MotivoRechazo(_ => 0)));
    }

    [Fact]
    public void HayVariosMotivosDistintos()
    {
        // Hemolizada / insuficiente / el paciente no acudió — al menos dos distintos según la tirada.
        var primero = LabWorkflow.MotivoRechazo(_ => 0);
        var segundo = LabWorkflow.MotivoRechazo(_ => 1);

        Assert.NotEqual(primero, segundo);
    }

    [Fact]
    public void LaTiradaRecibeElTamanoRealDeLaLista()
    {
        // El seam pasa el Count a nextInt: cualquier índice que este devuelva en [0, n) es válido.
        int? tamano = null;
        LabWorkflow.MotivoRechazo(n => { tamano = n; return n - 1; });

        Assert.NotNull(tamano);
        Assert.True(tamano >= 2);
    }
}
