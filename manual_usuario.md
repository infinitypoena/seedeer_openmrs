# Manual de Usuario — OpenMRS Clinical Simulator

> Versión del manual: **julio 2026** · Basada en el código fuente real
> Referencias complementarias: `parametrizacion_archivos.md` (todos los parámetros y esquemas CSV) · `CLAUDE.md` (notas técnicas y UUIDs verificados)

## Tabla de Contenidos

1. [¿Qué hace este simulador?](#1-qué-hace-este-simulador)
2. [Tour de capacidades — con ejemplos](#2-tour-de-capacidades--con-ejemplos)
3. [Requisitos previos](#3-requisitos-previos)
4. [Arranque del entorno](#4-arranque-del-entorno)
5. [Configuración](#5-configuración)
6. [Flujo completo de la simulación](#6-flujo-completo-de-la-simulación)
7. [Cómo usar la API](#7-cómo-usar-la-api)
8. [Catálogos CSV — qué son y cómo extenderlos](#8-catálogos-csv--qué-son-y-cómo-extenderlos)
9. [Casos de uso prácticos](#9-casos-de-uso-prácticos)
10. [Manejo del tiempo](#10-manejo-del-tiempo)
11. [Limpieza e idempotencia](#11-limpieza-e-idempotencia)
12. [Acceso directo a MariaDB](#12-acceso-directo-a-mariadb)
13. [Solución de problemas](#13-solución-de-problemas)
14. [Decisiones de diseño clave](#14-decisiones-de-diseño-clave)

---

## 1. ¿Qué hace este simulador?

El **OpenMRS Clinical Simulator** es una aplicación de consola en .NET 10 que puebla una instancia OpenMRS 3.x con historias clínicas ficticias pero **epidemiológica y clínicamente coherentes**. Está pensado como el "día a día" de una clínica de consulta externa pequeña (el escenario de referencia es una clínica de El Salvador con 3-4 médicos), reproducido a lo largo de un período histórico configurable.

En una sola corrida el simulador genera, para cada día del período:

- **Pacientes nuevos y recurrentes** con nombres centroamericanos realistas (2 nombres + 2 apellidos), edad y género según una distribución demográfica configurable.
- **Visitas completas**: registro, signos vitales, consulta médica con diagnóstico, órdenes de laboratorio **con su resultado**, prescripciones, alergias, y cierre de la visita 1–4 horas después.
- **Diagnósticos con criterio epidemiológico**: elegidos por edad, género y estación del año, con comorbilidades en pacientes mayores y sesgo hacia las enfermedades comunes.
- **Continuidad asistencial**: los pacientes crónicos vuelven a control de *su* enfermedad con *su* médico de cabecera; los agudos vuelven a control del *mismo* episodio; los diagnósticos crónicos quedan en la *problem list* y disparan la inscripción en programas de atención (VIH, diabetes).
- **Agenda real**: cuando el médico indica seguimiento, se crea una **cita en el módulo de citas de O3**; si el paciente vuelve, la cita pasa a *Completed*; si no, queda como no-show (*Missed*).

**Lo que NO hace:**

- No escribe SQL directamente — **todo** va por la REST API de OpenMRS (`/ws/rest/v1`). Eso garantiza que los datos pasan por las validaciones de negocio de OpenMRS y que funciona contra cualquier despliegue estándar.
- No modela hospitalización, urgencias, UCI, quirófano ni facturación — solo consulta externa.
- No genera imágenes ni documentos adjuntos (las imágenes/EKG se ordenan como *test orders* sin resultado).

**Cuándo usarlo:**

- Poblar una instancia de demostración o capacitación con datos clínicos realistas **en español**.
- Probar los módulos de OpenMRS O3 (registro, visitas, condiciones, laboratorio, medicación, alergias, programas, citas) con datos que "cuentan una historia".
- Generar un dataset histórico para análisis académico, reportería o pruebas de rendimiento.

---

## 2. Tour de capacidades — con ejemplos

Esta sección muestra **qué produce el simulador en la práctica**. Cada capacidad es configurable (los parámetros exactos están en `parametrizacion_archivos.md`).

### 2.1 Volumen diario realista

No genera "N pacientes fijos por día": parte de un promedio (`PacientesPorDiaMedio`), lo multiplica por el peso del día de la semana y le aplica variación aleatoria normal.

> **Ejemplo** (promedio 15): lunes ~18 pacientes, miércoles ~15, sábado ~7, domingo 0 (clínica cerrada). Ningún día es idéntico a otro, igual que en la realidad.

### 2.2 Diagnósticos según edad, género y sexo biológico

El diagnóstico de cada paciente sale de un modelo de dos niveles: primero la **categoría** (respiratorio, cardiovascular, diabetes… 13 en total) ponderada por edad y género según `epidemiology-profile.csv`; luego el **diagnóstico específico** dentro de esa categoría (catálogo de ~950 diagnósticos CIEL, incluidas enfermedades tropicales centroamericanas: dengue, chikungunya, zika).

> **Ejemplo**: una mujer de 68 años tiene alta probabilidad de caer en cardiovascular/diabetes (HTA, DM2); un niño de 5 en respiratorio/infeccioso (rinofaringitis, EDA). Un hombre **nunca** recibirá "embarazo ectópico" ni una mujer "hiperplasia prostática" — la columna `sexo` del catálogo excluye de forma dura.

### 2.3 Sesgo hacia enfermedades comunes

En una clínica real la mayoría de consultas son resfríos, diarreas, lumbalgias e hipertensión — no enfermedades raras. Cada corrida sortea qué fracción de pacientes (75–95%) se elige del **pool de diagnósticos comunes**; el resto puede caer en el catálogo completo.

> **Ejemplo**: el "top 10" de una corrida típica: rinofaringitis, EDA, HTA, gripe, ITU, dengue, lumbalgia, gastritis, diabetes, amigdalitis. Las enfermedades raras aparecen, pero de forma esporádica.

### 2.4 Comorbilidad (varios diagnósticos en una visita)

Con probabilidad creciente por edad, la visita agrega 1–2 diagnósticos extra **clínicamente afines** al primario (`comorbilidad_afinidades.csv`): diabetes atrae cardiovascular, respiratorio atrae infeccioso, etc. Todos quedan en el mismo encuentro (primario `rank=1`, secundarios `rank=2`).

> **Ejemplo**: hombre de 71 con dx primario "insuficiencia cardíaca" + comorbilidad "diabetes mellitus tipo 2". Sus laboratorios y fármacos se filtran por la **unión** de ambas categorías (le pueden pedir HbA1c *y* electrolitos).

### 2.5 Estacionalidad climática

Si existe `catalogs/clima.csv` (estación por semana ISO), las enfermedades etiquetadas con esa estación pesan más.

> **Ejemplo**: en semanas de `invierno` sube la gripe y la neumonía; en `verano`/`lluvia` suben dengue y enfermedad diarreica. En semanas calurosas la temperatura corporal registrada sube ligeramente.

### 2.6 Signos vitales coherentes con la enfermedad

Cada visita registra **8 observaciones** (peso, talla, PA sistólica/diastólica, temperatura, pulso, frecuencia respiratoria y SpO₂) derivadas de las categorías del paciente y la severidad del diagnóstico:

| Situación clínica | Efecto en vitales |
|---|---|
| Infeccioso / respiratorio moderado+ | Fiebre 37.5–40 °C, taquicardia, taquipnea |
| Respiratorio grave | SpO₂ baja (88–93%) |
| Cardiovascular | PA elevada (140–180 / 90–110) |
| Diabetes / endocrino | Peso acoplado a la talla vía IMC 27–38 |
| Hipertiroidismo (`vital_imc=bajo`) | IMC 16–19 (adelgazamiento) |
| Apendicitis (`vital_fiebre=true`) | Fiebre aunque su categoría sea digestivo |

> **Ejemplo**: paciente con dengue → temperatura 38.9 °C, pulso 104, FR 22, SpO₂ 97%. Paciente sano de control → 36.6 °C, pulso 76, FR 15, SpO₂ 98%. El peso ya no puede ser absurdo respecto a la talla (se calcula desde un IMC objetivo).

### 2.7 Laboratorios con resultado (ciclo orden → resultado)

Las órdenes de laboratorio no quedan "huérfanas": ~90% recibe el mismo día una **obs de resultado ligada a la orden**, coherente con la enfermedad. El ~10% restante queda pendiente (realista).

> **Ejemplos**:
> - Diabético → HbA1c **7.8%** (banda anormal); paciente sin diabetes → HbA1c 5.2%.
> - Paciente con dengue → antígeno NS1 **Positivo**; sin dengue → Negativo.
> - ITU → BUN/creatinina elevados.
> Los conceptos que no admiten decimales (ASAT, amilasa) reciben valores enteros automáticamente.

### 2.8 Prescripciones coherentes

Los fármacos (catálogo de ~30 productos reales del formulario de la instancia) se filtran por las categorías del paciente: amoxicilina para respiratorio/infeccioso, enalapril para cardiovascular, metformina para diabetes. Dosis, frecuencia ("una vez al día"), vía oral y duración (7/14/30 días) completan la orden.

### 2.9 Alergias con prevalencia realista

Solo los pacientes **nuevos** registran alergias (se documentan una vez, al ingresar). Cada corrida sortea su prevalencia (15–25%); de los alérgicos, la mayoría tiene 1 alergia, pocos 2, raros 3.

> **Ejemplo**: "Alergia a penicilina — severidad moderada — reacción: erupción cutánea".

### 2.10 Consultorios, médicos y médico de cabecera

`consultorios.csv` define consultorios con su médico (los médicos `SIM-MED-*` se crean automáticamente si no existen). Cada día "abren" 2-3 de los médicos del pool (roster rotativo). Cada paciente nuevo queda asignado a un **médico de cabecera**; cuando vuelve, cae con él un 70–90% de las veces (si ese día está de turno).

> **Ejemplo**: María vive sus 4 visitas del año con la Dra. Ramírez en el Consultorio 2; solo una vez (la doctora no estaba) la vio el Dr. Aguilar.

### 2.11 Problem list y programas de atención (crónicos)

Los diagnósticos marcados `cronica=true` (HTA, diabetes, EPOC, epilepsia, VIH…) se agregan a la **lista de problemas** del paciente (condición ACTIVE, sin duplicar entre visitas). Si el diagnóstico o su categoría coincide con `programas.csv`, el paciente se **inscribe en el programa** correspondiente.

> **Ejemplo**: paciente con dx de VIH → condición "Infección por VIH" en su problem list + inscripción en *HIV Care and Treatment* con estado inicial "On Antiretrovirals Treatment". Un diabético → programa *Outpatient Diabetes Education*.

### 2.12 Citas reales en la agenda de O3

Cuando la consulta decide seguimiento (~30% de las visitas), además de la nota "Return visit date" se **agenda una cita real** (módulo Bahmni Appointments) 7–30 días después, con el médico y consultorio de la visita, en slots de 15 minutos entre 08:00 y 15:45. Cuando el paciente efectivamente vuelve, su cita pasa a **Completed** (si llegó a ±3 días) o **Missed** (si la dejó vencer) — no-shows incluidos.

> **Ejemplo** (corrida de 1 mes): 91 citas agendadas → 84 quedaron *Scheduled* (a futuro o el paciente no volvió aún), 4 *Completed*, 3 *Missed*. El calendario por médico se ve poblado en la UI de O3.

### 2.13 Continuidad longitudinal (la historia tiene hilo)

Dos mecanismos evitan el clásico defecto de los generadores de datos (cada visita con una enfermedad aleatoria nueva):

- **Crónicos**: un paciente con HTA que vuelve tiene 70% de probabilidad de que la visita sea **control de su HTA** (no una queja nueva), con intervalo de control de 30–120 días.
- **Agudos**: un paciente con neumonía que vuelve a los 7–21 días tiene 70% de probabilidad de que la visita sea **control de la misma neumonía** (dentro de una ventana de 30 días); la visita de control cierra el episodio.

> **Efecto medido**: antes de esta lógica solo el 2,3% de las visitas consecutivas de un mismo paciente repetía diagnóstico; ahora ~62-64% (dengue→control de dengue a los 14 días, EDA→EDA a los 9, ITU→ITU a los 7).

### 2.14 Espaciamiento realista entre visitas

Un paciente no puede volver a consulta externa día tras día: tras cada visita queda inelegible hasta su próxima fecha (7–21 días si es agudo, 30–120 si es crónico).

### 2.15 Reproducibilidad

Con la misma `RandomSeed` y la misma configuración, dos corridas generan exactamente la misma secuencia de pacientes, diagnósticos y horas — útil para comparar escenarios o compartir un dataset descriptible.

---

## 3. Requisitos previos

El simulador es una **aplicación de consola (batch)** que se conecta a una instancia OpenMRS ya desplegada: se ejecuta, corre la simulación completa según `appsettings.json` y termina. OpenMRS puede estar en Docker, local o remoto — solo importa que su REST API responda.

**Opción A — con .NET SDK (recomendada para desarrollo):**

| Requisito | Versión | Verificación |
|-----------|---------|-------------|
| .NET SDK | 10.0+ | `dotnet --version` |
| OpenMRS 3.x con REST API activa | cualquier despliegue | `GET http://<host>/openmrs/ws/rest/v1` |

**Opción B — con Docker (sin instalar .NET):**

| Requisito | Versión | Verificación |
|-----------|---------|-------------|
| Docker Desktop / Engine | 4.x+ / 24.x+ | `docker --version` |
| OpenMRS 3.x con REST API activa | cualquier despliegue | `GET http://<host>/openmrs/ws/rest/v1` |

---

## 4. Arranque del entorno

### 4.1 Verificar que OpenMRS está accesible

```
GET http://<tu-host>/openmrs/ws/rest/v1
```

Debe devolver un JSON con información de la instancia. En despliegue local con Docker la URL base suele ser `http://localhost/openmrs/ws/rest/v1`.

> Si OpenMRS acaba de levantarse con Docker, el backend puede tardar **10–15 minutos en el primer arranque**.

### 4.2 Opción A: ejecutar con .NET SDK

```bash
cp openmrs_seeder_v1/openmrs_seeder_v1/appsettings.example.json \
   openmrs_seeder_v1/openmrs_seeder_v1/appsettings.json
# Editar appsettings.json: URL y contraseña de tu OpenMRS, y la ventana StartDate/EndDate

dotnet run --project openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj
```

**La simulación arranca de inmediato** según la parametrización, muestra el progreso en consola y el proceso termina al completarse. No hay servidor web ni pasos intermedios.

### 4.3 Opción B: ejecutar con Docker

```bash
cd docker
cp .env.example .env
# Editar .env con la URL de OpenMRS y la contraseña

docker compose -f docker/docker-compose.yml run --rm seeder
```

El contenedor corre la simulación y termina (es un job, no un servicio). Para limpiar datos: `docker compose -f docker/docker-compose.yml run --rm -i seeder clear`.

> **OpenMRS en el mismo host que Docker:** usar `host.docker.internal` (Windows/Mac) o la IP del gateway Docker, habitualmente `172.17.0.1` (Linux), en `OPENMRS_URL`.

---

## 5. Configuración

El archivo de configuración es `appsettings.json` (plantilla: `appsettings.example.json`). **La referencia completa de todos los parámetros está en `parametrizacion_archivos.md`** — aquí va lo esencial.

### 5.1 Sección `OpenMRS`

```json
{
  "OpenMRS": {
    "RestApi": {
      "BaseUrl": "http://localhost/openmrs/ws/rest/v1",
      "Username": "admin",
      "Password": "TU_PASSWORD"
    },
    "Defaults": { "...": "UUIDs de la instancia, ver 5.2" }
  }
}
```

> **Validación al arranque:** el simulador valida toda la configuración al iniciar. Si un valor es
> inválido (probabilidad fuera de 0–1, `StartDate` posterior a `EndDate`, banda `Min > Max`…), el
> proceso **no arranca** y el mensaje de error nombra cada campo violado. Las claves desconocidas
> (p. ej. un parámetro obsoleto que quedó en el JSON) generan un warning en el log al arrancar.

### 5.2 UUIDs en `OpenMRS.Defaults`

Estos UUIDs identifican los metadatos de **tu** instancia (tipos de encuentro, tipo de visita, locaciones, servicio de citas…). Los valores del `appsettings.example.json` ya están verificados contra la instancia de referencia (OpenMRS 3.6.0 Reference Application), pero **cada instancia puede tener UUIDs distintos**. La tabla completa con nombre y significado de cada clave está en `CLAUDE.md` § *Verified UUID Mappings*.

Claves importantes y cómo verificarlas:

| Clave | Qué es | Verificar con |
|-------|--------|---------------|
| `VisitTypeUuid` | Tipo de visita (OPD/consulta externa) | `GET /visittype` |
| `VitalsEncounterTypeUuid`, `ConsultaEncounterTypeUuid` | Tipos de encuentro | `GET /encountertype` |
| `RegistrationLocationUuid` | Locación de registro (Recepción) | `GET /location` |
| `PatientIdentifierTypeUuid`, `TrackingIdentifierTypeUuid` | "OpenMRS ID" y "Old Identification Number" | `GET /patientidentifiertype` |
| `AppointmentServiceUuid`, `AppointmentServiceTypeUuid` | Servicio de la agenda (vacío = citas desactivadas) | `GET /appointmentService/all/default` |
| `ProviderUuid`, `LocationUuid` | Fallback si no hay `consultorios.csv` | `GET /provider`, `GET /location` |

> ⚠️ **Regla de oro**: cualquier UUID nuevo (diagnóstico, lab, programa…) debe verificarse contra **esta** instancia con `GET /concept?q=nombre`. Un UUID equivocado puede ser un concepto *válido pero distinto* — OpenMRS lo acepta sin error y el dato queda mal.

### 5.3 Sección `Simulation` — parámetros principales

| Parámetro | Por defecto | Qué controla |
|-----------|-------------|--------------|
| `StartDate` / `EndDate` | — | Período histórico simulado |
| `PacientesPorDiaMedio` | 15–40 | Promedio de pacientes/día (antes de peso semanal y variación) |
| `PorcentajeRecurrentes` | 30 | % de visitas de pacientes ya existentes |
| `CommonProbMin/Max` | 0.75–0.95 | Banda del sesgo a enfermedades comunes (se sortea por corrida) |
| `SeguimientoCronicoProb` | 0.70 | Prob. de que un crónico recurrente venga a control de su enfermedad |
| `SeguimientoAgudoProb` / `VentanaSeguimientoAgudoDias` | 0.70 / 30 | Prob. y ventana del control del mismo episodio agudo |
| `MedicoCabeceraProbMin/Max` | 0.70–0.90 | Banda de "vuelve con su médico de cabecera" |
| `MinMedicosPorDia` / `MaxMedicosPorDia` | 2 / 3 | Cuántos médicos del pool abren consultorio cada día |
| `ReferralProbabilities` | ver abajo | Probabilidades de derivación |
| `Comorbidity` | — | Prob. base, escalado por edad y boost de afinidad |
| `Climate` | Enabled=true | Boost estacional y efecto en temperatura |
| `Allergy` | 0.15–0.25 | Banda de prevalencia (sorteada por corrida) + decaída del nº |
| `Recurrence` | 7–21 / 30–120 | Días mínimos entre visitas (agudo / crónico) |
| `Appointments.ToleranciaDias` | 3 | Margen para marcar una cita como cumplida |
| `RandomSeed` | 42 | Reproducibilidad |

**Probabilidades de derivación** (`ReferralProbabilities`):

| Parámetro | Defecto | Significado |
|-----------|:-------:|-------------|
| `LabOrder` | 0.40 | Ordenar laboratorio (sube a 80% si el dx marca `requiere_lab`) |
| `DrugOrder` | 0.65 | Prescribir (sube a 90% si `requiere_rx`) |
| `ClinicalExam` | 0.35 | Examen en consultorio (**inactivo**: catálogo vacío, ver §8) |
| `Urgent` | 0.20 | Urgencia STAT del lab (50% si el dx es grave) |
| `FollowUp` | 0.30 | Indicar seguimiento → obs "Return visit date" **+ cita en agenda** |
| `LabResult` | 0.90 | Fracción de órdenes que reciben resultado el mismo día |

**Pesos por día de semana** (`WeekdayWeights`): Lun/Mar 1.20, Mié/Jue 1.00, Vie 0.90, Sáb 0.50, **Dom 0.00** (cerrado).

### 5.4 Modos sugeridos

| Modo | Ventana | Volumen | Uso |
|------|---------|---------|-----|
| **Validación** | 1 semana | 5–8/día | Verificar conexión y pipeline en minutos |
| **Clínica pequeña** | 1 año | 15/día | Escenario de referencia (3 médicos) |
| **Carga** | 2 años | 40/día | Dataset grande / pruebas de rendimiento |

---

## 6. Flujo completo de la simulación

### 6.1 Pipeline por día y por paciente

```
appsettings.json + catalogs/*.csv
      │
      ▼
DailyScheduleGenerator ─── cuántos pacientes hoy (promedio × peso del día × variación normal)
      │
      ▼
SeedOrchestrator ─── loop por día del período
      │
      ├── ActivarMedicosDelDia ─── sortea qué 2-3 médicos abren consultorio hoy
      │
      ├── PACIENTE NUEVO
      │     ├── PatientProfileGenerator → nombre (nombres.csv/apellidos.csv), edad, género
      │     ├── EpidemiologySelector    → categoría + diagnóstico (edad/género/sexo/clima/común)
      │     ├── SelectComorbilidades    → 0-2 diagnósticos extra afines
      │     ├── PatientSeeder           → POST /patient (ID OpenMRS + SIM-XXXXXXXX)
      │     └── AllergySeeder           → POST /allergy (prevalencia 15-25%, solo nuevos)
      │
      ├── PACIENTE RECURRENTE (del pool, elegible por fecha, sin repetir el mismo día)
      │     ├── ¿tiene crónicas? 70% → control de SU enfermedad crónica
      │     ├── si no, ¿episodio agudo abierto (≤30 d)? 70% → control del MISMO dx agudo
      │     └── si no → queja nueva (selección epidemiológica normal)
      │
      └── ProcesarVisitaAsync (común a ambos)
            ├── AssignVisit          → médico (cabecera si está de turno) + consultorio
            ├── VisitSeeder          → POST /visit                      → VisitUuid
            ├── VitalsSeeder         → POST /encounter + 8 obs coherentes con el dx
            ├── ResolverCitasAsync   → citas pendientes → Completed / Missed
            ├── ConsultaSeeder       → POST /encounter (dx rank 1 + comorbilidades rank 2,
            │                          motivo de consulta, obs de seguimiento si aplica)
            ├── ConditionSeeder      → POST /condition (crónicas → problem list, sin duplicar)
            ├── ProgramEnrollmentSeeder → POST /programenrollment (si dx/categoría dispara programa)
            ├── LabOrderSeeder       → POST /order (testorder) + POST /obs (RESULTADO ligado a la orden)
            ├── PrescriptionSeeder   → POST /order (drugorder coherente con las categorías)
            ├── AppointmentSeeder    → POST /appointment (cita real si hubo seguimiento)
            └── VisitCloseSeeder     → POST /visit {stopDatetime} (visita cerrada 1-4 h después)
```

### 6.2 Requests HTTP por paciente

Un paciente nuevo con pipeline completo genera entre **15 y 30 requests**:

| Operación | Requests | Condición |
|-----------|:--------:|-----------|
| `POST /patient` | 1 | pacientes nuevos |
| `POST /patient/{uuid}/allergy` | 1–3 | ~15–25% de los nuevos |
| `POST /visit` | 1 | siempre |
| `POST /encounter` + 8 obs (vitales) | 9 | siempre |
| `POST /appointments/{uuid}/status-change` | 0–n | si tenía citas pendientes |
| `POST /encounter` (consulta, con diagnósticos embebidos) | 1 | siempre |
| `POST /obs` (motivo, seguimiento) | 1–2 | siempre / 30% |
| `POST /condition` | 0–2 | si hay dx crónicos nuevos |
| `POST /programenrollment` | 0–1 | si un dx dispara programa |
| `POST /order` (lab) + `POST /obs` (resultado) | 0–4 | ~40–80% |
| `POST /order` (prescripción) | 0–3 | ~65–90% |
| `POST /appointment` | 0–1 | 30% (seguimiento) |
| `POST /visit/{uuid}` (cierre) | 1 | siempre |

---

## 7. Cómo ejecutar el simulador

### 7.1 Correr una simulación

```bash
dotnet run --project openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj
```

No hay pasos intermedios: el proceso valida la configuración, muestra un **resumen inicial** y ejecuta la corrida completa. Salida típica:

```
info: Seeder[0]
      OpenMRS: ONLINE (http://localhost/openmrs/ws/rest/v1) | Ventana: 2025-04-01 → 2025-04-07 | 15 pac/día medio, 30% recurrentes, seed 42
info: Seeder[0]
      Catálogos: 948 diagnósticos, 30 medicamentos, 27 laboratorios, 21 alérgenos, 3 consultorios, 2 programas
info: Seeder[0]
      Progreso: 16% | día 1/7 (2025-04-02) | 16 pacientes | 0 errores
info: Seeder[0]
      Progreso: 50% | día 3/7 (2025-04-04) | 35 pacientes | 0 errores
...
info: Seeder[0]
      Resumen final: etapa 'completado' | 57 pacientes creados | 6/7 días | 0 errores
```

- El **resumen inicial** reemplaza al viejo `GET /status`: estado de OpenMRS, ventana, volumen y conteos de catálogos. Si OpenMRS no responde, el proceso termina sin tocar datos.
- El **progreso** se imprime cada ~15 segundos (porcentaje, fecha simulada, pacientes, errores).
- El **resumen final** lista los errores no fatales uno a uno (el pipeline continúa con el siguiente paciente ante errores individuales).
- **Ctrl+C** cancela limpiamente: los datos ya insertados persisten y se imprime el resumen parcial.

**Exit codes** (útiles para scripts/automatización):

| Código | Significado |
|:------:|-------------|
| `0` | Corrida completada (puede haber errores por-paciente, listados en el resumen) |
| `1` | Fallo del proceso completo (etapa `error`) |
| `2` | OpenMRS inaccesible o argumento CLI inválido — no se tocó ningún dato |

### 7.2 Limpiar los datos del simulador

```bash
dotnet run --project openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj -- clear
```

Cuenta los pacientes `SIM-`, **pide confirmación** (`¿Continuar? (s/N)`) y solo entonces anula (void, borrado lógico) cada paciente y sus visitas. Es **lento a propósito** (rate limiting de 200 ms/paciente para no saturar OpenMRS). Responder `N` (o Enter) aborta sin tocar nada. Ver §11 para qué se limpia y qué no.

---

## 8. Catálogos CSV — qué son y cómo extenderlos

Los catálogos viven en `openmrs_seeder_v1/openmrs_seeder_v1/catalogs/` y son **la fuente de todo el conocimiento clínico** del simulador — no hay listas de enfermedades ni fármacos en el código. Los esquemas columna a columna están en `parametrizacion_archivos.md`.

> **Importante:** los CSV se copian al `bin/` durante el build. Tras editar un CSV hay que **recompilar** (`dotnet build`) o copiarlo a mano a `bin/Debug/net10.0/catalogs/`, y reiniciar el simulador (se cargan una sola vez al arranque).

### 8.1 Estado actual

| Archivo | Filas | Estado | Para qué sirve |
|---------|:-----:|--------|----------------|
| `epidemiology-profile.csv` | 47 | Completo | Peso de cada categoría diagnóstica por edad/género |
| `diagnosticos.csv` | ~948 | Completo | Diagnósticos CIEL: categoría, severidad, edades, pesos M/F, `sexo`, `clima`, `cronica`, `comun`, `requiere_lab/rx`, `vital_fiebre`, `vital_imc` |
| `medicamentos.csv` | ~30 | Completo | Fármacos reales del formulario, con columnas `aplica_<categoría>` |
| `laboratorios.csv` | 27 | Completo | Pruebas + **columnas de resultado** (bandas normal/anormal, triggers por categoría o dx) |
| `alergenos.csv` | 15 | Completo | Alérgenos DRUG/FOOD/ENVIRONMENT verificados |
| `motivos_consulta.csv` | 37 | Completo | Frases de motivo de consulta en español por categoría |
| `nombres.csv` / `apellidos.csv` | ~155 / ~200 | Completo | Nombres y apellidos centroamericanos (2+2 por paciente) |
| `direcciones.csv` | ~55 | Opcional | Colonias/barrios/cantones de El Salvador con peso (captación de la clínica); ausente = direcciones genéricas |
| `consultorios.csv` | 3-4 | Opcional | Consultorios + médico; vacío = un solo médico/locación por defecto |
| `programas.csv` | 2 | Opcional | Programas de atención y sus disparadores (dx o categoría) |
| `clima.csv` | 52 | Opcional | Estación por semana ISO; ausente = sin estacionalidad |
| `comorbilidad_afinidades.csv` | 13 | Opcional | Qué categorías "atraen" a cuáles como comorbilidad |
| `examenes_clinicos.csv` | **0** | **Vacío** | Exámenes en consultorio — inactivo (los UUIDs de la instancia resultaron inválidos) |

### 8.2 Cómo agregar un diagnóstico (ejemplo completo)

Supongamos que se quiere agregar **"otitis media aguda"**:

1. **Buscar el concepto en la instancia** (nunca asumir el UUID CIEL estándar):
   ```
   GET /ws/rest/v1/concept?q=otitis media
   ```
   Tomar el `uuid` del resultado correcto y **abrir el concepto para confirmar que es la enfermedad correcta** (ha habido UUIDs "válidos pero de otra enfermedad").
2. **Agregar la fila** a `diagnosticos.csv`: categoría `respiratorio` (o `infeccioso`), severidad `leve`, `aplica_0_14=true` (es pediátrica), pesos, `comun=true` si debe ser frecuente, `cronica=false`.
3. **Recompilar**; verificar en el resumen inicial de la siguiente ejecución que el conteo subió.

### 8.3 Cómo agregar una categoría nueva

Agregar una categoría (p. ej. `oftalmologico`) requiere tocar código además de CSVs: columna `aplica_oftalmologico` en medicamentos/laboratorios + su parseo en `CatalogLoader` + el switch `AplicaCategoria` en los seeders + filas en `epidemiology-profile.csv`. El detalle está en `CLAUDE.md`.

### 8.4 Cómo activar un programa de atención

1. Listar los programas de la instancia: `GET /ws/rest/v1/program?v=full` (anotar `uuid` y, si tiene workflow, el `uuid` del estado inicial).
2. Agregar la fila a `programas.csv`: disparador por UUID de diagnóstico (`trigger_dx`) o por categoría (`trigger_categoria`), separados por `|` si son varios.
3. Recompilar. Los pacientes cuyo diagnóstico coincida quedarán inscritos (una sola vez por paciente).

### 8.5 Cómo activar las citas en agenda

1. Listar servicios: `GET /ws/rest/v1/appointmentService/all/default`.
2. Poner el `uuid` del servicio en `Defaults.AppointmentServiceUuid` (y opcionalmente el tipo en `AppointmentServiceTypeUuid`).
3. Dejar `AppointmentServiceUuid` **vacío desactiva la funcionalidad** por completo.

---

## 9. Casos de uso prácticos

### Caso 1 — Validación rápida del pipeline (5–10 minutos)

```json
"StartDate": "2024-01-02",
"EndDate":   "2024-01-08",
"PacientesPorDiaMedio": 5,
"PorcentajeRecurrentes": 0
```

~30 pacientes nuevos en 6 días hábiles. Al terminar, en OpenMRS: buscar `SIM-`, abrir un paciente y verificar visita → vitales → diagnóstico → labs con resultado → medicación. El campo `errores` del progress debe estar vacío.

### Caso 2 — Clínica pequeña, 1 año (escenario de referencia)

```json
"StartDate": "2024-01-01",
"EndDate":   "2024-12-31",
"PacientesPorDiaMedio": 15,
"PorcentajeRecurrentes": 30
```

Con `consultorios.csv` de 3 médicos produce ~4.500 visitas / ~3.200 pacientes (≈5-6 pacientes/médico/día — carga holgada, típica de clínica pequeña). Duración: ~4-6 horas. Para más carga por médico, subir `PacientesPorDiaMedio` a 20-24.

### Caso 3 — Demostrar la agenda de citas

Correr 1 mes con la configuración por defecto y abrir el módulo **Appointments** de O3: el calendario muestra las citas por médico. Las de pacientes que volvieron aparecen *Completed*; los no-shows, *Missed*. Consultar por fecha vía REST:

```
GET /ws/rest/v1/appointment/all?forDate=2024-03-15T00:00:00.000Z
```

### Caso 4 — Demostrar la historia de un crónico

Correr ≥3 meses. Buscar un paciente con HTA o diabetes en su problem list: sus visitas sucesivas muestran controles de la misma condición con su médico de cabecera, labs de seguimiento (HbA1c) y, si es diabético, su inscripción en el programa de educación. Esa historia "con hilo" es el diferencial del simulador.

### Caso 5 — Dataset reproducible para análisis académico

```json
"StartDate": "2024-01-01", "EndDate": "2024-06-30",
"PacientesPorDiaMedio": 30, "RandomSeed": 777
```

Misma seed + misma config = mismo dataset exacto. Limpiar con `DELETE /clear` y volver a correr para regenerarlo idéntico.

### Caso 6 — Escenario de alta carga cardiovascular

Editar `epidemiology-profile.csv`: subir el peso de `cardiovascular` en los grupos ≥30 años (p. ej. 60) y bajar el resto (5–10). Los pesos son relativos — no necesitan sumar 100.

### Caso 7 — Brote de dengue en época lluviosa

Con `clima.csv` presente y `Climate.SeasonalBoost` alto (p. ej. 4.0), las semanas etiquetadas `lluvia`/`verano` concentran dengue, chikungunya y EDA. Comparar el conteo de diagnósticos por mes para ver la curva estacional.

### Caso 8 — Ampliar el catálogo de medicamentos

1. Extraer UUIDs reales: `GET /ws/rest/v1/drug?v=full` (o el query SQL de §12).
2. Agregar filas a `medicamentos.csv` con `drug_uuid` **y** `concept_uuid` (DrugOrder exige ambos) y las columnas `aplica_*`.
3. Recompilar y verificar el conteo en el resumen inicial de la consola.

---

## 10. Manejo del tiempo

### 10.1 Período simulado vs. tiempo real

El simulador itera fechas históricas sin relación con el reloj del servidor: `StartDate`/`EndDate` son fechas pasadas que OpenMRS acepta sin restricción. La corrida avanza tan rápido como responda la API.

### 10.2 Volumen diario (Box-Muller)

```
μ = PacientesPorDiaMedio × WeekdayWeight[díaSemana]
σ = μ × 0.20
PacientesDelDía = max(0, round(Normal(μ, σ)))
```

Los domingos (peso 0.00) siempre dan 0. El split nuevos/recurrentes usa `PorcentajeRecurrentes` — pero el nº real de recurrentes puede quedar por debajo si pocos pacientes del pool están *elegibles* (ver 10.3).

### 10.3 Elegibilidad de recurrentes

Tras cada visita el paciente queda inelegible hasta `ProximoElegibleDesde`: 7–21 días (agudo) o 30–120 días (crónico). En ventanas cortas el % de recurrentes realizado será menor al configurado — es intencional: la recurrencia "madura" con el tiempo simulado.

### 10.4 Hora de visita y timestamps del pipeline

```
PicoAM (40%): 08:00–10:00 · PicoPM (30%): 13:00–15:00 · Resto (30%): 07:00–18:00
```

| Evento | Timestamp |
|--------|-----------|
| Inicio de visita / vitales | `VisitDatetime` |
| Consulta médica | `VisitDatetime + 30 min` (el médico atiende tras el triaje) |
| Cierre de visita | `VisitDatetime + 1–4 h` |
| Cita de seguimiento | 7–30 días después, slot de 15 min entre 08:00 y 15:45 |

### 10.5 Formato de fecha enviado a OpenMRS

```
"2024-03-15T09:45:00.000+0000"   (UTC)
```

Si la instancia tiene zona horaria local configurada (p. ej. UTC−6 El Salvador), la UI mostrará las horas desplazadas. Es el comportamiento estándar de OpenMRS, no un bug del simulador.

### 10.6 Fecha de nacimiento

La fecha de nacimiento se ancla a la **fecha de la visita simulada** (no a la fecha real del servidor), así la edad del paciente es válida en el momento histórico de su atención y OpenMRS nunca rechaza por `startDateCannotFallBeforeTheBirthDate`. Edad mínima: 6 meses (1 mes si `PediatricClinic=true`).

### 10.7 Reproducibilidad con RandomSeed

Los generadores usan seeds derivadas (`RandomSeed`, `+1`, `+2`, `+3`…), de modo que la misma configuración produce exactamente la misma corrida. Ojo: **dos corridas distintas con la misma seed** generan los mismos nombres → si no se limpia entre corridas habrá homónimos (con identificadores `SIM-` distintos).

### 10.8 Velocidad y estimación de duración

| Config | Pacientes | Requests aprox. | A 80 ms/req | A 150 ms/req |
|--------|:---------:|:---------------:|:-----------:|:------------:|
| 1 semana, 5/día | ~35 | ~700 | ~1 min | ~2 min |
| 1 mes, 15/día | ~390 | ~8.000 | ~11 min | ~20 min |
| 1 año, 15/día | ~4.500 | ~95.000 | ~2 h | ~4 h |
| 2 años, 40/día | ~20.800 | ~430.000 | ~10 h | ~18 h |

La latencia típica en instancia local es 80–150 ms/request. La corrida es secuencial a propósito (una clínica atiende de a un paciente; además evita condiciones de carrera en OpenMRS).

---

## 11. Limpieza e idempotencia

### Cómo el simulador rastrea sus datos

- Todo paciente lleva el identificador **`SIM-XXXXXXXX`** ("Old Identification Number") además de su OpenMRS ID válido.
- Visitas y encuentros llevan `SEEDED_BY_SIMULATOR` en la descripción; las citas, en `comments`.
- Los médicos generados llevan identificador **`SIM-MED-*`**.

### Qué limpia el subcomando `clear` (y qué no)

| Dato | ¿Se limpia? |
|------|-------------|
| Pacientes `SIM-` y sus visitas/encuentros/obs/órdenes | ✅ void lógico |
| Alergias, condiciones, inscripciones a programas, citas | ⚠️ No se anulan explícitamente, pero quedan **inaccesibles** al anular al paciente |
| Médicos `SIM-MED-*` y sus consultorios | ❌ Son datos de referencia — se **reutilizan** entre corridas |

### Reglas de idempotencia

- **Pacientes reales nunca se tocan** (no tienen prefijo `SIM-`).
- Cada `POST /run` crea pacientes nuevos con identificadores únicos — no hay colisión, pero sí dos "poblaciones" si no se limpia entre corridas.
- Si OpenMRS rechaza una visita por solapamiento (`visitCannotOverlapAnother`), el simulador reutiliza la visita activa existente en lugar de fallar.
- Los médicos del catálogo se crean **solo si no existen** (búsqueda exacta por identificador antes de crear).

---

## 12. Acceso directo a MariaDB

Útil solo para **leer** (extraer UUIDs para catálogos, verificar datos generados). La escritura siempre va por REST.

**Si OpenMRS corre en Docker** con el puerto 3306 expuesto:

```bash
mysql -h 127.0.0.1 -P 3306 -u openmrs -popenmrs openmrs
```

(Credenciales por defecto de la imagen oficial: `openmrs`/`openmrs`; verificar el `.env` del compose de tu instalación.)

**Queries útiles:**

```sql
-- Diagnósticos en español
SELECT c.uuid, cn.name FROM concept c
JOIN concept_name cn ON c.concept_id = cn.concept_id
WHERE cn.locale = 'es' AND cn.concept_name_type = 'FULLY_SPECIFIED'
  AND c.retired = 0 AND c.class_id = 4;   -- 4 = Diagnosis

-- Medicamentos (drug_uuid + concept_uuid, ambos necesarios)
SELECT d.uuid AS drug_uuid, c.uuid AS concept_uuid, cn.name, d.strength
FROM drug d
JOIN concept c ON d.concept_id = c.concept_id
JOIN concept_name cn ON c.concept_id = cn.concept_id
WHERE cn.locale = 'es' AND d.retired = 0;

-- Pruebas de laboratorio
SELECT c.uuid, cn.name FROM concept c
JOIN concept_name cn ON c.concept_id = cn.concept_id
WHERE cn.locale = 'es' AND c.class_id = 5 AND c.retired = 0;  -- 5 = Test
```

La carpeta `querys/` del repositorio incluye:

- `visita_detalle.sql` — queries de **verificación y QA** de los datos generados (volumen por día, coherencia de vitales, top de diagnósticos, reparto por médico…).
- `borrar_simulacion.sql` — **borrado duro** por SQL de todos los pacientes `SIM-` y su rastro clínico. Es el complemento extremo del subcomando `clear` (que hace borrado *lógico*/void): úsalo solo cuando quieras eliminar físicamente los datos simulados, con OpenMRS detenido o bajo tu responsabilidad.

> Si se cambia un UUID en un CSV: recompilar y reiniciar el simulador (los catálogos se cargan una vez al arranque).

### Respaldo de la base de datos

`scripts/backup_openmrs.ps1` hace un `mariadb-dump` consistente (sin detener la instancia), lo comprime con fecha en `backups/` y aplica retención:

```powershell
pwsh scripts/backup_openmrs.ps1                       # respaldo con retención de 7 copias
pwsh scripts/backup_openmrs.ps1 -Destino D:\resp -Retencion 14
```

Las instrucciones de **restore** están en el encabezado del script (requiere detener el backend). Para automatizarlo, programar ese comando en el Programador de tareas de Windows. La carpeta `backups/` está en `.gitignore`.

---

## 13. Solución de problemas

### `OpenMRS: OFFLINE` en el resumen inicial (exit code 2)

1. Probar desde donde corre el simulador: `curl -u admin:<password> http://<host>/openmrs/ws/rest/v1/session`
2. OpenMRS en Docker puede tardar 10–15 min en el primer arranque (`docker compose ps` → esperar `healthy`).
3. Simulador en Docker: la URL no puede ser `localhost` — usar `host.docker.internal` (Win/Mac) o `172.17.0.1` (Linux).
4. La URL debe terminar en `/ws/rest/v1` sin slash final extra.

### Errores de UUID en el resumen final

Mensajes `404`, `Invalid UUID`, `Resource does not exist` → los UUIDs de `OpenMRS.Defaults` (o de un catálogo) no corresponden a esta instancia. Verificar con los endpoints de §5.2. Estos errores no detienen el pipeline.

### Catálogos con 0 registros

Los CSV no se copiaron al directorio de salida → `dotnet build` y verificar que exista `bin/Debug/net10.0/catalogs/diagnosticos.csv`.

### La simulación termina con exit code 1 (`etapa 'error'`) muy pronto

Fallo del proceso completo (no de un paciente). Causas típicas: OpenMRS caído a mitad de corrida; catálogos vacíos; UUID de provider/location inválido; **un médico de `consultorios.csv` no se pudo crear/verificar** (el arranque es fail-fast a propósito: aborta antes de generar datos con un médico inexistente). El mensaje exacto se lista en el resumen final.

### Las horas aparecen desfasadas en la UI de O3

Las fechas se envían en UTC (`+0000`); si la instancia tiene zona horaria local, la UI las desplaza. Comportamiento estándar de OpenMRS (ver §10.5).

### Windows bloquea los binarios recién compilados

En Windows 11, **Smart App Control** puede bloquear el ejecutable recién compilado (`FileLoadException 0x800711C7`): `dotnet test` o el seeder no arrancan aunque la compilación fue exitosa. Solución: Configuración → Seguridad de Windows → Control de aplicaciones inteligente → *Desactivado* (o modo Evaluación).

### `Obs.error.precision` al registrar un resultado de laboratorio

El concepto no admite decimales (`concept_numeric.allow_decimal=0`). El generador ya emite enteros cuando la banda del catálogo tiene límites enteros — si aparece este error con un lab nuevo, definir sus bandas `res_min/res_max` con valores enteros en `laboratorios.csv`.

### No se crean citas en la agenda

- `Defaults.AppointmentServiceUuid` vacío = funcionalidad desactivada (comportamiento intencional).
- Verificar que el servicio existe: `GET /appointmentService/all/default`.
- Solo ~30% de las visitas (las que disparan `FollowUp`) generan cita.

### El subcomando `clear` es muy lento

Rate limiting intencional (200 ms/paciente) para no saturar OpenMRS. Para 1.000 pacientes: ~3-4 minutos como mínimo.

---

## 14. Decisiones de diseño clave

Para quien quiera extender el simulador sin romper su coherencia:

**Solo REST, nunca SQL directo.** Compatibilidad con cualquier despliegue y respeto de las validaciones de negocio de OpenMRS (Luhn en identificadores, integridad referencial, rangos absolutos de conceptos numéricos). El costo es la velocidad: la corrida va a la latencia HTTP de la instancia.

**Ejecución batch directa.** El proceso ES la corrida: `dotnet run` valida, ejecuta y termina con exit code. Sin servidor web, sin Swagger, sin `runId` que sondear — el progreso se imprime en consola y el estado vive lo que vive el proceso. Si se interrumpe (Ctrl+C o caída), los datos ya insertados persisten y una nueva ejecución crea una población nueva (o se limpia antes con `clear`).

**Catálogos CSV, no código.** Cualquier persona puede ampliar el conocimiento clínico (diagnósticos, pesos, fármacos, programas) editando CSVs, sin tocar C#. La contracara: los UUIDs de los CSV son de **una instancia concreta** y deben verificarse al migrar a otra.

**Seams puros y testeables.** Toda decisión probabilística o de clasificación (selección de diagnóstico, comorbilidades, vitales, resultados de lab, clasificación de citas, roster de médicos, elegibilidad de recurrentes) está aislada en funciones puras con RNG inyectado, cubiertas por la suite de tests (`dotnet test`, 128 tests). Las llamadas HTTP quedan en la cáscara de los seeders.

**Estado compartido del paciente en el pool.** Las colecciones del paciente (problem list, programas, citas pendientes, crónicas activas) se comparten **por referencia** entre la copia del pool y la copia de cada visita recurrente — así la historia del paciente es acumulativa a lo largo de la simulación.

**Encadenamiento de UUIDs.** Cada seeder escribe en `SimulatedPatient` los UUIDs que crea (visita, encuentro, orden) para que el siguiente los use. Si un seeder falla, los siguientes hacen skip y el error queda registrado sin detener la corrida.

**Fail-fast en recursos de referencia.** Los médicos del catálogo se verifican/crean **antes** de generar datos; si uno falla, la corrida aborta en estado `error` en vez de generar encuentros con un proveedor inválido.

---

*Fin del manual — basado en el código fuente a julio 2026*
