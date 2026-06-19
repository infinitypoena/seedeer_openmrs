# Detalle del Proyecto: OpenMRS Clinical Simulator API

## Descripción General

API REST en **C# ASP.NET Core 10** que actúa como **simulador clínico** para una instancia OpenMRS. Genera datos clínicos realistas en español siguiendo un modelo probabilístico que refleja la operación real de una clínica: volumen diario de pacientes con variación estadística, enfermedades distribuidas por edad y género, y coherencia entre diagnóstico, vitales, laboratorios, exámenes y prescripciones.

Se conecta a OpenMRS **exclusivamente via REST API** (`/ws/rest/v1`). No escribe SQL directo.

---

## Stack Tecnológico

| Componente | Tecnología |
|---|---|
| Lenguaje | C# (.NET 10) |
| Framework | ASP.NET Core 10 Minimal API + Controllers |
| Conectividad OpenMRS | REST API (`/ws/rest/v1`) via HttpClient |
| Generación de datos | Bogus (locale `es`) |
| Documentación API | Swashbuckle / Swagger UI |
| Catálogos | CSV estáticos (cargados al startup) |

---

## Infraestructura Docker

- **Ruta**: `C:\Users\Moises Molina\Documents\Estudios\Especializacion\PruebaOpenMRS1\open-omrs36-prod\openmrs-distro-referenceapplication-3.6.0`

| Servicio | Imagen | Puerto |
|---|---|---|
| gateway | openmrs-reference-application-3-gateway:3.6.0 | 80:80 |
| frontend | openmrs-reference-application-3-frontend:3.6.0 | interno |
| backend | openmrs-reference-application-3-backend:3.6.0 | interno (8080) |
| db | mariadb:10.11.7 | interno (no necesita exponerse) |

El puerto 3306 **no necesita exponerse** — el simulador usa la REST API del backend, no acceso directo a la DB.

Credenciales por defecto: `admin / Admin123`

---

## Configuración (appsettings.json)

Ver `parametrizacion_archivos.md` para referencia completa de todos los parámetros.

```json
{
  "OpenMRS": {
    "SeedMode": "RestApi",
    "RestApi": {
      "BaseUrl": "http://localhost/openmrs/ws/rest/v1",
      "Username": "admin",
      "Password": "Admin123"
    },
    "DirectDb": {
      "Server": "localhost",
      "Port": 3306,
      "Database": "openmrs",
      "User": "openmrs",
      "Password": "openmrs"
    }
  },
  "Simulation": {
    "StartDate": "2023-01-01",
    "EndDate": "2024-12-31",
    "PacientesPorDiaMedio": 40,
    "PorcentajeRecurrentes": 30,
    "Locale": "es",
    "RandomSeed": 42,
    "ClinicType": "ConsultaExterna",
    "HorarioAtencion": {
      "PicoAM": { "Inicio": "08:00", "Fin": "10:00", "Peso": 40 },
      "PicoPM": { "Inicio": "14:00", "Fin": "16:00", "Peso": 30 }
    },
    "DemographicProfile": {
      "AgeGroups": [
        { "Label": "0-14",  "Weight": 20 },
        { "Label": "15-29", "Weight": 18 },
        { "Label": "30-44", "Weight": 25 },
        { "Label": "45-64", "Weight": 25 },
        { "Label": "65+",   "Weight": 12 }
      ],
      "GenderRatio": { "M": 48, "F": 52 }
    },
    "ReferralProbabilities": {
      "LabOrder": 0.40,
      "ClinicalExam": 0.35,
      "DrugOrder": 0.65,
      "Urgent": 0.20,
      "FollowUp": 0.30,
      "AllergyOnNew": 0.15
    },
    "WeekdayWeights": {
      "Monday": 1.20, "Tuesday": 1.20, "Wednesday": 1.00,
      "Thursday": 1.00, "Friday": 0.90, "Saturday": 0.50, "Sunday": 0.00
    }
  }
}
```

---

## Estructura de Archivos del Proyecto C#

```
openmrs_seeder_v1/openmrs_seeder_v1/
├── Configuration/
│   ├── OpenMrsSettings.cs          # Mapea sección "OpenMRS": RestApi + DirectDb
│   └── SimulationSettings.cs       # Mapea sección "Simulation": volumen, demografía, probabilidades
├── Models/
│   ├── Api/
│   │   ├── PatientRequest.cs       # DTO para POST /patient
│   │   ├── VisitRequest.cs         # DTO para POST /visit
│   │   ├── EncounterRequest.cs     # DTO para POST /encounter
│   │   ├── ObsRequest.cs           # DTO para POST /obs
│   │   └── AllergyRequest.cs       # DTO para POST /patient/{uuid}/allergy
│   └── Simulation/
│       ├── SimulatedPatient.cs     # Estado del paciente durante la simulación
│       ├── DailySchedule.cs        # Agenda diaria generada (lista de atenciones)
│       └── EpidemiologyEntry.cs    # Fila del CSV epidemiology-profile
├── Services/
│   ├── CatalogLoader.cs            # Lee y cachea los 7 CSV al startup
│   ├── ConceptResolver.cs          # Resuelve UUIDs de concepts requeridos al startup
│   ├── DailyScheduleGenerator.cs   # Genera agenda diaria con variación estadística
│   ├── PatientProfileGenerator.cs  # Genera datos demográficos con Bogus locale "es"
│   └── SeedProgressTracker.cs      # Rastrea progreso por runId (ConcurrentDictionary)
├── Seeders/
│   ├── SeedOrchestrator.cs         # Coordina el pipeline completo por día y por paciente
│   ├── PatientSeeder.cs            # POST /patient (pacientes nuevos con prefijo SIM-)
│   ├── AllergySeeder.cs            # POST /patient/{uuid}/allergy (al crear paciente nuevo)
│   ├── VisitSeeder.cs              # POST /visit (OUTPATIENT, hora realista del día)
│   ├── VitalsSeeder.cs             # Encounter VITALS + 7 obs ajustados por diagnóstico
│   ├── ConsultaSeeder.cs           # Encounter ADULTINITIAL + dx + exámenes en consultorio
│   ├── LabOrderSeeder.cs           # POST /order testorder (lab externo)
│   ├── PrescriptionSeeder.cs       # POST /order drugorder (prescripción)
│   └── VisitCloseSeeder.cs         # POST /visit/{uuid} con stopDatetime
├── Clients/
│   └── OpenMrsRestClient.cs        # HttpClient wrapper (BasicAuth, retry, timeout)
├── Controllers/
│   └── SeedController.cs           # Endpoints REST del simulador
├── catalogs/
│   ├── epidemiology-profile.csv    # Pesos de categoría dx por edad/género
│   ├── diagnosticos.csv            # CIEL + booleanos por grupo etario
│   ├── medicamentos.csv            # Fármacos + booleanos por categoría dx
│   ├── laboratorios.csv            # Labs externos + booleanos por categoría dx
│   ├── examenes_clinicos.csv       # Exámenes en consultorio + booleanos por categoría dx
│   ├── alergenos.csv               # Alérgenos DRUG/FOOD/ENVIRONMENT
│   └── motivos_consulta.csv        # Frases de consulta en español
├── Program.cs
├── appsettings.json
└── appsettings.Development.json
```

---

## Endpoints REST

| Método | Ruta | Descripción | Respuesta |
|---|---|---|---|
| `GET` | `/api/seed/status` | Verifica conectividad OpenMRS REST + muestra config + conteo de datos seeded | 200 |
| `POST` | `/api/seed/run` | Inicia la simulación en background | 202 `{ runId: "guid" }` |
| `GET` | `/api/seed/progress/{runId}` | Estado del run: porcentaje, etapa actual, errores, pacientes creados | 200 |
| `DELETE` | `/api/seed/clear` | Void lógico de todos los registros `SIM-*` en cascada | 200 `{ eliminados: N }` |

---

## Pipeline de Simulación

Para cada día del rango `StartDate..EndDate`:

1. `DailyScheduleGenerator` calcula cuántos pacientes atiende ese día:
   - `PacientesPorDiaMedio × WeekdayWeight × Normal(1.0, 0.20)`
2. Para cada paciente del día:
   - **Si nuevo**: `PatientSeeder` crea el paciente con datos Bogus en español (prefijo `SIM-`)
     - Si `rand < AllergyOnNew`: `AllergySeeder` registra 1–3 alergias
   - **Si recurrente**: busca paciente existente con `SIM-` en OpenMRS
3. Selecciona categoría dx desde `epidemiology-profile.csv` (filtrado por edad+género, normalizado)
4. Selecciona diagnóstico desde `diagnosticos.csv` (filtrado por categoría + `aplica_GRUPO = true`)
5. `VisitSeeder`: crea visita OUTPATIENT con hora realista del día
6. `VitalsSeeder`: encounter VITALS con 7 obs — rangos ajustados por diagnóstico:
   - HTA → PA 140-180/90-110; Fiebre → Temp 37.5-39.5; Respiratorio grave → SpO2 88-94%
7. `ConsultaSeeder`: encounter ADULTINITIAL:
   - Motivo de consulta desde `motivos_consulta.csv`
   - Diagnóstico principal (concept CIEL)
   - Si `rand < ClinicalExam` o `dx.requiere_examen_clinico`: obs de examen en consultorio
8. Si `rand < LabOrder` o `dx.requiere_lab`: `LabOrderSeeder` → `POST /order` testorder
9. Si `rand < DrugOrder` o `dx.requiere_rx`: `PrescriptionSeeder` → `POST /order` drugorder
10. `VisitCloseSeeder`: cierra la visita con `stopDatetime`

---

## Modelo de Datos OpenMRS (referencia REST)

| Recurso | Endpoint | Notas |
|---|---|---|
| Paciente | `POST /patient` | Incluye person, names, address, identifier |
| Alergia | `POST /patient/{uuid}/allergy` | Tipos: DRUG, FOOD, ENVIRONMENT |
| Visita | `POST /visit` | tipo OUTPATIENT, location, startDatetime |
| Encuentro | `POST /encounter` | visit uuid, encounterType, provider |
| Observación | `POST /obs` | encounter uuid, concept uuid, value |
| Orden lab | `POST /order` | orderType=testorder, concept uuid |
| Prescripción | `POST /order` | orderType=drugorder, drug uuid, dosis, vía |
| Cerrar visita | `POST /visit/{uuid}` | stopDatetime |

---

## Idempotencia

- **Pacientes seeded**: identificador con prefijo `SIM-` (ej: `SIM-00042`)
- **Visitas/Encounters**: campo `description` = `SEEDED_BY_SIMULATOR`
- `DELETE /api/seed/clear`: busca `GET /patient?identifier=SIM-` → void lógico en cascada
- Pacientes `SIM-` existentes se reusan como "recurrentes" en ejecuciones subsiguientes

---

## Decisiones de Diseño

| Decisión | Elección | Razón |
|---|---|---|
| Modo de conexión | REST API (no SQL directo) | Sin exposición de puertos; compatible con cualquier deploy |
| Modelo de volumen | Pacientes/día + pesos día semana | Más realista que `PatientCount` fijo |
| Distribución de dx | CSV con booleanos por grupo etario | Fácil de editar en Excel, sin listas de UUIDs en celdas |
| Alergias | Registro al crear paciente | Las alergias son condición del paciente, no de la visita |
| Exámenes en consultorio | POST /obs en encounter ADULTINITIAL | Resultado inmediato; distinto de lab externo (POST /order) |
| Progreso del run | In-memory con runId GUID | Suficiente para uso local |
| Generación de datos | Bogus locale `es` | Nombres y direcciones latinoamericanas realistas |

---

## Flujo de Uso

```
1. docker compose up -d   (desde la ruta del distro)
2. Esperar ~10 min a que el backend OpenMRS esté healthy
3. dotnet run --project openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj
4. Abrir http://localhost:5197/swagger
5. GET /api/seed/status → verificar conexión OK + catálogos cargados
6. POST /api/seed/run → obtener runId
7. GET /api/seed/progress/{runId} → monitorear hasta 100%
8. Verificar datos en http://localhost (OpenMRS O3 UI)
```

---

## Comandos de Desarrollo

```bash
dotnet build openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj
dotnet run --project openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj
dotnet test
```
