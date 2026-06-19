# Fases de Implementación — OpenMRS Clinical Simulator

> **Convención de estado:**
> - `[ ]` Pendiente
> - `[~]` En progreso
> - `[x]` Completado

---

## FASE 1 — Proyecto C# base + configuración
**Estado:** `[ ]`
**Depende de:** Nada (punto de partida)

### Objetivo
Reemplazar el scaffold F# con un proyecto C# .NET 10 funcional, compilable, con toda la configuración cargada desde `appsettings.json` y un endpoint de status que verifique la conexión a OpenMRS.

### Entregables
- [ ] Eliminar `.fsproj` F# del proyecto
- [ ] Crear `openmrs_seeder_v1.csproj` (C#, net10.0)
- [ ] Actualizar `openmrs_seeder_v1.slnx` para apuntar al `.csproj`
- [ ] NuGet packages: `Bogus`, `MySqlConnector`, `Swashbuckle.AspNetCore`
- [ ] `Configuration/OpenMrsSettings.cs` — sección `OpenMRS` (RestApi + DirectDb)
- [ ] `Configuration/SimulationSettings.cs` — sección `Simulation` (volumen, demografía, probabilidades)
- [ ] `appsettings.json` con estructura completa (ver `detalle-seeder.md`)
- [ ] `Program.cs` — minimal API, DI, Swagger habilitado
- [ ] `Clients/OpenMrsRestClient.cs` — HttpClient wrapper (BasicAuth, retry 3x, timeout 30s)
- [ ] `Controllers/SeedController.cs` — solo `GET /api/seed/status`
- [ ] `Services/SeedProgressTracker.cs` — estructura base (ConcurrentDictionary<Guid, SeedRun>)

### Verificación
```
dotnet build → 0 errores
dotnet run → Swagger en http://localhost:5197/swagger
GET /api/seed/status → 200 con config cargada + ping a OpenMRS REST API
```

---

## FASE 2 — Catálogos clínicos
**Estado:** `[ ]`
**Depende de:** Docker corriendo (para queries SQL), Fase 1 (para CatalogLoader)

### Objetivo
Extraer catálogos del diccionario CIEL desde la DB OpenMRS y crear los catálogos manuales. Todos quedan como CSV estáticos en `catalogs/`. El `CatalogLoader` los carga al startup.

### Entregables
- [ ] Exponer puerto 3306 en `docker-compose.yml` del distro → solo para extracción de datos
- [ ] Ejecutar Query 1 → `catalogs/diagnosticos.csv` (+ agregar columnas booleanas manualmente)
- [ ] Ejecutar Query 2 → `catalogs/medicamentos.csv` (+ agregar columnas booleanas manualmente)
- [ ] Ejecutar Query 3 → `catalogs/laboratorios.csv` (+ agregar columnas booleanas manualmente)
- [ ] Crear manualmente `catalogs/epidemiology-profile.csv` (~60-80 filas por categoría/edad/género)
- [ ] Crear manualmente `catalogs/examenes_clinicos.csv` (~15 exámenes en consultorio)
- [ ] Crear manualmente `catalogs/alergenos.csv` (~20 alérgenos DRUG/FOOD/ENVIRONMENT)
- [ ] Crear manualmente `catalogs/motivos_consulta.csv` (~30 frases en español)
- [ ] `Services/CatalogLoader.cs` — lee y cachea los 7 CSV al startup
- [ ] `Services/ConceptResolver.cs` — valida que los UUIDs de concepts vitales existen en la DB

### Estructuras de catálogos

**epidemiology-profile.csv**
```
categoria,grupo_edad,genero,peso
respiratorio,0-14,M,35
cardiovascular,45-64,M,28
...
```

**diagnosticos.csv**
```
ciel_uuid,codigo_icd10,nombre_es,nombre_en,categoria,severidad,
aplica_0_14,aplica_15_29,aplica_30_44,aplica_45_64,aplica_65mas,
peso_M,peso_F,requiere_lab,requiere_rx,requiere_examen_clinico
```

**medicamentos.csv**
```
drug_uuid,nombre_generico,strength,via_uuid,
aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,
aplica_digestivo,aplica_osteomuscular,aplica_urologico,
aplica_infeccioso,aplica_endocrino,aplica_neurologico,aplica_mental
```

**laboratorios.csv**
```
ciel_uuid,nombre_es,nombre_en,clase,
aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,
aplica_digestivo,aplica_osteomuscular,aplica_urologico,
aplica_infeccioso,aplica_endocrino,aplica_neurologico
```

**examenes_clinicos.csv**
```
ciel_uuid,nombre_es,tipo_resultado,unidad,
aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,
aplica_digestivo,aplica_osteomuscular,aplica_urologico,
aplica_infeccioso,aplica_endocrino,aplica_neurologico,aplica_preventivo
```

**alergenos.csv**
```
concept_uuid,nombre_es,tipo_alergeno,severidad_tipica,reaccion_tipica_uuid
```

**motivos_consulta.csv**
```
id,descripcion,categoria_dx
```

### Queries SQL para extracción

**Query 1 — Diagnósticos CIEL con ICD-10:**
```sql
SELECT c.uuid AS ciel_uuid, crt.code AS codigo_icd10,
       cn.name AS nombre_es, cn_en.name AS nombre_en
FROM concept c
JOIN concept_name cn ON c.concept_id = cn.concept_id
    AND cn.locale = 'es' AND cn.concept_name_type = 'FULLY_SPECIFIED' AND cn.voided = 0
LEFT JOIN concept_name cn_en ON c.concept_id = cn_en.concept_id
    AND cn_en.locale = 'en' AND cn_en.concept_name_type = 'FULLY_SPECIFIED' AND cn_en.voided = 0
JOIN concept_reference_map crm ON c.concept_id = crm.concept_id
JOIN concept_reference_term crt ON crm.concept_reference_term_id = crt.concept_reference_term_id
JOIN concept_source cs ON crt.concept_source_id = cs.concept_source_id AND cs.name = 'ICD-10-WHO'
JOIN concept_class cc ON c.concept_class_id = cc.concept_class_id
WHERE c.voided = 0 AND cc.name IN ('Diagnosis','Finding')
ORDER BY cn.name LIMIT 500;
```

**Query 2 — Medicamentos:**
```sql
SELECT d.uuid AS drug_uuid, d.name AS nombre_generico, d.strength,
       c.uuid AS concept_uuid, cn.name AS nombre_concepto_es
FROM drug d
JOIN concept c ON d.concept_id = c.concept_id
LEFT JOIN concept_name cn ON c.concept_id = cn.concept_id
    AND cn.locale = 'es' AND cn.concept_name_type = 'FULLY_SPECIFIED' AND cn.voided = 0
WHERE d.retired = 0 ORDER BY d.name;
```

**Query 3 — Laboratorios:**
```sql
SELECT c.uuid AS ciel_uuid, cn.name AS nombre_es, cn_en.name AS nombre_en, cc.name AS clase
FROM concept c
JOIN concept_name cn ON c.concept_id = cn.concept_id
    AND cn.locale = 'es' AND cn.concept_name_type = 'FULLY_SPECIFIED' AND cn.voided = 0
LEFT JOIN concept_name cn_en ON c.concept_id = cn_en.concept_id
    AND cn_en.locale = 'en' AND cn_en.concept_name_type = 'FULLY_SPECIFIED' AND cn_en.voided = 0
JOIN concept_class cc ON c.concept_class_id = cc.concept_class_id
WHERE c.voided = 0 AND cc.name IN ('Test','LabSet','Lab Findings')
ORDER BY cn.name;
```

### Verificación
```
GET /api/seed/status
→ diagnosticos: N, medicamentos: N, laboratorios: N,
  examenes_clinicos: N, alergenos: N, motivos: N, epidemiology: N
→ ConceptResolver: todos los UUIDs de vitales resueltos OK
```

---

## FASE 3 — Seeder de pacientes + alergias
**Estado:** `[ ]`
**Depende de:** Fase 1, Fase 2

### Objetivo
Crear pacientes nuevos con datos demográficos realistas en español, y registrar alergias para el porcentaje configurado.

### UUIDs fijos
- Patient Identifier Type (OpenMRS ID): `05a29f94-c0ed-11e2-94be-8c13b969e334`
- Location de registro (Recepción): `c1000000-0000-0000-0000-000000000002`

### Entregables
- [ ] `Models/Api/PatientRequest.cs` — DTO para `POST /patient`
- [ ] `Models/Api/AllergyRequest.cs` — DTO para `POST /patient/{uuid}/allergy`
- [ ] `Models/Simulation/SimulatedPatient.cs` — estado del paciente en el simulador
- [ ] `Services/PatientProfileGenerator.cs` — genera nombre, género, edad, dirección con Bogus `es`
- [ ] `Services/DailyScheduleGenerator.cs` — calcula pacientes por día con WeekdayWeights + Normal(σ=0.20)
- [ ] `Seeders/PatientSeeder.cs`:
  - Nombre: givenName + familyName en español
  - Género: M/F según `GenderRatio`
  - Edad: desde distribución `AgeGroups`
  - Dirección: ciudad/departamento colombiano
  - Identificador: prefijo `SIM-` + número secuencial
- [ ] `Seeders/AllergySeeder.cs`:
  - Si `rand < AllergyOnNew` (default 15%): registrar 1–3 alergias al azar de `alergenos.csv`
  - `POST /patient/{uuid}/allergy` con allergenType, codedAllergen.uuid, severity.uuid, reactions
- [ ] `POST /api/seed/run` — acepta body opcional `{ "step": "patients" }`, retorna 202 con runId
- [ ] `GET /api/seed/progress/{runId}` — devuelve `{ porcentaje, etapa, pacientes_creados, errores }`

### Verificación
```
POST /api/seed/run { "step": "patients" }
→ 202 con runId
GET /api/seed/progress/{runId} → hasta 100%
→ Pacientes SIM-* visibles en OpenMRS O3 con nombre en español
→ ~15% tienen alergias registradas visibles en el chart
```

---

## FASE 4 — Seeder de visitas + vitales
**Estado:** `[ ]`
**Depende de:** Fase 3

### Objetivo
Crear visitas con hora realista del día y registrar signos vitales coherentes con el diagnóstico del paciente.

### UUIDs fijos
- Encounter type Vitals: `67a71486-1a54-468f-ac3e-7091a9a79584`
- Encounter role Clinician: `240b26f9-dd88-4172-823d-4a8bfeb7841f`
- Locations consultorios 1-4: `c1000000-0000-0000-0000-000000000011` a `...014`

### Conceptos CIEL vitales
| Concepto | UUID CIEL |
|---|---|
| Peso (kg) | `5089AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` |
| Talla (cm) | `5090AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` |
| PA sistólica (mmHg) | `5085AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` |
| PA diastólica (mmHg) | `5086AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` |
| Temperatura (°C) | `5088AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` |
| Pulso (lpm) | `5087AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` |
| SpO2 (%) | `5092AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` |

### Entregables
- [ ] `Models/Api/VisitRequest.cs`, `EncounterRequest.cs`, `ObsRequest.cs`
- [ ] `Seeders/VisitSeeder.cs` — `POST /visit` (OUTPATIENT, hora según HorarioAtencion, location aleatoria)
- [ ] `Seeders/VitalsSeeder.cs` — encounter VITALS + 7 obs con rangos ajustados por diagnóstico:
  - Rangos base: Peso 45-120kg, Talla 145-195cm, PA 100-130/60-85, Temp 36.0-37.4, Pulso 60-100, SpO2 96-100%
  - HTA: PA sistólica 140-180, diastólica 90-110
  - Infeccioso/fiebre: Temp 37.5-39.5, Pulso 90-110
  - Respiratorio grave: SpO2 88-94%
  - Diabetes: peso tendencia alta (BMI 25-35)

### Verificación
```
→ Historial de visitas visible en O3 chart del paciente
→ Signos vitales graficados en el widget de vitales con valores coherentes
```

---

## FASE 5 — Seeder de consulta clínica
**Estado:** `[ ]`
**Depende de:** Fase 4, Fase 2

### Objetivo
Crear el encounter ADULTINITIAL con diagnóstico coherente con el perfil epidemiológico, motivo de consulta y exámenes realizados en consultorio.

### UUIDs fijos
- Concepto motivo consulta: `162169AAAAAAAAAAAAAAAAAAAA`
- Concepto diagnóstico: `1284AAAAAAAAAAAAAAAAAAAA`
- Concepto certeza: `159946AAAAAAAAAAAAAAAAAAAA` (confirmado: `1066`, presuntivo: `1067`)

### Entregables
- [ ] `Seeders/ConsultaSeeder.cs`:
  - Selecciona categoría dx desde `epidemiology-profile.csv` (filtrado por edad+género)
  - Selecciona diagnóstico desde `diagnosticos.csv` (filtrado por categoría + `aplica_GRUPO = true`)
  - Certeza: 70% confirmado, 30% presuntivo
  - Motivo de consulta desde `motivos_consulta.csv` (filtrado por `categoria_dx`)
  - Si `rand < ClinicalExam` (0.35) o `dx.requiere_examen_clinico = true`:
    → obs del examen en consultorio (desde `examenes_clinicos.csv`, filtrado por categoría)
    → si `tipo_resultado = numerico`: registra valor con unidad
    → si `tipo_resultado = categorico`: registra normal/anormal

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
Crear órdenes de exámenes externos coherentes con el diagnóstico.

### Entregables
- [ ] `Seeders/LabOrderSeeder.cs`:
  - Aplica si `rand < LabOrder` (0.40) o `dx.requiere_lab = true`
  - 1-2 órdenes por visita
  - Selecciona desde `laboratorios.csv` filtrado por `aplica_CATEGORIA = true`
  - `POST /order` tipo `testorder`, care setting OUTPATIENT
  - Priority: ROUTINE (80%) / URGENT (20%); si `dx.severidad = "grave"` → URGENT sube al 50%

### Verificación
```
→ Órdenes visibles en módulo de laboratorio de O3
→ Coherencia: HTA genera perfil lipídico, diabetes genera HbA1c, etc.
```

---

## FASE 7 — Seeder de prescripciones
**Estado:** `[ ]`
**Depende de:** Fase 5, Fase 2

### Objetivo
Crear prescripciones de medicamentos coherentes con el diagnóstico.

### Vías de administración CIEL
- Oral: `160240AAAAAAAAAAAAAAAAAA`
- Inhalación: `160241AAAAAAAAAAAAAAAAAA`
- IV: `160242AAAAAAAAAAAAAAAAAA`
- IM: `160243AAAAAAAAAAAAAAAAAA`

### Entregables
- [ ] `Seeders/PrescriptionSeeder.cs`:
  - Aplica si `rand < DrugOrder` (0.65) o `dx.requiere_rx = true`
  - 1-3 medicamentos por visita
  - Selecciona desde `medicamentos.csv` filtrado por `aplica_CATEGORIA = true`
  - `POST /order` tipo `drugorder`
  - Frecuencia en español: "1 vez al día", "cada 8 horas", "cada 12 horas"
  - Duración: 7, 14 o 30 días

### Verificación
```
→ Medicamentos activos visibles en el chart del paciente en O3
→ Coherencia: HTA recibe enalapril/atenolol, diabetes recibe metformina, etc.
```

---

## FASE 8 — Orquestación completa + cierre de visitas
**Estado:** `[ ]`
**Depende de:** Fases 3-7

### Objetivo
Conectar todos los seeders en el `SeedOrchestrator` y cerrar las visitas correctamente.

### Entregables
- [ ] `Seeders/VisitCloseSeeder.cs` — `POST /visit/{uuid}` con `stopDatetime` (hora de fin del mismo día)
- [ ] `Seeders/SeedOrchestrator.cs` — implementación completa del pipeline diario:
  1. DailyScheduleGenerator → lista de atenciones del día
  2. Para cada atención: PatientSeeder → AllergySeeder → VisitSeeder → VitalsSeeder → ConsultaSeeder → LabOrderSeeder → PrescriptionSeeder → VisitCloseSeeder
- [ ] `POST /api/seed/run` sin `step` → ejecuta la simulación completa
- [ ] Progreso reportado por etapa: "Día 2023-01-03: 12/40 pacientes (30%)"

### Verificación
```
POST /api/seed/run → 202
GET /api/seed/progress/{runId} → progreso hasta 100%
→ Historial completo visible en O3: visitas, vitales, diagnósticos, labs, medicamentos, alergias
```

---

## FASE 9 — Endpoint de limpieza
**Estado:** `[ ]`
**Depende de:** Fase 3+

### Objetivo
Borrar todos los datos generados por el simulador de forma segura (void lógico via REST).

### Estrategia
`GET /ws/rest/v1/patient?identifier=SIM-` → lista pacientes seeded → void en cascada: visitas → encounters → obs → orders → alergias → paciente.

### Entregables
- [ ] `DELETE /api/seed/clear` — void lógico de todos los registros `SIM-*`
- [ ] Responde con `{ "pacientes": N, "visitas": N, "encounters": N, "ordenes": N }`

### Verificación
```
DELETE /api/seed/clear → 200 con conteo
GET /api/seed/status → conteo seeded = 0
→ Pacientes SIM-* ya no visibles en OpenMRS O3
```

---

## Registro de cambios

| Fecha | Fase | Cambio |
|-------|------|--------|
| 2026-06-18 | — | Plan inicial creado |
| 2026-06-18 | — | Rediseño: REST API en lugar de SQL directo |
| 2026-06-18 | — | Modelo epidemiológico con CSVs booleanos |
| 2026-06-18 | — | Agregados: AllergySeeder, ClinicalExamSeeder, DailyScheduleGenerator |
