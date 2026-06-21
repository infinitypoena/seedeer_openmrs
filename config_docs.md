# Documentación de configuración — `appsettings.json`

Esta guía explica **cada valor** del archivo `appsettings.json` del simulador clínico de OpenMRS,
qué significa, qué efecto tiene sobre los datos generados y cómo ajustarlo. Está pensada para que
cualquier persona —sin necesidad de leer el código— pueda parametrizar la simulación.

> El archivo vive en `openmrs_seeder_v1/openmrs_seeder_v1/appsettings.json`.
> Existe una copia sin contraseñas reales en `appsettings.example.json` (plantilla para clonar el repo).
> Para esquemas de los catálogos CSV ver `parametrizacion_archivos.md`.

---

## Índice

1. [Logging](#1-logging)
2. [AllowedHosts](#2-allowedhosts)
3. [OpenMRS](#3-openmrs) — conexión e identificadores de la instancia
   - [OpenMRS.RestApi](#31-openmrsrestapi)
   - [OpenMRS.DirectDb](#32-openmrsdirectdb)
   - [OpenMRS.Defaults](#33-openmrsdefaults)
4. [Simulation](#4-simulation) — el comportamiento de la simulación
   - [Parámetros generales](#41-parámetros-generales)
   - [HorarioAtencion](#42-horarioatencion)
   - [DemographicProfile](#43-demographicprofile)
   - [ReferralProbabilities](#44-referralprobabilities)
   - [WeekdayWeights](#45-weekdayweights)
   - [Comorbidity](#46-comorbidity)
   - [Climate](#47-climate)
   - [Allergy](#48-allergy)
5. [Modos recomendados](#5-modos-recomendados)

---

## 1. Logging

Controla el nivel de detalle de los mensajes que el API escribe en consola.

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning"
  }
}
```

| Clave | Significado |
|-------|-------------|
| `Default` | Nivel mínimo de log para el código del simulador. `Information` muestra el avance paciente a paciente (recomendado). Otros valores: `Trace`, `Debug`, `Warning`, `Error`. |
| `Microsoft.AspNetCore` | Nivel para los logs internos del framework web. `Warning` evita ruido de cada petición HTTP. |

**Cuándo cambiarlo:** sube `Default` a `Debug` si necesitas diagnosticar por qué un paciente o una
orden no se creó; bájalo a `Warning` para corridas largas de producción y reducir el volumen de logs.

---

## 2. AllowedHosts

```json
"AllowedHosts": "*"
```

Hosts desde los que el API acepta peticiones. `*` = cualquiera. Para un simulador local de uso
interno se deja en `*`; en un despliegue expuesto conviene restringirlo al dominio real.

---

## 3. OpenMRS

Define **cómo se conecta** el simulador a OpenMRS y los **UUID** de los catálogos propios de tu
instancia. Es la sección más sensible: si un UUID no corresponde a tu instalación, las creaciones
fallan.

```json
"OpenMRS": {
  "SeedMode": "RestApi",
  "RestApi": { ... },
  "DirectDb": { ... },
  "Defaults": { ... }
}
```

| Clave | Significado |
|-------|-------------|
| `SeedMode` | Modo de inserción. **`RestApi`** (recomendado y único soportado en la práctica): los datos se crean por la API REST de OpenMRS. `DirectDb` quedaría reservado para escritura SQL directa, no usado actualmente. |

### 3.1 OpenMRS.RestApi

Credenciales y URL del servidor OpenMRS.

```json
"RestApi": {
  "BaseUrl": "http://localhost/openmrs/ws/rest/v1",
  "Username": "admin",
  "Password": "Prueba01$$xD"
}
```

| Clave | Significado |
|-------|-------------|
| `BaseUrl` | Raíz de la API REST de OpenMRS. Debe terminar en `/ws/rest/v1`. Cambia `localhost` por la IP/host si OpenMRS corre en otra máquina o contenedor. |
| `Username` | Usuario con permisos para crear pacientes, visitas, encuentros y órdenes (típicamente `admin`). |
| `Password` | Contraseña del usuario. **No subir la real al repositorio** — usa `appsettings.example.json` con un placeholder. |

### 3.2 OpenMRS.DirectDb

Parámetros de conexión a la base de datos MariaDB. **No se usan en modo `RestApi`** (se conservan
por compatibilidad). En modo REST el puerto 3306 ni siquiera necesita estar expuesto.

| Clave | Significado |
|-------|-------------|
| `Server` / `Port` | Host y puerto de MariaDB. |
| `Database` | Nombre de la base de datos de OpenMRS. |
| `User` / `Password` | Credenciales de base de datos. |

### 3.3 OpenMRS.Defaults

UUID de los conceptos/metadatos **específicos de tu instancia** de OpenMRS. Cada uno apunta a un
objeto ya existente en OpenMRS que el simulador referencia al crear datos.

> ⚠️ Estos UUID se verificaron contra una instalación concreta (OpenMRS 3.6.0 Reference Application).
> **No asumas que son universales:** verifica los tuyos con `GET /ws/rest/v1/<recurso>?q=...` o el panel de administración.

| Clave | Qué referencia | Cómo obtenerlo |
|-------|----------------|----------------|
| `PatientIdentifierTypeUuid` | Tipo "OpenMRS ID" (con validador Luhn; idgen lo autogenera). | `GET /patientidentifiertype` |
| `TrackingIdentifierTypeUuid` | Tipo "Old Identification Number" — soporta el prefijo `SIM-` para rastrear/limpiar pacientes simulados. | `GET /patientidentifiertype` |
| `LocationUuid` | Ubicación por defecto de visitas y encuentros. | `GET /location` |
| `VisitTypeUuid` | Tipo de visita "Outpatient" (ambulatoria). | `GET /visittype` |
| `VitalsEncounterTypeUuid` | Tipo de encuentro "Vitals" (signos vitales). | `GET /encountertype` |
| `ConsultaEncounterTypeUuid` | Tipo de encuentro "Consultation" (la consulta médica). | `GET /encountertype` |
| `ProviderUuid` | Profesional de salud autor de los encuentros. | `GET /provider` |
| `EncounterRoleUuid` | Rol "Clinician" del proveedor dentro del encuentro. | `GET /encounterrole` |
| `OutpatientCareSettingUuid` | Care setting "Outpatient" para órdenes de lab/medicamentos. | `GET /caresetting` |
| `OnceDailyFrequencyUuid` | Frecuencia "Una vez por día" para prescripciones. | `GET /orderfrequency` |
| `DaysConceptUuid` | Concepto unidad "Días" (duración de prescripciones). | `GET /concept?q=Days` |
| `TabletConceptUuid` | Concepto unidad "Tableta(s)" (dosis de prescripciones). | `GET /concept?q=Tablet` |

---

## 4. Simulation

El corazón del simulador: define **cuántos** pacientes, **cuándo**, **de qué perfil** y **con qué
probabilidad** ocurre cada acción clínica. Cambiar estos valores cambia la forma de los datos sin
tocar código.

### 4.1 Parámetros generales

```json
"StartDate": "2023-01-01",
"EndDate": "2026-06-01",
"PacientesPorDiaMedio": 15,
"PorcentajeRecurrentes": 30,
"Locale": "es",
"RandomSeed": 42,
"ClinicType": "ConsultaExterna"
```

| Clave | Tipo | Significado |
|-------|------|-------------|
| `StartDate` | fecha | Primer día simulado (las visitas se fechan en el pasado a partir de aquí). |
| `EndDate` | fecha | Último día simulado. El rango `StartDate`→`EndDate` define la duración total. |
| `PacientesPorDiaMedio` | entero | Promedio de pacientes **nuevos** por día hábil. Sobre este promedio se aplica una variación aleatoria (~±20%, distribución normal) y el peso del día de la semana. |
| `PorcentajeRecurrentes` | 0–100 | Porcentaje de visitas que corresponden a pacientes **ya creados** (controles, crónicos) en lugar de pacientes nuevos. Ej.: `30` ≈ 30% de visitas recurrentes. |
| `Locale` | string | Idioma/región para generar nombres y direcciones (Bogus). `"es"` = español latinoamericano. |
| `RandomSeed` | entero | Semilla de aleatoriedad. **Con la misma semilla obtienes exactamente la misma simulación** (reproducibilidad). Cámbiala para generar un conjunto de datos distinto. |
| `CommonProbMin` / `CommonProbMax` | float (0-1) | **Factor inicial de selección.** Cada corrida sortea su probabilidad de "común" uniformemente en `[CommonProbMin, CommonProbMax]` (por defecto 0.75–0.95), así la proporción **varía entre corridas** pero siempre se inclina a lo común. Con esa probabilidad, el diagnóstico principal se elige del pool `comun=true`; el resto del tiempo de las raras/críticas. La etapa 2 (categoría + diagnóstico por peso) opera **dentro** del pool elegido con los pesos actuales. Sube `CommonProbMin` para inclinar aún más a lo común. |
| `ClinicType` | string | Perfil del establecimiento (`ConsultaExterna`, `HospitalUrgencias`, `CentroComunitario`). Es una etiqueta semántica orientativa; no fuerza valores por sí sola. |

### 4.2 HorarioAtencion

Distribuye la **hora del día** de cada visita en dos bloques pico; el resto del horario se reparte
de forma uniforme.

```json
"HorarioAtencion": {
  "PicoAM": { "Inicio": "08:00", "Fin": "10:00", "Peso": 40 },
  "PicoPM": { "Inicio": "13:00", "Fin": "15:00", "Peso": 30 }
}
```

| Clave | Significado |
|-------|-------------|
| `PicoAM` / `PicoPM` | Dos franjas de mayor afluencia (mañana y tarde). |
| `Inicio` / `Fin` | Rango horario del bloque (formato `HH:mm`). |
| `Peso` | Porcentaje aproximado de visitas que caen en ese bloque. En el ejemplo, 40% en la mañana + 30% en la tarde = 70%; el 30% restante se distribuye en el resto de la jornada. |

### 4.3 DemographicProfile

Define la **distribución de edad y sexo** de los pacientes nuevos.

```json
"DemographicProfile": {
  "AgeGroups": [
    { "Label": "0-14",  "Weight": 20 },
    { "Label": "15-29", "Weight": 18 },
    { "Label": "30-44", "Weight": 25 },
    { "Label": "45-64", "Weight": 25 },
    { "Label": "65+",   "Weight": 12 }
  ],
  "GenderRatio": { "M": 48, "F": 52 }
}
```

| Clave | Significado |
|-------|-------------|
| `AgeGroups[].Label` | Grupo etario. **Estas etiquetas son las mismas que usan los catálogos** (`epidemiology-profile.csv`, columnas `aplica_0_14`, etc.). No las cambies sin actualizar los catálogos. |
| `AgeGroups[].Weight` | Peso relativo del grupo. Los pesos se normalizan al 100% entre sí (no tienen que sumar 100). En el ejemplo, los grupos 30-44 y 45-64 son los más frecuentes. |
| `GenderRatio.M` / `GenderRatio.F` | Proporción de hombres/mujeres. Se normalizan entre sí (ej.: 48/52). |
| `MinPatientAgeMonths` | Edad **mínima** de cualquier paciente, en meses (por defecto `6`). Evita generar recién nacidos. La fecha de nacimiento se ancla a la fecha de la visita, así que la edad siempre es válida en esa fecha. |
| `PediatricClinic` | `true` = consultorio pediátrico: baja el mínimo a `PediatricMinAgeMonths`. No cambia la distribución de edades, solo el piso. |
| `PediatricMinAgeMonths` | Edad mínima en meses cuando `PediatricClinic` es `true` (por defecto `1`). |

### 4.4 ReferralProbabilities

Probabilidades **base** (0 a 1) de que ocurra cada acción clínica en una visita. Son los
"interruptores de frecuencia" del realismo clínico.

```json
"ReferralProbabilities": {
  "LabOrder": 0.40,
  "ClinicalExam": 0.35,
  "DrugOrder": 0.65,
  "Urgent": 0.20,
  "FollowUp": 0.30
}
```

| Clave | Significado |
|-------|-------------|
| `LabOrder` | Probabilidad base de ordenar laboratorio externo. **Nota:** si el diagnóstico está marcado `requiere_lab` en el catálogo, se usa una probabilidad alta fija (0.80) en lugar de esta. |
| `ClinicalExam` | Probabilidad base de registrar un examen en consultorio (obs inmediata). Si el diagnóstico requiere examen clínico, sube a 0.90. |
| `DrugOrder` | Probabilidad base de prescribir medicamento. Si el diagnóstico está marcado `requiere_rx`, sube a 0.90. |
| `Urgent` | Probabilidad de que una orden de laboratorio salga como **URGENTE** (`STAT`). Para diagnósticos `grave` sube a 0.50. |
| `FollowUp` | Probabilidad de generar una nota/indicación de seguimiento. |

> La probabilidad de alergias dejó de vivir aquí; ahora está en su propia sección [`Allergy`](#47-allergy).

> En resumen: estas probabilidades aplican como **piso**, pero el catálogo de diagnósticos puede
> elevarlas cuando la enfermedad lo amerita clínicamente (`requiere_lab`, `requiere_rx`, severidad).

### 4.5 WeekdayWeights

Multiplicador del **volumen de pacientes según el día de la semana**. Se aplica sobre
`PacientesPorDiaMedio`.

```json
"WeekdayWeights": {
  "Monday":    1.20,
  "Tuesday":   1.20,
  "Wednesday": 1.00,
  "Thursday":  1.00,
  "Friday":    0.90,
  "Saturday":  0.50,
  "Sunday":    0.00
}
```

| Valor | Efecto |
|-------|--------|
| `1.00` | Volumen igual al promedio. |
| `> 1.00` | Día más concurrido (ej.: lunes y martes con `1.20` = +20%). |
| `< 1.00` | Día más tranquilo (ej.: sábado `0.50` = mitad de pacientes). |
| `0.00` | **Sin atención** ese día (ej.: domingo). No se generan pacientes. |

### 4.6 Comorbidity

Controla la **multimorbilidad**: que un paciente presente más de una enfermedad detectada en la
**misma visita** (ej.: diabetes + hipertensión). Mejora el realismo de los datos.

```json
"Comorbidity": {
  "BaseProbability": 0.20,
  "MaxAdditional": 2,
  "SecondExtraProbability": 0.25,
  "AffinityBoost": 4.0,
  "AgeScaling": {
    "0-14":  0.3,
    "15-29": 0.5,
    "30-44": 0.8,
    "45-64": 1.3,
    "65+":   1.8
  },
  "Affinities": {
    "diabetes":       [ "cardiovascular", "endocrino" ],
    "cardiovascular": [ "diabetes", "endocrino" ],
    "endocrino":      [ "diabetes", "cardiovascular" ],
    "respiratorio":   [ "infeccioso" ],
    "infeccioso":     [ "respiratorio", "digestivo" ],
    "digestivo":      [ "infeccioso" ]
  }
}
```

| Clave | Tipo | Significado |
|-------|------|-------------|
| `BaseProbability` | 0–1 | Probabilidad **base** de que un paciente tenga al menos **una** enfermedad adicional al diagnóstico principal. `0` desactiva las comorbilidades por completo. |
| `MaxAdditional` | entero | Máximo de diagnósticos extra además del principal. `2` = hasta 3 enfermedades en la misma visita. |
| `SecondExtraProbability` | 0–1 | Dado que ya hay una comorbilidad, probabilidad de añadir una **segunda**. |
| `AffinityBoost` | factor | Cuánto se multiplica el peso de las categorías clínicamente afines al elegir la enfermedad extra. `4.0` = las categorías relacionadas tienen 4× más chance que una al azar. |
| `AgeScaling` | objeto | Multiplicador de `BaseProbability` **por grupo de edad**. Refleja que la multimorbilidad crece con la edad. Ej.: para `65+`, prob. efectiva = `0.20 × 1.8 = 0.36`. El resultado se limita a un máximo de 0.95. |
| `Affinities` | objeto | **Clusters de comorbilidad**: por cada categoría, lista de categorías clínicamente asociadas que reciben el `AffinityBoost`. Así un diabético tiende a presentar enfermedad cardiovascular/endocrina, no algo improbable. |

**Cómo funciona en conjunto:**
1. Se elige el diagnóstico **principal** como siempre (perfil epidemiológico por edad/sexo).
2. Con probabilidad `BaseProbability × AgeScaling[grupo]` se decide si hay comorbilidad.
3. La(s) enfermedad(es) extra se eligen de **otras** categorías, priorizando las afines (`Affinities` × `AffinityBoost`).
4. Todos los diagnósticos se registran en el **mismo encuentro** (principal `rank=1`, secundarios `rank=2`).
5. Las órdenes de laboratorio y las prescripciones cubren las categorías de **todas** las enfermedades del paciente, manteniendo la coherencia clínica.

> **Categorías válidas** (13, deben coincidir con las de los catálogos): `respiratorio`, `cardiovascular`,
> `diabetes`, `digestivo`, `osteomuscular`, `urologico`, `infeccioso`, `endocrino`, `neurologico`,
> `dermatologico`, `salud_mental`, `ginecoobstetrico`, `trauma`.

> **Lista de problemas (condiciones crónicas):** los diagnósticos marcados `cronica=true` en
> `diagnosticos.csv` (HTA, diabetes, EPOC, hipotiroidismo, epilepsia, depresión, VIH, artritis, etc.)
> se agregan a la **lista de problemas** del paciente vía `POST /condition`. Las imágenes (Rx, TC,
> ecografía) se generan como **órdenes de laboratorio** (test order), ya que la instancia no tiene un
> tipo de orden de procedimiento separado.

**Para que las comorbilidades sean más visibles** sube `BaseProbability` (ej.: `0.6`); para
desactivarlas, ponlo en `0`.

### 4.7 Climate

Hace que la **época del año** afecte la simulación: más gripe/respiratorias en invierno, más
dengue/EDA en verano y lluvia, y un leve aumento de la temperatura corporal por calor ambiental.
Funciona junto con el catálogo **opcional** `catalogs/clima.csv` (una fila por semana ISO del año:
`semana, estacion, temp_promedio_c`). Si el archivo **no existe**, o una semana no está listada, o
`Enabled` es `false`, el clima **no afecta nada** (comportamiento neutro).

```json
"Climate": {
  "Enabled": true,
  "SeasonalBoost": 2.5,
  "ComfortTempC": 24.0,
  "TempVitalsFactorC": 0.04,
  "TempVitalsMaxC": 0.5
}
```

| Clave | Tipo | Significado |
|-------|------|-------------|
| `Enabled` | bool | Si es `false`, ignora `clima.csv` aunque exista (apaga el efecto sin borrar el archivo). |
| `SeasonalBoost` | factor | Multiplicador de peso que reciben las enfermedades **y** las categorías favorecidas por la estación activa. `2.5` = ~2.5× más probables en su temporada. |
| `ComfortTempC` | °C | Temperatura ambiente de confort; por encima de ella el calor empieza a subir la temperatura corporal registrada. |
| `TempVitalsFactorC` | °C/°C | Grados de temperatura corporal añadidos por cada grado ambiente por encima del confort. |
| `TempVitalsMaxC` | °C | Tope del ajuste de temperatura corporal por calor. |

**Cómo se conecta con las enfermedades:** en `diagnosticos.csv` cada enfermedad tiene una columna
`clima` con la(s) estación(es) que la favorecen (p.ej. gripe → `invierno`, dengue → `verano,lluvia`,
EDA → `verano,lluvia`); vacío = sin efecto estacional. Estaciones válidas: `invierno`, `verano`,
`lluvia`, `seca`. La semana de la visita se calcula con la **semana ISO** del año (1-53) y aplica
cada año de la simulación. Solo el **calor** sube la temperatura corporal; el frío no la baja.

> El catálogo `clima.csv` que se incluye es un ejemplo editable: ajústalo a tu región (qué semanas
> son invierno/verano/lluvia/seca y sus temperaturas).

### 4.8 Allergy

Controla qué pacientes **nuevos** reciben alergias documentadas y **cuántas**. Modelo en dos pasos:

1. **Prevalencia por corrida** (¿es alérgico?): cada corrida sortea una vez su probabilidad
   uniformemente en `[BaseProbabilityMin, BaseProbabilityMax]` (igual que `CommonProbMin/Max`), así
   la proporción de alérgicos **varía entre corridas**. Por defecto ~15-25%. *Dato de referencia:*
   el 25-30% de la población tiene alguna alergia (OMS/SEAIC); aquí se modela la fracción
   **clínicamente documentada** (fármacos/alimentos/críticas), que es lo que registra OpenMRS.
2. **Cuántas alergias** (decaída condicional, igual patrón que comorbilidad): todo alérgico tiene
   ≥1; suma una **2ª** con `SecondAllergyProbability`; y solo si tiene 2, suma una **3ª** con
   `ThirdAllergyProbability`. Resultado: la gran mayoría tiene 1, pocos 2, raros 3.

```json
"Allergy": {
  "BaseProbabilityMin": 0.15,
  "BaseProbabilityMax": 0.25,
  "SecondAllergyProbability": 0.30,
  "ThirdAllergyProbability": 0.25,
  "MaxAllergies": 3
}
```

| Clave | Tipo | Significado |
|-------|------|-------------|
| `BaseProbabilityMin` / `BaseProbabilityMax` | 0-1 | Banda de prevalencia; la corrida sortea su valor entre ambos. Poner el mismo número en los dos = prevalencia fija. |
| `SecondAllergyProbability` | 0-1 | Prob. condicional de sumar una 2ª alergia dado que ya tiene 1. |
| `ThirdAllergyProbability` | 0-1 | Prob. condicional de sumar una 3ª alergia dado que ya tiene 2. |
| `MaxAllergies` | entero | Tope absoluto de alergias por paciente (también acotado por el tamaño de `alergenos.csv`). |

> Solo aplica a pacientes **nuevos**: la alergia se registra una vez en la primera visita y persiste.
> Los alérgenos se eligen al azar y sin repetir de `catalogs/alergenos.csv`.

---

## 5. Modos recomendados

Combinaciones típicas de parámetros según el objetivo:

| Objetivo | Ajustes sugeridos |
|----------|-------------------|
| **Validación rápida** (1 semana, bajo volumen) | `EndDate: "2023-01-08"`, `PacientesPorDiaMedio: 8` |
| **Producción** (dataset completo) | `EndDate: "2024-12-31"`, `PacientesPorDiaMedio: 40` |
| **Resaltar comorbilidades** (para revisar el escenario) | `Comorbidity.BaseProbability: 0.6` |
| **Reproducir exactamente un dataset** | mantener el mismo `RandomSeed` |
| **Generar un dataset distinto** | cambiar `RandomSeed` |

> Tras ajustar la configuración: reinicia el API (`dotnet run`), lanza la simulación con
> `POST /api/seed/run` (responde 202 y corre en segundo plano), consulta el avance por `runId`, y
> limpia los datos simulados con `DELETE /api/seed/clear` (elimina todo lo marcado con prefijo `SIM-`).
