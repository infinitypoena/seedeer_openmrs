# Manual de Usuario — OpenMRS Clinical Simulator

> Versión del manual: junio 2026 · Basada en el código fuente real

## Tabla de Contenidos

1. [¿Qué hace este simulador?](#1-qué-hace-este-simulador)
2. [Decisiones de diseño clave](#2-decisiones-de-diseño-clave)
3. [Requisitos previos](#3-requisitos-previos)
4. [Arranque del entorno](#4-arranque-del-entorno)
5. [Configuración](#5-configuración)
6. [Flujo completo de la simulación](#6-flujo-completo-de-la-simulación)
7. [Cómo usar la API](#7-cómo-usar-la-api)
8. [Catálogos CSV — estado real y cómo extenderlos](#8-catálogos-csv--estado-real-y-cómo-extenderlos)
9. [Casos de uso prácticos](#9-casos-de-uso-prácticos)
10. [Manejo del tiempo](#10-manejo-del-tiempo)
11. [Limpieza e idempotencia](#11-limpieza-e-idempotencia)
12. [Acceso directo a MariaDB](#12-acceso-directo-a-mariadb)
13. [Solución de problemas](#13-solución-de-problemas)

---

## 1. ¿Qué hace este simulador?

El **OpenMRS Clinical Simulator** es una API REST en .NET 10 que puebla una instancia OpenMRS 3.x con registros clínicos ficticios pero epidemiológicamente coherentes.

Simula el flujo operativo de una clínica de consulta externa durante un período histórico configurable:

- Pacientes nuevos y recurrentes con datos demográficos en español (generados con Bogus)
- Diagnósticos elegidos según edad, género y distribución epidemiológica real (CSV de pesos)
- Signos vitales coherentes con el diagnóstico (p. ej. PA elevada en cardiovascular)
- Motivos de consulta, órdenes de laboratorio y prescripciones farmacológicas
- Alergias para un porcentaje de pacientes nuevos

**Lo que NO hace:**
- No escribe SQL directamente — todo va por la REST API de OpenMRS (`/ws/rest/v1`)
- No genera imágenes, documentos, ni registros de admisión hospitalaria
- No modela alta hospitalaria, UCI ni urgencias (solo consulta externa)

**Cuándo usarlo:**
- Poblar una instancia de demostración con datos clínicos realistas en español
- Probar que los módulos de OpenMRS O3 (registros, labs, medicamentos, alergias) funcionen
- Proveer dataset histórico para entrenamiento de personal o análisis académico
- Evaluar el rendimiento de la instancia bajo volumen de datos real

---

## 2. Decisiones de diseño clave

Esta sección explica el *por qué* de cada decisión arquitectónica. Entenderla permite personalizar el simulador sin romper su coherencia.

### 2.1 Solo REST API — sin escritura SQL directa

**Decisión:** Toda la inserción de datos usa `POST` a los endpoints de OpenMRS (`/patient`, `/visit`, `/encounter`, `/obs`, `/order`, `/patient/{uuid}/allergy`).

**Por qué:** Garantiza compatibilidad con cualquier despliegue estándar de OpenMRS, independientemente de si la base de datos es MariaDB, MySQL o PostgreSQL (versiones futuras). También respeta las reglas de negocio que OpenMRS aplica en su capa de servicio (validación de UUIDs, integridad referencial, Luhn checkdigit en identificadores).

**Consecuencia práctica:** La velocidad de simulación depende de la latencia HTTP de OpenMRS, no del I/O de base de datos. Ver estimaciones en [sección 10.8](#108-velocidad-real-de-ejecución-y-estimación-de-tiempo).

---

### 2.2 Ejecución en segundo plano con runId

**Decisión:** `POST /api/seed/run` devuelve `202 Accepted` con un GUID (`runId`) inmediatamente. La simulación corre en un `Task.Run` separado. El progreso se consulta vía `GET /api/seed/progress/{runId}`.

**Por qué:** Una simulación de 2 años con 40 pacientes/día tarda entre 8 y 33 horas. Mantener la conexión HTTP abierta ese tiempo es inviable. El patrón "dispara y sondea" es el estándar para operaciones de larga duración.

**Consecuencia práctica:** El `runId` es solo en memoria (`ConcurrentDictionary`). Si el proceso del simulador se reinicia durante una ejecución, el progreso se pierde. Los datos ya insertados en OpenMRS persisten; habrá que decidir si limpiar y reiniciar o continuar desde el punto en que se paró.

---

### 2.3 Catálogos CSV en lugar de código hardcodeado

**Decisión:** Los diagnósticos, medicamentos, laboratorios, exámenes clínicos, alergenos y perfiles epidemiológicos están en archivos CSV, no en el código.

**Por qué:** Permite que alguien sin conocimientos de C# pueda ampliar el catálogo médico (agregar diagnósticos, cambiar pesos epidemiológicos, agregar medicamentos) sin recompilar. Los UUIDs en los CSV son los de la instancia OpenMRS real, verificados con queries.

**Consecuencia práctica:** Los CSV se copian al directorio `bin/` durante el build. **Cambiar un CSV en `catalogs/` requiere recompilar** (o copiar manualmente el archivo al `bin/Debug/net10.0/catalogs/`).

---

### 2.4 Selección ponderada (weighted random) por edad y género

**Decisión:** `EpidemiologySelector` no elige diagnósticos al azar uniforme. Primero filtra `epidemiology-profile.csv` por grupo etario y género, pondera los pesos relativos y elige la categoría diagnóstica. Luego dentro de esa categoría elige el diagnóstico específico ponderando por `peso_M` o `peso_F` según el género del paciente.

**Por qué:** Un simulador que asigna hipertensión a un niño de 8 años o diabetes infantil a un adulto de 60 no sirve para demostración clínica real. Los pesos reflejan la prevalencia epidemiológica aproximada por grupo poblacional.

**Consecuencia práctica:** Si un grupo etario/género no tiene ninguna entrada en `epidemiology-profile.csv`, el selector hace fallback a `"infeccioso"`. Asegurarse de que todos los grupos tengan al menos una fila.

---

### 2.5 Vitales coherentes con el diagnóstico

**Decisión:** `VitalsSeeder` genera rangos diferentes según la categoría del diagnóstico del paciente:

| Categoría | Cambio aplicado |
|-----------|----------------|
| `cardiovascular` | PA sistólica 140–180, diastólica 90–110 mmHg |
| `infeccioso` | Temperatura 37.5–39.5 °C, pulso 90–115 lpm |
| `respiratorio` (grave) | SpO₂ 88–93% |
| `diabetes` | Peso 70–130 kg |
| Resto | PA 100–130/60–85, Temp 36.0–37.4, Pulso 60–100, SpO₂ 95–98 |

**Por qué:** Un historial clínico donde el paciente tiene diagnóstico de insuficiencia cardíaca pero signos vitales perfectamente normales no tiene valor de demostración.

**Consecuencia práctica:** Los rangos están hardcodeados en `VitalsSeeder.cs`. Para ajustarlos hay que editar el código. La categoría viene del `DiagnosticoEntry.Categoria` del CSV.

---

### 2.6 Identificación dual SIM- + Luhn Mod-30

**Decisión:** Cada paciente creado recibe dos identificadores:
1. **OpenMRS ID** (tipo con validador Luhn Mod-30): identificador estándar de la instancia, necesario para que OpenMRS no rechace al paciente.
2. **SIM-XXXXXXXX** (tipo "Old Identification Number", sin validador): identificador de tracking para limpieza.

**Por qué:** OpenMRS requiere que el identificador principal tenga dígito verificador Luhn para el tipo "OpenMRS ID". Sin él, el `POST /patient` falla. El segundo identificador `SIM-` permite buscar y limpiar todos los datos del simulador sin afectar pacientes reales (que no tienen ese prefijo).

**Consecuencia práctica:** La búsqueda `GET /patient?identifier=SIM-` devuelve solo pacientes del simulador. El `DELETE /api/seed/clear` usa este mecanismo.

---

### 2.7 Deduplicación same-day en recurrentes

**Decisión:** `SeedOrchestrator` mantiene un `HashSet<string>` de UUIDs de OpenMRS de pacientes ya visitados en el día en curso. Un paciente recurrente no puede aparecer dos veces el mismo día.

**Por qué:** En la realidad, un paciente no tiene dos visitas de consulta externa el mismo día. Sin esta protección, la selección aleatoria del pool podría producir duplicados.

**Consecuencia práctica:** Si el pool de recurrentes es pequeño (pocas semanas de datos anteriores) y el `PorcentajeRecurrentes` es alto, el simulador puede no alcanzar el número de recurrentes pedidos ese día y asignará el cupo a nuevos pacientes.

---

### 2.8 Encadenamiento de UUIDs en el pipeline

**Decisión:** Cada seeder escribe en el objeto `SimulatedPatient` los UUIDs que genera, para que el siguiente los use:
- `PatientSeeder` → escribe `patient.OpenMrsUuid`
- `VisitSeeder` → escribe `patient.VisitUuid` y `patient.VisitDatetime`
- `ConsultaSeeder` → escribe `patient.ConsultaEncounterUuid`
- `LabOrderSeeder` y `PrescriptionSeeder` lo leen para asociar las órdenes al encounter correcto

**Por qué:** La REST API de OpenMRS requiere que cada recurso hijo referencie al padre por UUID. El estado en memoria del `SimulatedPatient` actúa como contexto de sesión de la visita.

**Consecuencia práctica:** Si un seeder falla (p. ej. `VisitSeeder` no puede crear la visita), los seeders posteriores hacen skip automáticamente porque el UUID que necesitan está vacío. El error se registra en `SeedRun.Errores` pero el pipeline sigue con el siguiente paciente.

---

## 3. Requisitos previos

El simulador es una API REST que se conecta a una instancia OpenMRS ya desplegada. OpenMRS puede estar en Docker, en un servidor local o remoto — no importa el método, solo que la REST API responda.

**Para correr el simulador hay dos opciones:**

**Opción A — con .NET SDK (recomendada para desarrollo):**

| Requisito | Versión | Verificación |
|-----------|---------|-------------|
| .NET SDK | 10.0+ | `dotnet --version` |
| OpenMRS 3.x con REST API activa | cualquier despliegue | `GET http://<host>/openmrs/ws/rest/v1` |

**Opción B — con Docker (sin instalar .NET):**

| Requisito | Versión | Verificación |
|-----------|---------|-------------|
| Docker Desktop (Windows/Mac) o Docker Engine (Linux) | 4.x+ / 24.x+ | `docker --version` |
| OpenMRS 3.x con REST API activa | cualquier despliegue | `GET http://<host>/openmrs/ws/rest/v1` |

---

## 4. Arranque del entorno

### 4.1 Verificar que OpenMRS está accesible

Antes de correr el simulador, confirmar que la REST API de OpenMRS responde:

```
GET http://<tu-host>/openmrs/ws/rest/v1
```

Debe devolver un JSON con información de la instancia. Si OpenMRS corre en local (p. ej. con Docker), la URL base suele ser `http://localhost/openmrs/ws/rest/v1`.

> Si OpenMRS está desplegado con Docker y tarda en inicializar, el backend puede demorar **10–15 minutos en el primer arranque** hasta que la base de datos quede lista.

### 4.2 Opción A: ejecutar con .NET SDK

Copiar las credenciales del archivo de ejemplo y editarlas:

```bash
cp openmrs_seeder_v1/openmrs_seeder_v1/appsettings.example.json \
   openmrs_seeder_v1/openmrs_seeder_v1/appsettings.json
```

Editar `appsettings.json` con la URL y contraseña de tu OpenMRS, luego ejecutar:

```bash
dotnet run --project openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj
```

El simulador quedará disponible en **`http://localhost:5197/swagger`**.

### 4.3 Opción B: ejecutar con Docker

Preparar el archivo de variables de entorno:

```bash
cd docker
cp .env.example .env
# Editar .env con la URL de OpenMRS y la contraseña
```

Construir y levantar el contenedor:

```bash
docker compose -f docker/docker-compose.yml up --build
```

El simulador quedará disponible en **`http://localhost:5197/swagger`**.

> **OpenMRS en el mismo host que Docker:**
> - Windows/Mac: usar `host.docker.internal` como hostname en `OPENMRS_URL`
> - Linux: usar la IP del gateway Docker, habitualmente `172.17.0.1`

Para detener: `docker compose -f docker/docker-compose.yml down`

El Swagger UI permite ejecutar todos los endpoints sin cliente HTTP adicional.

---

## 5. Configuración

El archivo de configuración es `appsettings.json`. El repositorio incluye `appsettings.example.json` como plantilla de referencia (sin credenciales reales).

### 5.1 Sección `OpenMRS`

```json
{
  "OpenMRS": {
    "SeedMode": "RestApi",
    "RestApi": {
      "BaseUrl": "http://localhost/openmrs/ws/rest/v1",
      "Username": "admin",
      "Password": "TU_PASSWORD"
    },
    "Defaults": { ... }
  }
}
```

`SeedMode` debe ser siempre `"RestApi"`. El modo `DirectDb` no está implementado.

### 5.2 UUIDs en `OpenMRS.Defaults`

Estos UUIDs deben coincidir con la instancia OpenMRS que se está usando. Los valores por defecto ya están verificados contra la instancia local de esta instalación:

| Clave | UUID configurado | Descripción |
|-------|-----------------|-------------|
| `PatientIdentifierTypeUuid` | `05a29f94-...` | Tipo "OpenMRS ID" (con validador Luhn) |
| `TrackingIdentifierTypeUuid` | `8d79403a-...` | "Old Identification Number" (para SIM-) |
| `LocationUuid` | `44c3efb0-...` | Ubicación de la clínica |
| `VisitTypeUuid` | `7b0f5697-...` | Tipo de visita "Outpatient" |
| `VitalsEncounterTypeUuid` | `67a71486-...` | Tipo de encuentro "Vitals" |
| `ConsultaEncounterTypeUuid` | `dd528487-...` | Tipo de encuentro "Consultation" |
| `ProviderUuid` | `f9badd80-...` | Proveedor/médico que firma encuentros |
| `EncounterRoleUuid` | `240b26f9-...` | Rol "Clinician" |
| `OutpatientCareSettingUuid` | `6f0c9a92-...` | Care setting para órdenes |
| `OnceDailyFrequencyUuid` | `136ebdb7-...` | Frecuencia "Una vez por día" |
| `DaysConceptUuid` | `1072AAAA...` | Unidad "Days" (CIEL) |
| `TabletConceptUuid` | `1513AAAA...` | Unidad "Tablet(s)" (CIEL) |

Si se usa una instancia diferente, verificar cada UUID con:

```
GET /ws/rest/v1/encountertype          → buscar "Vitals", "Consultation"
GET /ws/rest/v1/visittype              → buscar "Outpatient"
GET /ws/rest/v1/patientidentifiertype  → buscar "OpenMRS ID"
GET /ws/rest/v1/location               → buscar la ubicación de la clínica
GET /ws/rest/v1/provider               → buscar el proveedor
```

### 5.3 Sección `Simulation`

| Parámetro | Por defecto | Descripción |
|-----------|-------------|-------------|
| `StartDate` | `"2023-01-01"` | Inicio del período simulado |
| `EndDate` | `"2024-12-31"` | Fin del período simulado |
| `PacientesPorDiaMedio` | `40` | Promedio de pacientes por día (antes de peso de semana y variación normal) |
| `PorcentajeRecurrentes` | `30` | % de visitas que son de pacientes ya existentes |
| `RandomSeed` | `42` | Semilla para reproducibilidad (ver sección 10.7) |
| `ClinicType` | `"ConsultaExterna"` | Tipo de clínica (solo informativo, sin efecto en el pipeline actual) |

**Pesos por día de semana** (`WeekdayWeights`):

| Día | Peso | Pacientes esperados (medio=40) |
|-----|:----:|:------------------------------:|
| Lunes | 1.20 | ~48 |
| Martes | 1.20 | ~48 |
| Miércoles | 1.00 | ~40 |
| Jueves | 1.00 | ~40 |
| Viernes | 0.90 | ~36 |
| Sábado | 0.50 | ~20 |
| **Domingo** | **0.00** | **0 (siempre)** |

**Probabilidades de derivación** (`ReferralProbabilities`):

| Parámetro | Por defecto | Significado |
|-----------|:-----------:|-------------|
| `LabOrder` | 0.40 | Probabilidad base de ordenar laboratorio |
| `ClinicalExam` | 0.35 | Probabilidad base de registrar examen en consultorio |
| `DrugOrder` | 0.65 | Probabilidad base de prescribir medicamento |
| `Urgent` | 0.20 | Probabilidad de urgencia STAT en laboratorio |

> Las alergias tienen su propia sección `Allergy` en `appsettings.json` (prevalencia 15-25% sorteada por corrida + nº de alergias por decaída condicional). La prevalencia ya no vive en `ReferralProbabilities`. Detalle completo en `config_docs.md` §4.8.

Estas probabilidades son la **base**; el diagnóstico puede elevarlas. Si `diagnosticos.csv` marca `requiere_lab = true` para un diagnóstico, la probabilidad de ordenar lab sube al 80% independientemente del valor de `LabOrder`.

---

## 6. Flujo completo de la simulación

### 6.1 Diagrama de componentes

```
appsettings.json
      │
      ▼
DailyScheduleGenerator ──────────────────── genera agenda de días
      │
      ▼
SeedOrchestrator ← loop por día del período
      │
      ├── PACIENTES NUEVOS
      │     │
      │     ├── PatientProfileGenerator   → nombre, género, edad, dirección
      │     ├── EpidemiologySelector      → categoría dx y diagnóstico
      │     ├── PatientSeeder             → POST /patient → OpenMrsUuid
      │     └── AllergySeeder             → POST /allergy (15% prob.)
      │
      └── PACIENTES RECURRENTES
            │
            └── elige del pool de pacientes ya creados (sin repetir el mismo día)
                EpidemiologySelector → nueva categoría dx para esta visita

            ──── ProcesarVisitaAsync (común a nuevos y recurrentes) ────

            VisitSeeder        → POST /visit               → VisitUuid
            VitalsSeeder       → POST /encounter + 7 obs   (signos vitales)
            ConsultaSeeder     → POST /encounter + obs      (diagnóstico + motivo + examen)
            LabOrderSeeder     → POST /order (testorder)    (condicional)
            PrescriptionSeeder → POST /order (drugorder)    (condicional)
            VisitCloseSeeder   → POST /visit con stopDatetime
```

### 6.2 Decisiones condicionales en el pipeline

```
LabOrderSeeder:
  ¿dx.requiere_lab == true?  → probabilidad 80%
  Si no                      → usa ReferralProbabilities.LabOrder (0.40)
  ¿dx.severidad == "grave"?  → 50% probabilidad de urgencia STAT
  Si no                      → usa ReferralProbabilities.Urgent (0.20)
  Cantidad: 60% un lab, 40% dos labs

PrescriptionSeeder:
  ¿dx.requiere_rx == true?   → probabilidad 90%
  Si no                      → usa ReferralProbabilities.DrugOrder (0.65)
  Cantidad: 1 a 3 medicamentos (aleatorio)
  Duración: 7, 14 o 30 días (aleatorio)

AllergySeeder:
  ¿patient.EsNuevo == false? → skip automático (solo pacientes nuevos)
  Probabilidad               → prevalencia sorteada por corrida en Allergy.BaseProbabilityMin/Max (0.15-0.25)
  Cantidad: decaída condicional (≥1; +1 con SecondAllergyProbability; +1 con ThirdAllergyProbability)
            → la mayoría 1, pocos 2, raros 3

ConsultaSeeder (examen clínico):
  ¿dx.requiere_examen_clinico == true? → probabilidad 90%
  Si no                                → usa ReferralProbabilities.ClinicalExam (0.35)
  ⚠️  ACTUALMENTE NO EJECUTA: examenes_clinicos.csv está vacío
      (ver sección 8)
```

### 6.3 Requests HTTP por paciente

Un paciente nuevo con el pipeline completo genera entre 15 y 25 requests a OpenMRS:

| Operación | Requests | Condición |
|-----------|:--------:|-----------|
| `POST /patient` | 1 | siempre |
| `POST /patient/{uuid}/allergy` | 1–3 | 15% pacientes nuevos |
| `POST /visit` | 1 | siempre |
| `POST /encounter` (vitales) | 1 | siempre |
| `POST /obs` (vitales ×7) | 7 | siempre |
| `POST /encounter` (consulta) | 1 | siempre |
| `POST /obs` (diagnóstico + motivo) | 2–3 | siempre |
| `POST /order` (lab) | 1–2 | ~40–80% |
| `POST /order` (prescripción) | 1–3 | ~65–90% |
| `POST /visit/{uuid}` (cierre) | 1 | siempre |

---

## 7. Cómo usar la API

### 7.1 Verificar que todo está listo

```
GET http://localhost:5197/api/seed/status
```

Respuesta esperada:

```json
{
  "openmrs": {
    "online": true,
    "baseUrl": "http://localhost/openmrs/ws/rest/v1",
    "seedMode": "RestApi"
  },
  "simulation": {
    "startDate": "2023-01-01",
    "endDate": "2024-12-31",
    "pacientesPorDiaMedio": 15
  },
  "catalogs": {
    "epidemiologyProfile": 47,
    "diagnosticos": 11,
    "medicamentos": 10,
    "laboratorios": 8,
    "examenesClinicos": 0,
    "alergenos": 15,
    "motivosConsulta": 37
  }
}
```

Si `"online": false`, OpenMRS no está respondiendo. Ver [sección 13](#13-solución-de-problemas).

Si `"examenesClinicos": 0`, los exámenes clínicos en consultorio no se generarán (estado actual por defecto).

### 7.2 Iniciar una simulación

```
POST http://localhost:5197/api/seed/run
Content-Type: application/json

{}
```

Respuesta inmediata `202 Accepted`:

```json
{
  "runId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**Guardar el `runId`** — es el único identificador del progreso en memoria.

### 7.3 Monitorear el progreso

```
GET http://localhost:5197/api/seed/progress/3fa85f64-5717-4562-b3fc-2c963f66afa6
```

Respuesta:

```json
{
  "runId": "3fa85f64-...",
  "porcentaje": 42,
  "etapa": "simulando",
  "pacientesCreados": 1680,
  "diasProcesados": 306,
  "totalDias": 730,
  "fechaActual": "2023-11-03",
  "completado": false,
  "errores": [],
  "inicio": "2026-06-19T10:00:00Z"
}
```

| Campo | Descripción |
|-------|-------------|
| `porcentaje` | Avance 0–100 basado en días procesados |
| `etapa` | `"iniciando"` → `"simulando"` → `"completado"` / `"error"` |
| `pacientesCreados` | Pacientes ya creados en OpenMRS |
| `fechaActual` | Fecha simulada en proceso (no la fecha real del servidor) |
| `errores` | Errores no fatales — el pipeline continúa ante errores individuales |
| `completado` | `true` al terminar (con o sin errores) |

Cuando `completado = true` y `porcentaje = 100`: la simulación finalizó con éxito.

Cuando `completado = true` y `etapa = "error"`: falló el proceso completo (raramente). Revisar `errores`.

### 7.4 Limpiar todos los datos del simulador

```
DELETE http://localhost:5197/api/seed/clear
```

Respuesta:

```json
{
  "pacientesVoided": 500,
  "visitasVoided": 498
}
```

Esta operación es lenta: aplica rate limiting de 200 ms entre pacientes y 100 ms entre visitas del mismo paciente para no saturar OpenMRS. Para 500 pacientes con 1 visita cada uno: ~100 segundos.

Los registros se marcan como `voided = true` en OpenMRS (borrado lógico), no se eliminan físicamente de la base de datos.

---

## 8. Catálogos CSV — estado real y cómo extenderlos

Los catálogos están en `openmrs_seeder_v1/openmrs_seeder_v1/catalogs/`.

### 8.1 Estado actual de los catálogos

| Archivo | Filas reales | Estado | Notas |
|---------|:------------:|--------|-------|
| `epidemiology-profile.csv` | 47 | Completo | 8 categorías × 5 grupos × géneros |
| `alergenos.csv` | 15 | Completo | 7 DRUG, 3 ENV, 5 FOOD — UUIDs verificados |
| `motivos_consulta.csv` | 37 | Completo | Frases en español por categoría |
| `diagnosticos.csv` | 11 | Muestra | 1 por categoría — UUIDs verificados en instancia local |
| `medicamentos.csv` | 10 | Muestra | UUIDs de drug + concept verificados |
| `laboratorios.csv` | 8 | Muestra | UUIDs verificados |
| `examenes_clinicos.csv` | **0** | **VACÍO** | Solo cabecera — ver abajo |

**`examenes_clinicos.csv` está vacío.** El código de `ConsultaSeeder` para registrar exámenes en consultorio está implementado y es funcional, pero nunca se ejecuta porque no hay candidatos en el catálogo. Para activar esta funcionalidad hay que poblar el archivo (ver 8.6).

### 8.2 Esquema de `epidemiology-profile.csv`

```
categoria, grupo_edad, genero, peso
```

- `categoria`: nombre de la categoría diagnóstica (ej: `cardiovascular`, `infeccioso`)
- `grupo_edad`: `0-14`, `15-29`, `30-44`, `45-64`, `65+`
- `genero`: `M`, `F`, `Ambos`
- `peso`: número relativo (no necesita sumar 100 — se normaliza internamente)

### 8.3 Esquema de `diagnosticos.csv`

```
ciel_uuid, nombre_es, categoria, severidad,
aplica_0_14, aplica_15_29, aplica_30_44, aplica_45_64, aplica_65mas,
peso_M, peso_F, requiere_lab, requiere_rx, requiere_examen_clinico
```

- `ciel_uuid`: UUID del concepto en OpenMRS (verificar con `GET /concept?q=nombre`)
- `severidad`: `"leve"`, `"moderada"`, `"grave"` (afecta urgencia de labs)
- `aplica_*`: `true`/`false` — si el diagnóstico es aplicable para ese grupo etario
- `requiere_lab/rx/examen_clinico`: `true` eleva la probabilidad al 80%/90%/90%

Para obtener más UUIDs reales de diagnósticos:

```sql
SELECT c.uuid, cn.name, c.class_id
FROM concept c
JOIN concept_name cn ON c.concept_id = cn.concept_id
WHERE cn.name LIKE '%hipertensión%'
  AND cn.locale = 'es'
  AND c.retired = 0;
```

### 8.4 Esquema de `medicamentos.csv`

```
drug_uuid, concept_uuid, nombre_generico, strength, via_uuid,
aplica_respiratorio, aplica_cardiovascular, aplica_diabetes,
aplica_digestivo, aplica_osteomuscular, aplica_urologico,
aplica_infeccioso, aplica_endocrino
```

- `drug_uuid`: UUID del drug (tabla `drug` de OpenMRS)
- `concept_uuid`: UUID del concepto del drug (distinto del drug_uuid)
- `via_uuid`: UUID del concepto de la vía de administración (oral, IV, etc.)

Ambos UUIDs son necesarios porque DrugOrder los requiere por separado.

Para obtener UUIDs de medicamentos:

```sql
SELECT d.uuid as drug_uuid, c.uuid as concept_uuid, cn.name
FROM drug d
JOIN concept c ON d.concept_id = c.concept_id
JOIN concept_name cn ON c.concept_id = cn.concept_id
WHERE cn.locale = 'es' AND d.retired = 0;
```

### 8.5 Esquema de `laboratorios.csv`

```
ciel_uuid, nombre_es, clase,
aplica_respiratorio, aplica_cardiovascular, aplica_diabetes,
aplica_digestivo, aplica_osteomuscular, aplica_urologico,
aplica_infeccioso, aplica_endocrino
```

- `clase`: solo informativo (`"Hematologia"`, `"Bioquimica"`, etc.)

### 8.6 Esquema de `examenes_clinicos.csv` (VACÍO — pendiente de poblar)

```
ciel_uuid, nombre_es, tipo_resultado, unidad,
aplica_respiratorio, aplica_cardiovascular, aplica_diabetes,
aplica_digestivo, aplica_osteomuscular, aplica_urologico,
aplica_infeccioso, aplica_endocrino
```

- `tipo_resultado`: `"numerico"` o `"categorico"`
- `unidad`: `"mg/dL"`, `"mmHg"`, `"%"`, etc. (determina el rango de valores numéricos)

La lógica de generación de valores ya está implementada en `ConsultaSeeder.GenerateNumericValue`:
- `mg/dL` → glucosa: 100–300 (diabetes) / 70–130 (normal)
- `%` → SpO₂: 88–96 (respiratorio) / 95–100 (normal)
- `mmHg` → PA: 100–180

Para activar exámenes clínicos basta con agregar filas al CSV con UUIDs verificados en la instancia y recompilar.

### 8.7 Esquema de `alergenos.csv`

```
concept_uuid, nombre_es, tipo_alergeno, severidad_tipica
```

- `tipo_alergeno`: `DRUG`, `FOOD`, `ENVIRONMENT`
- `severidad_tipica`: `leve`, `moderada`, `grave`

> **Nota:** El formato de `codedAllergen` en la REST API de OpenMRS requiere un string UUID plano, NO un objeto `{uuid: "..."}`. El `AllergySeeder` ya implementa esto correctamente.

### 8.8 Esquema de `motivos_consulta.csv`

```
categoria, texto
```

Frases de texto libre en español para el chief complaint. Se elige aleatoriamente entre las frases de la categoría del diagnóstico del paciente.

---

## 9. Casos de uso prácticos

### Caso 1: Validación rápida del pipeline (5–10 minutos)

Objetivo: verificar que el simulador se conecta a OpenMRS y que todos los seeders funcionan.

```json
// appsettings.json
"StartDate": "2023-01-02",
"EndDate":   "2023-01-08",
"PacientesPorDiaMedio": 5,
"PorcentajeRecurrentes": 0
```

Esto procesa 6 días hábiles con ~5 pacientes/día = ~30 pacientes totales, todos nuevos. A 150 ms/request son ~3–5 minutos.

Verificar en OpenMRS:
1. Buscar paciente por `SIM-` — deben aparecer ~30 pacientes
2. Abrir uno y verificar: visita, vitales, diagnóstico, motivo de consulta
3. El campo `errores` del progress debe estar vacío

---

### Caso 2: Dataset de demostración de 1 año

Objetivo: poblar una instancia de demostración con datos de un año completo.

```json
"StartDate": "2024-01-01",
"EndDate":   "2024-12-31",
"PacientesPorDiaMedio": 20,
"PorcentajeRecurrentes": 25,
"RandomSeed": 42
```

Estimación: ~240 días hábiles × 20 pacientes = ~4.800 pacientes, ~96.000 requests. A 150 ms/request: ~4 horas.

Recomendación: lanzar antes de ir a dormir y revisar el progress a la mañana.

---

### Caso 3: Dataset reproducible para análisis académico

Objetivo: generar exactamente el mismo dataset dos veces para compartir con otros.

```json
"StartDate": "2023-01-01",
"EndDate":   "2023-06-30",
"PacientesPorDiaMedio": 30,
"RandomSeed": 777
```

Con la misma `RandomSeed` y misma configuración, dos ejecuciones producen exactamente los mismos pacientes (mismo nombre, misma fecha de nacimiento, mismo diagnóstico, misma hora de visita). Limpiar con `DELETE /clear` y volver a ejecutar para regenerar idénticamente.

---

### Caso 4: Escenario de alta carga cardiovascular

Objetivo: poblar datos donde el 60% de los pacientes tienen diagnósticos cardiovasculares.

Editar `epidemiology-profile.csv`: para cada grupo etario ≥30 años, asignar peso alto a `cardiovascular` (p. ej. 60) y pesos bajos al resto (p. ej. 5–10 cada uno). Los pesos son relativos y se normalizan.

---

### Caso 5: Limpiar y regenerar desde cero

```
1. DELETE http://localhost:5197/api/seed/clear
   → esperar respuesta con conteo de voided
2. Ajustar appsettings.json (si se quiere cambiar el período)
3. POST http://localhost:5197/api/seed/run
4. GET http://localhost:5197/api/seed/progress/{runId}
```

---

### Caso 6: Ampliar el catálogo de medicamentos con datos reales

1. Conectarse a la base MariaDB (ver sección 12)
2. Ejecutar el query de la sección 8.4 para obtener drug_uuid y concept_uuid
3. Copiar los resultados a `medicamentos.csv` con las columnas de aplicabilidad
4. Recompilar: `dotnet build openmrs_seeder_v1/openmrs_seeder_v1.csproj`
5. Verificar con `GET /api/seed/status` que el conteo de medicamentos aumentó

---

## 10. Manejo del tiempo

### 10.1 Período simulado vs. tiempo real

El simulador itera días históricos sin relación con el tiempo real del servidor. `StartDate` y `EndDate` son fechas del pasado (desde la perspectiva de OpenMRS), que acepta sin restricción. El simulador avanza tan rápido como responde la API.

### 10.2 Cálculo del volumen diario (Box-Muller)

```
μ = PacientesPorDiaMedio × WeekdayWeight[díaSemana]
σ = μ × 0.20

PacientesDelDía = max(0, round(Normal(μ, σ)))
```

La distribución normal se genera con Box-Muller para dar variación natural entre días. Los domingos (`weight = 0.00`) siempre producen 0 pacientes.

Con `PorcentajeRecurrentes = 30`:
```
NuevosPacientes      = round(PacientesDelDía × 0.70)
PacientesRecurrentes = PacientesDelDía - NuevosPacientes
```

### 10.3 Hora de visita

Cada paciente recibe una hora generada por `DailyScheduleGenerator.GenerateVisitTime`:

```
PicoAM (40%): 08:00–10:00  → hora aleatoria uniforme
PicoPM (30%): 14:00–16:00  → hora aleatoria uniforme
Resto  (30%): 07:00–18:00  → hora aleatoria uniforme
```

Esta hora es `patient.VisitDatetime` y es la base temporal de todo el pipeline.

### 10.4 Timestamps a lo largo del pipeline

| Seeder | Campo OpenMRS | Timestamp |
|--------|--------------|-----------|
| `VisitSeeder` | `startDatetime` | `VisitDatetime` |
| `VitalsSeeder` | `encounterDatetime` | `VisitDatetime` |
| `ConsultaSeeder` | `encounterDatetime` | `VisitDatetime + 30 min` |
| `VisitCloseSeeder` | `stopDatetime` | `VisitDatetime + 1–4 horas` |

El desfase de 30 minutos en la consulta refleja que el médico atiende al paciente después del registro de vitales.

### 10.5 Formato de fecha enviado a OpenMRS

```csharp
dt.ToString("yyyy-MM-dd'T'HH:mm:ss.000+0000")
// Ejemplo: "2023-03-15T09:45:00.000+0000"
```

El sufijo `+0000` indica UTC. Si OpenMRS tiene configurada una zona horaria local (p. ej. `America/Lima`, UTC−5), la UI mostrará las horas restando 5 horas. Este es el comportamiento estándar de OpenMRS — no es un bug del simulador.

### 10.6 Fecha de nacimiento

La fecha de nacimiento se calcula restando la edad a **la fecha real del servidor** (no a `StartDate`). Un paciente del grupo `45-64` generado en junio 2026 nació entre 1962 y 1981. Su edad al `StartDate = 2023` estaría entre 42 y 61 años — dentro del grupo correcto pero con ~3 años de diferencia respecto al año de simulación. Para proyectos académicos, esta discrepancia es aceptable.

### 10.7 Reproducibilidad con RandomSeed

| Componente | Seed usada |
|-----------|-----------|
| `DailyScheduleGenerator` | `RandomSeed` |
| `PatientProfileGenerator` (RNG) | `RandomSeed + 1` |
| `PatientProfileGenerator` (Bogus Faker) | `RandomSeed + 2` |
| `EpidemiologySelector` | `RandomSeed + 3` |

Con la misma seed y misma configuración, dos ejecuciones producen exactamente el mismo resultado.

### 10.8 Velocidad real de ejecución y estimación de tiempo

| Config | Pacientes totales | Requests aprox. | A 80ms/req | A 150ms/req |
|--------|:-----------------:|:---------------:|:----------:|:-----------:|
| 7 días, 5/día | ~35 | ~600 | ~1 min | ~2 min |
| 1 mes, 15/día | ~390 | ~7.000 | ~10 min | ~18 min |
| 6 meses, 20/día | ~2.600 | ~46.800 | ~1 hora | ~2 horas |
| 2 años, 40/día | ~20.800 | ~375.000 | ~8 horas | ~16 horas |

La latencia real en una instancia local suele estar entre 80–150 ms/request. En servidores remotos puede ser mayor dependiendo de la red.

---

## 11. Limpieza e idempotencia

### Cómo el simulador rastrea sus datos

Todo paciente creado recibe el identificador `SIM-XXXXXXXX` (8 hex en mayúsculas, tipo "Old Identification Number"). Las visitas y encuentros incluyen `"SEEDED_BY_SIMULATOR"` en el campo `description`.

El `DELETE /api/seed/clear` usa la búsqueda `GET /patient?identifier=SIM-&limit=100` paginada para encontrar todos los pacientes del simulador, luego hace void de sus visitas y del paciente.

### Reglas de idempotencia

- **Pacientes reales:** nunca se tocan (no tienen prefijo `SIM-`)
- **Doble ejecución:** cada `POST /run` crea pacientes nuevos con UUIDs de identificador distintos (basados en `Guid.NewGuid()`), aunque usen la misma seed. No hay colisión
- **Overlap de visita:** si OpenMRS rechaza una visita por overlap (`visitCannotOverlapAnother`), el simulador busca la visita activa existente y la reutiliza en lugar de fallar

---

## 12. Acceso directo a MariaDB

Acceder directamente a la base de datos de OpenMRS es útil para extraer UUIDs reales para los catálogos CSV. El método de acceso depende de cómo esté desplegado OpenMRS.

**Si OpenMRS corre en Docker** y el puerto 3306 está expuesto en el compose de OpenMRS:

```bash
mysql -h 127.0.0.1 -P 3306 -u openmrs -popenmrs openmrs
```

Las credenciales por defecto de la imagen oficial de OpenMRS 3.x son:

| Campo | Valor por defecto |
|-------|------------------|
| Host | `localhost` |
| Puerto | `3306` |
| Usuario | `openmrs` |
| Password | `openmrs` |
| Base de datos | `openmrs` |

> Verificar el `docker-compose.yml` de tu instalación de OpenMRS para confirmar si el puerto está expuesto y qué credenciales usa.

**Si OpenMRS corre en un servidor remoto**, solicitar acceso al DBA con permisos de lectura sobre la base `openmrs`.

**Queries útiles para extraer UUIDs para los catálogos:**

```sql
-- Diagnósticos en español
SELECT c.uuid, cn.name, c.class_id
FROM concept c
JOIN concept_name cn ON c.concept_id = cn.concept_id
WHERE cn.locale = 'es' AND cn.concept_name_type = 'FULLY_SPECIFIED'
  AND c.retired = 0 AND c.class_id = 4  -- class 4 = Diagnosis
ORDER BY cn.name;

-- Medicamentos (drug_uuid + concept_uuid)
SELECT d.uuid AS drug_uuid, c.uuid AS concept_uuid,
       cn.name, d.strength
FROM drug d
JOIN concept c ON d.concept_id = c.concept_id
JOIN concept_name cn ON c.concept_id = cn.concept_id
WHERE cn.locale = 'es' AND d.retired = 0
ORDER BY cn.name;

-- Órdenes de lab (testorder concepts)
SELECT c.uuid, cn.name
FROM concept c
JOIN concept_name cn ON c.concept_id = cn.concept_id
WHERE cn.locale = 'es' AND c.class_id = 5  -- class 5 = Test
  AND c.retired = 0
ORDER BY cn.name;
```

> **Importante:** Si se cambia un UUID en un CSV, reiniciar el simulador para que recargue los catálogos. El cargado se hace una única vez al startup en `Program.cs`.

---

## 13. Solución de problemas

### `"online": false` en el status

```
GET /api/seed/status → openmrs.online = false
```

1. Verificar que OpenMRS está accesible desde donde corre el simulador:
   ```
   curl -u admin:<password> http://<tu-host>/openmrs/ws/rest/v1/session
   ```
2. Si OpenMRS corre en Docker: puede tardar hasta 15 minutos en el primer arranque. Verificar con `docker compose ps` desde la carpeta del compose de OpenMRS y esperar estado `healthy`.
3. Si el simulador corre en Docker (Opción B): la URL no puede ser `localhost` — usar `host.docker.internal` (Windows/Mac) o `172.17.0.1` (Linux) en `OPENMRS_URL` del `docker/.env`.
4. Verificar que la URL en `appsettings.json` (o variable `OpenMRS__RestApi__BaseUrl`) termina en `/ws/rest/v1` sin trailing slash extra.

---

### Errores de UUID en el campo `errores` del progress

Mensajes como `"404 Not Found"`, `"Invalid UUID"` o `"Resource ... does not exist"`:

- Los UUIDs en `OpenMRS.Defaults` no corresponden a esta instancia
- Verificar con los endpoints de la sección 5.2
- Los errores no detienen el pipeline — el paciente afectado se registra con error y se continúa

---

### Catálogos con 0 registros en el status

```json
"catalogs": { "diagnosticos": 0 }
```

Los CSV no se copiaron al directorio de salida. Ejecutar:

```powershell
dotnet build openmrs_seeder_v1/openmrs_seeder_v1.csproj
```

Verificar que exista `bin/Debug/net10.0/catalogs/diagnosticos.csv`.

---

### Las horas de visita aparecen desfasadas en OpenMRS O3

Las horas se envían como UTC (`+0000`). Si la instancia OpenMRS tiene zona horaria configurada (p. ej. UTC−5), la UI restará 5 horas. Solución:

- Cambiar la zona horaria de OpenMRS a UTC, o
- Modificar `VisitSeeder.FormatDatetime` para incluir el offset local (p. ej. `+0500` para UTC+5)

---

### La simulación termina con `etapa: "error"` muy pronto

El proceso completo falló (no un paciente individual). Causas comunes:

1. OpenMRS se cayó durante la simulación — verificar `docker compose ps`
2. Los catálogos están vacíos (sin diagnósticos no puede generar pacientes)
3. El `ProviderUuid` o `LocationUuid` es incorrecto — OpenMRS rechaza todos los encuentros

Revisar el campo `errores` del progress para el mensaje exacto.

---

### Pacientes duplicados tras ejecutar dos veces sin limpiar

Cada `POST /run` genera nuevos pacientes con identificadores `SIM-` únicos (GUID). No hay colisión lógica, pero en OpenMRS aparecerán dos poblaciones de pacientes simulados. Limpiar con `DELETE /clear` antes de reiniciar si se quiere un dataset limpio.

---

### `DELETE /clear` es muy lento

El rate limiting de 200 ms entre pacientes es intencional para no saturar OpenMRS. Para 1.000 pacientes la limpieza toma ~200 segundos. No hay forma de acelerar esto sin modificar el código.

---

### SpO₂ se rechaza con error de valor fuera de rango

El concepto SpO₂ en esta instancia tiene `hiAbsolute = 99`. El simulador ya tiene esto corregido — envía máximo 98% para SpO₂. Si aparece este error, verificar que se está usando la versión actual del código.

---

*Fin del manual — basado en el código fuente a junio 2026*
