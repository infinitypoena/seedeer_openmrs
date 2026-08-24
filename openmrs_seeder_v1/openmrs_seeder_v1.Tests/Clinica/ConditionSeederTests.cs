using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Seeders;
using openmrs_seeder_v1.Tests.TestSupport;

namespace openmrs_seeder_v1.Tests.Clinica;

public class ConditionSeederTests
{
    [Fact]
    public async Task CondicionExitosaNoSePublicaDosVeces()
    {
        var handler = new SecuenciaHttpHandler(
            new HttpResponseMessage(HttpStatusCode.Created),
            new HttpResponseMessage(HttpStatusCode.Created));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var seeder = new ConditionSeeder(
            new OpenMrsRestClient(http),
            NullLogger<ConditionSeeder>.Instance);
        var patient = new SimulatedPatient
        {
            Identifier = "SIM-2",
            OpenMrsUuid = "patient-2",
            VisitDatetime = new DateTime(2023, 1, 1, 9, 0, 0),
            Diagnostico = Factorias.Dx(uuid: "dx-hta", cronica: true)
        };

        await seeder.SeedAsync(patient, CancellationToken.None);
        await seeder.SeedAsync(patient, CancellationToken.None);

        Assert.Single(handler.Paths);
        Assert.Contains("dx-hta", patient.ProblemListConcepts);
    }

    [Fact]
    public async Task FalloAlCrearCondicionPermiteReintento()
    {
        var handler = new SecuenciaHttpHandler(
            new HttpResponseMessage(HttpStatusCode.BadRequest),
            new HttpResponseMessage(HttpStatusCode.Created));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var client = new OpenMrsRestClient(http);
        var seeder = new ConditionSeeder(client, NullLogger<ConditionSeeder>.Instance);
        var patient = new SimulatedPatient
        {
            Identifier = "SIM-1",
            OpenMrsUuid = "patient-1",
            VisitDatetime = new DateTime(2023, 1, 1, 9, 0, 0),
            Diagnostico = Factorias.Dx(uuid: "dx-epilepsia", cronica: true)
        };

        await seeder.SeedAsync(patient, CancellationToken.None);

        Assert.Empty(patient.ProblemListConcepts);

        await seeder.SeedAsync(patient, CancellationToken.None);

        Assert.Equal(["condition", "condition"], handler.Paths);
        Assert.Contains("dx-epilepsia", patient.ProblemListConcepts);
    }

    private sealed class SecuenciaHttpHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath.Trim('/'));
            return Task.FromResult(_responses.Dequeue());
        }
    }
}