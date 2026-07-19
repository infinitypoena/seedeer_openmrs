using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

namespace openmrs_seeder_v1.Tests.Modelo;

/// <summary>Estadísticas de lo realmente sembrado, que alimentan el resumen final (etapa 4/4).</summary>
public class RunStatsTests
{
    private static DiagnosticoEntry Dx(string nombre) =>
        new() { CielUuid = nombre, NombreEs = nombre, Categoria = "respiratorio" };

    private static SimulatedPatient Paciente(string uuid, bool nuevo, params string[] dxs) => new()
    {
        OpenMrsUuid = uuid,
        EsNuevo = nuevo,
        Diagnostico = dxs.Length > 0 ? Dx(dxs[0]) : null,
        Comorbilidades = [.. dxs.Skip(1).Select(Dx)]
    };

    [Fact]
    public void SepararaVisitasDeNuevosYDeRecurrentes()
    {
        var s = new RunStats();
        s.RegistrarVisita(Paciente("a", nuevo: true, "gripe"), new DateOnly(2025, 1, 6));
        s.RegistrarVisita(Paciente("b", nuevo: true, "gripe"), new DateOnly(2025, 1, 6));
        s.RegistrarVisita(Paciente("a", nuevo: false, "gripe"), new DateOnly(2025, 1, 20));

        Assert.Equal(3, s.TotalVisitas);
        Assert.Equal(2, s.VisitasDeNuevos);
        Assert.Equal(1, s.VisitasDeRecurrentes);
    }

    [Fact]
    public void PacientesUnicosYQueVolvieron_CuentanPersonasNoVisitas()
    {
        var s = new RunStats();
        s.RegistrarVisita(Paciente("a", true, "gripe"), new DateOnly(2025, 1, 6));
        s.RegistrarVisita(Paciente("a", false, "gripe"), new DateOnly(2025, 1, 20));
        s.RegistrarVisita(Paciente("a", false, "gripe"), new DateOnly(2025, 2, 3));
        s.RegistrarVisita(Paciente("b", true, "gripe"), new DateOnly(2025, 1, 7));

        Assert.Equal(2, s.PacientesUnicos);        // 'a' cuenta una sola vez
        Assert.Equal(1, s.PacientesQueVolvieron);  // solo 'a' tiene 2+ visitas
        Assert.Equal(2.0, s.VisitasPorPaciente);   // 4 visitas / 2 pacientes
    }

    [Fact]
    public void AgrupaPorSemanaUsandoElLunesQueLaAbre()
    {
        var s = new RunStats();
        // 2025-01-08 es miércoles y 2025-01-12 domingo: misma semana (lunes 2025-01-06)
        s.RegistrarVisita(Paciente("a", true, "gripe"), new DateOnly(2025, 1, 8));
        s.RegistrarVisita(Paciente("b", false, "gripe"), new DateOnly(2025, 1, 12));
        // 2025-01-13 es el lunes siguiente → otra semana
        s.RegistrarVisita(Paciente("c", true, "gripe"), new DateOnly(2025, 1, 13));

        var semanas = s.PorSemana();

        Assert.Equal(2, semanas.Count);
        Assert.Equal((new DateOnly(2025, 1, 6), 2, 1, 1), semanas[0]);
        Assert.Equal((new DateOnly(2025, 1, 13), 1, 1, 0), semanas[1]);
    }

    [Theory]
    [InlineData("2025-01-06", "2025-01-06")]  // lunes → él mismo
    [InlineData("2025-01-12", "2025-01-06")]  // domingo → lunes anterior
    [InlineData("2025-01-13", "2025-01-13")]  // lunes siguiente
    public void InicioDeSemana_EsElLunes(string fecha, string esperado)
    {
        Assert.Equal(DateOnly.Parse(esperado), RunStats.InicioDeSemana(DateOnly.Parse(fecha)));
    }

    [Fact]
    public void TopDiagnosticos_CuentaPrimariosYComorbilidades_OrdenadosPorFrecuencia()
    {
        var s = new RunStats();
        s.RegistrarVisita(Paciente("a", true, "hipertensión", "diabetes"), new DateOnly(2025, 1, 6));
        s.RegistrarVisita(Paciente("b", true, "hipertensión"), new DateOnly(2025, 1, 7));
        s.RegistrarVisita(Paciente("c", true, "gripe"), new DateOnly(2025, 1, 8));

        var top = s.TopDiagnosticos(5);

        Assert.Equal(3, s.DiagnosticosDistintos);
        Assert.Equal("hipertensión", top[0].Nombre);
        Assert.Equal(2, top[0].Veces);
        // 3 visitas, 2 con hipertensión → 66,7 % de las visitas (los % pueden sumar >100: hay comorbilidades)
        Assert.Equal(66.7, Math.Round(top[0].PctVisitas, 1));
        Assert.Equal(1, top[1].Veces);
    }

    [Fact]
    public void TopDiagnosticos_RespetaElLimitePedido()
    {
        var s = new RunStats();
        foreach (var dx in new[] { "a", "b", "c", "d", "e", "f", "g" })
            s.RegistrarVisita(Paciente(dx, true, dx), new DateOnly(2025, 1, 6));

        Assert.Equal(5, s.TopDiagnosticos(5).Count);
    }

    [Fact]
    public void Reset_DejaLasEstadisticasLimpias()
    {
        var s = new RunStats();
        s.RegistrarVisita(Paciente("a", true, "gripe"), new DateOnly(2025, 1, 6));

        s.Reset();

        Assert.Equal(0, s.TotalVisitas);
        Assert.Equal(0, s.PacientesUnicos);
        Assert.Empty(s.PorSemana());
        Assert.Empty(s.TopDiagnosticos(5));
    }

    [Fact]
    public void RegistrarRemision_SeparaEpisodiosNuevosDeControles()
    {
        // El tripwire de L9: las remisiones de episodios nuevos son sanas; una emitida en el control
        // post-alta es el bucle de referencias re-refiriendo el mismo episodio.
        var s = new RunStats();
        s.RegistrarRemision(enControl: false);
        s.RegistrarRemision(enControl: false);
        s.RegistrarRemision(enControl: true);

        Assert.Equal(3, s.RemisionesTotales);
        Assert.Equal(1, s.RemisionesEnControl);

        s.Reset();

        Assert.Equal(0, s.RemisionesTotales);
        Assert.Equal(0, s.RemisionesEnControl);
    }
}
