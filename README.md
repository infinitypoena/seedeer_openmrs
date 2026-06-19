# OpenMRS Clinical Simulator

API REST en C# .NET 10 que actúa como **simulador clínico** para una instancia [OpenMRS 3.x](https://openmrs.org/). Genera datos clínicos realistas en español siguiendo un modelo probabilístico: volumen diario de pacientes con variación estadística, enfermedades distribuidas por edad y género, y coherencia entre diagnóstico, vitales, laboratorios, exámenes en consultorio, prescripciones y alergias.

---

## Inicio rápido

```bash
# 1. Levantar OpenMRS
cd "PruebaOpenMRS1/open-omrs36-prod/openmrs-distro-referenceapplication-3.6.0"
docker compose up -d
# Esperar ~10 min hasta que el backend esté healthy

# 2. Ejecutar el simulador
dotnet run --project openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj

# 3. Abrir Swagger UI
# http://localhost:5197/swagger

# 4. Verificar conexión
# GET /api/seed/status

# 5. Iniciar simulación
# POST /api/seed/run

# 6. Monitorear progreso
# GET /api/seed/progress/{runId}
```

---

## Cómo funciona

El simulador genera datos día por día dentro del rango `StartDate..EndDate`:

1. Calcula cuántos pacientes atiende cada día según `PacientesPorDiaMedio` y pesos por día de semana
2. Para cada paciente elige edad/género según la distribución configurada
3. Selecciona un diagnóstico del perfil epidemiológico (`epidemiology-profile.csv`) coherente con la edad y género
4. Crea en OpenMRS via REST API: paciente → alergias → visita → vitales → consulta → labs → prescripciones

Todo es configurable desde `appsettings.json` y los CSV de catálogos.

---

## Endpoints

| Método | Ruta | Descripción |
|--------|------|-------------|
| `GET` | `/api/seed/status` | Estado de conexión + config + conteo de datos seeded |
| `POST` | `/api/seed/run` | Inicia simulación en background → `{ runId }` |
| `GET` | `/api/seed/progress/{runId}` | Progreso: porcentaje, etapa, errores |
| `DELETE` | `/api/seed/clear` | Elimina todos los datos simulados (`SIM-*`) |

---

## Configuración principal

```json
"Simulation": {
  "StartDate": "2023-01-01",
  "EndDate": "2024-12-31",
  "PacientesPorDiaMedio": 40,
  "PorcentajeRecurrentes": 30,
  "ClinicType": "ConsultaExterna",
  "ReferralProbabilities": {
    "LabOrder": 0.40,
    "ClinicalExam": 0.35,
    "DrugOrder": 0.65,
    "AllergyOnNew": 0.15
  }
}
```

Ver `parametrizacion_archivos.md` para la referencia completa de todos los parámetros y catálogos.

---

## Documentación

| Archivo | Contenido |
|---------|-----------|
| `parametrizacion_archivos.md` | Referencia completa de parámetros y estructura de catálogos CSV |
| `detalle-seeder.md` | Arquitectura, stack, pipeline y decisiones de diseño |
| `fases_implementacion.md` | Plan de implementación por fases con entregables y verificación |
| `CLAUDE.md` | Guía para Claude Code al trabajar en este repositorio |

---

## Stack

- **C# .NET 10** — ASP.NET Core 10
- **OpenMRS 3.6.0** via REST API (`/ws/rest/v1`)
- **Bogus** (locale `es`) para generación de datos demográficos
- **Swashbuckle** para Swagger UI
