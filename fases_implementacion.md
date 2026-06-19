# Fases de Implementación — OpenMRS Seeder API

> **Convención de estado:**
> - `[ ]` Pendiente
> - `[~]` En progreso
> - `[x]` Completado

---

## FASE 1 — Proyecto C# base + configuración
**Estado:** `[ ]`
**Depende de:** Nada (punto de partida)

### Objetivo
Reemplazar el scaffold F# con un proyecto C# funcional, compilable, con toda la configuración cargada desde `appsettings.json`. Sin lógica de seeding aún.

### Entregables
- [ ] Eliminar archivos F# (`.fsproj`, `Program.fs`, `WeatherForecast.fs`, `Controllers/WeatherForecastController.fs`)
- [ ] Crear `openmrs_seeder_v1.csproj` (C#, net10.0)
- [ ] NuGet packages: `Bogus`, `MySqlConnector`, `Swashbuckle.AspNetCore`
- [ ] `Configuration/OpenMrsSettings.cs` — sección `OpenMRS` (RestApi + DirectDb)
- [ ] `Configuration/SeedSettings.cs` — sección `Seed` (StartDate, EndDate, PatientCount, etc.)
- [ ] `appsettings.json` con todas las secciones configuradas
- [ ] `Program.cs` — minimal API, DI, Swagger habilitado
- [ ] `Controllers/SeedController.cs` — solo `GET /api/seed/status` (ping REST API + muestra config)
- [ ] `Services/SeedProgressTracker.cs` — estructura base (sin lógica aún)

### Configuración appsettings.json
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
      "Password": ""
    }
  },
  "Seed": {
    "StartDate": "2023-01-01",
    "EndDate": "2024-12-31",
    "PatientCount": 200,
    "VisitsPerPatient": 8,
    "Locale": "es",
    "RandomSeed": 42
  }
}
```

### Verificación
```
dotnet run
→ Swagger en http://localhost:5197/swagger
→ GET /api/seed/status retorna 200 con config y estado de conexión a OpenMRS
```

---

## FASE 2 — Catálogos clínicos (extracción desde DB)
**Estado:** `[ ]`
**Depende de:** Docker corriendo con puerto 3306 expuesto

### Objetivo
Exponer MariaDB y extraer los catálogos reales del diccionario CIEL cargado en la instancia. Los CSV quedan como archivos estáticos en el proyecto .NET.

### Entregables
- [ ] Modificar `docker-compose.yml` del distro → agregar `ports: - "3306:3306"` al servicio `db`
- [ ] Ejecutar Query diagnósticos → `catalogs/diagnosticos.csv`
- [ ] Ejecutar Query medicamentos → `catalogs/medicamentos.csv`
- [ ] Ejecutar Query laboratorios → `catalogs/laboratorios.csv`
- [ ] Crear manualmente `catalogs/motivos_consulta.csv` (~30 frases en español)
- [ ] `Services/CatalogLoader.cs` — lee y cachea los 4 CSV al startup de la API

### Estructura de catálogos

**diagnosticos.csv**
```
ciel_uuid,codigo_icd10,nombre_es,nombre_en,categoria,severidad,labs_relacionados,medicamentos_relacionados
```
Categorías: `respiratorio`, `cardiovascular`, `digestivo`, `osteomuscular`, `endocrino`, `neurologico`, `urologico`, `dermatologico`, `infeccioso`, `mental`

**medicamentos.csv**
```
drug_uuid,concept_uuid,nombre_generico,strength,nombre_concepto_es,via_administracion
```

**laboratorios.csv**
```
ciel_uuid,nombre_es,nombre_en,clase,diagnosticos_relacionados
```

**motivos_consulta.csv**
```
id,descripcion,diagnosticos_relacionados
```
Ejemplos (~30):
- Dolor de cabeza intenso y persistente
- Fiebre mayor de 38°C y malestar general
- Dolor abdominal en epigastrio
- Tos seca de más de una semana
- Control de presión arterial alta
- Control de glucemia en diabético conocido
- Dolor lumbar que irradia a pierna
- Náuseas, vómitos y diarrea
- Dificultad para respirar al esfuerzo
- Ardor al orinar y frecuencia urinaria

### Queries SQL

**Query 1 — Diagnósticos con ICD-10:**
```sql
SELECT c.uuid AS ciel_uuid, cn.name AS nombre_es, cn_en.name AS nombre_en, crt.code AS codigo_icd10
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
SELECT d.uuid AS drug_uuid, d.name AS nombre_generico, d.strength, c.uuid AS concept_uuid, cn.name AS nombre_concepto_es
FROM drug d
JOIN concept c ON d.concept_id = c.concept_id
LEFT JOIN concept_name cn ON c.concept_id = cn.concept_id
    AND cn.locale = 'es' AND cn.concept_name_type = 'FULLY_SPECIFIED' AND cn.voided = 0
WHERE d.retired = 0 ORDER BY d.name;
```

**Query 3 — Laboratorios:**
```sql
SELECT c.uuid AS ciel_uuid, cn.name AS nombre_es, cc.name AS clase
FROM concept c
JOIN concept_name cn ON c.concept_id = cn.concept_id
    AND cn.locale = 'es' AND cn.concept_name_type = 'FULLY_SPECIFIED' AND cn.voided = 0
JOIN concept_class cc ON c.concept_class_id = cc.concept_class_id
WHERE c.voided = 0 AND cc.name IN ('Test','LabSet','Lab Findings')
ORDER BY cn.name;
```

### Verificación
```
GET /api/seed/status
→ Muestra conteo de registros cargados por catálogo:
  diagnosticos: N, medicamentos: N, laboratorios: N, motivos: N
```

---

## FASE 3 — Seeder de pacientes
**Estado:** `[ ]`
**Depende de:** Fase 1

### Objetivo
Registrar pacientes en OpenMRS via REST API con datos demográficos realistas en español.

### UUIDs fijos
- Patient Identifier Type (OpenMRS ID): `05a29f94-c0ed-11e2-94be-8c13b969e334`
- Locations de registro: `c1000000-0000-0000-0000-000000000002` (Recepción)

### Entregables
- [ ] `Clients/OpenMrsRestClient.cs` — HttpClient wrapper (Basic Auth, retry, timeout)
- [ ] `Models/Api/PatientRequest.cs` — DTO para `POST /patient`
- [ ] `Seeders/PatientSeeder.cs`:
  - Genera datos con Bogus locale `es`
  - Nombre: givenName + middleName + familyName en español
  - Género: M/F aleatorio (60/40)
  - Birthdate: entre 5 y 80 años atrás
  - Dirección: ciudad/departamento colombiano
  - Identificador: número secuencial con prefijo `SIM-`
- [ ] `POST /api/seed/run` — acepta body `{ "step": "patients" }` o sin filtro para todo
- [ ] Retorna `202 Accepted` con `{ "runId": "guid" }`
- [ ] `GET /api/seed/progress/{runId}` — devuelve `{ porcentaje, etapa, errores }`

### Verificación
```
POST /api/seed/run { "step": "patients" }
→ 202 con runId
GET /api/seed/progress/{runId} → hasta 100%
→ Pacientes visibles en http://localhost/openmrs/spa con nombre en español
```

---

## FASE 4 — Seeder de visitas + vitales
**Estado:** `[ ]`
**Depende de:** Fase 3

### Objetivo
Crear visitas distribuidas en el rango de fechas configurado y registrar signos vitales por encounter.

### UUIDs fijos
- Encounter type Vitals: `67a71486-1a54-468f-ac3e-7091a9a79584`
- Encounter role Clinician: `240b26f9-dd88-4172-823d-4a8bfeb7841f`
- Locations consulta: Consultorios 1-4 (`c1000000-0000-0000-0000-000000000011` a `...014`)
- Conceptos CIEL vitales: ver tabla en plan principal

### Entregables
- [ ] `Models/Api/VisitRequest.cs`, `EncounterRequest.cs`, `ObsRequest.cs`
- [ ] `Seeders/VisitSeeder.cs` — `POST /visit` (tipo Outpatient descubierto al startup, fecha en rango, location aleatoria entre consultorios)
- [ ] `Seeders/VitalsSeeder.cs` — encounter Vitals + 7 obs con rangos realistas por edad/género:
  - Peso: 45-110 kg
  - Talla: 145-185 cm
  - PA sistólica: 100-180 mmHg
  - PA diastólica: 60-110 mmHg
  - Temperatura: 36.0-38.5 °C
  - Pulso: 55-105 lpm
  - SpO2: 92-100 %

### Verificación
```
→ Historial de visitas visible en O3 chart del paciente
→ Signos vitales graficados en el widget de vitales
```

---

## FASE 5 — Seeder de consulta clínica
**Estado:** `[ ]`
**Depende de:** Fase 4, Fase 2 (catálogos)

### Objetivo
Crear el encounter de consulta con motivo de consulta, diagnóstico principal y secundarios.

### UUIDs fijos
- Concepto motivo consulta: `162169AAAAAAAAAAAAAAAAAAAA`
- Concepto diagnóstico: `1284AAAAAAAAAAAAAAAAAAAA`
- Concepto certeza: `159946AAAAAAAAAAAAAAAAAAAA` (confirmado: `1066`, presuntivo: `1067`)

### Entregables
- [ ] `Seeders/ConsultaSeeder.cs`:
  - Selecciona motivo de consulta del `motivos_consulta.csv`
  - Selecciona diagnóstico principal del `diagnosticos.csv` (coherente con el motivo)
  - 0-2 diagnósticos secundarios del mismo sistema orgánico
  - Certeza: 70% confirmado, 30% presuntivo
- [ ] Coherencia diagnóstico-vitales: hipertensión → PA alta, diabetes → obs adicional glucemia

### Verificación
```
→ Diagnósticos visibles en el chart del paciente en O3
→ Nota clínica con motivo de consulta en español
```

---

## FASE 6 — Seeder de órdenes de laboratorio
**Estado:** `[ ]`
**Depende de:** Fase 5, Fase 2 (catálogos)

### Objetivo
Crear órdenes de exámenes coherentes con el diagnóstico de la consulta.

### Entregables
- [ ] `Seeders/LabOrderSeeder.cs`:
  - 0-2 órdenes por visita
  - Selecciona del `laboratorios.csv` según `diagnosticos_relacionados`
  - `POST /order` tipo `testorder`, care setting OUTPATIENT
  - Priority: `ROUTINE` (80%) / `URGENT` (20%)
- [ ] Location de lab: `c1000000-0000-0000-0000-000000000031`

### Verificación
```
→ Órdenes visibles en módulo de laboratorio de O3
```

---

## FASE 7 — Seeder de prescripciones
**Estado:** `[ ]`
**Depende de:** Fase 5, Fase 2 (catálogos)

### Objetivo
Crear órdenes de medicamentos coherentes con el diagnóstico.

### UUIDs fijos (rutas de administración CIEL)
- Oral: `160240AAAAAAAAAAAAAAAAAA`
- IM: `160243AAAAAAAAAAAAAAAAAA`
- IV: `160242AAAAAAAAAAAAAAAAAA`
- Inhalación: `160241AAAAAAAAAAAAAAAAAA`

### Entregables
- [ ] `Seeders/PrescriptionSeeder.cs`:
  - 0-3 medicamentos por visita
  - Selecciona del `medicamentos.csv` según diagnóstico
  - `POST /order` tipo `drugorder`
  - Dosis, vía, frecuencia y duración en español (1 vez/día, cada 8 horas, etc.)
  - Duración: 7, 14 o 30 días según tipo de medicamento

### Verificación
```
→ Medicamentos activos visibles en el chart del paciente
```

---

## FASE 8 — Cierre de visita + cola de servicio
**Estado:** `[ ]`
**Depende de:** Fase 4

### Objetivo
Completar el ciclo clínico cerrando la visita y opcionalmente simulando la cola de atención.

### UUIDs fijos (prioridades de cola)
- No urgente: `f4620bfa-3f64-4b5a-b38c-8a817e16b0c3`
- Urgente: `dc3492ef-b5ed-4f9e-9b21-1fd46a1ad0cb`
- Emergencia: `04f6f7e0-b932-4e53-b08d-42b6be804cf8`

### Entregables
- [ ] `Seeders/VisitCloseSeeder.cs` — `POST /visit/{uuid}` con `stopDatetime` (al final del mismo día)
- [ ] *(Opcional)* `Seeders/QueueSeeder.cs` — `POST /queue-entry` con prioridad aleatoria (85% No urgente, 12% Urgente, 3% Emergencia)

### Verificación
```
→ Visitas con fecha de cierre en O3
→ Historial completo de episodio visible
```

---

## FASE 9 — Endpoint de limpieza
**Estado:** `[ ]`
**Depende de:** Fase 3+

### Objetivo
Borrar todos los datos generados por el seeder de forma segura (void lógico).

### Estrategia de identificación
Los pacientes seeded se identifican por el prefijo `SIM-` en su identificador OpenMRS ID.
`GET /ws/rest/v1/patient?identifier=SIM-` → lista pacientes seeded → void lógico en cascada.

### Entregables
- [ ] `DELETE /api/seed/clear` — void lógico de pacientes + visitas + encounters + obs
- [ ] Responde con `{ "pacientes_eliminados": N, "visitas_eliminadas": N }`

### Verificación
```
DELETE /api/seed/clear → 200
GET /api/seed/status → conteo seeded = 0
```

---

## Registro de cambios

| Fecha | Fase | Cambio |
|-------|------|--------|
| 2026-06-18 | — | Plan inicial creado |
