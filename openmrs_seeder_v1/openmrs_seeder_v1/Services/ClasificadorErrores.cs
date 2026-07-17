namespace OpenmrsSeeder.Services;

/// <summary>
/// Tipo de un error de operación, derivado de la excepción tipada. La distinción que importa al leer el
/// resumen: un 4xx es un dato que OpenMRS rechazó (determinista — reintentar la corrida repetirá el fallo,
/// hay que arreglar el catálogo/payload), mientras que un 5xx o un timeout es un problema del entorno
/// (transitorio — la misma corrida en otro momento pasaría).
/// </summary>
public enum TipoError
{
    /// <summary>HTTP 4xx: OpenMRS rechazó el dato (payload/catálogo malo, se repetirá).</summary>
    DatoRechazado,
    /// <summary>HTTP 5xx: fallo del lado del servidor OpenMRS.</summary>
    Servidor,
    /// <summary>HttpClient agotó su timeout (TaskCanceledException con TimeoutException dentro).</summary>
    Timeout,
    /// <summary>Fallo de red sin respuesta HTTP: conexión rechazada, DNS, socket…</summary>
    Red,
    /// <summary>Cancelación que no es timeout (Ctrl+C u otro token).</summary>
    Cancelado,
    /// <summary>Sin excepción o excepción no HTTP.</summary>
    Otro
}

/// <summary>
/// Seam puro: clasifica la excepción que acompaña a un LogError. Requiere que el seeder pase la excepción
/// al logger (<c>LogError(ex, …)</c>) — sin ella, el evento clasifica como <see cref="TipoError.Otro"/>,
/// que es honesto y visible (cubre seeders futuros que olviden pasarla).
/// </summary>
public static class ClasificadorErrores
{
    public static TipoError Clasificar(Exception? ex) => ex switch
    {
        HttpRequestException { StatusCode: { } s } when (int)s is >= 400 and < 500 => TipoError.DatoRechazado,
        HttpRequestException { StatusCode: { } s } when (int)s >= 500              => TipoError.Servidor,
        HttpRequestException                                                       => TipoError.Red,
        // El timeout de HttpClient llega como TaskCanceledException con TimeoutException dentro
        // (así se distingue del Ctrl+C, que cancela por token y no lleva inner de timeout).
        TaskCanceledException { InnerException: TimeoutException }                 => TipoError.Timeout,
        OperationCanceledException                                                 => TipoError.Cancelado,
        _                                                                          => TipoError.Otro
    };

    /// <summary>Etiqueta corta para el resumen ("4xx (dato rechazado)", "timeout"…).</summary>
    public static string Etiqueta(TipoError tipo) => tipo switch
    {
        TipoError.DatoRechazado => "4xx (dato rechazado)",
        TipoError.Servidor      => "5xx (fallo del servidor)",
        TipoError.Timeout       => "timeout",
        TipoError.Red           => "red",
        TipoError.Cancelado     => "cancelado",
        _                       => "otro"
    };

    /// <summary>Código compacto para el CSV ("4xx", "5xx", "timeout", "red", "cancelado", "otro").</summary>
    public static string Codigo(TipoError tipo) => tipo switch
    {
        TipoError.DatoRechazado => "4xx",
        TipoError.Servidor      => "5xx",
        TipoError.Timeout       => "timeout",
        TipoError.Red           => "red",
        TipoError.Cancelado     => "cancelado",
        _                       => "otro"
    };
}
