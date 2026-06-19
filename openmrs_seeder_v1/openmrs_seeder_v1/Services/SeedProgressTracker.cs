using System.Collections.Concurrent;

namespace OpenmrsSeeder.Services;

public class SeedRun
{
    public Guid RunId { get; set; }
    public int Porcentaje { get; set; }
    public string Etapa { get; set; } = "";
    public int PacientesCreados { get; set; }
    public int TotalDias { get; set; }
    public int DiasProcesados { get; set; }
    public string FechaActual { get; set; } = "";
    public List<string> Errores { get; set; } = [];
    public bool Completado { get; set; }
    public DateTime Inicio { get; set; } = DateTime.UtcNow;
}

public class SeedProgressTracker
{
    private readonly ConcurrentDictionary<Guid, SeedRun> _runs = new();

    public Guid CreateRun()
    {
        var run = new SeedRun { RunId = Guid.NewGuid() };
        _runs[run.RunId] = run;
        return run.RunId;
    }

    public SeedRun? GetRun(Guid runId) => _runs.GetValueOrDefault(runId);

    public void Update(Guid runId, Action<SeedRun> update)
    {
        if (_runs.TryGetValue(runId, out var run))
            update(run);
    }
}
