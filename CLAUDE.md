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
- **Dual identifier**: patients get an OpenMRS ID (Luhn Mod-30 valid) + "Old Identification Number" with `SIM-` prefix for tracking.
- **Same-day deduplication**: `SeedOrchestrator` uses a `HashSet<string>` of OpenMrsUuids per day so a patient cannot appear as both new and recurring on the same day.
- **Comorbidity (multimorbidity per visit)**: after the primary diagnosis, `EpidemiologySelector.SelectComorbilidades` may add 1..N extra diagnoses (config `Simulation.Comorbidity`). Probability scales with age (`AgeScaling`) and the extra category is weighted toward clinically-related clusters (`Affinities` × `AffinityBoost`). All diagnoses land in one encounter; `LabOrderSeeder`/`PrescriptionSeeder` filter catalogs by `patient.Categorias` (union of all diagnoses' categories) so labs/meds stay coherent across comorbidities.

## Infrastructure

- Docker compose path: `C:\Users\Moises Molina\Documents\Estudios\Especializacion\PruebaOpenMRS1\open-omrs36-prod\openmrs-distro-referenceapplication-3.6.0`
- OpenMRS 3.6.0 Reference Application (O3)
- MariaDB 10.11 — port 3306 does NOT need to be exposed (REST API mode)
- Credentials: `admin / Prueba01$$xD`

## Configuration

See `parametrizacion_archivos.md` for full parameter reference. Key sections in `appsettings.json`:

```json
{
  "OpenMRS": {
    "SeedMode": "RestApi",
    "RestApi": { "BaseUrl": "http://localhost/openmrs/ws/rest/v1", "Username": "admin", "Password": "Prueba01$$xD" }
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
    },
    "Comorbidity": {
      "BaseProbability": 0.20, "MaxAdditional": 2, "SecondExtraProbability": 0.25, "AffinityBoost": 4.0,
      "AgeScaling": { "0-14": 0.3, "15-29": 0.5, "30-44": 0.8, "45-64": 1.3, "65+": 1.8 },
      "Affinities": { "diabetes": ["cardiovascular","endocrino"], "cardiovascular": ["diabetes","endocrino"], "respiratorio": ["infeccioso"] }
    }
  }
}
```

**Validation mode** (1 week, low volume): `EndDate: "2023-01-08"`, `PacientesPorDiaMedio: 8`  
**Production mode**: `EndDate: "2024-12-31"`, `PacientesPorDiaMedio: 40`

## Catalog Files

All under `catalogs/`. See `parametrizacion_archivos.md` for column schemas.

| File | Source | Purpose | Notes |
|------|--------|---------|-------|
| `epidemiology-profile.csv` | Manual | Category weights by age/gender | |
| `diagnosticos.csv` | Query DB + manual | CIEL diagnoses + boolean age group columns | |
| `medicamentos.csv` | Query DB + manual | Drugs + boolean category columns | Has `drug_uuid` + `concept_uuid` columns |
| `laboratorios.csv` | Query DB + manual | Lab tests + boolean category columns | 7 entries with verified UUIDs |
| `examenes_clinicos.csv` | Manual | In-clinic exams recorded as obs | **Empty** — all UUIDs wrong in this instance |
| `alergenos.csv` | Manual | Allergens (DRUG/FOOD/ENVIRONMENT) | 15 entries with verified UUIDs |
| `motivos_consulta.csv` | Manual | Consultation reason phrases in Spanish | |

## Verified UUID Mappings (this OpenMRS instance)

These were confirmed via `GET /ws/rest/v1/concept?q=...` against this specific installation. Do NOT assume standard CIEL UUIDs without verifying — this instance has different mappings.

### Encounter types / visit / location (appsettings.json → Defaults)
| Key | UUID | Name |
|-----|------|------|
| `VitalsEncounterTypeUuid` | `67a71486-1a54-468f-ac3e-7091a9a79584` | Vitals |
| `ConsultaEncounterTypeUuid` | `dd528487-82a5-4082-9c72-ed246bd49591` | Consultation |
| `VisitTypeUuid` | `7b0f5697-27e3-40c4-8bae-f4049abfb4ed` | Outpatient |
| `LocationUuid` | `44c3efb0-2583-4c80-a79e-1f756a03c0a1` | |
| `OnceDailyFrequencyUuid` | `136ebdb7-e989-47cf-8ec2-4e8b2ffe0ab3` | Una vez por día |
| `TabletConceptUuid` | `1513AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` | Tablet |
| `DaysConceptUuid` | `1072AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` | Days |

### Vitals concepts (VitalsSeeder hardcoded constants)
| Concept | UUID | Note |
|---------|------|------|
| Weight | `5089AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` | |
| Height | `5090AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` | |
| Systolic BP | `5085AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` | |
| Diastolic BP | `5086AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` | |
| Temperature | `5088AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` | |
| Pulse | `5087AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` | |
| SpO2 | `5242AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` | hiAbsolute=99 in this instance — max value to send: 98 |

## OpenMRS REST API Constraints (Learned from Validation)

- **Diagnoses**: must go in `encounter.diagnoses[]` field, NOT as separate obs. Fields required: `rank` (int, NOT NULL — `1` for primary, `2` for secondary/comorbidities), `certainty` (`CONFIRMED` or `PROVISIONAL`, NOT `PRESUMED`), `diagnosis.coded` (concept UUID). A single encounter may carry multiple diagnoses (comorbidity) — `ConsultaSeeder` emits `patient.TodosDiagnosticos` (primary rank=1 + each comorbidity rank=2).
- **Order urgency**: valid values are `ROUTINE`, `STAT`, `ON_SCHEDULED_DATE`. `URGENT` is NOT valid.
- **DrugOrder (dosing type "simple")**: must send `concept` (concept UUID, separate from `drug`), `quantity`, `quantityUnits`, `frequency`, `route`, `dose`, `doseUnits`.
- **Visit overlap**: if `visitCannotOverlapAnother` is returned, fetch existing active visit via `GET visit?patient={uuid}&includeInactive=false` and reuse its UUID.
- **Allergen format**: `POST /patient/{uuid}/allergy` — `allergen.codedAllergen` must be a **plain UUID string** (e.g. `"codedAllergen": "uuid-here"`), NOT a nested object `{"uuid": "..."}`. Using an object triggers `ResourceDoesNotSupportOperationException` in `ConceptResource1_8`. `severity` and `reactions[].reaction` accept `{uuid: "..."}` normally.

## Key Design Docs

- `parametrizacion_archivos.md` — full parameter and CSV schema reference
- `detalle-seeder.md` — project overview, stack, endpoints, data model
- `fases_implementacion.md` — implementation phases with deliverables and verification steps
