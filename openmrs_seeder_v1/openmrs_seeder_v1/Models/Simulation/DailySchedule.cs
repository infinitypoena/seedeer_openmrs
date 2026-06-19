namespace OpenmrsSeeder.Models.Simulation;

public class DailySchedule
{
    public DateOnly Date { get; set; }
    public int TotalPatients { get; set; }
    public int NuevosPacientes { get; set; }
    public int PacientesRecurrentes { get; set; }
}
