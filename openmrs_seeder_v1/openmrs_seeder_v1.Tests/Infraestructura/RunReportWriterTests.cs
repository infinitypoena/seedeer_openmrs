using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests.Infraestructura;

public class RunReportWriterTests
{
    private static readonly DateOnly Cierre = new(2024, 12, 31);

    private static (RunReportWriter Writer, SimulationSettings Settings, string Dir) Nuevo()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var settings = new SimulationSettings();
        settings.Salida.Carpeta = dir;
        // Fijadas, no heredadas de los defaults: los tests afirman sobre quién sigue "activo" a una fecha
        // dada y sobre el % de mercado que sale en el CSV.
        settings.Crecimiento.VentanaActividadDias = 180;
        settings.Crecimiento.PoblacionCaptacion   = 20000;
        return (new RunReportWriter(settings, NullLogger<RunReportWriter>.Instance), settings, dir);
    }

    /// <summary>
    /// Lee el CSV aunque la corrida lo tenga abierto para escribir (es justo lo que hace un usuario que
    /// se asoma a la curva a media simulación). Sin FileShare.ReadWrite, Windows lo rechaza.
    /// </summary>
    private static string[] Leer(string dir, string archivo)
    {
        using var fs = new FileStream(
            Path.Combine(dir, archivo), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r')).ToArray();
    }

    private static SimulatedPatient Paciente(string id, DateOnly? ultimaVisita, params int[] notas) =>
        new()
        {
            Identifier     = id,
            OpenMrsUuid    = $"uuid-{id}",
            Visitas        = notas.Length,
            Calificaciones = [.. notas],
            Insatisfecho   = SatisfaccionPolicy.EsInsatisfecho(notas, umbral: 3.0),
            UltimaVisita   = ultimaVisita
        };

    /// <summary>Día de ejemplo; los parámetros con nombre dejan claro qué se está afirmando.</summary>
    private static DiaDeCrecimiento Dia(
        DateOnly fecha, int activos = 10, int captados = 10, int satisfechos = 0,
        double lambda = 10.5, double media = 15, int atendidos = 12, int nuevos = 12,
        int recurrentes = 0, int aforo = 25, int rechazados = 0, double nota = 4.1, double saturacion = 0) =>
        new(fecha, activos, captados, satisfechos, lambda, media, atendidos, nuevos, recurrentes,
            aforo, rechazados, nota, saturacion);

    [Fact]
    public void EscribeLaCurvaDeCrecimientoConSuCabecera()
    {
        var (writer, _, dir) = Nuevo();
        try
        {
            writer.Iniciar();
            writer.RegistrarDia(new DiaDeCrecimiento(
                new DateOnly(2023, 3, 15), Activos: 400, CaptadosTotal: 412, Satisfechos: 26,
                Lambda: 11.3, MediaMovil: 16.1, Atendidos: 15, Nuevos: 10, Recurrentes: 5,
                Aforo: 25, NuevosRechazados: 0, CalificacionMedia: 4.2, Saturacion: 0));
            writer.Dispose();

            var lineas = Leer(dir, "crecimiento_diario.csv");

            Assert.Equal(2, lineas.Length);
            Assert.StartsWith("fecha,activos_A,captados_total,satisfechos_activos_S,lambda_altas,media_movil_30d", lineas[0]);
            // Decimales con punto (cultura invariante), % de recurrentes del día (5/15 = 33,3 %) y % de
            // mercado sobre la clientela ACTIVA (400/20000).
            Assert.Equal("2023-03-15,400,412,26,11.3,16.1,15,10,5,33.3,25,0,4.2,0,2", lineas[1]);
        }
        finally { writer.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ElPadronCuentaLasVisitasDeVerdad_NoLasCalificaciones()
    {
        // Regresión: la columna `visitas` salía de Calificaciones.Count, que SOLO existe con la
        // satisfacción activa. Con Satisfaccion.Enabled=false el padrón decía que nadie había venido nunca.
        var (writer, _, dir) = Nuevo();
        try
        {
            var sinNotas = new SimulatedPatient
            {
                Identifier   = "SIM-X",
                OpenMrsUuid  = "uuid-X",
                Visitas      = 4,      // vino cuatro veces…
                Calificaciones = [],   // …pero nadie le pidió opinión
                UltimaVisita = Cierre
            };

            writer.Iniciar();
            writer.Finalizar([sinNotas], Cierre);

            Assert.Contains("SIM-X,uuid-X,4,", Leer(dir, "clientes_recurrentes.csv")[1]);
        }
        finally { writer.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void CadaDiaSeEscribeAlVuelo_AsiUnCtrlCNoSeLlevaLaCurva()
    {
        var (writer, _, dir) = Nuevo();
        try
        {
            writer.Iniciar();
            writer.RegistrarDia(Dia(new DateOnly(2023, 1, 2)));

            // Sin cerrar el writer: el fichero ya tiene el día (se hace flush en cada fila).
            var lineas = Leer(dir, "crecimiento_diario.csv");
            Assert.Equal(2, lineas.Length);
        }
        finally { writer.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ElPadronDePacientesDistingueSatisfechosYActivos()
    {
        var (writer, _, dir) = Nuevo();
        try
        {
            var pool = new List<SimulatedPatient>
            {
                Paciente("SIM-1", Cierre.AddDays(-10),  5, 4, 5),   // contento y activo
                Paciente("SIM-2", Cierre.AddDays(-10),  2, 1),      // descontento (se aleja)
                Paciente("SIM-3", Cierre.AddDays(-300), 5, 5),      // contento pero ya no viene
            };

            writer.Iniciar();
            writer.Finalizar(pool, Cierre);

            var lineas = Leer(dir, "clientes_recurrentes.csv");

            Assert.Equal(4, lineas.Length);   // cabecera + 3
            Assert.StartsWith("identifier,uuid,visitas,calificaciones,promedio,satisfecho,activo", lineas[0]);

            Assert.Contains("SIM-1,uuid-SIM-1,3,5|4|5,4.67,true,true", lineas[1]);
            Assert.Contains("SIM-2,uuid-SIM-2,2,2|1,1.5,false,true", lineas[2]);      // no satisfecho, aún "activo" por fecha
            Assert.Contains("SIM-3,uuid-SIM-3,2,5|5,5,true,false", lineas[3]);        // satisfecho pero inactivo
        }
        finally { writer.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void CarpetaVacia_LaFeatureQuedaApagada_YNoEscribeNada()
    {
        var settings = new SimulationSettings();
        settings.Salida.Carpeta = "";
        using var writer = new RunReportWriter(settings, NullLogger<RunReportWriter>.Instance);

        Assert.Null(writer.Carpeta);
        Assert.False(writer.Activo);

        // No debe reventar: simplemente no hace nada.
        writer.Iniciar();
        writer.RegistrarDia(Dia(new DateOnly(2023, 1, 2)));
        writer.Finalizar([], Cierre);
    }

    [Fact]
    public void UnaCorridaNuevaNoSeMezclaConLaAnterior()
    {
        var (writer, _, dir) = Nuevo();
        try
        {
            writer.Iniciar();
            writer.RegistrarDia(Dia(new DateOnly(2023, 1, 2)));

            writer.Iniciar();   // segunda corrida: el CSV se estrena de cero
            writer.RegistrarDia(Dia(new DateOnly(2024, 1, 2)));
            writer.Dispose();

            var lineas = Leer(dir, "crecimiento_diario.csv");

            Assert.Equal(2, lineas.Length);
            Assert.Contains("2024-01-02", lineas[1]);
        }
        finally { writer.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void CarpetaRelativa_CuelgaDeLaCarpetaDelBinario()
    {
        var settings = new SimulationSettings();   // por defecto: "output"
        var writer = new RunReportWriter(settings, NullLogger<RunReportWriter>.Instance);

        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "output"), writer.Carpeta);
    }

    [Fact]
    public void LosDecimalesNoDependenDeLaCulturaDeLaMaquina()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");   // coma decimal
        var (writer, _, dir) = Nuevo();
        try
        {
            writer.Iniciar();
            writer.RegistrarDia(Dia(new DateOnly(2023, 1, 2), lambda: 10.5, media: 15.25, nota: 4.5, saturacion: 0.25));
            writer.Dispose();

            var fila = Leer(dir, "crecimiento_diario.csv")[1];

            Assert.Contains("10.5", fila);
            Assert.Contains("15.25", fila);
            Assert.DoesNotContain(";", fila);   // si la cultura se colara, el separador CSV se rompería
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            writer.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }
}
