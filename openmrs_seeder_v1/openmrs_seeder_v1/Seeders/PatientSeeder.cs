using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

namespace OpenmrsSeeder.Seeders;

public class PatientSeeder
{
    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _settings;
    private readonly ILogger<PatientSeeder> _logger;

    public PatientSeeder(OpenMrsRestClient client, OpenMrsSettings settings, ILogger<PatientSeeder> logger)
    {
        _client   = client;
        _settings = settings;
        _logger   = logger;
    }

    public async Task<string?> CreateAsync(SimulatedPatient patient, CancellationToken ct)
    {
        // Registro del paciente en Recepción/Admisión (no en un consultorio); fallback a LocationUuid
        var registrationLocation = string.IsNullOrEmpty(_settings.Defaults.RegistrationLocationUuid)
            ? _settings.Defaults.LocationUuid
            : _settings.Defaults.RegistrationLocationUuid;

        var payload = new
        {
            person = new
            {
                names = new[]
                {
                    new
                    {
                        givenName   = patient.GivenName,
                        middleName  = patient.SecondGivenName,
                        familyName  = patient.FamilyName,
                        familyName2 = patient.SecondFamilyName,
                        preferred   = true
                    }
                },
                gender    = patient.Gender,
                birthdate = patient.BirthDate.ToString("yyyy-MM-dd"),
                addresses = new[]
                {
                    new
                    {
                        address1    = patient.Address1,
                        cityVillage = patient.City,
                        country     = "España",
                        preferred   = true
                    }
                }
            },
            identifiers = new object[]
            {
                // OpenMRS ID (required): ID válido generado con Luhn Mod-30
                new
                {
                    identifier     = LuhnMod30Generator.Next(),
                    identifierType = _settings.Defaults.PatientIdentifierTypeUuid,
                    location       = registrationLocation,
                    preferred      = false
                },
                // Old Identification Number: prefijo SIM- para identificar y borrar datos simulados
                new
                {
                    identifier     = patient.Identifier,
                    identifierType = _settings.Defaults.TrackingIdentifierTypeUuid,
                    location       = registrationLocation,
                    preferred      = true
                }
            }
        };

        try
        {
            var json = await _client.PostAsync("patient", payload, ct);
            var doc  = JsonSerializer.Deserialize<JsonElement>(json);
            if (doc.TryGetProperty("uuid", out var uuidProp))
            {
                var uuid = uuidProp.GetString();
                _logger.LogInformation("[Patient] Creado {Id} → {Uuid}", patient.Identifier, uuid);
                return uuid;
            }
            _logger.LogWarning("[Patient] Respuesta sin uuid para {Id}", patient.Identifier);
        }
        catch (Exception ex)
        {
            _logger.LogError("[Patient] Error creando {Id}: {Msg}", patient.Identifier, ex.Message);
        }

        return null;
    }
}
