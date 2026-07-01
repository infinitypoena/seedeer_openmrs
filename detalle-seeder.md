# Detalle del Proyecto: OpenMRS Clinical Simulator API

> **⚠️ Estado actual (jun 2026) — `CLAUDE.md` es la fuente de verdad viva.** Este documento es el
> snapshot de **diseño** original; varias cifras/estados de abajo quedaron desfasados. Correcciones clave:
> - **Pipeline completo y funcional** — todas las "Fases" marcadas abajo están **terminadas**. Ya se corrió
>   el **año 2023 completo** (~4.178 pacientes SIM) y ventanas de 2024.
> - **Credenciales OpenMRS**: `admin / Prueba01$$xD` (NO `Admin123`). DB MariaDB expuesta en `localhost:3306`
>   (user `openmrs`, pass en el `.env` del compose).
> - **Catálogos poblados**, no "de muestra": ~948 diagnósticos (13 categorías, incl. tropicales CA),
>   ~30 medicamentos verificados, 7 labs, 15 alérgenos. Catálogos extra: `clima.csv`, `consultorios.csv`,
>   `comorbilidad_afinidades.csv`, `nombres.csv`, `apellidos.csv`.
> - **Mejoras de realismo añadidas** (ver bullets en `CLAUDE.md`): nombres únicos centroamericanos (2 nombres
>   + 2 apellidos), continuidad longitudinal de crónicos, espaciamiento realista entre visitas, coherencia
>   por sexo (columna `sexo`), y localización al español de conceptos CIEL (`scripts/agregar_nombres_es.ps1`).
> - **UUIDs verificados** de esta instancia: ver la tabla en `CLAUDE.md` (difieren de los "estándar").

## Descripción General

API REST en **C# ASP.NET Core 10** que actúa como **simulador clínico** para una instancia OpenMRS. Genera datos clínicos realistas en español siguiendo un modelo probabilístico que refleja la operación real de una clínica: volumen diario de pacientes con variación estadística, enfermedades distribuidas por edad y género, y coherencia entre diagnóstico, vitales, laboratorios, exámenes y prescripciones.

Se conecta a OpenMRS **exclusivamente via REST API** (`/ws/rest/v1`). No escribe SQL directo.

---

## Stack Tecnológico

| Componente | Tecnología |
|---|---|
| Lenguaje | C# (.NET 10) |
| Framework | ASP.NET Core 10 + Controllers |
| Conectividad OpenMRS | REST API (`/ws/rest/v1`) via HttpClient |
| Generación de datos | Bogus 35.x (locale `es`) |
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
| db | mariadb:10.11.7 | interno |

El puerto 3306 **no necesita exponerse** — el simulador usa la REST API del backend.

Credenciales por defecto: `admin / Prueba01$$xD`

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
      "Password": "Prueba01$$xD"
    },
    "DirectDb": {
      "Server": "localhost",
      "Port": 3306,
      "Database": "openmrs",
      "User": "openmrs",
      "Password": "openmrs"
    },
    "Defaults": {
      "PatientIdentifierTypeUuid": "05a29f94-c0ed-11e2-94be-8c13b969e334",
      "LocationUuid": "44c3efb0-2583-4c80-a79e-1f756a03c0a1",
      "VisitTypeUuid": "7b0f5697-27e3-40c4-8bae-f4049abfb4ed",
      "VitalsEncounterTypeUuid": "67a71486-1a54-468f-ac3e-7091a9a79584",
      "ConsultaEncounterTypeUuid": "92a52cce-c614-4046-b5f2-07f32f0bcf91",
      "ProviderUuid": "f9badd80-ab76-11e2-9e96-0800200c9a66"
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
      "FollowUp": 0.30
    },
    "Allergy": {
      "BaseProbabilityMin": 0.15,
      "BaseProbabilityMax": 0.25,
      "SecondAllergyProbability": 0.30,
      "ThirdAllergyProbability": 0.25,
      "MaxAllergies": 3
    },
    "WeekdayWeights": {
      "Monday": 1.20, "Tuesday": 1.20, "Wednesday": 1.00,
      "Thursday": 1.00, "Friday": 0.90, "Saturday": 0.50, "Sunday": 0.00
    }
  }
}
```

> **Nota sobre `Defaults`:** Los UUIDs son los valores estándar del OpenMRS Reference App 3.6 pero pueden variar por instancia. Verificar con `GET /ws/rest/v1/visittype`, `GET /ws/rest/v1/encountertype`, `GET /ws/rest/v1/location` antes de correr la simulación.

---

## Estructura de Archivos del Proyecto C#

```
openmrs_seeder_v1/openmrs_seeder_v1/
├── Configuration/
│   ├── OpenMrsSettings.cs          # RestApi + DirectDb + Defaults (6 UUIDs configurables)
│   └── SimulationSettings.cs       # Volumen, demografía, probabilidades, pesos día semana
├── Models/
│   ├── Catalogs/
│   │   ├── EpidemiologyEntry.cs    # Fila de epidemiology-profile.csv
│   │   ├── DiagnosticoEntry.cs     # Fila de diagnosticos.csv (con booleanos aplica_*)
│   │   ├── MedicamentoEntry.cs     # Fila de medicamentos.csv
│   │   ├── LaboratorioEntry.cs     # Fila de laboratorios.csv
│   │   ├── ExamenClinicoEntry.cs   # Fila de examenes_clinicos.csv
│   │   ├── AlergenoEntry.cs        # Fila de alergenos.csv
│   │   └── MotivoConsultaEntry.cs  # Fila de motivos_consulta.csv
│   ├── Api/                        # (Fase 4+) DTOs para POST a OpenMRS REST
│   │   ├── VisitRequest.cs
│   │   ├── EncounterRequest.cs
│   │   └── ObsRequest.cs
│   └── Simulation/
│       ├── SimulatedPatient.cs     # Estado del paciente durante la simulación
│       └── DailySchedule.cs        # Agenda de un día: fecha + conteo nuevos/recurrentes
├── Services/
│   ├── CatalogLoader.cs            # Lee y cachea los 7 CSV al startup
│   ├── DailyScheduleGenerator.cs   # Genera agenda completa con Box-Muller + WeekdayWeights
│   ├── PatientProfileGenerator.cs  # Genera datos demográficos con Bogus locale "es"
│   └── SeedProgressTracker.cs      # ConcurrentDictionary<Guid, SeedRun> para tracking
├── Seeders/
│   ├── PatientSeeder.cs            # POST /patient (pacientes nuevos con prefijo SIM-)      [✓]
│   ├── AllergySeeder.cs            # POST /patient/{uuid}/allergy                           [Fase 7]
│   ├── VisitSeeder.cs              # POST /visit (OUTPATIENT, hora realista)                [Fase 4]
│   ├── VitalsSeeder.cs             # Encounter VITALS + 7 obs coherentes con dx             [Fase 4]
│   ├── ConsultaSeeder.cs           # Encounter ADULTINITIAL + dx + exámenes consultorio     [Fase 5]
│   ├── LabOrderSeeder.cs           # POST /order testorder                                  [Fase 6]
│   ├── PrescriptionSeeder.cs       # POST /order drugorder                                  [Fase 7]
│   ├── VisitCloseSeeder.cs         # POST /visit/{uuid} con stopDatetime                   [Fase 8]
│   └── SeedOrchestrator.cs         # Coordina el pipeline completo por día                 [Fase 8]
├── Clients/
│   └── OpenMrsRestClient.cs        # HttpClient wrapper (BasicAuth, GetAsync/PostAsync/PingAsync)
├── Controllers/
│   └── SeedController.cs           # GET /status, POST /run, GET /progress/{runId}, DELETE /clear
├── catalogs/
│   ├── epidemiology-profile.csv    # 46 filas: pesos de categoría dx por edad/género       [✓ completo]
│   ├── diagnosticos.csv            # 11 filas de muestra                                   [pendiente datos reales]
│   ├── medicamentos.csv            # 10 filas de muestra                                   [pendiente UUIDs reales]
│   ├── laboratorios.csv            # 10 filas de muestra                                   [pendiente datos reales]
│   ├── examenes_clinicos.csv       # 12 exámenes en consultorio                            [✓ completo]
│   ├── alergenos.csv               # 20 alérgenos DRUG/FOOD/ENVIRONMENT                   [✓ completo]
│   └── motivos_consulta.csv        # 36 frases de consulta en español                     [✓ completo]
├── Program.cs
├── appsettings.json
└── appsettings.Development.json
```

---

## Endpoints REST

| Método | Ruta | Estado | Descripción |
|---|---|---|---|
| `GET` | `/api/seed/status` | ✓ | Conectividad OpenMRS + config + conteos de catálogos |
| `POST` | `/api/seed/run` | ✓ parcial | Inicia simulación en background (Fase 3: solo crea pacientes) |
| `GET` | `/api/seed/progress/{runId}` | ✓ | Progreso del run: %, etapa, fechaActual, pacientesCreados, errores |
| `DELETE` | `/api/seed/clear` | esqueleto | Void lógico de todos los registros `SIM-*` (Fase 9) |

### Respuesta de `/api/seed/status`
```json
{
  "openmrs": { "online": true, "baseUrl": "...", "seedMode": "RestApi" },
  "simulation": { "clinicType": "ConsultaExterna", "startDate": "2023-01-01", ... },
  "referralProbabilities": { "labOrder": 0.40, "clinicalExam": 0.35, ... },
  "catalogs": {
    "epidemiologyProfile": 46, "diagnosticos": 11, "medicamentos": 10,
    "laboratorios": 10, "examenesClinicos": 12, "alergenos": 20, "motivosConsulta": 36
  }
}
```

### Respuesta de `/api/seed/progress/{runId}`
```json
{
  "runId": "guid",
  "porcentaje": 42,
  "etapa": "creando_pacientes",
  "pacientesCreados": 1680,
  "diasProcesados": 150,
  "totalDias": 730,
  "fechaActual": "2023-06-01",
  "completado": false,
  "errores": [],
  "inicio": "2026-06-18T10:00:00Z"
}
```

---

## Pipeline de Simulación

Para cada día del rango `StartDate..EndDate`:

1. `DailyScheduleGenerator` calcula cuántos pacientes atiende ese día:
   - `PacientesPorDiaMedio × WeekdayWeight × Normal(μ=1, σ=0.20)` — Box-Muller transform
   - Split en `NuevosPacientes` y `PacientesRecurrentes` según `PorcentajeRecurrentes`
2. Para cada **paciente nuevo**:
   - `PatientSeeder`: genera perfil Bogus (nombre español, edad/género por distribución), crea en OpenMRS, identificador `SIM-XXXXXXXX`
   - Si `rand < prevalencia de la corrida` (15-25%, banda `Allergy.BaseProbabilityMin/Max`): `AllergySeeder` registra alergias (nº por decaída condicional, mayoría 1) _(Fase 7)_
3. Para cada **paciente recurrente**:
   - Busca UUID en OpenMRS: `GET /patient?identifier=SIM-` _(Fase 8)_
4. Selecciona categoría dx: `epidemiology-profile.csv` filtrado por edad+género, normaliza pesos _(Fase 5)_
5. Selecciona diagnóstico: `diagnosticos.csv` filtrado por categoría + `aplica_GRUPO = true`, normaliza `peso_M`/`peso_F` _(Fase 5)_
6. `VisitSeeder`: `POST /visit` OUTPATIENT con hora realista del día _(Fase 4)_
7. `VitalsSeeder`: encounter VITALS + 7 obs — rangos ajustados por categoría del dx _(Fase 4)_
8. `ConsultaSeeder`: encounter ADULTINITIAL — motivo de consulta + diagnóstico + examen en consultorio (si aplica) _(Fase 5)_
9. `LabOrderSeeder`: `POST /order` testorder si `rand < LabOrder` o `dx.RequiereLab = true` _(Fase 6)_
10. `PrescriptionSeeder`: `POST /order` drugorder si `rand < DrugOrder` o `dx.RequiereRx = true` _(Fase 7)_
11. `VisitCloseSeeder`: `POST /visit/{uuid}` con `stopDatetime` _(Fase 8)_

---

## Modelo de Datos OpenMRS (referencia REST)

| Recurso | Endpoint | Notas |
|---|---|---|
| Paciente | `POST /patient` | Incluye person, names, address, identifier (SIM-XXXXXXXX) |
| Alergia | `POST /patient/{uuid}/allergy` | Tipos: DRUG, FOOD, ENVIRONMENT |
| Visita | `POST /visit` | tipo OUTPATIENT, location, startDatetime |
| Encuentro | `POST /encounter` | visit uuid, encounterType, provider |
| Observación | `POST /obs` | encounter uuid, concept uuid, value |
| Orden lab | `POST /order` | orderType=testorder, concept uuid |
| Prescripción | `POST /order` | orderType=drugorder, drug uuid, dosis, vía |
| Cerrar visita | `POST /visit/{uuid}` | stopDatetime |

---

## Idempotencia

- **Pacientes seeded**: identificador con prefijo `SIM-` (ej: `SIM-A3F8C201`)
- **Visitas/Encounters**: campo `description` = `SEEDED_BY_SIMULATOR`
- `DELETE /api/seed/clear`: busca `GET /patient?identifier=SIM-` → void lógico en cascada
- Pacientes `SIM-` existentes se reusan como "recurrentes" en ejecuciones subsiguientes

---

## Decisiones de Diseño

| Decisión | Elección | Razón |
|---|---|---|
| Modo de conexión | REST API (no SQL directo) | Sin exposición de puertos; compatible con cualquier deploy |
| Modelo de volumen | Pacientes/día + Box-Muller + pesos día semana | Más realista que `PatientCount` fijo |
| Distribución de dx | CSV con booleanos por grupo etario | Fácil de editar en Excel, sin listas de UUIDs en celdas |
| Alergias | Registro al crear paciente (no por visita) | Las alergias son condición del paciente, no de la visita |
| Exámenes en consultorio | POST /obs en encounter ADULTINITIAL | Resultado inmediato; distinto de lab externo (POST /order) |
| Progreso del run | In-memory ConcurrentDictionary por runId | Suficiente para uso local de un solo proceso |
| Generación de datos | Bogus 35.x locale `es` | Nombres y direcciones latinoamericanas realistas |
| UUIDs configurables | Sección `OpenMRS.Defaults` en appsettings.json | Cada instancia OpenMRS puede tener UUIDs distintos |

---

## Flujo de Uso

```
1. docker compose up -d          (desde la ruta del distro)
2. Esperar ~10 min a que el backend esté healthy
3. Actualizar OpenMRS.Defaults en appsettings.json con UUIDs de tu instancia
4. dotnet run --project openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj
5. Abrir http://localhost:5197/swagger
6. GET /api/seed/status → verificar "online: true" + catálogos cargados
7. POST /api/seed/run → obtener runId
8. GET /api/seed/progress/{runId} → monitorear hasta "completado: true"
9. Verificar datos en http://localhost (OpenMRS O3 UI)
```

---

## Comandos de Desarrollo

```bash
dotnet build openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj
dotnet run --project openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj
dotnet test
```
