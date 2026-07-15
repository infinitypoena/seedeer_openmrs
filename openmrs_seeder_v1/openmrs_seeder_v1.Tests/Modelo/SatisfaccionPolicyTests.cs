using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests.Modelo;

public class SatisfaccionPolicyTests
{
    /// <summary>Ajustes sin ruido: la nota queda determinista y se puede afirmar sobre ella.</summary>
    private static SatisfaccionSettings SinRuido() => new() { Desviacion = 0 };

    private static ContextoVisita Visita(
        bool cabecera = false, bool cita = false, bool grave = false, int pacientesDelDia = 0) =>
        new(cabecera, cita, grave, pacientesDelDia);

    // ── Saturación: cuánto se pasa la clínica de lo que puede atender con holgura ──────────────────

    [Theory]
    [InlineData(10, 20, 0.0)]    // va holgada
    [InlineData(20, 20, 0.0)]    // justo en su capacidad cómoda
    [InlineData(30, 20, 0.5)]    // 50 % por encima
    [InlineData(40, 20, 1.0)]    // el doble
    public void Saturacion_MideElExcesoSobreLaCapacidadComoda(int pacientes, int comoda, double esperada)
    {
        Assert.Equal(esperada, SatisfaccionPolicy.Saturacion(pacientes, comoda), precision: 6);
    }

    [Fact]
    public void Saturacion_ConCapacidadNoConfigurada_EsCero()
    {
        Assert.Equal(0, SatisfaccionPolicy.Saturacion(100, capacidadComoda: 0));
    }

    // ── La nota ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VisitaNeutra_SacaLaNotaBase()
    {
        var s = SinRuido();   // MediaBase 4.0
        Assert.Equal(4, SatisfaccionPolicy.Calificar(Visita(), s, new Random(1)));
    }

    [Fact]
    public void LaContinuidadAsistencialSubeLaNota()
    {
        var s = SinRuido();
        s.MediaBase = 4.3;   // 4.3 → 4; con el bonus de cabecera (+0.4) → 4.7 → 5

        Assert.Equal(4, SatisfaccionPolicy.Calificar(Visita(), s, new Random(1)));
        Assert.Equal(5, SatisfaccionPolicy.Calificar(Visita(cabecera: true), s, new Random(1)));
    }

    [Fact]
    public void UnCuadroGraveBajaLaNota()
    {
        var s = SinRuido();
        s.MediaBase = 3.6;                  // 3.6 → 4
        s.PenalizacionCuadroGrave = 0.3;    // 3.3 → 3

        Assert.Equal(4, SatisfaccionPolicy.Calificar(Visita(), s, new Random(1)));
        Assert.Equal(3, SatisfaccionPolicy.Calificar(Visita(grave: true), s, new Random(1)));
    }

    [Fact]
    public void LaClinicaDesbordadaAtiendePeor_YSeNotaEnLaNota()
    {
        // El freno del sistema: si esto no baja la nota, el crecimiento no tiene realimentación negativa.
        var s = SinRuido();   // base 4.0, penalización 1.2 por saturación 1.0, capacidad cómoda 20

        var holgado    = SatisfaccionPolicy.Calificar(Visita(pacientesDelDia: 15), s, new Random(1));
        var justo      = SatisfaccionPolicy.Calificar(Visita(pacientesDelDia: 25), s, new Random(1));
        var desbordado = SatisfaccionPolicy.Calificar(Visita(pacientesDelDia: 40), s, new Random(1));

        Assert.Equal(4, holgado);       // 4.0
        Assert.Equal(4, justo);         // 4.0 − 1.2×0.25 = 3.7 → 4
        Assert.Equal(3, desbordado);    // 4.0 − 1.2×1.00 = 2.8 → 3
        Assert.True(desbordado < holgado);
    }

    [Fact]
    public void LaNotaSiempreCaeEnLaEscala1a5()
    {
        var s = SinRuido();

        s.MediaBase = 5.0;
        s.BonusMedicoCabecera = 3.0;   // se pasaría de 5
        Assert.Equal(5, SatisfaccionPolicy.Calificar(Visita(cabecera: true), s, new Random(1)));

        s = SinRuido();
        s.PenalizacionSaturacion = 20;  // se pasaría por debajo de 1
        Assert.Equal(1, SatisfaccionPolicy.Calificar(Visita(pacientesDelDia: 200), s, new Random(1)));
    }

    [Fact]
    public void ConRuido_LasNotasVarianPeroNuncaSalenDeLaEscala()
    {
        var s = new SatisfaccionSettings();   // MediaBase 4.0, Desviacion 0.8
        var rng = new Random(7);

        var notas = Enumerable.Range(0, 5_000)
            .Select(_ => SatisfaccionPolicy.Calificar(Visita(), s, rng))
            .ToList();

        Assert.All(notas, n => Assert.InRange(n, 1, 5));
        Assert.True(notas.Distinct().Count() > 1, "con ruido no pueden salir todas las notas iguales");
        Assert.Equal(4.0, notas.Average(), precision: 1);   // sin sesgo: la media se conserva
    }

    // ── El umbral de satisfacción ──────────────────────────────────────────────────────────────────

    [Fact]
    public void EsInsatisfecho_EsElPromedio_YElUmbralEsEstricto()
    {
        Assert.False(SatisfaccionPolicy.EsInsatisfecho([5, 4], umbral: 3.0));
        Assert.False(SatisfaccionPolicy.EsInsatisfecho([4, 3], umbral: 3.0));   // media 3.5 > 3
        Assert.True(SatisfaccionPolicy.EsInsatisfecho([3, 3], umbral: 3.0));    // empatar NO basta
        Assert.True(SatisfaccionPolicy.EsInsatisfecho([1, 2], umbral: 3.0));
    }

    [Fact]
    public void SinCalificaciones_NadieNaceInsatisfecho()
    {
        Assert.False(SatisfaccionPolicy.EsInsatisfecho([], umbral: 3.0));
    }

    [Fact]
    public void UnaBuenaRachaRescataAlPacienteDeUnaMalaVisita()
    {
        // El promedio es lo que manda: una visita mala no expulsa a un paciente fiel.
        Assert.False(SatisfaccionPolicy.EsInsatisfecho([5, 5, 1], umbral: 3.0));   // media 3.67
        Assert.True(SatisfaccionPolicy.EsInsatisfecho([2, 2, 1], umbral: 3.0));    // media 1.67
    }

    // ── La fracción satisfecha teórica (solo alimenta la proyección del informe previo) ────────────

    [Fact]
    public void FraccionSatisfechaTeorica_CuadraConLoQueDeVerdadSale()
    {
        var s = new SatisfaccionSettings();   // base 4.0, σ 0.8, umbral 3
        var teorica = SatisfaccionPolicy.FraccionSatisfechaTeorica(s);

        var rng = new Random(11);
        var real = Enumerable.Range(0, 20_000)
            .Count(_ => SatisfaccionPolicy.Calificar(Visita(), s, rng) > s.UmbralSatisfaccion) / 20_000.0;

        Assert.InRange(teorica, real - 0.03, real + 0.03);
    }

    [Fact]
    public void FraccionSatisfechaTeorica_SinRuido_EsTodoONada()
    {
        Assert.Equal(1.0, SatisfaccionPolicy.FraccionSatisfechaTeorica(
            new SatisfaccionSettings { Desviacion = 0, MediaBase = 4.0, UmbralSatisfaccion = 3.0 }));
        Assert.Equal(0.0, SatisfaccionPolicy.FraccionSatisfechaTeorica(
            new SatisfaccionSettings { Desviacion = 0, MediaBase = 2.0, UmbralSatisfaccion = 3.0 }));
    }

    [Fact]
    public void FraccionSatisfechaTeorica_CuentaConLaSaturacion()
    {
        // Si no descontara la saturación, la proyección creería que la clínica desbordada deja contenta a
        // tanta gente como la holgada — la calibración apuntaría demasiado alto y la corrida real
        // aterrizaría por debajo del objetivo.
        var s = new SatisfaccionSettings();

        var holgada    = SatisfaccionPolicy.FraccionSatisfechaTeorica(s, saturacion: 0);
        var apretada   = SatisfaccionPolicy.FraccionSatisfechaTeorica(s, saturacion: 0.5);
        var desbordada = SatisfaccionPolicy.FraccionSatisfechaTeorica(s, saturacion: 1.0);

        Assert.True(apretada < holgada);
        Assert.True(desbordada < apretada);
        Assert.All(new[] { holgada, apretada, desbordada }, f => Assert.InRange(f, 0, 1));
    }

    [Fact]
    public void FraccionSatisfechaTeorica_ConcuerdaConLasNotasQueDeVerdadSalenBajoSaturacion()
    {
        var s = new SatisfaccionSettings();
        const double sat = 0.6;
        // La clínica atiende un 60 % por encima de su capacidad cómoda.
        var pacientes = (int)Math.Round(s.CapacidadComodaPorDia * (1 + sat));

        var teorica = SatisfaccionPolicy.FraccionSatisfechaTeorica(s, sat);

        var rng = new Random(31);
        var real = Enumerable.Range(0, 20_000)
            .Count(_ => SatisfaccionPolicy.Calificar(Visita(pacientesDelDia: pacientes), s, rng)
                        > s.UmbralSatisfaccion) / 20_000.0;

        Assert.InRange(teorica, real - 0.03, real + 0.03);
    }
}
