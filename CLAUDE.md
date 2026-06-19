# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Purpose

.NET Core 10 Web API that seeds and simulates realistic clinical operations for an [OpenMRS](https://github.com/openmrs/openmrs-core) database. The goal is to populate OpenMRS with believable patient records, visits, encounters, observations, and orders that reflect a real-world clinic workflow.

## Build & Run

```bash
dotnet build
dotnet run --project src/Seedeer.Api
dotnet test
dotnet test --filter "FullyQualifiedName~ClassName"   # run a single test class
```

## Architecture

The API targets the OpenMRS MySQL/MariaDB schema directly — it does not go through the OpenMRS REST API. Seeders write to OpenMRS tables using the OpenMRS data model conventions (UUIDs as PKs via `uuid()`, audit columns `creator`/`date_created`/`voided`, etc.).

Key architectural decisions:
- **Seeder pipeline**: each domain (patients, visits, encounters, observations) is a self-contained seeder class that receives a shared `SeedContext` (DB connection + randomization state).
- **Realistic data generation**: use a faker library (e.g., Bogus) with locale `es` (Spanish-speaking clinic) to generate names, addresses, phone numbers, and dates.
- **OpenMRS concept dependencies**: observations reference concept IDs that must exist in the target OpenMRS instance. The seeder validates required concepts at startup before writing any data.
- **Idempotency**: all seeders check for existing seeded records (via a tag in `description`/`comment` fields) so re-running is safe.

## OpenMRS Data Model Notes

- Every persisted entity requires: `uuid` (char 38), `creator` (FK → `users.user_id`), `date_created`, `voided` (tinyint, default 0).
- `patient` extends `person` — insert `person` first, then `patient`.
- Visit → Encounter → Obs hierarchy: a visit holds one or more encounters; each encounter holds observations.
- Concept IDs vary by OpenMRS instance/distribution. Use `concept.uuid` lookups rather than hardcoded IDs; resolve at startup and cache.
- Required reference data: at minimum one `Location`, one `Provider`, one admin `User` must exist before seeding.

## Configuration

```json
// appsettings.json
{
  "OpenMRS": {
    "ConnectionString": "Server=localhost;Database=openmrs;User=root;Password=...",
    "AdminUserId": 1,
    "DefaultLocationUuid": "...",
    "DefaultProviderUuid": "..."
  },
  "Seed": {
    "PatientCount": 100,
    "VisitsPerPatient": 5,
    "Locale": "es"
  }
}
```
