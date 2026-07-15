using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests.Modelo;

public class BassGrowthModelTests
{
    private static readonly DateOnly Hoy = new(2024, 6, 10);

    /// <summary>
    /// Ajustes con el boca a boca y la ventana FIJADOS, no heredados de los defaults: estos tests afirman
    /// sobre números concretos (1/25, 180 días) y no deben romperse cuando se recalibre la config.
    /// </summary>
    private static CrecimientoSettings Cr() => new()
    {
        PoblacionCaptacion          = 20000,
        RecurrentesPorPacienteExtra = 25,    // override manual: q = 0,04
        VentanaActividadDias        = 180
    };

    /// <summary>Paciente del pool con N visitas, nota fija y su última visita hace X días.</summary>
    private static SimulatedPatient P(int visitas, int nota, int diasDesdeUltimaVisita)
    {
        var p = new SimulatedPatient
        {
            OpenMrsUuid    = Guid.NewGuid().ToString(),
            Visitas        = visitas,
            Calificaciones = Enumerable.Repeat(nota, visitas).ToList(),
            UltimaVisita   = Hoy.AddDays(-diasDesdeUltimaVisita)
        };
        p.Insatisfecho = SatisfaccionPolicy.EsInsatisfecho(p.Calificaciones, umbral: 3.0);
        return p;
    }

    // ── p: el coeficiente de innovación se DERIVA del volumen de arranque ──────────────────────────

    [Fact]
    public void ConPoolVacio_LambdaEsExactamenteElVolumenDeArranque()
    {
        // La propiedad que hace que activar el crecimiento no cambie el punto de partida: con S=0 y A=0
        // (y por tanto SIN ningún retorno posible), λ = p·M = los pacientes del día base — todos altas.
        var p = BassGrowthModel.CoeficienteInnovacion(pacientesPorDiaMedio: 15, poblacionCaptacion: 20000);

        var lambda = BassGrowthModel.Lambda(p, q: 0.04, poblacionCaptacion: 20000, activos: 0, satisfechos: 0);

        Assert.Equal(15.0, lambda, precision: 6);
    }

    // ── k: cuánta consulta genera el panel POR SÍ SOLO ─────────────────────────────────────────────

    [Fact]
    public void ElPanelGeneraSuPropiaConsulta_YEsoFijaLaFraccionDeRecurrentes()
    {
        // Es la magnitud que el modelo viejo IGNORABA: derivaba el total de las altas y le imponía un 30 %
        // de recurrentes, cuando en realidad la clínica cita a ~2 de cada 3 pacientes.
        var sim = SimObjetivo(25);

        var k = BassGrowthModel.ProbabilidadDeRetorno(sim);

        // Con FollowUp 0,30 / crónico 0,90 / grave 0,80 y asistencia 0,75, más el retorno espontáneo.
        Assert.InRange(k, 0.55, 0.85);
        // Cada paciente vuelve varias veces: NO es un ticket de una sola visita.
        Assert.True(BassGrowthModel.VisitasPorPaciente(k) > 2.0);
    }

    [Fact]
    public void SinCitasNiRetornoEspontaneo_ElPacienteNoVuelveNunca()
    {
        var sim = SimObjetivo(25);
        sim.ReferralProbabilities.FollowUp        = 0;
        sim.ReferralProbabilities.FollowUpCronico = 0;
        sim.ReferralProbabilities.FollowUpGrave   = 0;
        sim.Recurrence.VisitasEspontaneasPorPacienteAno = 0;

        Assert.Equal(0, BassGrowthModel.ProbabilidadDeRetorno(sim), precision: 6);
        Assert.Equal(1.0, BassGrowthModel.VisitasPorPaciente(0), precision: 6);
    }

    [Fact]
    public void MasRetornosEspontaneos_MasVisitasPorPaciente()
    {
        double V(double porAnio)
        {
            var sim = SimObjetivo(25);
            sim.Recurrence.VisitasEspontaneasPorPacienteAno = porAnio;
            return BassGrowthModel.VisitasPorPaciente(BassGrowthModel.ProbabilidadDeRetorno(sim));
        }

        Assert.True(V(0.0) < V(0.5));
        Assert.True(V(0.5) < V(2.0));
    }

    // ── q: la regla "+1 alta/día por cada 25 recurrentes satisfechos" ──────────────────────────────

    [Fact]
    public void ElBocaABoca_EsMasUnaAltaDiaPorCadaVeinticincoSatisfechos()
    {
        const int m = 20000;
        var p = BassGrowthModel.CoeficienteInnovacion(15, m);
        var q = BassGrowthModel.CoeficienteImitacion(Cr());   // 1/25 = 0.04

        // Con la clientela aún pequeña (A ≪ M), 25 satisfechos suman ~1 alta al día sobre el suelo.
        var sinSatisfechos = BassGrowthModel.Lambda(p, q, m, activos: 0, satisfechos: 0);
        var con25          = BassGrowthModel.Lambda(p, q, m, activos: 0, satisfechos: 25);
        var con50          = BassGrowthModel.Lambda(p, q, m, activos: 0, satisfechos: 50);

        Assert.Equal(1.0, con25 - sinSatisfechos, precision: 6);
        Assert.Equal(2.0, con50 - sinSatisfechos, precision: 6);
    }

    [Fact]
    public void CoeficienteImitacion_EsElInversoDeLosRecurrentesPorPacienteExtra()
    {
        Assert.Equal(0.04, BassGrowthModel.CoeficienteImitacion(new CrecimientoSettings { RecurrentesPorPacienteExtra = 25 }), precision: 6);
        Assert.Equal(0.20, BassGrowthModel.CoeficienteImitacion(new CrecimientoSettings { RecurrentesPorPacienteExtra = 5 }), precision: 6);
    }

    // ── (M − A): el techo de mercado, el freno que evita la explosión ──────────────────────────────

    [Fact]
    public void ConTodoElAreaYaEnLaClinica_NoLleganPacientesNuevos()
    {
        const int m = 1000;
        var p = BassGrowthModel.CoeficienteInnovacion(15, m);
        var q = BassGrowthModel.CoeficienteImitacion(Cr());

        // Todo el barrio es ya paciente activo: por muchos satisfechos que haya, no queda a quién captar.
        Assert.Equal(0, BassGrowthModel.Lambda(p, q, m, activos: m, satisfechos: 500), precision: 6);
        // Y no se vuelve negativo si la clientela supera al área (config inconsistente).
        Assert.Equal(0, BassGrowthModel.Lambda(p, q, m, activos: m + 500, satisfechos: 500), precision: 6);
    }

    [Fact]
    public void LambdaDecrece_ConformeElAreaSeVuelveClientela()
    {
        const int m = 1000;
        var p = BassGrowthModel.CoeficienteInnovacion(15, m);
        var q = BassGrowthModel.CoeficienteImitacion(Cr());

        var alPrincipio = BassGrowthModel.Lambda(p, q, m, activos: 0,   satisfechos: 100);
        var aMedias     = BassGrowthModel.Lambda(p, q, m, activos: 500, satisfechos: 100);
        var casiAlFinal = BassGrowthModel.Lambda(p, q, m, activos: 900, satisfechos: 100);

        Assert.True(alPrincipio > aMedias);
        Assert.True(aMedias > casiAlFinal);
    }

    // ── A(d): la clientela ACTUAL, no el histórico ─────────────────────────────────────────────────

    [Fact]
    public void ElQueYaNoVieneVuelveAlMercado_YNoCuentaComoClientela()
    {
        // Esto es lo que impide que el área "se agote" y la clínica acabe vaciándose: un paciente que
        // lleva más de la ventana sin aparecer ya no es suyo, hay que volver a captarlo.
        var cr = Cr();   // ventana 180 d
        var pool = new List<SimulatedPatient>
        {
            P(visitas: 1, nota: 5, diasDesdeUltimaVisita: 0),     // ✓ vino hoy
            P(visitas: 3, nota: 2, diasDesdeUltimaVisita: 100),   // ✓ activo aunque esté descontento
            P(visitas: 2, nota: 5, diasDesdeUltimaVisita: 180),   // ✓ justo en el límite
            P(visitas: 5, nota: 5, diasDesdeUltimaVisita: 181),   // ✗ se fue: vuelve al mercado
            new() { OpenMrsUuid = "sin-visitas" },                // ✗ nunca llegó a ser atendido
        };

        Assert.Equal(3, BassGrowthModel.PacientesActivos(pool, Hoy, cr));
    }

    // ── S(d): quién cuenta como recurrente activo y satisfecho ─────────────────────────────────────

    [Fact]
    public void SoloCuentanLosQueVolvieron_QuedaronContentos_YSiguenViniendo()
    {
        var cr = Cr();   // MinVisitasRecurrente = 2, ventana 180 d, umbral 3
        var pool = new List<SimulatedPatient>
        {
            P(visitas: 3, nota: 5, diasDesdeUltimaVisita: 10),    // ✓ recurrente, contento, activo
            P(visitas: 2, nota: 4, diasDesdeUltimaVisita: 179),   // ✓ justo dentro de la ventana
            P(visitas: 1, nota: 5, diasDesdeUltimaVisita: 5),     // ✗ aún no ha vuelto (1 visita)
            P(visitas: 4, nota: 2, diasDesdeUltimaVisita: 5),     // ✗ descontento
            P(visitas: 3, nota: 5, diasDesdeUltimaVisita: 181),   // ✗ se alejó (fuera de la ventana)
            new() { OpenMrsUuid = "sin-visitas" },                // ✗ ni siquiera tiene última visita
        };

        Assert.Equal(2, BassGrowthModel.RecurrentesActivosSatisfechos(pool, Hoy, cr));
    }

    [Fact]
    public void CuentaLasVisitasDeVerdad_NoLasCalificaciones()
    {
        // Regresión: se contaban las Calificaciones, que SOLO existen con la satisfacción activa. Con
        // Satisfaccion.Enabled=false todo el mundo parecía tener 0 visitas y S(d) era siempre 0 → el boca
        // a boca no arrancaba nunca y la feature quedaba muerta en silencio.
        var cr = Cr();
        var sinNotas = new SimulatedPatient
        {
            OpenMrsUuid  = "x",
            Visitas      = 3,          // volvió dos veces…
            Calificaciones = [],       // …pero nadie le pidió opinión (satisfacción apagada)
            UltimaVisita = Hoy
        };

        Assert.Equal(1, BassGrowthModel.RecurrentesActivosSatisfechos([sinNotas], Hoy, cr));
    }

    [Fact]
    public void ElPacienteJustoEnElUmbral_NoEstaSatisfecho()
    {
        // El umbral es estricto: hay que SUPERAR el 3, no empatarlo.
        var cr = Cr();
        var pool = new List<SimulatedPatient> { P(visitas: 2, nota: 3, diasDesdeUltimaVisita: 1) };

        Assert.Equal(0, BassGrowthModel.RecurrentesActivosSatisfechos(pool, Hoy, cr));
    }

    // ── Las altas del día: Bass gobierna la CAPTACIÓN y nada más ───────────────────────────────────

    [Fact]
    public void LasAltasSalenDeLambda_YNoArrastranNingunTotal()
    {
        // La invariante del modelo nuevo: NuevosDelDia devuelve SOLO las altas. El volumen del día es su
        // suma con los retornos, que los pone el panel — nunca un cociente sobre las altas.
        var rng = new Random(1);
        var muestras = Enumerable.Range(0, 5000)
            .Select(_ => BassGrowthModel.NuevosDelDia(lambda: 10.0, pesoDia: 1.2, rng))
            .ToList();

        Assert.All(muestras, n => Assert.True(n >= 0));
        Assert.Equal(12.0, muestras.Average(), precision: 0);   // λ × peso
    }

    [Fact]
    public void DiaCerrado_NoAtiendeANadie()
    {
        Assert.Equal(0, BassGrowthModel.NuevosDelDia(10, pesoDia: 0, new Random(3)));
        Assert.Equal(0, BassGrowthModel.NuevosDelDia(lambda: 0, 1.0, new Random(4)));
    }

    // ── El aforo: la consulta llena deja de CAPTAR ─────────────────────────────────────────────────

    [Fact]
    public void ElAforoEsElObjetivo_EscaladoPorElPesoDelDia()
    {
        var sim = SimObjetivo(25);

        // El aforo se escala por peso/pesoMedio (0,967 con los pesos por defecto), no por el peso a secas.
        Assert.Equal(31, BassGrowthModel.CapacidadDelDia(sim, pesoDia: 1.2));   // lunes: cabe más gente
        Assert.Equal(26, BassGrowthModel.CapacidadDelDia(sim, pesoDia: 1.0));
        Assert.Equal(13, BassGrowthModel.CapacidadDelDia(sim, pesoDia: 0.5));   // sábado: media jornada
        Assert.Equal(0,  BassGrowthModel.CapacidadDelDia(sim, pesoDia: 0));     // domingo: cerrado
    }

    [Fact]
    public void ElAforoMedioDeUnaSemana_CaeExactamenteEnElObjetivo()
    {
        // Lo que le da sentido al mando: "25 pacientes/día" tiene que significar 25 de media en los días
        // que la clínica abre. Sin dividir por el peso medio se quedaría en 24,2 — y el objetivo no diría
        // lo que dice (ni la ley L8 podría juzgarlo).
        var sim = SimObjetivo(25);

        var aforos = Enum.GetValues<DayOfWeek>()
            .Select(d => DailyScheduleGenerator.PesoDelDia(d, sim.WeekdayWeights))
            .Where(peso => peso > 0)
            .Select(peso => BassGrowthModel.CapacidadDelDia(sim, peso))
            .ToList();

        Assert.Equal(6, aforos.Count);                       // seis días abiertos
        Assert.Equal(25, aforos.Average(), precision: 0);
    }

    [Fact]
    public void ElAforoNuncaPasaDelTechoDeSeguridad()
    {
        var sim = SimObjetivo(100);
        sim.Crecimiento.PacientesPorDiaMax = 45;

        Assert.Equal(45, BassGrowthModel.CapacidadDelDia(sim, pesoDia: 1.0));
    }

    // ── Poisson (el proceso de llegadas) ───────────────────────────────────────────────────────────

    [Fact]
    public void Poisson_TieneMediaLambda()
    {
        var rng = new Random(123);
        const double lambda = 10.5;

        var muestras = Enumerable.Range(0, 20_000).Select(_ => BassGrowthModel.Poisson(lambda, rng)).ToList();

        Assert.All(muestras, n => Assert.True(n >= 0));
        Assert.Equal(lambda, muestras.Average(), precision: 1);
    }

    [Fact]
    public void Poisson_ConLambdaNoPositiva_EsCero()
    {
        Assert.Equal(0, BassGrowthModel.Poisson(0, new Random(1)));
        Assert.Equal(0, BassGrowthModel.Poisson(-5, new Random(1)));
    }

    // ── La proyección determinista ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Proyeccion_DibujaUnaCurvaQueCreceYSeFrena()
    {
        var sim = Sim(poblacion: 60000, recurrentesPorExtra: 10);
        var plan = new DailyScheduleGenerator(sim).Generate();

        var curva = BassGrowthModel.Proyectar(plan, sim);
        var abiertos = curva.Where(d => d.Total > 0).ToList();

        Assert.NotEmpty(abiertos);

        // El histórico de pacientes captados solo puede crecer.
        for (var i = 1; i < curva.Count; i++)
            Assert.True(curva[i].CaptadosTotal >= curva[i - 1].CaptadosTotal);

        // La clientela ACTIVA nunca puede pasar del área de influencia (no hay más gente en el barrio).
        Assert.All(curva, d => Assert.True(d.Activos <= sim.Crecimiento.PoblacionCaptacion));

        // La clínica crece: en algún momento atiende bastante más que el primer día.
        Assert.True(abiertos.Max(d => d.Total) > abiertos[0].Total);
    }

    [Fact]
    public void Proyeccion_ElPanelMadura_LaFraccionDeRecurrentesSube()
    {
        // ⚠️ LA regresión de este arreglo. Antes el mix era una CONSTANTE por construcción
        // (total = altas / (1 − 30 %)) y la corrida de 3,5 años salió con 31 % de recurrentes el primer
        // mes y 31 % el último. Una clínica con tres años de historia no puede tener el mismo mix que el
        // día que abrió: su panel le genera consulta.
        var sim  = SimObjetivo(25);
        var plan = new DailyScheduleGenerator(sim).Generate();

        var curva = BassGrowthModel.Proyectar(plan, sim).Where(d => d.Total > 0).ToList();

        double MixDe(IEnumerable<ProyeccionDia> dias)
        {
            var d = dias.ToList();
            var total = d.Sum(x => x.Total);
            return total == 0 ? 0 : (double)d.Sum(x => x.Recurrentes) / total;
        }

        var primerTrimestre  = MixDe(curva.Take(75));
        var ultimoTrimestre  = MixDe(curva.TakeLast(75));

        Assert.True(primerTrimestre < 0.35,
            $"el primer trimestre ya viene lleno de recurrentes ({primerTrimestre:P0}): el panel no existe aún");
        Assert.True(ultimoTrimestre > 0.55,
            $"el panel no madura: {primerTrimestre:P0} → {ultimoTrimestre:P0} de recurrentes");
        Assert.True(ultimoTrimestre > primerTrimestre * 1.5);
    }

    [Fact]
    public void Proyeccion_LaClinicaNoSeVaciaAunqueSeLlenaraElArea()
    {
        // Regresión: con A(d) = captados desde siempre, el área "se agotaba", λ caía a cero y la clínica se
        // quedaba sin pacientes, perdiendo hasta a sus crónicos. Con A(d) = clientela activa, el sistema
        // llega a un equilibrio y se mantiene.
        var sim = Sim(poblacion: 800, recurrentesPorExtra: 5);   // área diminuta: se llenaría enseguida
        var plan = new DailyScheduleGenerator(sim).Generate();

        var curva = BassGrowthModel.Proyectar(plan, sim);
        var abiertos = curva.Where(d => d.Total > 0).ToList();

        // La clínica sigue atendiendo al final de la ventana.
        var ultimoMes = abiertos.TakeLast(25).ToList();
        Assert.All(ultimoMes, d => Assert.True(d.Total > 0, $"la clínica se vació el {d.Fecha}"));
    }

    [Fact]
    public void Proyeccion_ConAforo_ElRecorteCaeSobreLasAltas_NoSobreLosRetornos()
    {
        // La ley L3, en la proyección: la consulta llena deja de CAPTAR, jamás abandona a quien tenía cita.
        var sim  = SimObjetivo(25);
        var plan = new DailyScheduleGenerator(sim).Generate();

        var curva = BassGrowthModel.Proyectar(plan, sim, q: 0.5, aplicarAforo: true)   // q absurda a propósito
            .Where(d => d.Total > 0)
            .ToList();

        foreach (var d in curva)
        {
            var peso  = DailyScheduleGenerator.PesoDelDia(d.Fecha.DayOfWeek, sim.WeekdayWeights);
            var aforo = BassGrowthModel.CapacidadDelDia(sim, peso);
            if (d.Total <= aforo) continue;

            // Un día PUEDE pasarse del aforo — pero solo por sus retornos: al paciente le toca su control
            // el sábado (medio aforo) igual que el lunes, y no se le manda a casa. Lo que no puede pasar
            // NUNCA es que ese día se sigan captando altas.
            Assert.Equal(0, d.Nuevos);
            Assert.True(d.Recurrentes > aforo,
                $"el {d.Fecha} se pasó del aforo ({d.Total} > {aforo}) sin que fueran los retornos");
        }

        // Y con la clínica llena, las altas se estrangulan: es el freno del crecimiento, medido.
        var alFinal = curva.TakeLast(50).ToList();
        Assert.True(alFinal.Average(d => d.Nuevos) < alFinal.Average(d => d.Recurrentes));
    }

    private static SimulationSettings Sim(int poblacion, int recurrentesPorExtra)
    {
        var sim = new SimulationSettings
        {
            StartDate = new DateTime(2023, 1, 1),
            EndDate   = new DateTime(2024, 12, 31),
            PacientesPorDiaMedio = 15
        };
        sim.Crecimiento.PoblacionCaptacion          = poblacion;
        sim.Crecimiento.RecurrentesPorPacienteExtra = recurrentesPorExtra;   // override manual
        sim.Crecimiento.PacientesPorDiaObjetivo     = 40;
        sim.Crecimiento.PacientesPorDiaMax          = 80;
        return sim;
    }

    // ── Calibración: el boca a boca se DERIVA del destino ──────────────────────────────────────────

    /// <summary>Clínica de 3 años que arranca en 6 altas/día — el escenario real.</summary>
    private static SimulationSettings SimObjetivo(int objetivo)
    {
        var sim = new SimulationSettings
        {
            StartDate = new DateTime(2023, 1, 1),
            EndDate   = new DateTime(2025, 12, 31),
            PacientesPorDiaMedio = 6
        };
        sim.Crecimiento.PacientesPorDiaObjetivo     = objetivo;
        sim.Crecimiento.RecurrentesPorPacienteExtra = 0;   // 0 = derivar del objetivo
        sim.Crecimiento.PoblacionCaptacion          = 60000;
        sim.Crecimiento.PacientesPorDiaMax          = Math.Max(45, (int)(objetivo * 1.8));
        sim.Satisfaccion.CapacidadComodaPorDia      = objetivo;
        return sim;
    }

    [Theory]
    [InlineData(25)]
    [InlineData(35)]
    [InlineData(50)]
    public void LaClinicaAterrizaEnElObjetivoQueSeLePide(int objetivo)
    {
        var sim  = SimObjetivo(objetivo);
        var plan = new DailyScheduleGenerator(sim).Generate();

        var q      = BassGrowthModel.CalibrarImitacion(plan, sim);
        var meseta = BassGrowthModel.MesetaProyectada(plan, sim, q);

        Assert.InRange(meseta, objetivo * 0.95, objetivo * 1.05);
    }

    [Fact]
    public void ObjetivoPorDebajoDeLoQueElPanelProduceSolo_NoNecesitaBocaABoca()
    {
        // El "suelo" ya no es PacientesPorDiaMedio: el panel devuelve a cada paciente varias veces, así que
        // 6 altas/día ya producen ~6 × 1/(1−k) visitas/día sin ninguna recomendación. Pedir menos que eso
        // no es un objetivo de crecimiento.
        var sim  = SimObjetivo(objetivo: 8);
        var plan = new DailyScheduleGenerator(sim).Generate();

        Assert.Equal(0, BassGrowthModel.CalibrarImitacion(plan, sim));
        Assert.True(BassGrowthModel.MesetaProyectada(plan, sim, 0) >= 8);
    }

    [Fact]
    public void CuantoMasAltoElObjetivo_MasFuerteElBocaABoca()
    {
        double Q(int objetivo)
        {
            var sim = SimObjetivo(objetivo);
            return BassGrowthModel.CalibrarImitacion(new DailyScheduleGenerator(sim).Generate(), sim);
        }

        Assert.True(Q(25) < Q(40));
        Assert.True(Q(40) < Q(60));
    }

    [Fact]
    public void LaCurvaArrancaEnElVolumenBase_CreceYSeEstabiliza()
    {
        // La forma que se le pide al modelo: arranque lento, rampa sostenida, meseta al final.
        var sim   = SimObjetivo(25);
        var plan  = new DailyScheduleGenerator(sim).Generate();
        var curva = BassGrowthModel.Proyectar(plan, sim, BassGrowthModel.CalibrarImitacion(plan, sim),
            aplicarAforo: false);

        double MediaEnElMes(int mes)
        {
            var desde = new DateOnly(2023, 1, 1).AddMonths(mes);
            var dias  = curva.Where(d => d.Fecha >= desde && d.Fecha < desde.AddMonths(1) && d.Total > 0).ToList();
            return dias.Count == 0 ? 0 : dias.Average(d => d.Total);
        }

        // Crece de forma monótona a lo largo de los 3 años…
        Assert.True(MediaEnElMes(6)  > MediaEnElMes(0));
        Assert.True(MediaEnElMes(12) > MediaEnElMes(6));
        Assert.True(MediaEnElMes(24) > MediaEnElMes(12));

        // …y acaba cerca del objetivo.
        Assert.InRange(MediaEnElMes(35), 25 * 0.80, 25 * 1.10);

        // Desacelerando: en el último año sube menos de lo que subió en el primero — la curva se aplana,
        // que es lo que se le pide (crecimiento y luego estabilidad).
        var primerAnio = MediaEnElMes(12) - MediaEnElMes(0);
        var ultimoAnio = MediaEnElMes(35) - MediaEnElMes(23);
        Assert.True(ultimoAnio < primerAnio,
            $"la curva no se aplana: primer año +{primerAnio:0.0}, último +{ultimoAnio:0.0}");
    }

    [Fact]
    public void ObjetivoInalcanzable_NoCuelga_YSeQuedaCorto()
    {
        // Área de 300 personas: por mucho boca a boca que haya, no hay a quién captar para llegar a 60/día.
        var sim = SimObjetivo(objetivo: 60);
        sim.Crecimiento.PoblacionCaptacion = 300;
        var plan = new DailyScheduleGenerator(sim).Generate();

        var q      = BassGrowthModel.CalibrarImitacion(plan, sim);
        var meseta = BassGrowthModel.MesetaProyectada(plan, sim, q);

        Assert.True(q > 0);                                              // lo intentó con todo
        Assert.True(meseta < sim.Crecimiento.PacientesPorDiaObjetivo);   // y aun así se queda corta
    }

    [Fact]
    public void ElOverrideManualGanaSobreLaCalibracion()
    {
        var sim = SimObjetivo(objetivo: 25);
        sim.Crecimiento.RecurrentesPorPacienteExtra = 20;   // fijado a mano
        var plan = new DailyScheduleGenerator(sim).Generate();

        Assert.Equal(0.05, BassGrowthModel.CoeficienteImitacion(plan, sim), precision: 6);
    }

    [Fact]
    public void SinOverride_ElCoeficienteSaleDeLaCalibracion()
    {
        var sim  = SimObjetivo(objetivo: 25);
        var plan = new DailyScheduleGenerator(sim).Generate();

        Assert.Equal(
            BassGrowthModel.CalibrarImitacion(plan, sim),
            BassGrowthModel.CoeficienteImitacion(plan, sim),
            precision: 10);
    }
}
