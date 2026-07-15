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
  - `PorcentajeRecurrentes` → split nuevos/recurrentes por día · ⚠️ **RETIRADO el 14-jul-2026**: el cupo fijo de recurrentes estranguló la agenda (ver el registro de cambios al final). Hoy los recurrentes son la demanda real del panel, no un porcentaje.
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
**Estado:** `[x]`
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
**Estado:** `[x]`
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
**Estado:** `[x]`
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
**Estado:** `[x]`
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
**Estado:** `[x]`
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
**Estado:** `[x]`
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
manual_usuario.md existe en la raíz del proyecto junto con fases_implementacion.md
```

---

## Backlog de mejoras (inventario P1–P9, jul 2026)

> Inventario de pendientes acordado en jul 2026. **Fuente de verdad del "qué sigue"** — actualizar
> el estado aquí al completar cada punto (un commit por bloque a `develop2`).
> Patrón por bloque: verificar UUIDs contra la instancia → código+tests → smoke REST → corrida corta → docs → commit.

| # | Punto | Estado | Notas |
|---|-------|--------|-------|
| P1 | Rotar password root de MariaDB (quedó en historial de git) | `[ ]` decisión del usuario | Solo importa si el repo se hace público |
| P2 | Doc de usuario/rol dedicado para el seeder (privilegios mínimos) | `[x]` | `manual_usuario.md` §5 — commit `f4f99b0` |
| P3 | Revivir exámenes clínicos (rama muerta, UUIDs malos) | `[x]` | 10 conceptos smoke-tested + `res_min/max` — commit `f4f99b0` |
| P4 | Citas: sweep global de vencidas al cierre + `clear` cancela citas | `[x]` | commit `12b8db4` |
| P5 | Labs v2: paneles (obs-group hemograma) + resultado retrasado | `[x]` | `paneles.csv` + `ResultadosPendientes` — esta entrega. De paso: fix `dateActivated` de órdenes |
| P6 | ~~Vitales a catálogo~~ → **reformulado**: overrides de vitales por enfermedad (`vital_pa`, `vital_fc`, `vital_spo2`) | `[x]` | El CSV de rangos aportaba poco (lógica acoplada, constantes estables); lo que se quería era que la ENFERMEDAD dispare el vital anormal — mismo patrón `vital_fiebre`/`vital_imc` |
| P7 | Atributos de persona: teléfono salvadoreño + estado civil coherente con edad | `[x]` | Anidados en el `person` del `POST /patient`; answers de Estado civil `1054` verificados; `Defaults.*AttributeTypeUuid` vacío = off |
| P8 | Address Hierarchy de El Salvador (best-effort) | `[ ]` | Si el módulo no expone REST práctico, documentar procedimiento UI/CSV con `direcciones.csv` como fuente; no bloquear |
| P9 | Quitar `_poolLock` vestigial (la corrida es secuencial) | `[x]` | Eliminado con P6 |

Verificación final del paquete (al cerrar P6–P7): corrida de 1 mes comprobando exámenes presentes,
0 citas `Scheduled` vencidas, hemograma con `groupMembers`, resultados retrasados en segunda visita,
atributos en `person_attribute`, vitales con distribución idéntica al hardcodeado.

### Cierre de brechas de realismo longitudinal (Bloques 1–6, jul 2026)

Auditoría del código vs. la historia esperada de un crónico a un año: la agenda no hacía volver al
paciente, un crónico nunca repetía lab/fármaco, y su talla/edad/comorbilidades eran incoherentes.

| # | Punto | Estado | Notas |
|---|-------|--------|-------|
| B2 | Órdenes repetibles: `OrderedConcepts` HashSet→`Dictionary<uuid,vigenteHasta>`, `autoExpireDate`, `Orders.LabVigenciaDias` | `[x]` | Seam `OrderVigencia.EstaActivo` + tests |
| B1 | La cita gobierna el retorno: `SeguimientoPolicy` (dx-condicionado) + `RecurrentSelector` (cita hoy ±tol, `Appointments.AsistenciaProb`) + unificación cita=próxima elegible | `[x]` | Seams + tests |
| B3 | Continuidad física: `TallaCm`/`ImcBasal` persistentes, talla pediátrica por edad, `GrupoEdad` recalculado | `[x]` | Seams `GrupoEdad`/`EdadEnMeses`/`TallaPediatricaCm` + tests |
| B4 | Coherencia de la visita: A6 examen por unión de categorías · A8 obs fechadas con `FechaConsulta` · A9 comorbilidades estables en control · A10 Ctrl+C limpio (`ErrorTally.MarcarCancelacion`) · A11 relleno de cupo con nuevos | `[x]` | — |
| B5 | Posología de catálogo: columnas opcionales `dosis`/`unidad_dosis_uuid`/`frecuencia_uuid`/`dias_tratamiento` (vacías = 1 tableta/una vez al día) | `[x]` | Verificar UUID de frecuencia/unidad contra la instancia antes de poblar |
| B6 | Documentación: `manual_usuario.md`, `parametrizacion_archivos.md`, `CLAUDE.md`, este registro | `[x]` | UTC→`UtcOffset`, CLI batch, conteos 21/86/73/53, TOC §7, nº tests, docstring `LabResult`, `RepeticionDamping` |

Verificación end-to-end pendiente (requiere instancia): agenda ≥60 % citas vencidas en `Completed`;
crónico con ≥3 controles con ≥2 HbA1c y ≥2 recetas del mismo fármaco y problem list estable; una sola
talla por adulto; 0 obs con `obs_datetime` < `encounter_datetime`; 0 `AmbiguousOrderException`.

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
| 2026-06 | 6-9 | Fases 6-9 completadas: LabOrderSeeder, PrescriptionSeeder + AllergySeeder, SeedOrchestrator + VisitCloseSeeder, DELETE /clear. Validación superada; corridas de año completo (2023 y 2024) |
| 2026-06/07 | — | Iteración de realismo (post-fases, ver bullets en CLAUDE.md): comorbilidad, clima estacional, consultorios + médico de cabecera, problem list (crónicas), nombres únicos centroamericanos, continuidad de crónicos, espaciamiento entre visitas, coherencia por sexo, localización es de conceptos CIEL |
| 2026-07 | — | Cierre de brechas longitudinales (Bloques 1–6): la cita gobierna el retorno, órdenes repetibles por vigencia, talla/IMC/edad persistentes, coherencia de la visita (A6/A8/A9/A10/A11), posología de catálogo, y sincronización documental. +33 tests (177→210) |
| 2026-07 | — | Roster diario de médicos (2-3/día) + resultados de laboratorio ligados a la orden (ciclo orden→resultado) |
| 2026-07 | — | Inscripción a programas de atención (HIV Care and Treatment, Diabetes Education) vía POST /programenrollment |
| 2026-07 | — | Citas reales en la agenda O3 (Bahmni Appointments): FollowUp agenda cita; al volver el paciente se marca Completed/Missed. Fix precisión ASAT/amilasa (allow_decimal=0) |
| 2026-07-10 | — | Seguimiento agudo coherente: el no-crónico que vuelve retorna por el MISMO dx agudo (2,3% → ~62-64% de pares consecutivos con mismo dx; verificado en corridas feb/mar 2025) |
| 2026-07-10 | — | **Conversión a app de consola batch** (`develop2`): `dotnet run` ejecuta la simulación completa y termina (exit codes 0/1/2, Ctrl+C limpio); se elimina la capa de controllers/Swagger (la Web API queda en `develop1`). Saneamiento de appsettings + validación fail-fast (`SettingsValidator`); fix TSH (concepto Drug datatype N/A) + contador preciso de errores (`ErrorTally`); variedad de diagnósticos (damping anti-repetición: 144→182 dx distintos); hardening de la instancia (backup, TZ America/El_Salvador, UI en es); direcciones salvadoreñas (`direcciones.csv`) |
| 2026-07-11 | — | Direcciones ampliadas a los 14 departamentos (~142 zonas) con pesos por **anillos de distancia** (clínica en San Salvador); paquete de auditoría: reproducibilidad, retry REST, credencial y hardening |
| 2026-07-11 | — | Exámenes clínicos revividos (10 UUIDs smoke-tested: Glasgow, dolor, PHQ-4, flujo pico, obstétricos…) + doc de usuario/rol dedicado; sweep de citas vencidas al cierre (0 `Scheduled` vencidas) + `clear` cancela citas antes de voidear |
| 2026-07-11 | — | **Resultados de paneles (obs-group) + resultados diferidos**: nuevo `catalogs/paneles.csv` (hemograma: Hb/Hto/leucocitos/plaquetas con bandas normal/anormal por componente y trigger por categoría); el resultado se postea como obs padre ligada a la orden + `groupMembers`. El ~10% que no "vuelve el mismo día" ya no se pierde: queda en `ResultadosPendientes` y se entrega en la **siguiente visita** del paciente (`LabOrderSeeder.ProcesarPendientesAsync`). Verificado con corrida dic-2025 (152 pac, 0 errores, 104 órdenes, paneles con 4 componentes en BD, 1 entrega diferida) |
| 2026-07-11 | — | **Atributos de persona (P7)**: teléfono salvadoreño sintético (móvil/fijo 80/20) y estado civil coherente con la edad (answers del concepto `1054`: <18 soltero, adultos mayoría casado/acompañado, viudez en 65+), anidados como `attributes` en el `POST /patient`. Feature off si los `Defaults.*AttributeTypeUuid` están vacíos. Verificado con corrida 26-27 dic 2025 (13 pac, 0 errores, atributos correctos en `person_attribute`) |
| 2026-07-11 | — | **Overrides de vitales por enfermedad (P6 reformulado) + limpieza `_poolLock` (P9)**: columnas opcionales `vital_pa`=alta, `vital_fc`=alta\|baja, `vital_spo2`=baja en `diagnosticos.csv` (~35/48/11 filas por reglas de keywords en `ajustar_diagnosticos.ps1`; `fetal` excluido de fc, anemia sin spo2) → `ComputeVitals` con 3 overrides que ganan sobre la categoría (preeclampsia→PA alta, hipotiroidismo→bradicardia 42-58 incluso febril, insuf. cardíaca→SpO2 88-94). Verificado: taquicardia ventricular → pulso 120 en BD. `_poolLock` eliminado (corrida secuencial) |
| 2026-07-11 | — | **Fix fecha de órdenes (`dateActivated`)**: sin el campo, OpenMRS fechaba toda orden (lab y prescripción) con el **reloj real de la corrida** en vez del día de la visita simulada (afecta a todos los datos previos). Ambos seeders de órdenes envían ahora `dateActivated` = datetime del encounter de consulta (helper `ConsultaSeeder.FechaConsulta`; no puede ser anterior al encounter — `Order.error.encounterDatetimeAfterDateActivated`) |
| 2026-07-14 | — | **⚠️ El volumen del día deja de derivarse de las altas + las LEYES DE LA SIMULACIÓN.** El modelo de crecimiento (Bass, jul-2026) había roto la continuidad longitudinal **en silencio**: compilaba, pasaba los 380 tests y la corrida de 3,5 años terminó en `completado` con exit code 0. Auditando los CSV a mano: **79 % de los pacientes con una sola visita**, **65 % de los crónicos (7.686) sin volver jamás a un control**, **14.025 citas `Missed` contra 14.115 `Completed`**, 1,45 visitas/paciente y el mix de recurrentes **plano en el 31 % durante 42 meses**. Causa: `total = altas / (1 − PorcentajeRecurrentes)` → los recurrentes eran un cupo fijo del 30 %, y la clínica agendaba ~20 controles/día contra 14 huecos. **Arreglo**: `visitas = altas (Bass) + retornos (demanda real del panel)` — una suma; `RecurrentSelector` sin cupo (citas de hoy + retorno espontáneo por tasa, nuevo `Recurrence.VisitasEspontaneasPorPacienteAno`); el **aforo** recorta las ALTAS, nunca a quien tenía cita; `PorcentajeRecurrentes` **eliminado**; `Proyectar` reescrito como proyección de cohortes (calibraba sobre un modelo distinto del que corría: creía 8,2 visitas/paciente-año cuando la realidad daba 1,45 en 3,5 años → `q` 3× de más → λ=101/día contra un objetivo de 25 → 33 de 42 meses contra el techo). **Y la pieza de proceso**: `leyes_simulacion.md` + `Services/Invariantes.cs` — 8 invariantes que se **ejecutan en cada corrida** (etapa 2/5 sobre la proyección, 4/5 sobre lo sembrado) y devuelven **exit code 3** si se rompen, con la corrida rota como test de regresión. Nuevo log en fichero (`output/corrida_<fecha>.log`, espejo de la consola). Curva resultante: 6,2 → 25 visitas/día con el mix de recurrentes subiendo del **4 % al 60 %**. 406 tests |
