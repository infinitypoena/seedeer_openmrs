# Fases de Implementación — OpenMRS Clinical Simulator

> **Convención de estado:**
> - `[ ]` Pendiente
> - `[~]` En progreso
> - `[x]` Completado

---

## FASE 1 — Proyecto C# base + configuración
**Estado:** `[x]`
**Depende de:** Nada (punto de partida)

### Objetivo
Reemplazar el scaffold F# con un proyecto C# .NET 10 funcional, compilable, con toda la configuración cargada desde `appsettings.json` y un endpoint de status que verifique la conexión a OpenMRS.

### Entregables
- [x] Eliminar `.fsproj` F# del proyecto
- [x] Crear `openmrs_seeder_v1.csproj` (C#, net10.0)
- [x] Actualizar `openmrs_seeder_v1.slnx` para apuntar al `.csproj`
- [x] NuGet packages: `Bogus`, `MySqlConnector`, `Swashbuckle.AspNetCore`
- [x] `Configuration/OpenMrsSettings.cs` — sección `OpenMRS` (RestApi + DirectDb)
- [x] `Configuration/SimulationSettings.cs` — sección `Simulation` (volumen, demografía, probabilidades, pesos día semana)
- [x] `appsettings.json` con estructura completa
- [x] `Program.cs` — DI, Swagger habilitado
- [x] `Clients/OpenMrsRestClient.cs` — HttpClient wrapper (BasicAuth, timeout 30s, GetAsync/PostAsync/PingAsync)
- [x] `Controllers/SeedController.cs` — `GET /api/seed/status`, `POST /run`, `GET /progress/{runId}`, `DELETE /clear` (esqueleto)
- [x] `Services/SeedProgressTracker.cs` — ConcurrentDictionary<Guid, SeedRun>

### Verificación
```
dotnet build → 0 errores
dotnet run → Swagger en http://localhost:5197/swagger
GET /api/seed/status → 200 con config cargada + ping a OpenMRS REST API
```

---

## FASE 2 — Catálogos clínicos
**Estado:** `[x]`
**Depende de:** Fase 1

### Objetivo
Crear los catálogos CSV con datos clínicos reales y el `CatalogLoader` que los carga al startup.

### Estrategia de datos
- **4 CSVs manuales** (datos completos, sin Docker): `epidemiology-profile.csv`, `examenes_clinicos.csv`, `alergenos.csv`, `motivos_consulta.csv`
- **3 CSVs de muestra** (10-11 filas de prueba, reemplazar con datos reales cuando Docker corra): `diagnosticos.csv`, `medicamentos.csv`, `laboratorios.csv`

### Entregables
- [x] Carpeta `catalogs/` dentro del proyecto C# con los 7 CSVs
- [x] `.csproj` actualizado con `<Content Include="catalogs\**\*.csv">` para copiar al output
- [x] `catalogs/epidemiology-profile.csv` — 46 filas: pesos por categoría/edad/género
- [x] `catalogs/examenes_clinicos.csv` — 12 exámenes en consultorio con booleanos por categoría
- [x] `catalogs/alergenos.csv` — 20 alérgenos DRUG/FOOD/ENVIRONMENT
- [x] `catalogs/motivos_consulta.csv` — 36 frases en español por categoría
- [x] `catalogs/diagnosticos.csv` — 11 filas de muestra (pendiente datos reales de DB)
- [x] `catalogs/medicamentos.csv` — 10 filas de muestra (pendiente UUIDs reales de drug)
- [x] `catalogs/laboratorios.csv` — 10 filas de muestra (pendiente datos reales de DB)
- [x] `Models/Catalogs/` — 7 clases POCO: `EpidemiologyEntry`, `DiagnosticoEntry`, `MedicamentoEntry`, `LaboratorioEntry`, `ExamenClinicoEntry`, `AlergenoEntry`, `MotivoConsultaEntry`
- [x] `Services/CatalogLoader.cs` — lee y cachea los 7 CSV al startup con parser CSV que soporta campos entre comillas
- [x] `SeedController.GET /status` actualizado con sección `catalogs` (conteos)

### Queries SQL para reemplazar los 3 CSVs de muestra
Ejecutar cuando Docker esté corriendo (`docker exec -it <db_container> mysql -u openmrs -popenmrs openmrs`):

**Query 1 — Diagnósticos CIEL con ICD-10:**
```sql
SELECT c.uuid AS ciel_uuid, cn.name AS nombre_es, cc.name AS categoria
FROM concept c
JOIN concept_name cn ON c.concept_id = cn.concept_id
    AND cn.locale = 'es' AND cn.concept_name_type = 'FULLY_SPECIFIED' AND cn.voided = 0
JOIN concept_class cc ON c.concept_class_id = cc.concept_class_id
WHERE c.voided = 0 AND cc.name IN ('Diagnosis','Finding')
ORDER BY cn.name LIMIT 500;
-- Luego agregar manualmente: severidad, aplica_*, peso_M, peso_F, requiere_lab, requiere_rx, requiere_examen_clinico
```

**Query 2 — Medicamentos:**
```sql
SELECT d.uuid AS drug_uuid, d.name AS nombre_generico, d.strength
FROM drug d WHERE d.retired = 0 ORDER BY d.name;
-- Luego agregar: via_uuid y columnas aplica_*
```

**Query 3 — Laboratorios (concepts tipo Test):**
```sql
SELECT c.uuid AS ciel_uuid, cn.name AS nombre_es, cc.name AS clase
FROM concept c
JOIN concept_name cn ON c.concept_id = cn.concept_id
    AND cn.locale = 'es' AND cn.concept_name_type = 'FULLY_SPECIFIED' AND cn.voided = 0
JOIN concept_class cc ON c.concept_class_id = cc.concept_class_id
WHERE c.voided = 0 AND cc.name IN ('Test','LabSet','Lab Findings')
ORDER BY cn.name;
-- Luego agregar columnas aplica_*
```

### Verificación
```
GET /api/seed/status → "catalogs": { "epidemiologyProfile": 46, "diagnosticos": 11, ... }
```

---

## FASE 3 — Agenda diaria + Seeder de pacientes
**Estado:** `[x]`
**Depende de:** Fase 1, Fase 2

### Objetivo
Implementar el generador de agenda diaria con variación estadística y crear pacientes nuevos en OpenMRS con datos demográficos realistas en español.

### Entregables
- [x] `Models/Simulation/DailySchedule.cs` — fecha, TotalPatients, NuevosPacientes, PacientesRecurrentes
- [x] `Models/Simulation/SimulatedPatient.cs` — datos del paciente + OpenMrsUuid, AgeGroup, Categoria, Address
- [x] `Services/DailyScheduleGenerator.cs` — genera agenda completa del rango StartDate..EndDate:
  - `PacientesPorDiaMedio × WeekdayWeight × Normal(μ=1, σ=0.20)` — Box-Muller transform
  - `PorcentajeRecurrentes` → split nuevos/recurrentes por día
- [x] `Services/PatientProfileGenerator.cs` — genera perfil demográfico con Bogus locale `es`:
  - Género según `GenderRatio`, grupo etario según `AgeGroups`, fecha de nacimiento dentro del rango etario
  - Identificador `SIM-XXXXXXXX` (8 hex aleatorios), nombre/apellido/dirección con Bogus
- [x] `Seeders/PatientSeeder.cs` — `POST /ws/rest/v1/patient` con person, names, birthdate, gender, address, identifier
- [x] `Configuration/OpenMrsSettings.cs` — agrega `DefaultsSettings` con 6 UUIDs configurables:
  - `PatientIdentifierTypeUuid`, `LocationUuid`, `VisitTypeUuid`, `VitalsEncounterTypeUuid`, `ConsultaEncounterTypeUuid`, `ProviderUuid`
- [x] `appsettings.json` — sección `OpenMRS.Defaults` con valores por defecto del Reference App
- [x] `Services/SeedProgressTracker.cs` — agrega `TotalDias`, `DiasProcesados`, `FechaActual` a `SeedRun`
- [x] `SeedController.POST /run` — pipeline real en background: genera agenda → por cada día → crea NuevosPacientes
- [x] `SeedController.GET /progress/{runId}` — retorna `diasProcesados`, `totalDias`, `fechaActual`

### Nota sobre UUIDs en Defaults
Los valores por defecto son del OpenMRS Reference App 3.x estándar pero **deben verificarse** contra la instancia real:
```
GET /ws/rest/v1/patientidentifiertype  → buscar "OpenMRS ID"
GET /ws/rest/v1/location               → buscar ubicación de la clínica
GET /ws/rest/v1/visittype              → buscar "Outpatient"
GET /ws/rest/v1/encountertype          → buscar "Vitals" y "Consultation"
```

### Verificación
```
dotnet build → 0 errores
POST /api/seed/run → 202 { runId }
GET /api/seed/progress/{runId} → porcentaje aumenta hasta 100%, fechaActual avanza
→ Pacientes SIM-* visibles en OpenMRS O3 UI con nombres en español
```

---

## FASE 4 — Seeder de visitas + vitales
**Estado:** `[ ]`
**Depende de:** Fase 3

### Objetivo
Crear visitas con hora realista del día y registrar signos vitales coherentes con el diagnóstico del paciente.

### Conceptos CIEL vitales (UUIDs estándar OpenMRS Reference App)

| Concepto | UUID CIEL |
|---|---|
| Peso (kg) | `5089AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` |
| Talla (cm) | `5090AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` |
| PA sistólica (mmHg) | `5085AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` |
| PA diastólica (mmHg) | `5086AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` |
| Temperatura (°C) | `5088AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` |
| Pulso (lpm) | `5087AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` |
| Frecuencia respiratoria (rpm) | `5242AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` (hiAbsolute=99) |
| SpO2 (%) | `5092AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` (⚠️ NO `5242`, que es frecuencia respiratoria) |

### Entregables
- [ ] `Models/Api/VisitRequest.cs`, `EncounterRequest.cs`, `ObsRequest.cs`
- [ ] `Seeders/VisitSeeder.cs` — `POST /visit` (tipo OUTPATIENT, hora calculada según HorarioAtencion pico AM/PM, location configurable)
- [ ] `Seeders/VitalsSeeder.cs` — encounter VITALS + 7 obs con rangos ajustados por categoría dx:
  - Rangos base: Peso 45-120 kg, Talla 145-195 cm, PA 100-130/60-85, Temp 36.0-37.4, Pulso 60-100, SpO2 96-100%
  - `cardiovascular` (HTA): PA sistólica 140-180, diastólica 90-110
  - `infeccioso` (fiebre): Temp 37.5-39.5, Pulso 90-110
  - `respiratorio` grave: SpO2 88-94%, FR elevada
  - `diabetes`: peso tendencia alta (BMI 25-35)
- [ ] Integrar en el pipeline de `/run`: `VisitSeeder` → `VitalsSeeder` tras crear el paciente

### Verificación
```
→ Historial de visitas visible en O3 chart del paciente
→ Signos vitales graficados con valores coherentes al diagnóstico
```

---

## FASE 5 — Seeder de consulta clínica
**Estado:** `[ ]`
**Depende de:** Fase 4, Fase 2

### Objetivo
Crear el encounter ADULTINITIAL con diagnóstico coherente con el perfil epidemiológico, motivo de consulta y exámenes realizados en consultorio.

### Conceptos clave de OpenMRS para diagnósticos
- Concept diagnóstico: `1284AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` (coded diagnosis)
- Concept motivo de consulta: `162169AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA`
- Certeza confirmado: `1066AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA`
- Certeza presuntivo: `1067AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA`

### Entregables
- [ ] `Seeders/ConsultaSeeder.cs`:
  - Selecciona categoría dx desde `epidemiology-profile.csv` (filtrado por edad+género del paciente, pesos normalizados)
  - Selecciona diagnóstico desde `diagnosticos.csv` (filtrado por `categoria` + `aplica_GRUPO = true`, normaliza `peso_M`/`peso_F`)
  - Certeza: 70% confirmado (`1066`), 30% presuntivo (`1067`)
  - Obs motivo de consulta: texto desde `motivos_consulta.csv` filtrado por `categoria`
  - Si `rand < ClinicalExam` (0.35) **o** `dx.RequiereExamenClinico = true` (prob sube a 90%):
    → filtrar `examenes_clinicos.csv` por `aplica_CATEGORIA = true`
    → si `TipoResultado = "numerico"`: obs con valor numérico en rango razonable + unidad
    → si `TipoResultado = "categorico"`: obs con valor "normal" (80%) o "anormal" (20%)
- [ ] Asignar `patient.Categoria` en el perfil antes de crear la visita (viene del paso de selección epidemiológica)
- [ ] Integrar en el pipeline de `/run`

### Verificación
```
→ Diagnósticos visibles en el chart del paciente en O3
→ Motivo de consulta en español en la nota clínica
→ Obs de examen en consultorio (ej: glucometría, ECG) visible en el encuentro
```

---

## FASE 6 — Seeder de órdenes de laboratorio
**Estado:** `[ ]`
**Depende de:** Fase 5, Fase 2

### Objetivo
Crear órdenes de exámenes externos coherentes con el diagnóstico elegido.

### Entregables
- [ ] `Seeders/LabOrderSeeder.cs`:
  - Aplica si `rand < LabOrder` (0.40) **o** `dx.RequiereLab = true`
  - 1-2 órdenes por visita
  - Selecciona desde `laboratorios.csv` filtrado por `aplica_CATEGORIA = true`
  - `POST /order` tipo `testorder`, care setting OUTPATIENT
  - Priority: ROUTINE por defecto; URGENT si `rand < Urgent` (0.20) o `dx.Severidad = "grave"` (prob. sube a 50%)
- [ ] Integrar en el pipeline de `/run`

### Verificación
```
→ Órdenes visibles en módulo de laboratorio de O3
→ Coherencia: HTA genera perfil lipídico, diabetes genera HbA1c, infeccioso genera hemograma
```

---

## FASE 7 — Prescripciones + Alergias
**Estado:** `[ ]`
**Depende de:** Fase 5, Fase 2

### Objetivo
Crear prescripciones coherentes con el diagnóstico y registrar alergias para pacientes nuevos.

### Entregables
- [ ] `Seeders/PrescriptionSeeder.cs`:
  - Aplica si `rand < DrugOrder` (0.65) **o** `dx.RequiereRx = true`
  - 1-3 medicamentos por visita
  - Selecciona desde `medicamentos.csv` filtrado por `aplica_CATEGORIA = true`
  - `POST /order` tipo `drugorder` con drug uuid, dosis, vía de administración
  - Duración: 7, 14 o 30 días; frecuencia: "ONCE DAILY", "TWICE DAILY", "THREE TIMES DAILY"
- [ ] `Seeders/AllergySeeder.cs`:
  - Aplica **al crear paciente nuevo** si `rand < AllergyOnNew` (0.15)
  - Registrar 1-3 alergias al azar de `alergenos.csv`
  - `POST /patient/{uuid}/allergy` con `allergenType`, `codedAllergen.uuid`, `severity.uuid`
- [ ] Integrar ambos en el pipeline de `/run`

### Verificación
```
→ Medicamentos activos visibles en el chart del paciente en O3
→ Coherencia: HTA recibe enalapril/losartán, diabetes recibe metformina, respiratorio recibe amoxicilina
→ ~15% de pacientes nuevos tienen alergias visibles en el chart
```

---

## FASE 8 — SeedOrchestrator + cierre de visitas
**Estado:** `[ ]`
**Depende de:** Fases 3-7

### Objetivo
Extraer la lógica de coordinación del pipeline desde `SeedController` hacia un `SeedOrchestrator` dedicado, y cerrar las visitas correctamente al final de cada día.

### Entregables
- [ ] `Seeders/VisitCloseSeeder.cs` — `POST /visit/{uuid}` con `stopDatetime` (1-4 horas después del `startDatetime`)
- [ ] `Seeders/SeedOrchestrator.cs` — contiene el pipeline completo por día y por paciente:
  1. `DailyScheduleGenerator` → lista de atenciones
  2. Para cada **paciente nuevo**: `PatientSeeder` → `AllergySeeder` → `VisitSeeder` → `VitalsSeeder` → `ConsultaSeeder` → `LabOrderSeeder` → `PrescriptionSeeder` → `VisitCloseSeeder`
  3. Para cada **paciente recurrente**: buscar UUID en OpenMRS (`GET /patient?identifier=SIM-`) → `VisitSeeder` → pipeline desde VitalsSeeder en adelante
- [ ] `SeedController.POST /run` — delega completamente al `SeedOrchestrator`
- [ ] Progreso detallado: etapa actual, fecha del día procesado, errores por paciente sin detener el pipeline

### Verificación
```
POST /api/seed/run → 202
GET /api/seed/progress/{runId} → progreso hasta 100% con etapa y fecha actual
→ Historial completo en O3: visitas, vitales, diagnósticos, labs, medicamentos, alergias
→ Pacientes recurrentes tienen múltiples visitas en el historial
```

---

## FASE 9 — Endpoint de limpieza
**Estado:** `[ ]`
**Depende de:** Fase 3+

### Objetivo
Borrar todos los datos generados por el simulador de forma segura (void lógico via REST).

### Estrategia
`GET /ws/rest/v1/patient?identifier=SIM-&v=full` → lista todos los pacientes seeded → void en cascada: visitas → encounters → obs → orders → alergias → paciente.

### Entregables
- [ ] `DELETE /api/seed/clear` — void lógico de todos los registros `SIM-*`
- [ ] Responde con `{ "pacientes": N, "visitas": N, "encounters": N, "ordenes": N }`
- [ ] Rate limiting interno: 200ms entre requests para no saturar el backend OpenMRS

### Verificación
```
DELETE /api/seed/clear → 200 con conteo
→ Pacientes SIM-* ya no visibles en OpenMRS O3
```

---

## FASE 10 — Manual de Usuario
**Estado:** `[x]`
**Depende de:** Ninguna fase de código (documentación independiente)

### Objetivo
Crear documentación de usuario completa con énfasis especial en el manejo del tiempo, dado que es el aspecto más crítico del simulador para entender los datos generados.

### Entregables
- [x] `manual_usuario.md` con secciones: introducción, prerrequisitos, configuración, arranque Docker, ejecución, monitoreo, verificación, limpieza, personalización, manejo del tiempo, troubleshooting
- [x] Sección 10 "Manejo del Tiempo" con detalle de:
  - Ventana de simulación (`StartDate`/`EndDate`)
  - Cálculo de volumen diario (Box-Muller, tabla de pesos por día de semana)
  - Distribución intradiaria de horas (PicoAM / PicoPM / Resto)
  - Flujo completo de timestamps por cada seeder (tabla con campo OpenMRS + ejemplo)
  - Formato UTC enviado a OpenMRS REST (`FormatDatetime`)
  - Cálculo de fecha de nacimiento (referencia `DateTime.Today`, implicaciones)
  - Reproducibilidad (`RandomSeed` y sus variantes por componente)
  - Estimación de tiempo real de ejecución (tabla latencia × pacientes totales)

### Verificación
```
manual_usuario.md existe en la raíz del proyecto junto con fases_implementacion.md y detalle-seeder.md
```

---

## Registro de cambios

| Fecha | Fase | Cambio |
|-------|------|--------|
| 2026-06-18 | — | Plan inicial creado |
| 2026-06-18 | — | Rediseño: REST API en lugar de SQL directo |
| 2026-06-18 | — | Modelo epidemiológico con CSVs booleanos |
| 2026-06-18 | — | Agregados: AllergySeeder, ClinicalExamSeeder, DailyScheduleGenerator |
| 2026-06-18 | 1 | Fase 1 completada: proyecto C# base, DI, Swagger, configuración |
| 2026-06-18 | 2 | Fase 2 completada: 7 CSVs + 7 modelos POCO + CatalogLoader |
| 2026-06-18 | 3 | Fase 3 completada: DailyScheduleGenerator + PatientProfileGenerator + PatientSeeder + DefaultsSettings + pipeline en /run |
| 2026-06-18 | 4 | Fase 4 completada: VisitSeeder + VitalsSeeder + pipeline actualizado |
| 2026-06-19 | 5 | Fase 5 completada: ConsultaSeeder (ADULTINITIAL + dx + certeza + motivo + examen clínico) |
| 2026-06-19 | 10 | Fase 10 completada: manual_usuario.md con sección detallada de manejo del tiempo |
