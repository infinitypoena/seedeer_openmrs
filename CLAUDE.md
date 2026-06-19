# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Purpose

.NET 10 Web API that acts as a **clinical simulator** for an OpenMRS instance. It generates realistic patient records, visits, encounters, observations, orders, and allergies that reflect a real-world clinic workflow — driven by a probabilistic epidemiological model parameterized by age, gender, and clinic type.

## Build & Run

```bash
dotnet build openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj
dotnet run --project openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj
dotnet test
```

Swagger UI available at `http://localhost:5197/swagger` after running.

## Architecture

**The API connects to OpenMRS exclusively via REST API** (`/ws/rest/v1`). It does NOT write SQL directly. All data is inserted through OpenMRS REST endpoints (POST /patient, POST /visit, POST /encounter, POST /obs, POST /order, POST /patient/{uuid}/allergy).

Key architectural decisions:
- **Simulator model**: daily patient volume is driven by `PacientesPorDiaMedio` + weekday weights + normal distribution. Each patient gets a diagnosis chosen from `epidemiology-profile.csv` weighted by age/gender.
- **Catalog-driven coherence**: diagnoses, labs, medications, clinical exams, and allergies are selected from CSV catalogs using boolean columns (`aplica_CATEGORIA`, `aplica_GRUPO_EDAD`) — no hardcoded lists.
- **Seeder pipeline**: each domain is a self-contained seeder (`PatientSeeder`, `AllergySeeder`, `VitalsSeeder`, `ConsultaSeeder`, `LabOrderSeeder`, `PrescriptionSeeder`, `VisitCloseSeeder`) coordinated by `SeedOrchestrator`.
- **Background execution**: `POST /api/seed/run` returns 202 immediately; simulation runs in a background task tracked by `SeedProgressTracker` (in-memory, keyed by runId GUID).
- **Idempotency**: all seeded patients have identifier prefix `SIM-`; visits/encounters have `SEEDED_BY_SIMULATOR` in description. `DELETE /api/seed/clear` voids them all.

## Infrastructure

- Docker compose path: `C:\Users\Moises Molina\Documents\Estudios\Especializacion\PruebaOpenMRS1\open-omrs36-prod\openmrs-distro-referenceapplication-3.6.0`
- OpenMRS 3.6.0 Reference Application (O3)
- MariaDB 10.11 — port 3306 does NOT need to be exposed (REST API mode)
- Default credentials: `admin / Admin123`

## Configuration

See `parametrizacion_archivos.md` for full parameter reference. Key sections in `appsettings.json`:

```json
{
  "OpenMRS": {
    "SeedMode": "RestApi",
    "RestApi": { "BaseUrl": "http://localhost/openmrs/ws/rest/v1", "Username": "admin", "Password": "Admin123" }
  },
  "Simulation": {
    "StartDate": "2023-01-01",
    "EndDate": "2024-12-31",
    "PacientesPorDiaMedio": 40,
    "PorcentajeRecurrentes": 30,
    "ClinicType": "ConsultaExterna",
    "ReferralProbabilities": {
      "LabOrder": 0.40, "ClinicalExam": 0.35, "DrugOrder": 0.65,
      "Urgent": 0.20, "FollowUp": 0.30, "AllergyOnNew": 0.15
    }
  }
}
```

## Catalog Files

All under `catalogs/`. See `parametrizacion_archivos.md` for column schemas.

| File | Source | Purpose |
|------|--------|---------|
| `epidemiology-profile.csv` | Manual | Category weights by age/gender |
| `diagnosticos.csv` | Query DB + manual | CIEL diagnoses + boolean age group columns |
| `medicamentos.csv` | Query DB + manual | Drugs + boolean category columns |
| `laboratorios.csv` | Query DB + manual | Lab tests + boolean category columns |
| `examenes_clinicos.csv` | Manual | In-clinic exams recorded as obs |
| `alergenos.csv` | Manual | Allergens (DRUG/FOOD/ENVIRONMENT) |
| `motivos_consulta.csv` | Manual | Consultation reason phrases in Spanish |

## Key Design Docs

- `parametrizacion_archivos.md` — full parameter and CSV schema reference
- `detalle-seeder.md` — project overview, stack, endpoints, data model
- `fases_implementacion.md` — implementation phases with deliverables and verification steps
