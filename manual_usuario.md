# Manual de Usuario — OpenMRS Clinical Simulator

## Tabla de Contenidos

1. [Introducción y propósito](#1-introducción-y-propósito)
2. [Requisitos previos](#2-requisitos-previos)
3. [Configuración inicial](#3-configuración-inicial)
4. [Arranque del entorno Docker](#4-arranque-del-entorno-docker)
5. [Ejecución de la simulación](#5-ejecución-de-la-simulación)
6. [Monitoreo del progreso](#6-monitoreo-del-progreso)
7. [Verificación de datos en OpenMRS O3](#7-verificación-de-datos-en-openmrs-o3)
8. [Limpieza de datos](#8-limpieza-de-datos)
9. [Personalización avanzada](#9-personalización-avanzada)
10. [Manejo del Tiempo en el Simulador](#10-manejo-del-tiempo-en-el-simulador)
11. [Solución de problemas frecuentes](#11-solución-de-problemas-frecuentes)

---

## 1. Introducción y propósito

El **OpenMRS Clinical Simulator** es una API REST que genera registros clínicos realistas en una instancia OpenMRS 3.x. Simula el flujo operativo de una clínica durante un período de tiempo configurable: pacientes que llegan con variación estadística diaria, reciben diagnósticos coherentes con su edad y género, y generan visitas, signos vitales, observaciones, órdenes de laboratorio y prescripciones.

**No escribe SQL directo.** Toda la carga de datos se hace exclusivamente vía la REST API de OpenMRS (`/ws/rest/v1`), lo que lo hace compatible con cualquier despliegue estándar de OpenMRS.

**Casos de uso típicos:**

- Poblar una instancia de demostración o prueba con datos clínicos realistas en español
- Verificar que módulos de OpenMRS O3 (registros, labs, prescripciones) funcionen correctamente
- Proveer datos de entrenamiento para personal médico o técnico
- Evaluar el rendimiento de la instancia bajo carga de datos histórica

---

## 2. Requisitos previos

| Requisito | Versión mínima | Notas |
|-----------|---------------|-------|
| .NET SDK | 10.0 | Descargar en https://dotnet.microsoft.com |
| Docker Desktop | 4.x | Para correr OpenMRS |
| OpenMRS Reference App | 3.6.0 | Incluido en el docker-compose del proyecto |
| RAM disponible | 4 GB | Para Docker con OpenMRS + MariaDB |

Verificar instalación de .NET:
```
dotnet --version
```

---

## 3. Configuración inicial

### 3.1 Verificar UUIDs contra la instancia OpenMRS

La sección `OpenMRS.Defaults` del `appsettings.json` contiene UUIDs que deben coincidir con la instancia. Los valores por defecto corresponden al OpenMRS Reference App 3.6.0 estándar, pero pueden variar.

Verificar con los siguientes endpoints REST (usando las credenciales de la instancia):

```
GET http://localhost/openmrs/ws/rest/v1/patientidentifiertype
  → Buscar "OpenMRS ID" → copiar su uuid como PatientIdentifierTypeUuid

GET http://localhost/openmrs/ws/rest/v1/location
  → Buscar la ubicación de la clínica → copiar su uuid como LocationUuid

GET http://localhost/openmrs/ws/rest/v1/visittype
  → Buscar "Outpatient" → copiar su uuid como VisitTypeUuid

GET http://localhost/openmrs/ws/rest/v1/encountertype
  → Buscar "Vitals" → VitalsEncounterTypeUuid
  → Buscar "Consultation" o "Visit Note" → ConsultaEncounterTypeUuid

GET http://localhost/openmrs/ws/rest/v1/provider
  → Buscar el proveedor que firmará los encuentros → ProviderUuid
```

### 3.2 Parámetros de appsettings.json

**Sección `OpenMRS`:**

| Parámetro | Descripción | Valor por defecto |
|-----------|-------------|-------------------|
| `SeedMode` | Modo de conexión | `"RestApi"` |
| `RestApi.BaseUrl` | URL base de la API REST | `"http://localhost/openmrs/ws/rest/v1"` |
| `RestApi.Username` | Usuario de OpenMRS | `"admin"` |
| `RestApi.Password` | Contraseña de OpenMRS | `"Admin123"` |
| `Defaults.PatientIdentifierTypeUuid` | UUID del tipo de identificador del paciente | ver instancia |
| `Defaults.LocationUuid` | UUID de la ubicación de la clínica | ver instancia |
| `Defaults.VisitTypeUuid` | UUID del tipo de visita (OUTPATIENT) | ver instancia |
| `Defaults.VitalsEncounterTypeUuid` | UUID del tipo de encuentro de vitales | ver instancia |
| `Defaults.ConsultaEncounterTypeUuid` | UUID del tipo de encuentro de consulta | ver instancia |
| `Defaults.ProviderUuid` | UUID del proveedor que firma los encuentros | ver instancia |
| `Defaults.EncounterRoleUuid` | UUID del rol del proveedor en el encuentro | ver instancia |

**Sección `Simulation`:**

| Parámetro | Descripción | Valor por defecto |
|-----------|-------------|-------------------|
| `StartDate` | Fecha de inicio del período simulado | `"2023-01-01"` |
| `EndDate` | Fecha de fin del período simulado | `"2024-12-31"` |
| `PacientesPorDiaMedio` | Promedio de pacientes por día hábil | `40` |
| `PorcentajeRecurrentes` | % de visitas de pacientes existentes | `30` |
| `Locale` | Idioma para generación de nombres (Bogus) | `"es"` |
| `RandomSeed` | Semilla para reproducibilidad | `42` |
| `ClinicType` | Tipo de establecimiento | `"ConsultaExterna"` |
| `WeekdayWeights.Monday`..`Sunday` | Multiplicador de volumen por día de semana | lunes/martes 1.20, domingo 0.00 |
| `ReferralProbabilities.LabOrder` | Probabilidad de orden de laboratorio | `0.40` |
| `ReferralProbabilities.ClinicalExam` | Probabilidad de examen en consultorio | `0.35` |
| `ReferralProbabilities.DrugOrder` | Probabilidad de prescripción | `0.65` |
| `ReferralProbabilities.AllergyOnNew` | Probabilidad de alergia en paciente nuevo | `0.15` |

### 3.3 Catálogos CSV

Los catálogos se encuentran en `openmrs_seeder_v1/openmrs_seeder_v1/catalogs/` y se copian automáticamente al directorio de salida al compilar.

| Archivo | Filas | Estado |
|---------|-------|--------|
| `epidemiology-profile.csv` | 46 | Datos completos |
| `examenes_clinicos.csv` | 12 | Datos completos |
| `alergenos.csv` | 20 | Datos completos |
| `motivos_consulta.csv` | 36 | Datos completos |
| `diagnosticos.csv` | 11 | Muestra — reemplazar con datos reales de DB |
| `medicamentos.csv` | 10 | Muestra — reemplazar con datos reales de DB |
| `laboratorios.csv` | 10 | Muestra — reemplazar con datos reales de DB |

Para obtener datos reales de los 3 archivos de muestra, ejecutar las queries SQL del archivo `fases_implementacion.md` (Sección Fase 2) contra la base MariaDB de la instancia.

---

## 4. Arranque del entorno Docker

```powershell
# Desde la ruta del distro:
cd "C:\Users\Moises Molina\Documents\Estudios\Especializacion\PruebaOpenMRS1\open-omrs36-prod\openmrs-distro-referenceapplication-3.6.0"

docker compose up -d
```

Esperar ~10 minutos a que el backend esté healthy. Verificar con:
```
docker compose ps
```
Todos los servicios deben mostrar `healthy` o `running`. El backend OpenMRS tarda más que el frontend.

Acceder a: `http://localhost` → login con `admin / Admin123`.

---

## 5. Ejecución de la simulación

### Compilar y ejecutar la API del simulador

```powershell
# Desde la raíz del proyecto:
cd "C:\Users\Moises Molina\Documents\Estudios\Especializacion\proyecto_api_seedeer\openmrs_seeder_v1"

dotnet run --project openmrs_seeder_v1/openmrs_seeder_v1.csproj
```

El simulador estará disponible en: `http://localhost:5197/swagger`

### Paso 1 — Verificar conectividad

```
GET http://localhost:5197/api/seed/status
```

Respuesta esperada:
```json
{
  "openmrs": { "online": true, "baseUrl": "...", "seedMode": "RestApi" },
  "catalogs": {
    "epidemiologyProfile": 46, "diagnosticos": 11,
    "medicamentos": 10, "laboratorios": 10,
    "examenesClinicos": 12, "alergenos": 20, "motivosConsulta": 36
  }
}
```

Si `"online": false`, verificar que Docker esté corriendo y que la URL base sea correcta.

### Paso 2 — Iniciar la simulación

```
POST http://localhost:5197/api/seed/run
Content-Type: application/json

{}
```

Respuesta inmediata (202 Accepted):
```json
{ "runId": "3fa85f64-5717-4562-b3fc-2c963f66afa6" }
```

La simulación corre en segundo plano. Guardar el `runId` para monitorear el progreso.

---

## 6. Monitoreo del progreso

```
GET http://localhost:5197/api/seed/progress/{runId}
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
| `porcentaje` | Avance 0–100 basado en días procesados vs. total |
| `etapa` | Estado actual: `"iniciando"`, `"simulando"`, `"completado"`, `"error"` |
| `pacientesCreados` | Total de pacientes creados en OpenMRS hasta el momento |
| `diasProcesados` | Días del período simulado ya procesados |
| `totalDias` | Total de días en el rango StartDate–EndDate |
| `fechaActual` | Fecha simulada que se está procesando actualmente |
| `errores` | Lista de errores no fatales (el pipeline continúa ante errores individuales) |
| `completado` | `true` cuando termina, independientemente de si hubo errores |

Cuando `completado = true` y `porcentaje = 100`, la simulación finalizó.

---

## 7. Verificación de datos en OpenMRS O3

Acceder a `http://localhost` con `admin / Admin123`.

**Verificaciones recomendadas:**

1. **Listado de pacientes** → buscar por identificador `SIM-` → debe mostrar pacientes con nombres en español
2. **Chart del paciente** → abrir cualquier paciente `SIM-` → verificar:
   - Visitas en el historial con fechas del período simulado
   - Signos vitales graficados
   - Diagnósticos en el resumen clínico
   - Órdenes de laboratorio (si Fase 6 implementada)
   - Medicamentos activos (si Fase 7 implementada)
   - Alergias (si Fase 7 implementada)
3. **Coherencia temporal** → las horas de las visitas deben caer mayormente entre 08:00–10:00 y 14:00–16:00

---

## 8. Limpieza de datos

```
DELETE http://localhost:5197/api/seed/clear
```

Hace void lógico (no borrado físico) de todos los registros con prefijo `SIM-`:
- Pacientes → visitas → encuentros → observaciones → órdenes → alergias

Respuesta:
```json
{ "pacientes": 500, "visitas": 498, "encounters": 994, "ordenes": 320 }
```

Los registros se marcan como `voided = true` en OpenMRS pero permanecen en la base de datos. Esto permite recuperación si fuera necesario.

---

## 9. Personalización avanzada

### Cambiar el período simulado

```json
"StartDate": "2022-01-01",
"EndDate": "2022-12-31"
```

Un año completo ≈ 365 días ≈ 11,900 pacientes (con promedio 40/día y ausentismo de domingos).

### Reducir volumen para pruebas rápidas

```json
"StartDate": "2023-01-01",
"EndDate": "2023-01-07",
"PacientesPorDiaMedio": 5
```

Esto procesa solo 7 días con ~5 pacientes/día ≈ 35 pacientes totales. Ideal para verificar el pipeline rápidamente.

### Ajustar distribución de diagnósticos

Editar `catalogs/epidemiology-profile.csv`. La columna `peso` es relativa: valores más altos aumentan la probabilidad de esa categoría para ese grupo edad/género. No requieren sumar 100.

### Cambiar perfiles de horario

```json
"HorarioAtencion": {
  "PicoAM": { "Inicio": "07:00", "Fin": "09:00", "Peso": 50 },
  "PicoPM": { "Inicio": "15:00", "Fin": "17:00", "Peso": 20 }
}
```

El peso restante (100 - 50 - 20 = 30%) se distribuye entre 07:00 y 18:00.

### Cambiar la semilla de aleatoriedad

```json
"RandomSeed": 99
```

Produce una distribución de pacientes diferente pero completamente reproducible. Útil para generar múltiples escenarios distintos.

---

## 10. Manejo del Tiempo en el Simulador

Esta sección describe con precisión cómo el simulador genera, transforma y envía fechas y horas a OpenMRS en cada paso del pipeline.

### 10.1 Ventana de simulación

Los parámetros `StartDate` y `EndDate` definen el rango de fechas históricas que el simulador va a "poblar" en OpenMRS. Son fechas de calendario (`DateOnly` en C#), sin componente de hora.

```json
"StartDate": "2023-01-01",
"EndDate":   "2024-12-31"
```

- El simulador itera **exactamente día por día** desde `StartDate` hasta `EndDate` inclusive.
- Con los valores por defecto se procesan **730 días** (2 años).
- Estos días son fechas **pasadas desde el punto de vista de OpenMRS**. La REST API de OpenMRS acepta fechas históricas sin restricción.
- No existe relación entre el tiempo real de ejecución del simulador y el período simulado. El simulador avanza tan rápido como responde la REST API.

### 10.2 Cálculo del volumen diario

Para cada día del período, el simulador calcula cuántos pacientes se atienden ese día:

```
μ (media)     = PacientesPorDiaMedio × WeekdayWeight[díaSemana]
σ (desviación) = μ × 0.20

PacientesDelDía = max(0, round(Normal(μ, σ)))
```

La distribución normal se genera con el algoritmo **Box-Muller transform** (ver `DailyScheduleGenerator.cs`, método `SampleNormal`):

```csharp
var u1 = 1.0 - _rng.NextDouble();
var u2 = 1.0 - _rng.NextDouble();
var z  = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
return mean + stdDev * z;
```

**Pesos por día de semana** (configurables en `WeekdayWeights`):

| Día | Peso por defecto | Pacientes esperados (medio=40) | Rango típico (±20%) |
|-----|:-:|:-:|:-:|
| Lunes | 1.20 | 48 | 39–57 |
| Martes | 1.20 | 48 | 39–57 |
| Miércoles | 1.00 | 40 | 32–48 |
| Jueves | 1.00 | 40 | 32–48 |
| Viernes | 0.90 | 36 | 29–43 |
| Sábado | 0.50 | 20 | 16–24 |
| **Domingo** | **0.00** | **0** | **siempre 0** |

El peso 0.00 del domingo no entra en el cálculo Box-Muller — directamente se asigna `count = 0`.

**División nuevos/recurrentes:** El total del día se divide según `PorcentajeRecurrentes`:

```
NuevosPacientes      = round(PacientesDelDía × (1 - PorcentajeRecurrentes / 100))
PacientesRecurrentes = PacientesDelDía - NuevosPacientes
```

Con `PorcentajeRecurrentes = 30`: un día con 40 pacientes produce 28 nuevos + 12 recurrentes.

### 10.3 Hora de visita — distribución intradiaria

Cada paciente del día recibe una hora de llegada generada por `DailyScheduleGenerator.GenerateVisitTime(DateOnly date)`. La distribución respeta los bloques horarios configurados en `HorarioAtencion`:

```
PicoAM (Peso 40%):  08:00–10:00  → hora aleatoria uniforme en 120 minutos
PicoPM (Peso 30%):  14:00–16:00  → hora aleatoria uniforme en 120 minutos
Resto  (Peso 30%):  07:00–18:00  → hora aleatoria uniforme en 660 minutos
```

Algoritmo de selección:

```csharp
var pick = _rng.NextDouble() * 100;

if (pick < pesoAM)         // [0, 40)    → bloque AM
    ...
else if (pick < pesoAM + pesoPM) // [40, 70) → bloque PM
    ...
else                        // [70, 100)  → resto del día
    var offset = _rng.Next(0, 660); // 07:00 + N minutos
```

El resultado es un `TimeOnly` que se combina con la fecha del día:

```csharp
return date.ToDateTime(time);  // → DateTime con fecha simulada + hora generada
```

Este valor se almacena en `patient.VisitDatetime` y es la **referencia temporal base** para todo el pipeline de ese paciente.

### 10.4 Flujo de timestamps a lo largo del pipeline

Una vez fijado `patient.VisitDatetime`, cada seeder lo utiliza de la siguiente forma:

| Paso | Seeder | Campo OpenMRS | Timestamp calculado | Ejemplo (llegada 09:15) |
|------|--------|---------------|---------------------|------------------------|
| 4 | `VisitSeeder` | `startDatetime` | `VisitDatetime` | `2023-03-15T09:15:00` |
| 5 | `VitalsSeeder` | `encounterDatetime` | `VisitDatetime` | `2023-03-15T09:15:00` |
| 5 | `VitalsSeeder` | `obsDatetime` (×7) | `VisitDatetime` | `2023-03-15T09:15:00` |
| 6 | `ConsultaSeeder` | `encounterDatetime` | `VisitDatetime + 30 min` | `2023-03-15T09:45:00` |
| 6 | `ConsultaSeeder` | `obsDatetime` (×2–3) | `VisitDatetime` | `2023-03-15T09:15:00` |
| 8* | `VisitCloseSeeder` | `stopDatetime` | `VisitDatetime + 1–4 h` | `2023-03-15T11:15:00` |

*Implementado en Fase 8.

**Razonamiento de los desfases:**
- El encuentro de vitales ocurre **al llegar** — es lo primero que se registra.
- El encuentro de consulta ocurre **30 minutos después** — el médico atiende al paciente tras los vitales.
- Las obs dentro de cada encuentro usan la hora del encuentro padre.
- El cierre de visita se produce entre 1 y 4 horas después de la llegada (duración total de la atención).

### 10.5 Formato de fecha enviado a OpenMRS

Todos los timestamps se convierten a string con el método estático `VisitSeeder.FormatDatetime`:

```csharp
internal static string FormatDatetime(DateTime dt) =>
    dt.ToString("yyyy-MM-dd'T'HH:mm:ss.000+0000");
```

Ejemplo de salida: `"2023-03-15T09:45:00.000+0000"`

**Implicaciones del sufijo `+0000` (UTC):**

- OpenMRS interpreta y almacena estas fechas como **UTC**.
- Si la instancia OpenMRS tiene configurada una zona horaria local (ej: `America/Lima`, UTC-5), la UI de OpenMRS O3 mostrará la hora restándole 5 horas: `09:45 UTC` → `04:45 local`. Esto es comportamiento esperado de OpenMRS.
- El simulador **no aplica conversión de zona horaria**. Las horas generadas (08:00–18:00) representan horas de clínica y se envían tal cual como UTC.
- Si se desea que las horas se muestren correctamente en la UI local, la instancia OpenMRS debe tener su zona horaria configurada en UTC, o bien ajustar el offset en el método `FormatDatetime` (ej: `+0500` para UTC+5).

### 10.6 Fecha de nacimiento de los pacientes

La fecha de nacimiento (`BirthDate`) se calcula en `PatientProfileGenerator.GenerateBirthDate` a partir del grupo etario asignado al paciente:

```csharp
private DateOnly GenerateBirthDate(string ageGroup)
{
    var (min, max) = ageGroup switch
    {
        "0-14"  => (0,  14),
        "15-29" => (15, 29),
        "30-44" => (30, 44),
        "45-64" => (45, 64),
        "65+"   => (65, 85),
        _       => (18, 60)
    };
    var age       = _rng.Next(min, max + 1);         // edad en años (uniforme en el rango)
    var dayOffset = _rng.Next(0, 365);                // día aleatorio dentro del año
    return DateOnly.FromDateTime(DateTime.Today)      // ← referencia: fecha REAL del servidor
                   .AddYears(-age)
                   .AddDays(-dayOffset);
}
```

**Referencia temporal: `DateTime.Today` (fecha real del servidor)**

La fecha de nacimiento se calcula restando la edad a **la fecha del día en que corre el simulador**, no a `StartDate`. Esto implica:

- Un paciente del grupo "30-44" generado el 19 de junio de 2026 tendrá un `BirthDate` entre `1982-06-19` y `1996-06-19`.
- Si el simulador se ejecuta en 2030 con `StartDate = 2023-01-01`, los pacientes aparecerán en OpenMRS con edades 7 años mayores de lo esperado para el año 2023.

**Impacto práctico:** Para un proyecto académico con `StartDate = 2023`, la discrepancia en 2026 es de ~3 años. Los grupos etarios seguirán siendo correctos (ej: un "30-44" seguirá siendo adulto en 2023), pero la edad exacta no será precisa al año de inicio de la simulación.

**Grupos etarios disponibles** (configurables en `DemographicProfile.AgeGroups`):

| Label | Rango de edad en años | Peso por defecto |
|-------|----------------------|:---:|
| `"0-14"` | 0–14 | 20 |
| `"15-29"` | 15–29 | 18 |
| `"30-44"` | 30–44 | 25 |
| `"45-64"` | 45–64 | 25 |
| `"65+"` | 65–85 | 12 |

### 10.7 Reproducibilidad — RandomSeed

El parámetro `RandomSeed` (entero, por defecto `42`) inicializa los generadores de números aleatorios del simulador:

| Componente | Seed usada |
|-----------|-----------|
| `DailyScheduleGenerator` | `RandomSeed` |
| `PatientProfileGenerator` (RNG) | `RandomSeed + 1` |
| `PatientProfileGenerator` (Bogus Faker) | `RandomSeed + 2` |

Con la **misma seed y misma configuración**, dos ejecuciones del simulador producen exactamente el mismo resultado:
- Mismo número de pacientes por día.
- Mismas horas de visita.
- Mismos nombres, géneros y grupos etarios.
- Mismo orden de diagnósticos y probabilidades.

Cambiar `RandomSeed` genera un escenario completamente diferente pero igualmente reproducible. Útil para producir múltiples datasets alternativos sobre la misma instancia (con períodos de fecha distintos para evitar colisiones).

### 10.8 Velocidad real de ejecución vs. tiempo simulado

La simulación **no corre en tiempo real**. El tiempo de ejecución depende únicamente de la latencia de la REST API de OpenMRS por cada request HTTP. No existe pausa artificial entre requests.

**Número de requests por paciente nuevo** (Fases 1–8 completas):

| Operación | Requests |
|-----------|:---:|
| `POST /patient` | 1 |
| `POST /patient/{uuid}/allergy` (si aplica, 15%) | 1–3 |
| `POST /visit` | 1 |
| `POST /encounter` (vitales) | 1 |
| `POST /obs` (vitales ×7) | 7 |
| `POST /encounter` (consulta) | 1 |
| `POST /obs` (diagnóstico + certeza + examen) | 2–3 |
| `POST /order` (lab, si aplica 40%) | 1–2 |
| `POST /order` (prescripción, si aplica 65%) | 1–3 |
| `POST /visit/{uuid}` (cerrar) | 1 |
| **Total aproximado** | **~15–20** |

**Estimación de tiempo total** con `PacientesPorDiaMedio = 40`, `2 años (730 días)`, ~23,000 pacientes nuevos:

| Latencia promedio/request | Requests/paciente | Tiempo estimado |
|:---:|:---:|:---:|
| 300 ms | 17 | ~33 horas |
| 150 ms | 17 | ~16 horas |
| 80 ms | 17 | ~8.5 horas |
| 50 ms | 17 | ~5.4 horas |

La latencia real depende del hardware del servidor Docker y de si está corriendo en la misma máquina. En una instalación local típica (mismo equipo, Docker Desktop) se esperan latencias de 80–150 ms.

**Recomendaciones para acelerar la simulación:**

```json
// Para pruebas: rango corto + volumen bajo
"StartDate": "2023-01-01",
"EndDate":   "2023-01-31",
"PacientesPorDiaMedio": 10
// → ~310 pacientes, ~50 segundos a 150ms/request
```

```json
// Para carga completa: reducir semana laboral si solo importan días hábiles
"WeekdayWeights": {
  "Saturday": 0.00,   // eliminar sábados
  "Sunday":   0.00
}
```

---

## 11. Solución de problemas frecuentes

### La simulación no arranca (`"online": false`)

1. Verificar que Docker esté corriendo: `docker ps`
2. Esperar más tiempo al backend — puede tardar hasta 15 minutos en el primer arranque
3. Verificar la URL en `appsettings.json`: debe ser `http://localhost/openmrs/ws/rest/v1`
4. Probar manualmente: `curl -u admin:Admin123 http://localhost/openmrs/ws/rest/v1/session`

### Errores de UUID en el progress

Si `errores` contiene mensajes como `"404 Not Found"` o `"Invalid UUID"`:
1. Los UUIDs de `OpenMRS.Defaults` no corresponden a la instancia
2. Verificar cada UUID con los endpoints de la sección 3.1

### Catálogos con 0 registros

```json
"catalogs": { "diagnosticos": 0, ... }
```

Los archivos CSV no se copiaron al output. Ejecutar:
```
dotnet build openmrs_seeder_v1/openmrs_seeder_v1.csproj
```
Y verificar que exista `bin/Debug/net10.0/catalogs/diagnosticos.csv`.

### La hora en OpenMRS aparece desfasada

Ver sección [10.5](#105-formato-de-fecha-enviado-a-openmrs). La instancia OpenMRS tiene una zona horaria distinta de UTC. O bien ajustar la zona horaria en la configuración de OpenMRS, o modificar el offset en `VisitSeeder.FormatDatetime`.

### El simulador se detiene con error crítico

Revisar `etapa: "error"` en el progress. Los errores críticos se capturan y el run queda marcado como completado pero fallido. Revisar el campo `errores` para diagnóstico y verificar que OpenMRS sigue respondiendo.

### Pacientes duplicados al ejecutar dos veces

Cada ejecución crea pacientes nuevos con identificadores `SIM-` únicos (basados en GUID), aunque usen la misma seed. Ejecutar `DELETE /api/seed/clear` antes de volver a correr para limpiar los datos anteriores.
