using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Services;

/// <summary>Selecciona tratamientos desde reglas investigadas, sin inventar fármacos por categoría.</summary>
public static class TreatmentSelector
{
    private static readonly HashSet<string> AccionesConMedicamento =
        ["manejo_sintomatico", "tratamiento_especifico"];

    public static IReadOnlyList<MedicamentoEntry> Seleccionar(
        SimulatedPatient patient,
        IEnumerable<TratamientoEntry> reglas,
        IEnumerable<MedicamentoEntry> medicamentos,
        DateOnly fecha,
        Random rng)
    {
        var productos = medicamentos
            .SelectMany(m => new[] { (Uuid: m.ConceptUuid, Medicamento: m), (Uuid: m.DrugUuid, Medicamento: m) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Uuid))
            .GroupBy(x => x.Uuid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Medicamento, StringComparer.OrdinalIgnoreCase);
        var reglasLista = reglas.ToList();
        var elegidos = new List<MedicamentoEntry>();

        foreach (var dx in patient.TodosDiagnosticos)
        {
            var regla = reglasLista.FirstOrDefault(r =>
                r.DiagnosticoUuid.Equals(dx.CielUuid, StringComparison.OrdinalIgnoreCase))
                ?? reglasLista.FirstOrDefault(r =>
                    string.IsNullOrEmpty(r.DiagnosticoUuid)
                    && r.Categoria.Equals(dx.Categoria, StringComparison.OrdinalIgnoreCase));

            if (regla is null || !AccionesConMedicamento.Contains(regla.Accion)) continue;

            foreach (var uuid in regla.MedicamentoUuids.OrderBy(_ => rng.Next()))
            {
                if (!productos.TryGetValue(uuid, out var medicamento)) continue;
                if (OrderVigencia.EstaActivo(patient.OrderedConcepts, medicamento.ConceptUuid, fecha)) continue;
                if (elegidos.Any(m => m.ConceptUuid.Equals(medicamento.ConceptUuid, StringComparison.OrdinalIgnoreCase))) continue;
                if (!string.IsNullOrWhiteSpace(medicamento.GrupoTerapeutico)
                    && elegidos.Any(m => m.GrupoTerapeutico.Equals(medicamento.GrupoTerapeutico, StringComparison.OrdinalIgnoreCase))) continue;
                elegidos.Add(medicamento);
                if (elegidos.Count >= regla.MaxMedicamentos) break;
            }
        }

        return elegidos;
    }
}