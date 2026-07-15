using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Services;

/// <summary>Un día de la proyección determinista del crecimiento (informe previo, etapa 2/5).</summary>
/// <param name="Activos">Pacientes que la clínica tiene ahora mismo (visitaron dentro de la ventana).</param>
/// <param name="CaptadosTotal">Pacientes distintos que han pasado por la clínica alguna vez.</param>
public readonly record struct ProyeccionDia(
    DateOnly Fecha,
    double Lambda,
    int Total,
    int Nuevos,
    int Recurrentes,
    int Activos,
    int CaptadosTotal,
    int Satisfechos);

/// <summary>
/// Crecimiento de la clínica por <b>difusión de Bass</b> (1969). Bass gobierna <b>la captación</b> —
/// cuántos pacientes NUEVOS llegan cada día— y <b>nada más</b>:
///
/// <code>
/// λ(d) = [ p + q · S(d)/M ] · ( M − A(d) )        pacientes NUEVOS / día
/// visitas(d) = λ(d) · peso(d)  +  retornos(d)     ← una SUMA
/// </code>
///
/// <para>⚠️ <b>El error que esto corrige, y es el más caro del proyecto.</b> El volumen del día se
/// derivaba de las altas: <c>total = nuevos / (1 − PorcentajeRecurrentes)</c>, y los recurrentes eran el
/// residuo — un cupo fijo del 30 %, el mes 1 y el mes 42. La demanda real del panel (qué crónicos tocaba
/// controlar hoy, qué citas vencían hoy) <b>no entraba en la ecuación</b>. Medido en la corrida de 3,5
/// años: 14.025 citas Missed contra 14.115 Completed, el 65 % de los crónicos sin volver jamás a un
/// control, 1,45 visitas por paciente y el mix de recurrentes clavado en el 31 % de principio a fin.
/// Ahora los retornos los cuenta <see cref="RecurrentSelector"/> sobre el pool de verdad y el total es su
/// suma con las altas: la fracción de recurrentes <b>emerge y crece con el panel</b>, como en una clínica
/// que madura.</para>
///
/// <list type="bullet">
/// <item><b>p · M</b> — los que llegan solos, sin que nadie se la recomiende. Es el suelo, y <c>p</c> se
/// <b>deriva</b> para que el día 0 (pool vacío, todo el mundo es nuevo) la clínica atienda exactamente sus
/// <c>PacientesPorDiaMedio</c> de siempre. Ver <see cref="CoeficienteInnovacion"/>.</item>
/// <item><b>q · S(d)</b> — el boca a boca. <c>q</c> tampoco se configura: se <b>deriva del objetivo</b> por
/// bisección (<see cref="CalibrarImitacion"/>), porque es un coeficiente de un lazo de realimentación
/// positiva y nadie puede apuntar con él a ojo.</item>
/// <item><b>(M − A(d))</b> — el techo de mercado. <c>A(d)</c> son los pacientes <b>ACTIVOS</b>
/// (<see cref="PacientesActivos"/>), no los captados desde siempre: quien lleva un año sin venir vuelve al
/// mercado. Con el conteo acumulado el área "se agotaba" y la clínica se vaciaba.</item>
/// </list>
///
/// El otro freno vive en <see cref="SatisfaccionPolicy"/>: la clínica saturada atiende peor, y peor
/// atención reduce S(d). Y el tercero, el más directo, es el <b>aforo</b>
/// (<see cref="CapacidadDelDia"/>): una consulta llena deja de captar gente nueva.
///
/// Clase estática pura (RNG inyectado, sin red ni reloj) → testeable de forma determinista.
/// </summary>
public static class BassGrowthModel
{
    /// <summary>
    /// <c>p</c> — coeficiente de innovación. No se configura: se deriva de la media diaria de arranque, de
    /// modo que con el pool vacío (S=0, A=0, y por tanto <b>sin ningún retorno posible</b>) la fórmula
    /// devuelve <c>p·M = PacientesPorDiaMedio</c>. Activar el crecimiento no cambia el punto de partida,
    /// solo su evolución.
    ///
    /// <para>Antes se multiplicaba por <c>(1 − PorcentajeRecurrentes/100)</c>, lo cual era ruido: el día 0
    /// no existe ningún paciente al que pueda tocarle un control, así que los pacientes del arranque son
    /// <b>todos</b> nuevos por definición.</para>
    /// </summary>
    public static double CoeficienteInnovacion(int pacientesPorDiaMedio, int poblacionCaptacion)
    {
        if (poblacionCaptacion <= 0) return 0;
        return Math.Max(0, pacientesPorDiaMedio) / (double)poblacionCaptacion;
    }

    /// <summary>
    /// <c>q</c> puesto <b>a mano</b> (override avanzado): el inverso de <c>RecurrentesPorPacienteExtra</c>
    /// (25 → q = 0,04 ≈ un paciente nuevo al día por cada 25 satisfechos). Devuelve <b>0 = sin fijar</b>,
    /// que es el modo normal: entonces `q` lo deriva <see cref="CalibrarImitacion"/> del objetivo.
    /// </summary>
    public static double CoeficienteImitacion(CrecimientoSettings cr) =>
        cr.RecurrentesPorPacienteExtra < 1 ? 0 : 1.0 / cr.RecurrentesPorPacienteExtra;

    /// <summary>
    /// El <c>q</c> que se va a usar en esta corrida: el manual si está fijado, si no el derivado del
    /// objetivo. Determinista → el informe previo y la corrida obtienen el mismo valor llamándolo por
    /// separado.
    /// </summary>
    public static double CoeficienteImitacion(IReadOnlyList<DailySchedule> plan, SimulationSettings sim)
    {
        var manual = CoeficienteImitacion(sim.Crecimiento);
        return manual > 0 ? manual : CalibrarImitacion(plan, sim);
    }

    // ── Cuánta consulta genera el panel por sí solo ────────────────────────────────────────────────

    /// <summary>
    /// <c>k</c> — probabilidad de que una visita genere <b>otra visita del mismo paciente</b>. Es lo que
    /// gobierna cuánta consulta produce el panel por su cuenta: un paciente hace <c>1/(1−k)</c> visitas en
    /// toda su relación con la clínica (<see cref="VisitasPorPaciente"/>) y, en régimen, la fracción de
    /// visitas recurrentes tiende a <c>k</c>.
    ///
    /// <para>Las dos vías son las mismas que aplica <see cref="RecurrentSelector.Seleccionar"/>: la
    /// <b>cita de control</b> (se agenda con probabilidad <c>pCita</c> y se acude con
    /// <c>AsistenciaProb</c>) y el <b>retorno espontáneo</b> (que ocurre si al paciente le pasa algo nuevo
    /// antes de alejarse de la clínica).</para>
    ///
    /// <para>⚠️ Es una <b>estimación para la proyección previa</b>: cuántas visitas serán crónicas o graves
    /// no se sabe sin correr la simulación, así que <c>pCita</c> se toma como la media de las tres bandas
    /// de seguimiento. Contraste con la corrida real de 3,5 años: 29.561 citas sobre 45.828 visitas =
    /// <b>64,5 %</b>, contra el <b>66,7 %</b> que da esta media. Quien mide de verdad es
    /// <see cref="Invariantes"/>, sobre lo que pasó.</para>
    /// </summary>
    public static double ProbabilidadDeRetorno(SimulationSettings sim)
    {
        var rp = sim.ReferralProbabilities;
        var pCita   = (rp.FollowUp + rp.FollowUpCronico + rp.FollowUpGrave) / 3.0;
        var porCita = Math.Clamp(pCita, 0, 1) * Math.Clamp(sim.Appointments.AsistenciaProb, 0, 1);

        // Antes de alejarse de la clínica, al paciente le quedan `VentanaActividadDias` para que le pase
        // algo nuevo y vuelva por su cuenta.
        var rDiaria = RecurrentSelector.ProbRetornoEspontaneoDiaria(sim.Recurrence);
        var porEspontaneo = 1 - Math.Pow(1 - rDiaria, Math.Max(0, sim.Crecimiento.VentanaActividadDias));

        // Las dos vías no se solapan: si le citaron y acudió, ya volvió.
        return Math.Clamp(porCita + (1 - porCita) * porEspontaneo, 0, 0.95);
    }

    /// <summary>Visitas que hace un paciente en toda su relación con la clínica: <c>1/(1−k)</c>.</summary>
    public static double VisitasPorPaciente(double k) => 1.0 / Math.Max(0.05, 1 - k);

    /// <summary>Intervalo medio entre dos visitas del mismo paciente (mezcla de las bandas aguda y crónica).</summary>
    public static double IntervaloMedioDias(RecurrenceSettings re) =>
        Math.Max(1.0, (re.MinDiasAgudo + re.MaxDiasAgudo + re.MinDiasCronico + re.MaxDiasCronico) / 4.0);

    /// <summary>
    /// <b>El aforo de la consulta</b> ese día. Con el crecimiento activo la clínica trabaja a su
    /// <c>PacientesPorDiaObjetivo</c>: el objetivo <b>es</b> su capacidad de trabajo, no una casualidad
    /// (por eso <c>Satisfaccion.CapacidadComodaPorDia</c> vale lo mismo).
    /// <c>PacientesPorDiaMax</c> queda de red de seguridad por encima.
    ///
    /// <para>El peso del día lo escala <b>relativo al peso medio de apertura</b>: en un lunes (1,2) cabe más
    /// gente que en un sábado (0,5), pero la media a lo largo de la semana cae <b>exactamente</b> en el
    /// objetivo. Dividir por el peso medio no es cosmético: sin él, "25/día" acabaría siendo 24,2 de media
    /// y el objetivo no significaría lo que dice.</para>
    ///
    /// <para>Cuando la demanda lo supera, <b>lo que se recorta son las altas</b> (ver
    /// <c>SeedOrchestrator</c>): una consulta llena deja de captar gente nueva, no le da plantón al crónico
    /// que tenía cita.</para>
    /// </summary>
    public static int CapacidadDelDia(SimulationSettings sim, double pesoDia)
    {
        if (pesoDia <= 0) return 0;

        var cr = sim.Crecimiento;
        var tope = cr.Enabled
            ? Math.Min(cr.PacientesPorDiaObjetivo, cr.PacientesPorDiaMax)
            : cr.PacientesPorDiaMax;

        var pesoMedio = DailyScheduleGenerator.PesoMedioDeApertura(sim.WeekdayWeights);
        var aforo = tope * pesoDia / Math.Max(0.001, pesoMedio);

        // El techo duro es absoluto: no se escala por el día.
        return Math.Max(0, Math.Min(
            cr.PacientesPorDiaMax,
            (int)Math.Round(aforo, MidpointRounding.AwayFromZero)));
    }

    // ── λ y el estado del pool ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pacientes nuevos esperados hoy (λ). <paramref name="activos"/> es la clientela ACTUAL de la
    /// clínica (ver <see cref="PacientesActivos"/>), no el histórico. Nunca negativo.
    /// </summary>
    public static double Lambda(double p, double q, int poblacionCaptacion, int activos, int satisfechos)
    {
        if (poblacionCaptacion <= 0) return 0;
        var porCaptar = Math.Max(0, poblacionCaptacion - activos);
        var presion   = p + q * satisfechos / (double)poblacionCaptacion;
        return Math.Max(0, presion * porCaptar);
    }

    /// <summary>
    /// Altas de hoy: proceso de llegadas de Poisson con la media escalada por el peso del día.
    /// <b>Solo las altas</b> — los retornos los cuenta <see cref="RecurrentSelector"/> sobre el pool real,
    /// y el volumen del día es la suma de ambos.
    /// </summary>
    public static int NuevosDelDia(double lambda, double pesoDia, Random rng)
    {
        if (pesoDia <= 0 || lambda <= 0) return 0;
        return Poisson(lambda * pesoDia, rng);
    }

    /// <summary>
    /// <c>A(d)</c> — la clientela ACTUAL: pacientes que han pisado la clínica dentro de la ventana de
    /// actividad. El que lleva meses sin venir ya no cuenta como suyo: vuelve a estar "en el mercado" y
    /// hay que captarlo de nuevo. Es lo que impide que el área se agote y la clínica se vacíe.
    /// </summary>
    public static int PacientesActivos(
        IReadOnlyList<SimulatedPatient> pool, DateOnly fecha, CrecimientoSettings cr)
    {
        var n = 0;
        foreach (var p in pool)
        {
            if (p.UltimaVisita is not { } ultima) continue;
            if (fecha.DayNumber - ultima.DayNumber > cr.VentanaActividadDias) continue;
            n++;
        }
        return n;
    }

    /// <summary>
    /// <c>S(d)</c> — recurrentes activos y satisfechos: los que ya volvieron alguna vez, quedaron
    /// contentos y siguen viniendo. Solo estos recomiendan la clínica. La ventana de actividad es lo que
    /// impide que S sea un contador acumulado que solo sube: el que se alejó deja de hablar de ella.
    /// </summary>
    public static int RecurrentesActivosSatisfechos(
        IReadOnlyList<SimulatedPatient> pool, DateOnly fecha, CrecimientoSettings cr)
    {
        var n = 0;
        foreach (var p in pool)
        {
            if (p.Insatisfecho) continue;
            if (p.Visitas < cr.MinVisitasRecurrente) continue;
            if (p.UltimaVisita is not { } ultima) continue;
            if (fecha.DayNumber - ultima.DayNumber > cr.VentanaActividadDias) continue;
            n++;
        }
        return n;
    }

    /// <summary>Muestreo de Poisson (algoritmo de Knuth): el nº de llegadas en un día con media λ.</summary>
    public static int Poisson(double lambda, Random rng)
    {
        if (lambda <= 0) return 0;

        var limite = Math.Exp(-lambda);
        // Con λ grande, exp(−λ) se va a 0 y el bucle no terminaría: se acumula en logaritmos.
        if (double.IsSubnormal(limite) || limite == 0)
        {
            var logLimite = -lambda;
            var logP = 0.0;
            var k = 0;
            do
            {
                k++;
                logP += Math.Log(1.0 - rng.NextDouble());
            } while (logP > logLimite);
            return k - 1;
        }

        var p = 1.0;
        var n = 0;
        do
        {
            n++;
            p *= rng.NextDouble();
        } while (p > limite);
        return n - 1;
    }

    // ── La proyección determinista (informe previo + motor de la calibración) ──────────────────────

    /// <summary>
    /// Proyección determinista de la curva, con el <c>q</c> que se va a usar (manual o derivado).
    /// </summary>
    public static IReadOnlyList<ProyeccionDia> Proyectar(IReadOnlyList<DailySchedule> plan, SimulationSettings sim) =>
        Proyectar(plan, sim, CoeficienteImitacion(plan, sim));

    /// <summary>
    /// Proyección determinista (sin RNG) de la curva de crecimiento, para el informe previo y para la
    /// bisección que calibra el boca a boca.
    ///
    /// <para><b>Modela lo mismo que la corrida</b>, que es el punto: cada visita genera, con probabilidad
    /// <c>k</c> (<see cref="ProbabilidadDeRetorno"/>), otra visita del mismo paciente repartida por la
    /// banda de recurrencia; el total del día es <c>altas + retornos</c>, y el aforo recorta las altas.
    /// La clientela activa sale de la <b>ley de Little</b> (altas × vida media del paciente en la
    /// clínica).</para>
    ///
    /// <para>⚠️ Antes esto era <b>un modelo distinto del que se ejecutaba</b> — estimaba los recurrentes
    /// distintos como <c>visitas / (ventana / intervalo)</c> = 8,2 visitas por paciente y año, cuando la
    /// corrida real daba <b>1,45 visitas por paciente en TRES AÑOS</b>. Subestimaba S, la bisección elegía
    /// una <c>q</c> tres veces mayor de la cuenta, λ se disparaba a 101 nuevos/día contra un objetivo de
    /// 25 y la clínica pasaba 33 de sus 42 meses clavada contra el techo de seguridad. Calibrar sobre un
    /// modelo que no es el que corre es no calibrar nada.</para>
    ///
    /// <para>Sigue siendo una <b>estimación, no una predicción</b>: la corrida real mide A, S y los
    /// retornos de verdad sobre el pool y los vuelca a <c>crecimiento_diario.csv</c>.</para>
    /// </summary>
    /// <param name="aplicarAforo">
    /// <c>false</c> = demanda libre, sin recortar por capacidad. Lo usa la calibración: si se bisecciona
    /// sobre la curva ya recortada, la meseta nunca puede pasar del aforo y <c>q</c> se dispararía a su
    /// máximo buscando un objetivo que el propio recorte le impide superar.
    /// </param>
    public static IReadOnlyList<ProyeccionDia> Proyectar(
        IReadOnlyList<DailySchedule> plan, SimulationSettings sim, double q, bool aplicarAforo = true)
    {
        var cr = sim.Crecimiento;
        var p  = CoeficienteInnovacion(sim.PacientesPorDiaMedio, cr.PoblacionCaptacion);

        var k                  = ProbabilidadDeRetorno(sim);
        var visitasPorPaciente = VisitasPorPaciente(k);
        var intervalo          = IntervaloMedioDias(sim.Recurrence);

        // Vida media de un paciente en la clínica: las visitas de más que hará, espaciadas por su
        // intervalo, y la ventana de actividad que tarda en darse de baja tras la última.
        var vidaMediaDias = (int)Math.Round((visitasPorPaciente - 1) * intervalo + cr.VentanaActividadDias);

        // Reparto de un retorno por la banda de recurrencia (de la aguda más corta a la crónica más
        // larga): un kernel plano evita que la demanda de retorno llegue en oleadas resonantes.
        var re      = sim.Recurrence;
        var minDias = Math.Max(1, re.MinDiasAgudo);
        var maxDias = Math.Max(minDias, re.MaxDiasCronico);
        var anchura = maxDias - minDias + 1;

        // ⚠️ Los retornos se llevan por SEPARADO según sean la SEGUNDA visita del paciente o una posterior.
        // Es lo que permite saber cuántos pacientes DISTINTOS han vuelto ya (los que cuentan para S), y no
        // solo cuántas visitas de retorno hay. Sin esta distinción, S se estimaba como `activos × k` —la
        // fracción de RÉGIMEN— y en una clínica joven eso es sencillamente falso: el panel aún no ha tenido
        // tiempo de volver. Contrastado con la corrida real de 3 meses: la proyección cantaba 489
        // recurrentes satisfechos y hubo 95, y por eso predecía 1.165 visitas donde salieron 633.
        var segundasVisitas   = new Dictionary<DateOnly, double>();   // el paciente vuelve por 1ª vez
        var visitasPosteriores = new Dictionary<DateOnly, double>();  // 3ª, 4ª…

        // Colas para la ley de Little: quién sigue siendo clientela de la clínica.
        var altasRecientes     = new Queue<(DateOnly Fecha, double Nuevos)>();
        var returnersRecientes = new Queue<(DateOnly Fecha, double Returners)>();
        var activosAcum        = 0.0;
        var returnersAcum      = 0.0;
        var captadosTotal      = 0.0;
        // Saturación del día anterior: la nota de hoy la pone quien ya vivió la consulta llena. Arrancar
        // en 0 evita la referencia circular (la carga de hoy depende de S, que depende de la nota).
        var saturacionPrevia = 0.0;

        var resultado = new List<ProyeccionDia>(plan.Count);

        foreach (var dia in plan)
        {
            while (altasRecientes.Count > 0 &&
                   dia.Date.DayNumber - altasRecientes.Peek().Fecha.DayNumber > vidaMediaDias)
                activosAcum -= altasRecientes.Dequeue().Nuevos;

            while (returnersRecientes.Count > 0 &&
                   dia.Date.DayNumber - returnersRecientes.Peek().Fecha.DayNumber > vidaMediaDias)
                returnersAcum -= returnersRecientes.Dequeue().Returners;

            var activos = (int)Math.Min(activosAcum, cr.PoblacionCaptacion);

            var fraccionSatisfecha = SatisfaccionPolicy.FraccionSatisfechaTeorica(sim.Satisfaccion, saturacionPrevia);
            // S(d) = pacientes que YA HAN VUELTO al menos una vez (no la fracción de régimen: los que de
            // verdad han tenido tiempo de volver), siguen activos y quedaron contentos.
            var satisfechos = (int)Math.Min(returnersAcum * fraccionSatisfecha, activos);

            var peso   = DailyScheduleGenerator.PesoDelDia(dia.Date.DayOfWeek, sim.WeekdayWeights);
            var lambda = Lambda(p, q, cr.PoblacionCaptacion, activos, satisfechos);

            if (peso <= 0)
            {
                // Día cerrado: no se atiende a nadie y la demanda de retorno de hoy se pierde (igual que
                // en la corrida: nadie viene un domingo).
                resultado.Add(new ProyeccionDia(
                    dia.Date, lambda, 0, 0, 0, activos, (int)captadosTotal, satisfechos));
                continue;
            }

            // Los que vuelven hoy: los que estrenan su 2ª visita (pacientes que pasan a ser recurrentes)
            // y los que ya iban por la 3ª o más.
            var segundas   = segundasVisitas.GetValueOrDefault(dia.Date);
            var posteriores = visitasPosteriores.GetValueOrDefault(dia.Date);
            var retornos   = segundas + posteriores;

            // La demanda de altas la pone Bass; sin crecimiento, el plan estático precalculado.
            var demandaNuevos = cr.Enabled ? lambda * peso : dia.NuevosPacientes;

            // El aforo recorta LAS ALTAS, nunca los retornos: la consulta llena deja de captar.
            var nuevos = demandaNuevos;
            if (aplicarAforo)
                nuevos = Math.Max(0, Math.Min(demandaNuevos, CapacidadDelDia(sim, peso) - retornos));

            var total = nuevos + retornos;

            // Cada visita de hoy genera, con probabilidad k, otra visita del mismo paciente, repartida por
            // la banda de recurrencia. La visita que genera un ALTA es la 2ª del paciente (lo convierte en
            // recurrente); la que genera un retorno es la 3ª o posterior.
            if (k > 0)
            {
                var deAltas   = nuevos   * k / anchura;
                var deRetornos = retornos * k / anchura;
                for (var t = minDias; t <= maxDias; t++)
                {
                    var fecha = dia.Date.AddDays(t);
                    if (deAltas > 0)
                        segundasVisitas[fecha] = segundasVisitas.GetValueOrDefault(fecha) + deAltas;
                    if (deRetornos > 0)
                        visitasPosteriores[fecha] = visitasPosteriores.GetValueOrDefault(fecha) + deRetornos;
                }
            }

            captadosTotal += nuevos;
            if (nuevos > 0)
            {
                altasRecientes.Enqueue((dia.Date, nuevos));
                activosAcum += nuevos;
            }
            // Los que hoy han venido por 2ª vez pasan a ser recurrentes: desde hoy cuentan para el boca a boca.
            if (segundas > 0)
            {
                returnersRecientes.Enqueue((dia.Date, segundas));
                returnersAcum += segundas;
            }

            if (total > 0)
                saturacionPrevia = SatisfaccionPolicy.Saturacion(
                    (int)Math.Round(total), sim.Satisfaccion.CapacidadComodaPorDia);

            resultado.Add(new ProyeccionDia(
                dia.Date, lambda,
                (int)Math.Round(total), (int)Math.Round(nuevos), (int)Math.Round(retornos),
                activos, (int)captadosTotal, satisfechos));
        }

        return resultado;
    }

    // ── Calibración: el boca a boca se DERIVA del destino ──────────────────────────────────────────

    /// <summary>
    /// Media diaria de <b>visitas totales</b> (altas + retornos) máxima que alcanza la clínica con ese
    /// <c>q</c> — la meseta de la curva, medida sobre la demanda <b>sin recortar por aforo</b>.
    ///
    /// <para>⚠️ Es el <b>máximo</b>, no el último día. Si el área de captación se agota, la curva hace cima
    /// y decae, así que el último día cae en la rama descendente: calibrando contra él, la bisección
    /// sobreexcitaba toda la corrida para que el final cuadrase (medido: pedías 25/día y la clínica pasaba
    /// dos años a 34/día antes de desinflarse).</para>
    /// </summary>
    public static double MesetaProyectada(IReadOnlyList<DailySchedule> plan, SimulationSettings sim, double q) =>
        MediasProyectadas(plan, sim, q).Pico;

    /// <summary>
    /// Media diaria de visitas en el <paramref name="q"/> dado: el <b>pico</b> (la meseta) y la del
    /// <b>último mes</b>. Que el final quede muy por debajo del pico significa que el área se está agotando.
    ///
    /// <para>Se mide como <b>media móvil de un mes de días abiertos</b> — exactamente la misma magnitud que
    /// <see cref="RunStats.MediaDiariaMaxima"/>, que es la que juzgará la ley L8 sobre la corrida real. Así
    /// la proyección, el aforo y la ley hablan en las <b>mismas unidades</b>: visitas en un día que abre.</para>
    ///
    /// <para>⚠️ Antes se normalizaba cada día por su peso (<c>Total / peso</c>), y eso <b>rompía la
    /// calibración</b>: los retornos NO escalan con el peso del día (a un paciente le toca su control el
    /// sábado igual que el lunes), así que en un sábado —con la mitad de aforo— el cociente se disparaba,
    /// ese día se convertía en el "pico" y la bisección daba por alcanzado el objetivo con un boca a boca
    /// prácticamente nulo. Medido: la clínica se quedaba clavada en 15,7 visitas/día pidiéndole 25.</para>
    /// </summary>
    public static (double Pico, double Final) MediasProyectadas(
        IReadOnlyList<DailySchedule> plan, SimulationSettings sim, double q)
    {
        var abiertos = Proyectar(plan, sim, q, aplicarAforo: false)
            .Where(d => DailyScheduleGenerator.PesoDelDia(d.Fecha.DayOfWeek, sim.WeekdayWeights) > 0)
            .Select(d => (double)d.Total)
            .ToList();
        if (abiertos.Count == 0) return (sim.PacientesPorDiaMedio, sim.PacientesPorDiaMedio);

        var ventana = Math.Min(RunStats.VentanaMediaMovil, abiertos.Count);
        var medias = Enumerable.Range(0, abiertos.Count - ventana + 1)
            .Select(i => abiertos.Skip(i).Take(ventana).Average())
            .ToList();

        return (medias.Max(), medias[^1]);
    }

    /// <summary>
    /// Deriva <c>q</c> (la fuerza del boca a boca) de <b>dónde quiere el usuario que acabe la clínica</b>
    /// (<c>PacientesPorDiaObjetivo</c>, en <b>visitas totales</b>/día), por bisección sobre la proyección.
    ///
    /// <para>⚠️ <b>Por qué esto no es un lujo.</b> El crecimiento es un lazo de realimentación positiva cuyo
    /// punto fijo vale <c>1/(1 − ganancia)</c> — y eso <b>explota</b> cuando la ganancia se acerca a 1.
    /// Pedirle al usuario que acierte con <c>q</c> a ojo es pedirle que apunte a un blanco que se mueve
    /// exponencialmente. Aquí se le da la vuelta: el usuario dice el <b>destino</b> y el modelo calcula el
    /// boca a boca que lo produce — igual que <c>p</c> se deriva del <b>arranque</b>.</para>
    ///
    /// <para>Devuelve <b>0</b> si el objetivo ya se alcanza <b>sin nada de boca a boca</b>: el panel por sí
    /// solo produce <c>PacientesPorDiaMedio × 1/(1−k)</c> visitas al día, y si eso ya cubre el objetivo, la
    /// clínica no necesita crecer. Si el objetivo es inalcanzable (el área o el techo muerden antes),
    /// devuelve el <c>q</c> máximo probado y el informe previo avisa de que la meseta se queda corta.</para>
    ///
    /// <para>⚠️ <b>Se calibra sobre la VENTANA CONFIGURADA</b>, porque el objetivo significa "dónde quiero
    /// que esté la clínica al final de lo que estoy simulando". Consecuencia práctica: <b>acortar la ventana
    /// hace el boca a boca feroz</b> — pedirle llegar de 5 a 25/día en 3 meses exige un <c>q</c> ~100 veces
    /// mayor que en 3 años, y la clínica se planta en su aforo en semanas. Es coherente con lo que se le
    /// pide, pero significa que <b>una corrida corta NO sirve para juzgar la forma de la curva</b>: sirve
    /// para probar el pipeline. Para ver la curva de verdad hay que correr la ventana de producción.</para>
    /// </summary>
    public static double CalibrarImitacion(IReadOnlyList<DailySchedule> plan, SimulationSettings sim)
    {
        var objetivo = sim.Crecimiento.PacientesPorDiaObjetivo;

        // El panel solo (sin recomendaciones) ya llega al objetivo → no hay nada que hacer crecer.
        if (MesetaProyectada(plan, sim, 0) >= objetivo) return 0;

        const double QMax = 1.0;   // absurdo (cada satisfecho traería 1 paciente/día): sobra de margen
        var lo = 0.0;
        var hi = QMax;

        if (MesetaProyectada(plan, sim, QMax) < objetivo) return QMax;   // inalcanzable

        // La meseta crece monótonamente con q → bisección. 40 pasos dan más precisión de la que el
        // redondeo a pacientes enteros puede aprovechar.
        for (var i = 0; i < 40; i++)
        {
            var medio = (lo + hi) / 2;
            if (MesetaProyectada(plan, sim, medio) < objetivo) lo = medio;
            else hi = medio;
        }
        return (lo + hi) / 2;
    }
}
