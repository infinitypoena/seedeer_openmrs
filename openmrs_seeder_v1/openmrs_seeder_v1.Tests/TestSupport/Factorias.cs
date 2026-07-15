using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Models.Simulation;

namespace openmrs_seeder_v1.Tests.TestSupport;

/// <summary>Constructores compactos de las entidades de dominio que los tests montan una y otra vez.</summary>
public static class Factorias
{
    /// <summary>Diagnóstico mínimo viable: aplica a todas las edades y a ambos sexos salvo que se diga.</summary>
    public static DiagnosticoEntry Dx(
        string uuid = "dx-1",
        bool cronica = false,
        string severidad = "",
        string categoria = "respiratorio",
        string nombre = "",
        int peso = 10,
        bool comun = true,
        string sexo = "",
        string ambito = "") => new()
    {
        CielUuid   = uuid,
        NombreEs   = string.IsNullOrEmpty(nombre) ? uuid : nombre,
        Categoria  = categoria,
        Severidad  = severidad,
        EsCronica  = cronica,
        EsComun    = comun,
        Sexo       = sexo,
        Ambito     = ambito,
        PesoM      = peso,
        PesoF      = peso,
        Aplica0_14 = true, Aplica15_29 = true, Aplica30_44 = true, Aplica45_64 = true, Aplica65mas = true,
    };

    /// <summary>
    /// Paciente del padrón, con lo que las leyes y el selector de retornos necesitan leer:
    /// visitas acumuladas, crónica que arrastra, satisfacción y su próxima cita.
    /// </summary>
    public static SimulatedPatient P(
        int visitas = 1,
        DateOnly? ultimaVisita = null,
        bool cronico = false,
        bool insatisfecho = false,
        DateOnly? proximaCita = null,
        DateOnly? proximoElegibleDesde = null,
        string? uuid = null)
    {
        var p = new SimulatedPatient
        {
            OpenMrsUuid          = uuid ?? Guid.NewGuid().ToString("N"),
            Visitas              = visitas,
            UltimaVisita         = ultimaVisita,
            Insatisfecho         = insatisfecho,
            ProximaCita          = proximaCita,
            ProximoElegibleDesde = proximoElegibleDesde ?? proximaCita,
        };
        if (cronico) p.CronicasActivas.Add(Dx(uuid: "dx-cronico", cronica: true));
        return p;
    }

    public static CitaPendiente Cita(string uuid, DateTime fecha) => new(uuid, fecha);

    /// <summary>Padrón de n pacientes construidos por índice.</summary>
    public static List<SimulatedPatient> Pool(int n, Func<int, SimulatedPatient> factory) =>
        Enumerable.Range(0, n).Select(factory).ToList();
}
