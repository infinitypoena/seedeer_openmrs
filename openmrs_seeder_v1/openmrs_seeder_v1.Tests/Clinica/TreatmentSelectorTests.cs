using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;
using openmrs_seeder_v1.Tests.TestSupport;

namespace openmrs_seeder_v1.Tests.Clinica;

public class TreatmentSelectorTests
{
    [Fact]
    public void PoliticaSintomaticaSinMedicamentoNoInventaReceta()
    {
        var patient = new SimulatedPatient { Diagnostico = Factorias.Dx(categoria: "respiratorio") };
        var reglas = new[]
        {
            new TratamientoEntry { Categoria = "respiratorio", Accion = "manejo_sintomatico" }
        };
        var medicamento = new MedicamentoEntry { ConceptUuid = "concepto-1", DrugUuid = "drug-1" };

        var resultado = TreatmentSelector.Seleccionar(patient, reglas, [medicamento], new DateOnly(2026, 8, 23), new Random(42));

        Assert.Empty(resultado);
    }

    [Fact]
    public void ReglaEspecificaSeleccionaSoloElMedicamentoDeclaradoYRespetaVigencia()
    {
        var patient = new SimulatedPatient
        {
            Diagnostico = Factorias.Dx(uuid: "dx-diabetes", categoria: "diabetes"),
            OrderedConcepts = new Dictionary<string, DateOnly>
            {
                ["concepto-metformina"] = new DateOnly(2026, 8, 22)
            }
        };
        var reglas = new[]
        {
            new TratamientoEntry
            {
                DiagnosticoUuid = "dx-diabetes",
                Accion = "tratamiento_especifico",
                MedicamentoUuids = ["drug-metformina"]
            }
        };
        var medicamentos = new[]
        {
            new MedicamentoEntry { ConceptUuid = "concepto-metformina", DrugUuid = "drug-metformina" },
            new MedicamentoEntry { ConceptUuid = "concepto-otro", DrugUuid = "drug-otro" }
        };

        var resultado = TreatmentSelector.Seleccionar(patient, reglas, medicamentos, new DateOnly(2026, 8, 23), new Random(42));

        var medicamento = Assert.Single(resultado);
        Assert.Equal("concepto-metformina", medicamento.ConceptUuid);
    }

    [Fact]
    public void NoSeleccionaDosMedicamentosDelMismoGrupo()
    {
        var patient = new SimulatedPatient { Diagnostico = Factorias.Dx(uuid: "dx-dolor") };
        var reglas = new[]
        {
            new TratamientoEntry
            {
                DiagnosticoUuid = "dx-dolor",
                Accion = "tratamiento_especifico",
                MedicamentoUuids = ["drug-ibuprofeno", "drug-diclofenaco"],
                MaxMedicamentos = 2
            }
        };
        var medicamentos = new[]
        {
            new MedicamentoEntry { ConceptUuid = "concepto-ibu", DrugUuid = "drug-ibuprofeno", GrupoTerapeutico = "aines" },
            new MedicamentoEntry { ConceptUuid = "concepto-diclo", DrugUuid = "drug-diclofenaco", GrupoTerapeutico = "aines" }
        };

        var resultado = TreatmentSelector.Seleccionar(patient, reglas, medicamentos, new DateOnly(2026, 8, 23), new Random(42));

        Assert.Single(resultado);
    }
}
