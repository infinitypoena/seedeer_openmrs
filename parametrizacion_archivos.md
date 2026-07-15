# Parametrización del Simulador Clínico OpenMRS

Este documento describe todos los archivos de configuración y catálogos del simulador, qué controla cada parámetro y cómo interactúan entre sí.

---

## 1. appsettings.json — Configuración central

Todo el comportamiento del simulador se controla desde aquí.

> **Validación al arranque (fail-fast):** `SettingsValidator` revisa la configuración al iniciar el
> proceso. Valores inválidos (probabilidades fuera de `[0,1]`, bandas invertidas `Min > Max`,
> `StartDate > EndDate`, volúmenes ≤ 0…) **impiden arrancar** con un mensaje que lista cada campo
> violado. Las claves del JSON que no correspondan a ningún parámetro (p. ej. una clave obsoleta de
> una versión anterior) generan un **warning** en el log — el binding de .NET las ignoraría en
> silencio.

```json
{
  "OpenMRS": {
    "RestApi": {
      "BaseUrl": "http://localhost/openmrs/ws/rest/v1",
      "Username": "admin",
      "Password": "Prueba01$$xD"
    },
    "Defaults": {
      "PatientIdentifierTypeUuid": "05a29f94-c0ed-11e2-94be-8c13b969e334",
      "LocationUuid": "44c3efb0-2583-4c80-a79e-1f756a03c0a1",
      "RegistrationLocationUuid": "c1000000-0000-0000-0000-000000000002",
      "VisitTypeUuid": "287463d3-2233-4c69-9851-5841a1f5e109",
      "VitalsEncounterTypeUuid": "67a71486-1a54-468f-ac3e-7091a9a79584",
      "ConsultaEncounterTypeUuid": "92a52cce-c614-4046-b5f2-07f32f0bcf91",
      "ProviderUuid": "f9badd80-ab76-11e2-9e96-0800200c9a66"
    }
  },
  "Simulation": {
    "StartDate": "2023-01-01",
    "EndDate": "2024-12-31",
    "PacientesPorDiaMedio": 6,
    "Locale": "es",
    "RandomSeed": 42,
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
    },
    "Comorbidity": {
      "BaseProbability": 0.20,
      "MaxAdditional": 2,
      "SecondExtraProbability": 0.25,
      "AffinityBoost": 4.0,
      "AgeScaling": { "0-14": 0.3, "15-29": 0.5, "30-44": 0.8, "45-64": 1.3, "65+": 1.8 }
    }
  }
}
```

### Referencia de parámetros — sección Simulation

| Parámetro | Tipo | Descripción |
|-----------|------|-------------|
| `StartDate` / `EndDate` | date | Rango temporal de la simulación. |
| `PacientesPorDiaMedio` | int | **Altas** (pacientes nuevos) por día hábil con las que ARRANCA la clínica. El día 0 el pool está vacío, así que son todas las visitas de ese día. ⚠️ **No** es el volumen de la clínica madura: el panel devuelve a cada paciente ~3,3 veces (ver §14). |
| ~~`PorcentajeRecurrentes`~~ | — | **ELIMINADO.** Forzaba un cupo fijo de recurrentes y estranguló la agenda: media agenda perdida, el 65 % de los crónicos sin volver jamás a un control. La fracción de recurrentes **emerge** del panel. Ver §14 y `leyes_simulacion.md`. |
| `SeguimientoCronicoProb` | float (0-1) | Continuidad longitudinal: prob. (def. 0.70) de que una visita recurrente de un paciente con condición crónica conocida sea un **control de esa misma condición** en vez de un motivo agudo nuevo. Solo aplica si el paciente arrastra ≥1 dx crónico. |
| `SeguimientoAgudoProb` | float (0-1) | Espejo agudo: prob. (def. 0.70) de que un recurrente NO crónico que vuelve dentro de la ventana de su episodio agudo regrese por el **mismo dx** (control/mejoría) en vez de una enfermedad aleatoria. El control cierra el episodio. |
| `VentanaSeguimientoAgudoDias` | int (días) | Vigencia del episodio agudo desde su última visita (def. 30). Fuera de la ventana el retorno vuelve a ser un motivo nuevo. |
| `Recurrence.MinDiasAgudo` / `MaxDiasAgudo` | int (días) | Intervalo mínimo/máximo para que un paciente **no crónico** vuelva (seguimiento agudo, def. 7–21). Evita retornos día-a-día. |
| `Recurrence.MinDiasCronico` / `MaxDiasCronico` | int (días) | Intervalo del **control crónico** (def. 30–120). |
| **`Recurrence.VisitasEspontaneasPorPacienteAno`** | float | **Veces al año que un paciente del panel vuelve POR SU CUENTA**, sin que nadie le haya citado (le pasa algo nuevo meses después). Def. **0.5**. Es la segunda vía de retorno, junto a la cita, y la que hace que el padrón de pacientes **se use**. Solo aplica a los **activos** (última visita dentro de `Crecimiento.VentanaActividadDias`) que ya cumplieron su intervalo mínimo; el insatisfecho no vuelve solo. **Es el mando que fija cuánta consulta genera el panel por sí mismo**, y con él la fracción de recurrentes en régimen: subirlo hace la clínica más "de barrio" (más controles, menos captación); 0 = solo se vuelve si hay cita. |
| `Locale` | string | Locale de Bogus (solo fallback de nombres si faltan `nombres.csv`/`apellidos.csv`). `"es"` = español. |
| `UtcOffset` | string | Offset UTC de TODAS las fechas enviadas a OpenMRS (`"±HH:mm"`, p.ej. `"-06:00"` El Salvador). Debe coincidir con la `TZ` del backend para que las horas se lean como hora local en la UI. Vacío = UTC (histórico). ⚠️ Cambiarlo desalinea los datos ya insertados con el offset anterior — aplicar antes de regenerar. |
| `RandomSeed` | int | Semilla para reproducibilidad. Mismo seed = misma simulación. |
| `CommonProbMin` / `CommonProbMax` | float (0-1) | Factor inicial: cada corrida sortea su P(común) en `[min,max]` (def. 0.75–0.95) → el principal cae mayormente en el pool `comun=true`, variando entre corridas. |
| `MedicoCabeceraProbMin` / `MedicoCabeceraProbMax` | float (0-1) | Médico de cabecera: cada corrida sortea en `[min,max]` (def. 0.70–0.90) la prob. de que un recurrente vuelva con el mismo médico/consultorio de su primera visita; si no, cae con otro. Requiere `catalogs/consultorios.csv`. |
| `HorarioAtencion.PicoAM/PM` | objeto | Bloque horario pico con peso (% de atenciones). El resto se distribuye uniformemente. |
| `DemographicProfile.AgeGroups` | array | Distribución etaria. Los `Weight` se normalizan al 100%. |
| `DemographicProfile.GenderRatio` | objeto | Proporción M/F (se normalizan entre sí). |
| `DemographicProfile.MinPatientAgeMonths` | int | Edad mínima de pacientes en meses (def. 6). La fecha de nacimiento se ancla a la fecha de la visita. |
| `DemographicProfile.PediatricClinic` | bool | Consultorio pediátrico: baja el mínimo a `PediatricMinAgeMonths`. |
| `DemographicProfile.PediatricMinAgeMonths` | int | Edad mínima en meses en modo pediátrico (def. 1). |
| `ReferralProbabilities.LabOrder` | float (0-1) | Probabilidad base de orden de laboratorio externo (testorder). |
| `ReferralProbabilities.ClinicalExam` | float (0-1) | Probabilidad base de examen en consultorio (obs inmediata). |
| `ReferralProbabilities.DrugOrder` | float (0-1) | Probabilidad base de prescripción de medicamento. |
| `ReferralProbabilities.Urgent` | float (0-1) | Probabilidad de que una orden de lab sea URGENTE. |
| `ReferralProbabilities.FollowUp` | float (0-1) | Probabilidad de registrar una cita de control **cuando el cuadro es leve**: obs fecha "Return visit date" (`5096`) en la fecha de la banda de recurrencia (agudo 7–21 d / crónico 30–120 d) **+ cita real en la agenda** (Bahmni Appointments) con el médico/consultorio de la visita, si `Defaults.AppointmentServiceUuid` está configurado (def. 0.30). |
| `ReferralProbabilities.FollowUpCronico` | float (0-1) | Probabilidad de agendar control cuando el cuadro incluye una condición crónica (def. 0.90). |
| `ReferralProbabilities.FollowUpGrave` | float (0-1) | Probabilidad de agendar control cuando el cuadro (no crónico) es grave (def. 0.80). |
| `Appointments.ToleranciaDias` | int (días) | Resolución de citas al volver el paciente: cita a ±tolerancia de la visita → `Completed`; anterior a la ventana → `Missed` (no-show); futura → sigue `Scheduled`. También es el margen con que la selección de recurrentes atiende a quien tiene cita para hoy (def. 3). |
| `Appointments.AsistenciaProb` | float (0-1) | Probabilidad de que un paciente con cita para hoy (±tolerancia) efectivamente asista; el resto son no-shows cuya cita, al vencer, pasa a `Missed` (def. 0.75). |
| `Orders.LabVigenciaDias` | int (días) | Días que una orden de laboratorio sigue activa (`autoExpireDate`). Mientras esté vigente no se re-ordena el mismo test; pasado el plazo, un control crónico puede volver a pedirlo (def. 7). |
| `Variedad.RepeticionDamping` | float (≥0) | Amortiguación anti-repetición: cada vez que un dx sale en la corrida su peso efectivo baja (`peso / (1 + damping × usos)`) → se explora la cola larga del catálogo (~950 dx). No altera el perfil por edad/sexo/clima ni los controles crónicos/agudos. `0` = apagado (def. 0.25). |
| `Laboratorio.ProbRechazo` | float (0-1) | Fracción de muestras que el laboratorio **rechaza** (`DECLINED`: hemolizada, insuficiente, el paciente no acudió). Def. 0.04 |
| `Laboratorio.ProbResultadoLlega` | float (0-1) | Fracción de muestras tomadas cuyo resultado acaba llegando. El resto se pierde y su orden se queda en `IN_PROGRESS` (def. 0.95). ⚠️ Sustituye al viejo `ReferralProbabilities.LabResult`: **cuándo** llega el resultado ya no es una probabilidad, lo decide el catálogo (`se_realiza_en_clinica` / `dias_entrega_*`, §5) |
| `Laboratorio.MinutosHastaTomaMin` / `Max` | int | Minutos entre la consulta y la toma de la muestra (el paciente pasa por el laboratorio). Def. 20–90 |
| `MinMedicosPorDia` / `MaxMedicosPorDia` | int | Roster diario: cada día se activan aleatoriamente entre `Min` y `Max` médicos del pool de `consultorios.csv` (def. 2/3). Pool ≤ Min = todos disponibles. |
| `Allergy.BaseProbabilityMin` / `BaseProbabilityMax` | float (0-1) | Banda de prevalencia de alergias: cada corrida sortea su valor en `[min,max]` (def. 0.15–0.25, fracción clínicamente documentada del ~25-30% poblacional) → el % de pacientes nuevos alérgicos varía entre corridas. |
| `Allergy.SecondAllergyProbability` | float (0-1) | Dado que el paciente ya tiene 1 alergia, probabilidad de sumar una 2ª (decaída condicional). |
| `Allergy.ThirdAllergyProbability` | float (0-1) | Dado que ya tiene 2, probabilidad de sumar una 3ª. |
| `Allergy.MaxAllergies` | int | Tope de alergias por paciente (también limitado por el tamaño de `alergenos.csv`). |
| `WeekdayWeights` | objeto | Multiplicador de volumen por día. `1.0` = promedio, `0.0` = sin atención. |
| `Comorbidity.BaseProbability` | float (0-1) | Probabilidad base de que un paciente tenga ≥1 diagnóstico adicional (comorbilidad) en la misma visita. |
| `Comorbidity.MaxAdditional` | int | Tope de diagnósticos adicionales además del primario. |
| `Comorbidity.SecondExtraProbability` | float (0-1) | Dado que ya hay una comorbilidad, probabilidad de añadir una segunda. |
| `Comorbidity.AffinityBoost` | float | Multiplicador del peso de las categorías clínicamente afines al elegir la enfermedad adicional. |
| `Comorbidity.AgeScaling` | objeto | Multiplicador de `BaseProbability` por grupo de edad (la multimorbilidad crece con la edad). El producto se limita a 0.95. |
| _Afinidades de comorbilidad_ | catálogo | Movido a `catalogs/comorbilidad_afinidades.csv` (ver §10). Clusters categoría→afines que reciben `AffinityBoost`. |

> **Comorbilidad en una sola visita:** el primario se elige como antes; luego, con probabilidad `BaseProbability × AgeScaling[grupo]`, se añaden 1..`MaxAdditional` diagnósticos de **otras** categorías (priorizando las afines). Todos se registran en el mismo encounter (`rank=1` primario, `rank=2` secundarios) y las órdenes de laboratorio y prescripciones cubren las categorías de **todas** las enfermedades del paciente.

| `Climate.Enabled` | bool | Activa el efecto estacional (requiere `catalogs/clima.csv`). Si `false`, se ignora el clima. |
| `Climate.SeasonalBoost` | float | Multiplicador de peso de enfermedades y categorías favorecidas por la estación activa. |
| `Climate.ComfortTempC` | float | Temperatura ambiente de confort; por encima sube la temperatura corporal registrada. |
| `Climate.TempVitalsFactorC` | float | °C de temperatura corporal por cada °C ambiente sobre el confort. |
| `Climate.TempVitalsMaxC` | float | Tope del ajuste de temperatura corporal por calor. |

> **Clima estacional (opcional):** si existe `catalogs/clima.csv` (una fila por semana ISO: `semana,estacion,temp_promedio_c` con estación ∈ invierno/verano/lluvia/seca), la simulación favorece las enfermedades marcadas con esa estación en la columna `clima` de `diagnosticos.csv` (gripe→invierno, dengue/EDA→verano,lluvia) y el calor sube levemente la temperatura corporal. Si el archivo falta o la semana no está listada → efecto neutro.

### Referencia de parámetros — sección OpenMRS.Defaults

| Parámetro | Descripción | Cómo obtenerlo |
|-----------|-------------|----------------|
| `PatientIdentifierTypeUuid` | UUID del tipo de ID "OpenMRS ID" | `GET /ws/rest/v1/patientidentifiertype` |
| `TrackingIdentifierTypeUuid` | Tipo de ID "Old Identification Number" — lleva el prefijo `SIM-` que hace idempotente al `clear` | `GET /ws/rest/v1/patientidentifiertype` |
| `LocationUuid` | Ubicación de **respaldo** si no hay `catalogs/consultorios.csv` | `GET /ws/rest/v1/location` |
| `RegistrationLocationUuid` | Ubicación de registro/admisión (Recepción) del identificador del paciente | `GET /ws/rest/v1/location` |
| `VisitTypeUuid` | UUID del tipo de visita "OPD Visit" (consulta externa; `287463d3-…`) | `GET /ws/rest/v1/visittype` |
| `VitalsEncounterTypeUuid` | UUID del tipo de encuentro "Vitals" | `GET /ws/rest/v1/encountertype` |
| `ConsultaEncounterTypeUuid` | UUID del tipo de encuentro "Consultation" | `GET /ws/rest/v1/encountertype` |
| `ProviderUuid` | Médico de **respaldo** si no hay `catalogs/consultorios.csv` | `GET /ws/rest/v1/provider` |
| `EncounterRoleUuid` | Rol del médico en el encuentro ("Clinician") | `GET /ws/rest/v1/encounterrole` |
| `OutpatientCareSettingUuid` | Care setting de las órdenes (ambulatorio) | `GET /ws/rest/v1/caresetting` |
| `OnceDailyFrequencyUuid`, `DaysConceptUuid`, `TabletConceptUuid` | Frecuencia, unidad de duración y unidad de dosis de las prescripciones | `GET /ws/rest/v1/orderfrequency`, `GET /ws/rest/v1/concept?q=` |
| `AppointmentServiceUuid` / `AppointmentServiceTypeUuid` | Servicio (y tipo) de la agenda Bahmni. **Vacío = no se agendan citas** | `GET /ws/rest/v1/appointmentService/all/default` |
| `TelephoneAttributeTypeUuid` | Person attribute "Telephone Number" (formato String). **Vacío = paciente sin teléfono** | `GET /ws/rest/v1/personattributetype` |
| `CivilStatusAttributeTypeUuid` | Person attribute "Civil Status" (formato **Concept** → el valor enviado es el UUID de una *answer* del concepto `1054`). **Vacío = paciente sin estado civil** | `GET /ws/rest/v1/personattributetype`; las answers, con `GET /concept/1054…?v=full` (⚠️ `?q=` con rep. personalizada de `answers` da NPE en esta instancia) |

### Referencia de parámetros — sección OpenMRS.Database (etapa 5/5, fechas de auditoría)

Es la **única parte del simulador que habla directamente con MariaDB**, y está **desactivada por defecto**:
con `CorregirFechas: false` (o `ConnectionString` vacío) el proyecto sigue siendo REST puro. Existe porque
OpenMRS sella `date_created` con el reloj real del servidor y por REST no hay forma de mandarlo, así que sin
esta etapa todas las filas de una corrida de años quedan creadas el mismo día. Detalle completo en
**`correccion_fechas.md`**.

| Parámetro | Descripción | Valor típico |
|-----------|-------------|--------------|
| `CorregirFechas` | Interruptor. `true` = se ejecuta la etapa 5/5 al terminar de sembrar | `false` (por defecto) |
| `ConnectionString` | Cadena de conexión a MariaDB. **Vacía = feature apagada** aunque `CorregirFechas` sea `true`. Requiere el puerto 3306 expuesto al host | `Server=localhost;Port=3306;Database=openmrs;User Id=openmrs;Password=…;` |
| `PrefijoPaciente` | Prefijo del identificador de los pacientes simulados. **Acota todo lo que el proceso puede tocar**: nada fuera de ese conjunto se modifica | `SIM-` |
| `TamanoLote` | Filas por lote de `UPDATE` (transacciones cortas: ni undo log enorme ni bloqueos largos) | `20000` |
| `PedirConfirmacion` | Preguntar `s/N` por consola antes de escribir. `false` para corridas desatendidas | `true` |

⚠️ Tras aplicar hay que **reiniciar el backend** (`docker compose restart backend`) o Hibernate seguirá
sirviendo las fechas viejas desde su caché.

---

## 2. catalogs/epidemiology-profile.csv — Pesos por categoría/edad/género

Archivo simple. Una fila por combinación de categoría + grupo etario + género.

```csv
categoria,grupo_edad,genero,peso
respiratorio,0-14,M,35
respiratorio,0-14,F,32
cardiovascular,45-64,M,28
cardiovascular,45-64,F,22
diabetes,30-44,Ambos,12
diabetes,45-64,Ambos,20
```

| Columna | Tipo | Descripción |
|---------|------|-------------|
| `categoria` | string | Sistema orgánico. Ver tabla de categorías abajo. |
| `grupo_edad` | string | `0-14`, `15-29`, `30-44`, `45-64`, `65+` |
| `genero` | string | `M`, `F`, o `Ambos` (aplica para ambos géneros) |
| `peso` | int | Probabilidad relativa. Se normaliza al 100% dentro de cada grupo edad+género. |

### Categorías de diagnóstico disponibles

| Categoría | Descripción | Grupos etarios más afectados |
|-----------|-------------|------------------------------|
| `respiratorio` | Infecciones vías aéreas, asma, EPOC, bronquitis | 0-14, 65+ |
| `cardiovascular` | HTA, ICC, angina, arritmias | 45-64, 65+ |
| `diabetes` | DM2, prediabetes, control glicémico | 30-44, 45-64, 65+ |
| `digestivo` | Gastritis, colitis, parasitosis | Todos |
| `osteomuscular` | Artritis, lumbalgia, fracturas, tendinitis | 30-44, 65+ |
| `urologico` | ITU, litiasis, IRC, prostática | F 15-44, M 45+ |
| `infeccioso` | Fiebre sin foco, infecciones generales | Todos |
| `endocrino` | Hipotiroidismo, obesidad, dislipidemia | 30+, predominio F |

### Cómo funciona la selección de categoría

Para un paciente de 52 años, masculino:
1. Filtrar filas donde `grupo_edad = "45-64"` y `genero = "M"` o `"Ambos"`
2. Normalizar los `peso` → probabilidades (ej: cardiovascular 28%, diabetes 20%...)
3. Elegir `categoria` al azar con esas probabilidades
4. Ir a `diagnosticos.csv` a elegir el diagnóstico específico dentro de esa categoría

---

## 3. catalogs/diagnosticos.csv — Catálogo con booleanos por grupo etario

Extraído de OpenMRS DB + enriquecido manualmente con columnas booleanas.

```csv
ciel_uuid,nombre_es,categoria,severidad,aplica_0_14,aplica_15_29,aplica_30_44,aplica_45_64,aplica_65mas,peso_M,peso_F,requiere_lab,requiere_rx,requiere_examen_clinico
120748AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA,Bronquitis aguda,respiratorio,leve,true,true,true,true,true,10,10,false,true,true
117321AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA,Hipertensión arterial,cardiovascular,moderado,false,false,false,true,true,28,22,true,true,true
119481AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA,Diabetes mellitus tipo 2,diabetes,moderado,false,false,true,true,true,15,18,true,true,true
```

| Columna | Descripción |
|---------|-------------|
| `ciel_uuid` | UUID del concept OpenMRS — se usa en `POST /encounter` para el diagnóstico |
| `nombre_es` | Nombre en español (FULLY_SPECIFIED en OpenMRS) |
| `categoria` | Categoría del sistema orgánico — enlaza con `epidemiology-profile.csv` |
| `severidad` | `leve`, `moderado`, `grave` — afecta probabilidad de lab urgente |
| `aplica_0_14` | `true`/`false` — ¿puede aparecer en niños 0-14? |
| `aplica_15_29` | `true`/`false` — ¿puede aparecer en adultos jóvenes 15-29? |
| `aplica_30_44` | `true`/`false` — ¿puede aparecer en adultos 30-44? |
| `aplica_45_64` | `true`/`false` — ¿puede aparecer en adultos mayores 45-64? |
| `aplica_65mas` | `true`/`false` — ¿puede aparecer en adultos 65+? |
| `peso_M` | Peso relativo dentro de la categoría para hombres (se normaliza entre dx de la misma categoría) |
| `peso_F` | Peso relativo dentro de la categoría para mujeres (se normaliza) |
| `requiere_lab` | `true` si este dx siempre pide laboratorio (aumenta la probabilidad base de `LabOrder`) |
| `requiere_rx` | `true` si este dx siempre recibe prescripción (aumenta la probabilidad base de `DrugOrder`) |
| `requiere_examen_clinico` | `true` si este dx típicamente requiere examen en consultorio (sube prob. a 90%) |
| `clima` | Estación(es) que favorecen el dx (`invierno`/`verano`/`lluvia`/`seca`, separadas por coma). Vacío = sin efecto estacional |
| `cronica` | `true` → se agrega a la lista de problemas del paciente (`POST /condition`) |
| `comun` | `true` → pertenece al pool de enfermedades frecuentes (sesgo de selección inicial) |
| `vital_fiebre` *(opcional)* | `true` → fuerza fiebre en los vitales aunque la categoría no sea febril (p.ej. apendicitis, pielonefritis). Vacío = neutro |
| `vital_imc` *(opcional)* | `alto` (sobrepeso/obesidad) o `bajo` (desnutrición/caquexia: TB, cáncer, hipertiroidismo, VIH…) para fijar el IMC objetivo. **Gana sobre la categoría.** Vacío = neutro |
| `vital_pa` *(opcional)* | `alta` → banda hipertensiva (140-180/90-110) aunque la categoría no sea cardiovascular (preeclampsia/eclampsia, enfermedad renal, Cushing, hipertiroidismo). Vacío = neutro |
| `vital_fc` *(opcional)* | `alta` → taquicardia 100-130 (hipertiroidismo, anemia, hipovolemia/hemorragia) o `baja` → bradicardia 42-58 (hipotiroidismo, bloqueos AV). **Gana incluso sobre la taquicardia febril.** Vacío = neutro |
| `vital_spo2` *(opcional)* | `baja` → SpO2 88-94 fuera de respiratorio (insuficiencia cardíaca, TEP). ⚠️ La anemia NO va aquí (satura normal). Vacío = neutro |
| `sexo` *(opcional)* | `M` o `F` → el dx **solo** aparece en ese sexo (exclusión dura: embarazo/eclampsia = F, próstata/testículo = M). Vacío = ambos. Se puebla con `scripts/ajustar_diagnosticos.ps1` (reglas por palabra clave) |
| **`ambito`** | Qué puede hacer la clínica con el cuadro: **`clinica`** (o vacío) = lo trata ella misma · **`referencia`** = lo detecta, lo estabiliza y lo **manda al hospital** (ver abajo) |

> **Fuente**: catálogo curado a mano sobre conceptos CIEL de la instancia. Las columnas `vital_*` y `ambito` son **opcionales** (el loader tolera su ausencia → neutro / `clinica`).

### La columna `ambito` — la clínica es de PRIMER NIVEL

Una consulta externa no opera un abdomen agudo ni maneja un infarto: **lo detecta, lo estabiliza y lo
refiere**. Los diagnósticos con `ambito=referencia` (64 filas: apendicitis, IAM, sepsis, eclampsia,
politraumatismo, cetoacidosis…) hacen que la consulta registre la referencia al hospital, con cuatro obs:

| Se registra | Valor |
|---|---|
| Remisiones solicitadas | Hospital |
| ¿Paciente referido a hospital? | Sí |
| Prioridad de referencia | **Emergencia** si el cuadro es grave, **Urgente** si no |
| Motivo de la referencia | texto con los diagnósticos que lo justifican |

Y el episodio se vuelve coherente: **no se prescribe** (el tratamiento definitivo lo pauta el hospital), los
laboratorios salen en **`STAT`** (son de estabilización) y se agenda un **control post-alta a 15-30 días**
(`Recurrence.MinDiasPostReferencia`/`Max`), para cuando le den de alta. El paciente **vuelve**, y se ve la
continuidad completa.

> ⚠️ **`severidad=grave` NO significa referir.** Es el matiz que decide si esto sale bien clínicamente:
> el VIH, la tuberculosis (el DOTS es de primer nivel), el pie diabético o el trastorno bipolar son
> graves y **los maneja el primer nivel** — de hecho ya tienen sus programas de atención
> (`programas.csv`). Referirlos sería un error. La severidad solo decide **la prisa** del traslado.
> Por eso `ambito` **se cura a mano**, no por regla automática, y el validador **avisa** si una fila es
> `referencia` y `cronica` a la vez.

### Los dos scripts del catálogo tienen contratos OPUESTOS

| Script | Qué hace | ⚠️ |
|---|---|---|
| `scripts/ajustar_diagnosticos.ps1` | Normaliza `vital_*`, `cronica`, `severidad`, `sexo` por reglas de palabra clave | **Solo añade / eleva. Nunca pisa** un valor ya puesto a mano |
| `scripts/limpiar_diagnosticos.ps1` | La poda de 875 → 394 filas: borra la cosecha cruda de CIEL y lo no endémico, corrige categorías, marca `ambito` | **BORRA filas y SOBRESCRIBE** valores. Haz commit antes |

Ambos son **idempotentes** (aplicarlos dos veces da el mismo resultado). El de limpieza imprime al terminar
el conteo por categoría: **ninguna puede quedar en 0** o el validador aborta el arranque.

---

## 4. catalogs/medicamentos.csv — Catálogo de fármacos con booleanos por categoría

Extraído de la tabla `drug` de OpenMRS + columnas booleanas por categoría diagnóstica.

```csv
drug_uuid,nombre_generico,strength,via_uuid,aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,aplica_digestivo,aplica_osteomuscular,aplica_urologico,aplica_infeccioso,aplica_endocrino
SAMPLE_amoxicilina,Amoxicilina,500mg,SAMPLE_oral,true,false,false,false,false,false,true,false
SAMPLE_enalapril,Enalapril,10mg,SAMPLE_oral,false,true,false,false,false,false,false,false
SAMPLE_metformina,Metformina,850mg,SAMPLE_oral,false,false,true,false,false,false,false,false
```

| Columna | Descripción |
|---------|-------------|
| `drug_uuid` | UUID del drug en OpenMRS — se usa en `POST /order` tipo drugorder |
| `nombre_generico` | Nombre genérico del medicamento |
| `strength` | Concentración (ej: `500mg`, `10mg`, `100mcg`) |
| `via_uuid` | UUID CIEL de la vía de administración (oral, inhalado, IV, IM) |
| `aplica_CATEGORIA` | `true`/`false` — si este medicamento es coherente para esa categoría diagnóstica |
| `dosis` | *(opcional)* Dosis por toma (`1`, `0.5`, `5`). Vacío = `1` |
| `unidad_dosis_uuid` | *(opcional)* UUID de la unidad de dosis (tableta, mL, mg). Vacío = tableta (`Defaults.TabletConceptUuid`) |
| `frecuencia_uuid` | *(opcional)* UUID de la frecuencia (cada 8 h, dos veces al día). Vacío = una vez al día (`Defaults.OnceDailyFrequencyUuid`) |
| `dias_tratamiento` | *(opcional)* Días de tratamiento. Vacío/`0` = duración aleatoria (7/14/30) |

> **Posología opcional**: las cuatro últimas columnas son opcionales (el loader tolera su ausencia → comportamiento histórico: 1 tableta / una vez al día). Para usarlas hay que **verificar los UUID contra la instancia** (`GET /ws/rest/v1/orderfrequency`, `GET /ws/rest/v1/concept?q=`); un UUID inexistente rompería esa orden, así que ante la duda dejar la celda vacía.
>
> **Nota**: Los valores actuales usan prefijo `SAMPLE_` en los UUIDs. Deben reemplazarse con UUIDs reales extraídos de la DB OpenMRS (Query 2 en `fases_implementacion.md`).

---

## 5. catalogs/laboratorios.csv — Catálogo de exámenes externos con booleanos + resultado

Extraído de OpenMRS (concept class Test/LabSet) + columnas booleanas de categoría + **columnas de resultado** que alimentan `LabResultGenerator` (el resultado se registra como obs ligada a la orden).

```csv
ciel_uuid,nombre_es,aplica_respiratorio,...,aplica_trauma,datatype,res_min,res_max,res_min_anormal,res_max_anormal,res_normal_uuid,res_anormal_uuid,res_trigger,res_trigger_dx
160912AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA,Glucemia en ayunas,false,...,false,numeric,70,99,126,260,,,diabetes|endocrino,
58b969e7-77ef-4941-a0ec-72372a2fa716,Antígeno NS1 dengue,false,...,false,coded,,,,,664AAAA...(Negativo),703AAAA...(Positivo),,142592AAAA...|61304dd2...(dengue)
1019AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA,Hemograma completo,true,...,true,panel,,,,,,,,
```

| Columna | Descripción |
|---------|-------------|
| `ciel_uuid` | UUID del concept — se usa en `POST /order` tipo testorder |
| `nombre_es` | Nombre del examen en español (documental: no lo lee la simulación, pero identifica la fila en los errores de `CatalogValidator`) |
| `aplica_CATEGORIA` | `true`/`false` — si este lab es coherente para esa categoría diagnóstica |
| `datatype` | `numeric` \| `coded` \| `panel` \| `imagen` (vacío = sin resultado; solo numeric/coded generan valor) |
| `res_min` / `res_max` | Banda numérica **normal** (inclusive) |
| `res_min_anormal` / `res_max_anormal` | Banda numérica **anormal** (cuando la enfermedad dispara el examen) |
| `res_normal_uuid` / `res_anormal_uuid` | UUID de la respuesta normal/anormal para tests **codificados** (p.ej. Negativo/Positivo) |
| `res_trigger` | Categorías (`\|`-separadas) que hacen anormal el resultado — típico de numéricos (p.ej. `diabetes\|endocrino`) |
| `res_trigger_dx` | UUIDs de diagnósticos específicos que disparan el anormal — típico de codificados disease-specific (p.ej. dengue → NS1 Positivo) |
| `se_realiza_en_clinica` | `true` = la clínica toma y procesa el examen (resultado **el mismo día**, dentro de la visita) · `false` = se **refiere a un laboratorio externo** y el resultado vuelve días después. Columna ausente = `true` (retrocompatible) |
| `dias_entrega_min` / `dias_entrega_max` | Días hasta que el resultado está disponible (banda inclusiva; `0` = mismo día). Interno `0-0`; externo p.ej. `2-5` |

Con disparo presente, el resultado es anormal con prob. `LabResultGenerator.ProbAnormalSiTrigger` (0.80); si no, normal. Las **imágenes** quedan solo como orden; los **paneles** (`datatype=panel`) buscan sus componentes en `paneles.csv` (§5b) — si el panel no tiene filas ahí, la orden queda sola.

**Dónde se procesa cada examen** decide todo el ciclo posterior (ver §5c). Clasificación actual: **en la clínica** hemograma, glucemia, orina, creatinina, BUN, AST, ALT, bilirrubina, ácido úrico, PCR y las pruebas rápidas (NS1 dengue, embarazo, VDRL); **externos** perfil lipídico, HbA1c, TSH, T4, potasio, amilasa, urocultivo, VIH ELISA y las 6 imágenes (la clínica no tiene radiología). Mover la línea = editar la columna, nada más.

---

## 5b. catalogs/paneles.csv — Componentes de paneles de laboratorio

**Opcional.** Define los componentes de los labs con `datatype=panel` en `laboratorios.csv`. El resultado del panel se registra como **obs-group**: una obs padre (concepto del panel, ligada a la orden) + una obs hija por componente (`groupMembers`).

```csv
panel_uuid,componente_uuid,nombre,res_min,res_max,res_min_anormal,res_max_anormal,res_trigger
1019AAAA...(hemograma),21AAAA...(Hb),Hemoglobina,12.0,16.5,7.5,10.9,digestivo
1019AAAA...(hemograma),729AAAA...(plaquetas),Plaquetas,150,450,60,140,infeccioso
```

| Columna | Descripción |
|---------|-------------|
| `panel_uuid` | UUID del concepto del panel — debe coincidir con una fila `datatype=panel` de `laboratorios.csv` |
| `componente_uuid` | UUID del concepto numérico del componente (Hb `21…`, Hto `1015…`, leucocitos `678…`, plaquetas `729…`) |
| `nombre` | Nombre legible (documental) |
| `res_min` / `res_max` | Banda numérica **normal** del componente |
| `res_min_anormal` / `res_max_anormal` | Banda **anormal** cuando se dispara el trigger |
| `res_trigger` | Categorías (`\|`-separadas) que disparan la banda anormal de **este** componente |

Cada componente sortea su banda **de forma independiente** (`LabResultGenerator.GenerarComponentes`, misma prob. 0.80): un paciente infeccioso (dengue) sale con plaquetas bajas y leucocitos alterados pero hemoglobina normal; uno digestivo (sangrado) con anemia. Misma regla de precisión que los numéricos simples (límites enteros → valor entero). ⚠️ datatype=panel **solo vale si el concepto es de verdad un LabSet con componentes**: el perfil lipídico apuntaba a *colesterol total* (un solo analito) y orina/urocultivo/VIH tampoco eran paneles — los cuatro se ordenaban y **nunca registraban resultado**. Corregido: hoy los paneles son el **hemograma** 1019… y el **perfil lipídico** 1010… (colesterol total, HDL, LDL, triglicéridos, VLDL); los otros tres pasaron a coded. Un panel sin filas aquí = orden sola, y el validador lo avisa.

---

## 5c. catalogs/personal_laboratorio.csv — Quién trabaja en el laboratorio

**Opcional.** Sin este catálogo, el resultado lo firma el médico de la consulta (comportamiento histórico). Con él, el laboratorio es un servicio con gente propia: el **técnico** toma la muestra y registra el resultado, y el **responsable** lo valida (es quien cierra la orden en `COMPLETED`).

```csv
identifier,nombre,genero,rol
SIM-LAB-01,Ana Beatriz Portillo,F,tecnico
SIM-LAB-02,Mario Alberto Cruz,M,tecnico
SIM-LAB-03,Silvia Regina Menjivar,F,responsable
```

| Columna | Descripción |
|---------|-------------|
| `identifier` | Identificador del provider. Prefijo `SIM-LAB-*` — son **datos de referencia**: se crean una vez (idempotente), se reutilizan entre corridas y el subcomando `clear` **no** los anula (igual que los médicos `SIM-MED-*`) |
| `nombre` | Nombre completo (se parte en nombre + apellidos al crear la `person`) |
| `genero` | `M` \| `F` |
| `rol` | `tecnico` (toma la muestra y registra el resultado) \| `responsable` (lo valida). Sin responsable, valida el propio técnico |

**Fail-fast**: si alguien del catálogo no se puede crear como provider, la corrida **aborta antes de sembrar** — nunca quedan encuentros de laboratorio firmados por un provider inexistente.

### El ciclo de vida de la orden

Esto es lo que la app de laboratorio de O3 muestra en su cola, y sale de `Order.fulfillerStatus`:

| Paso | `fulfillerStatus` | Qué pasa |
|------|-------------------|----------|
| El médico pide el examen | *(sin estado)* | La orden lleva `accessionNumber` (nº de muestra) y `commentToFulfiller` (procesar aquí / referir fuera) |
| El laboratorio la recoge | `IN_PROGRESS` | "Muestra tomada por \<técnico\>" (en una **imagen** no hay muestra: "Paciente referido al centro de imágenes") |
| Sale el resultado | `COMPLETED` | Las obs cuelgan de un **encuentro propio** (tipo *Lab Results*, ubicación *Laboratorio*, firmado por el técnico), validado por el responsable |
| La muestra no sirve | `DECLINED` | Motivo: hemolizada / insuficiente / mal identificada / el paciente no acudió |

**Interno** (`se_realiza_en_clinica=true`): el resultado sale el mismo día y su encuentro va **dentro de la visita**.
**Externo**: el valor se genera con el contexto clínico de la visita que lo ordenó, pero se registra **el día que llega** (`dias_entrega_*`), en un encuentro **sin visita** — el paciente no está delante: la muestra se procesa fuera. ⚠️ Esto ya **no** depende de que el paciente vuelva a consulta (antes, un resultado diferido solo se posteaba si había otra visita).

QA en BD: `querys/laboratorio.sql`.

---

## 6. catalogs/examenes_clinicos.csv — Exámenes realizados en consultorio

Diferencia fundamental con `laboratorios.csv`: estos exámenes los realiza el médico en el consultorio y el resultado se registra **inmediatamente** como observación (`POST /obs`). No se genera una orden.

```csv
ciel_uuid,nombre_es,tipo_resultado,aplica_respiratorio,...,aplica_trauma,res_min,res_max
160347AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA,Escala de Glasgow para coma,numerico,false,...,true,12,15
159434AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA,Flujo pico espiratorio,numerico,true,...,false,200,550
160622AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA,Reflejo plantar,categorico,false,...,false,,
```

| Columna | Descripción |
|---------|-------------|
| `ciel_uuid` | UUID del concept OpenMRS — se usa en `POST /obs` dentro del encounter de consulta |
| `nombre_es` | Nombre del examen en español (documental: identifica la fila en los errores de `CatalogValidator`) |
| `tipo_resultado` | `numerico` (registra un valor de la banda) o `categorico` (normal / anormal, answers genéricos `1115`/`1116`) |
| `res_min` / `res_max` | Banda del valor numérico — **obligatoria** si `tipo_resultado=numerico` (la exige `CatalogValidator` al arranque); entero si los límites son enteros (Glasgow, escala de dolor), 1 decimal si no. Vacías si `categorico`. |
| `aplica_CATEGORIA` | `true`/`false` — si este examen es coherente para la categoría diagnóstica |

> Si el diagnóstico tiene `requiere_examen_clinico = true`, la probabilidad sube al 90% independientemente del valor de `ClinicalExam`.

---

## 7. catalogs/alergenos.csv — Catálogo de alérgenos

Las alergias se registran **al crear el paciente nuevo** (no en cada visita) vía `POST /patient/{uuid}/allergy`.

```csv
concept_uuid,nombre_es,tipo_alergeno,severidad_tipica
162171AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA,Penicilina,DRUG,moderada
162186AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA,Mariscos,FOOD,grave
162188AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA,Látex,ENVIRONMENT,moderada
162174AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA,Ácaros del polvo,ENVIRONMENT,leve
```

| Columna | Descripción |
|---------|-------------|
| `concept_uuid` | UUID del concept OpenMRS del alérgeno |
| `nombre_es` | Nombre del alérgeno en español |
| `tipo_alergeno` | `DRUG`, `FOOD`, `ENVIRONMENT` — valores requeridos por la API de OpenMRS |
| `severidad_tipica` | `leve`, `moderada`, `grave` — se usa como valor por defecto, con variación aleatoria |

**Flujo de alergias en el pipeline**:
1. Al iniciar la corrida se sortea su prevalencia en `[Allergy.BaseProbabilityMin, BaseProbabilityMax]` (def. 15–25%).
2. Al crear paciente nuevo: si `rand < prevalencia_de_la_corrida` → es alérgico.
3. Nº de alergias por decaída condicional: 1 fija; +1 con `SecondAllergyProbability`; +1 más (si ya hay 2) con `ThirdAllergyProbability`; acotado por `MaxAllergies` y el tamaño del catálogo. La mayoría tiene 1, pocos 2, raros 3.
4. Elegir esa cantidad de alérgenos al azar **sin repetir** del catálogo.
5. Para cada alérgeno: `POST /patient/{uuid}/allergy` con `allergenType`, `codedAllergen` (UUID plano), `severity.uuid`.

---

## 8. catalogs/motivos_consulta.csv — Frases de motivo de consulta

Creado manualmente. Frases en español por categoría diagnóstica.

```csv
categoria,texto
respiratorio,Tos con flema desde hace varios días
cardiovascular,Dolor en el pecho al caminar
diabetes,Control de glucemia y revisión de medicación
digestivo,Dolor abdominal fuerte después de comer
```

| Columna | Descripción |
|---------|-------------|
| `categoria` | Categoría diagnóstica — se filtra para elegir una frase coherente con el dx elegido |
| `texto` | Frase de texto libre en español que se registra en el encuentro ADULTINITIAL |

---

## 9. catalogs/consultorios.csv — Consultorios y su médico

**Opcional.** Define los consultorios entre los que rotan las visitas; cada uno con su médico, que se
crea de forma **idempotente** en OpenMRS al iniciar la corrida (se busca por `medico_identifier`; si
no existe se crea con `medico_nombre`). Si el archivo falta, el seeder cae al
`Defaults.LocationUuid`/`ProviderUuid` (un solo recurso).

```csv
location_uuid,medico_identifier,medico_nombre,medico_genero
c1000000-0000-0000-0000-000000000011,SIM-MED-C1,Carlos Méndez,M
c1000000-0000-0000-0000-000000000012,SIM-MED-C2,Ana Rivas,F
```

| Columna | Descripción |
|---------|-------------|
| `location_uuid` | UUID de la ubicación (Visit Location) del consultorio — `GET /location`. |
| `medico_identifier` | Identificador único del proveedor/médico (idempotencia). Prefijo `SIM-` recomendado. |
| `medico_nombre` | Nombre del médico a crear si no existe (ej. "Carlos Méndez"). |
| `medico_genero` | Género (`M` o `F`) con que se crea la persona en OpenMRS. Vacío = `M`. |

> Los médicos son **datos de referencia** (personal): se reutilizan entre corridas y el subcomando
> `clear` **no** los elimina. El registro del paciente va a `Defaults.RegistrationLocationUuid`
> (Recepción), no a un consultorio. Los recurrentes vuelven a su médico de cabecera según
> `MedicoCabeceraProbMin/Max`.

> **Fail-fast:** si un médico del catálogo no se puede crear ni encontrar al iniciar la corrida, el
> seeder **aborta** con un error claro (exit code 1, listando los identificadores en el resumen final)
> **antes** de crear datos — así no quedan encuentros firmados por "Unknown Provider".

---

## 10. catalogs/nombres.csv y catalogs/apellidos.csv — Nombres realistas de pacientes

**Opcionales pero recomendados.** Alimentan el nombre completo del paciente (primer + segundo nombre y
primer + segundo apellido), lo que evita el cuello de botella del locale de Bogus (`"es"` daba solo ~24
nombres de pila → miles de pacientes con nombre repetido). Si faltan, el generador cae al Bogus previo
(un solo nombre y un apellido).

```csv
nombre,genero
José,M
María,F
```
```csv
apellido
García
López
```

| Archivo | Columna | Descripción |
|---------|---------|-------------|
| `nombres.csv` | `nombre` | Nombre de pila. |
| `nombres.csv` | `genero` | `M` o `F`. El generador elige 2 nombres distintos del pool del género del paciente. |
| `apellidos.csv` | `apellido` | Apellido. Se eligen 2 distintos (primer y segundo apellido). |

> Los campos se envían a OpenMRS como `givenName` / `middleName` / `familyName` / `familyName2`.
> Con ~150 nombres/género y ~200 apellidos el espacio de combinaciones supera el millón → colisiones
> de nombre completo prácticamente nulas para una corrida de un año.

---

## 10b. catalogs/direcciones.csv — Direcciones salvadoreñas coherentes

**Opcional.** Una fila por zona residencial (colonia/barrio/cantón). El generador elige por `peso`
— la mayoría de pacientes vive cerca de la clínica (área metropolitana de San Salvador) y hay una
cola de municipios lejanos, como la captación real de una consulta externa.

| Columna | Descripción |
|---------|-------------|
| `departamento` | Departamento (→ `state_province` en OpenMRS). |
| `municipio` | Municipio (→ `city_village`). |
| `zona` | Colonia/Barrio/Cantón, tal cual encabeza el `address1` (p.ej. "Colonia Zacamil"). |
| `peso` | Peso relativo de la zona (mayor = más pacientes de allí). |

El `address1` final añade detalle urbano ("Colonia Zacamil, pasaje C, casa #8"); los **cantones**
(rurales) van sin numeración. `country` = "El Salvador". **Archivo ausente/vacío = fallback Bogus**
(calles genéricas y país "España", comportamiento histórico).

**Criterio de pesos — anillos de distancia (la clínica está en el municipio de San Salvador):**

| Anillo | Zonas | Cuota objetivo | Pesos |
|:------:|-------|:--------------:|-------|
| 0 | San Salvador municipio | ~45 % | 8–20 |
| 1 | Colindantes (Mejicanos, Soyapango, Ciudad Delgado, Cuscatancingo, Ayutuxtepeque, San Marcos) | ~28 % | 5–18 |
| 2 | Resto del AMSS + metro La Libertad (Apopa, Ilopango, Santa Tecla, Antiguo Cuscatlán…) | ~18 % | 2–6 |
| 3 | Interior del país (viaje de 1–3 h — caso ocasional) | ~9 % | 1 (2 en cabeceras con hospital de referencia: Santa Ana, San Miguel, Cojutepeque, Zacatecoluca) |

Si el escenario "muda" la clínica a otra ciudad, basta reponderar el CSV con este mismo criterio
(mayor peso = más cerca de la clínica); no hay que tocar código.

---

## 10c. Atributos de persona — teléfono y estado civil

No son un catálogo: se generan por código (`PatientProfileGenerator`, con el `Random` sembrado, así
que son reproducibles) y viajan **anidados** como `attributes:[{attributeType,value}]` dentro del
`person` del `POST /patient`. Se activan solo si el UUID del attribute type está configurado en
`OpenMRS.Defaults` (vacío = el paciente se crea sin ese atributo, mismo patrón que
`AppointmentServiceUuid`).

| Atributo | Formato | Cómo se genera |
|----------|---------|----------------|
| Teléfono (`TelephoneAttributeTypeUuid`) | String | Número salvadoreño sintético: móvil `7###-####` (~80 %) o fijo `2###-####` (~20 %). En menores de edad representa el contacto del tutor. Seam puro `GenerarTelefono`. |
| Estado civil (`CivilStatusAttributeTypeUuid`) | **Concept** → el valor es el UUID de una *answer* del concepto `1054` | Coherente con la edad (seam puro `GenerarEstadoCivil(edad, rng)`): <18 siempre **soltero**; conforme sube la edad crece **casado/acompañado**; la **viudez** solo se vuelve visible en 65+. |

> ⚠️ Para listar las answers de `1054` hay que pedir el concepto **por UUID con `v=full`**;
> `GET /concept?q=…` con representación personalizada de `answers` devuelve NPE en esta instancia.

---

## 10d. catalogs/comorbilidad_afinidades.csv — Clusters de comorbilidad

**Opcional.** Por cada categoría, las categorías clínicamente afines que reciben el `AffinityBoost`
al elegir una comorbilidad. Así un diabético tiende a presentar enfermedad cardiovascular/endocrina.
Si el archivo falta, las comorbilidades se eligen sin sesgo de afinidad.

```csv
categoria,afines
diabetes,cardiovascular|endocrino|neurologico
respiratorio,infeccioso
```

| Columna | Descripción |
|---------|-------------|
| `categoria` | Categoría origen (una de las 13). |
| `afines` | Categorías afines **separadas por `\|`** (no por coma, que es el delimitador CSV). |

---

## 11. Coherencia entre archivos — Cómo se conectan

```
appsettings.json
  └── Simulation.DemographicProfile  ──► edad/género del paciente generado (PatientProfileGenerator)
  └── Simulation.PacientesPorDiaMedio ──► cuántos pacientes por día (DailyScheduleGenerator × WeekdayWeight × Normal)
  └── Simulation.ReferralProbabilities ─► probabilidades base de cada acción clínica

AL CREAR PACIENTE NUEVO:
alergenos.csv
  └── rand < prevalencia corrida (0.15-0.25) ──► nº por decaída condicional (≥1) → POST /patient/{uuid}/allergy

POR CADA VISITA:

PASO 1 — elegir categoría:
epidemiology-profile.csv
  └── filtrar por [grupo_edad + genero] ─► filas candidatas
  └── normalizar [peso] ─────────────────► elegir categoria (ej: "cardiovascular")

PASO 2 — elegir diagnóstico:
diagnosticos.csv
  └── filtrar por [categoria = elegida] ─► filas del sistema orgánico
  └── filtrar por [aplica_GRUPO = true] ─► solo dx válidos para la edad
  └── normalizar [peso_M] o [peso_F] ────► elegir diagnóstico específico
  └── [requiere_lab = true] ─────────────► aumentar prob. de lab externo
  └── [requiere_rx = true] ───────────────► aumentar prob. de prescripción
  └── [requiere_examen_clinico = true] ──► aumentar prob. de examen en consultorio (a 90%)
  └── [severidad = "grave"] ─────────────► aumentar prob. de lab urgente (a 50%)

PASO 3 — vitales (siempre):
VitalsSeeder ajusta rangos según categoría del diagnóstico elegido

PASO 4 — examen en consultorio (si aplica):
examenes_clinicos.csv
  └── rand < ClinicalExam (0.35) o requiere_examen_clinico
        → filtrar por [aplica_CATEGORIA = true]
        → elegir 1 al azar → POST /obs en encounter ADULTINITIAL

PASO 5 — laboratorio (si aplica):
laboratorios.csv
  └── rand < LabOrder (0.40) o requiere_lab
        → filtrar por [aplica_CATEGORIA = true]
        → elegir 1-2 al azar → POST /order testorder (+ accessionNumber, + instrucción al laboratorio)
        → ciclo de la orden (LabWorkflowSeeder → fulfillerStatus):
            rand < ProbRechazo (0.04) → DECLINED con motivo, sin resultado
            si no → IN_PROGRESS ("muestra tomada por <técnico>")
                 → rand > ProbResultadoLlega (0.95) → el resultado se pierde, queda IN_PROGRESS
                 → se_realiza_en_clinica → encuentro "Lab Results" DENTRO de la visita (mismo día)
                                            + POST /obs ligada a la orden → COMPLETED
                 → externo → el valor espera a dias_entrega_min..max y lo registra el BARRIDO DIARIO,
                             en un encuentro "Lab Results" SIN visita → COMPLETED

PASO 6 — prescripción (si aplica):
medicamentos.csv
  └── rand < DrugOrder (0.65) o requiere_rx
        → filtrar por [aplica_CATEGORIA = true]
        → elegir 1-3 al azar → POST /order drugorder

PASO 7 — motivo de consulta (siempre):
motivos_consulta.csv
  └── filtrar por [categoria = categoria del dx] → elegir 1 al azar → texto libre en ADULTINITIAL
```

---

## 12. Vitales coherentes con diagnóstico

Los signos vitales se derivan (`VitalsSeeder.ComputeVitals`) de la **unión de categorías** de
**todos** los diagnósticos del paciente (primario + comorbilidades), la **peor severidad**, y
los overrides opcionales por enfermedad (`vital_fiebre`, `vital_imc`, `vital_pa`, `vital_fc`,
`vital_spo2`). El override gana sobre la categoría.

| Condición | Ajuste en vitales |
|-----------|-------------------|
| `cardiovascular` o `vital_pa=alta` | PA 140-180 / 90-110 mmHg; pulso 80-110 (solo categoría) |
| `infeccioso`, `respiratorio` (≥moderado) o `vital_fiebre=true` | Temperatura 37.5-39.5°C (hasta 40 si grave); pulso 90-120; FR elevada |
| `respiratorio` grave / moderado | SpO2 88-93 / 92-96% |
| `vital_fc=alta` (hipertiroidismo, anemia, hipovolemia) | Pulso 100-130 — gana sobre fiebre/categoría |
| `vital_fc=baja` (hipotiroidismo, bloqueos) | Pulso 42-58 — gana sobre fiebre/categoría |
| `vital_spo2=baja` (insuf. cardíaca, TEP) | SpO2 88-94% aunque no sea respiratorio |
| `diabetes`, `endocrino` o `vital_imc=alto` | IMC objetivo 27-38 (sobrepeso/obesidad) |
| `vital_imc=bajo` (TB, cáncer, hipertiroidismo, VIH…) | IMC objetivo 16-19 (bajo peso) |
| Resto | IMC 18.5-27; temperatura/pulso/FR/SpO2 normales con variación |

> **Peso acoplado a la talla:** el peso ya **no** es un rango independiente. Se elige un IMC
> objetivo (según la tabla) y se calcula `peso = IMC × (talla/100)²`, de modo que peso y talla
> siempre son coherentes (no más IMC de 50 en pacientes de consulta externa).

Rangos base de talla (de ahí sale el peso vía IMC):

| Signo vital | Rango | Unidad |
|---|---|---|
| Talla (hombre) | 160-185 | cm |
| Talla (mujer) | 150-172 | cm |
| Talla (0-14) | 90-160 | cm |
| Temperatura (afebril) | 36.0-37.4 | °C |
| Pulso (basal) | 60-100 | lpm |
| Frecuencia respiratoria (basal) | 12-20 | rpm |
| SpO2 (basal) | 95-99 | % |

---

## 13. Idempotencia y limpieza

Todos los registros creados por el simulador son identificables:
- **Pacientes**: identificador con prefijo `SIM-` (ej: `SIM-A3F8C201`)
- **Visitas/Encounters**: campo `description` contiene `SEEDED_BY_SIMULATOR`

Esto permite:
- `dotnet run -- clear` (pide confirmación) → busca pacientes `SIM-*` → void lógico en cascada (visitas, encounters, obs, orders)
- Re-ejecuciones seguras: pacientes `SIM-` existentes se usan como "recurrentes"

---

## 14. Crecimiento de la clínica — difusión de Bass

La clínica **crece**: los pacientes que quedan contentos vuelven y **traen gente nueva**. La captación de
pacientes nuevos por día la gobierna el modelo de **difusión de Bass** (1969), el estándar de la
literatura para adopción por boca a boca:

> ⚠️ **Bass gobierna LAS ALTAS y nada más.** Los pacientes que vuelven no salen de aquí: salen del
> **panel** (quién tiene cita hoy, quién vuelve por su cuenta — §14b). El volumen del día es una **suma**.

```
λ(d) = [ p + q · S(d)/M ] · ( M − A(d) )          ALTAS (pacientes nuevos) al día

altas(d)    = Poisson( λ(d) × peso del día de la semana )
retornos(d) = citas de control de hoy (a las que se acude)
            + pacientes activos que vuelven por su cuenta        ← la demanda del PANEL
visitas(d)  = altas(d) + retornos(d)                             ← una SUMA, no un cociente

si visitas(d) > aforo(d):  se recortan LAS ALTAS
   (una consulta llena deja de captar gente nueva; jamás le da plantón a quien tenía cita)
```

| Símbolo | Qué es | De dónde sale |
|---------|--------|---------------|
| `M` | Población del área de influencia de la clínica | `Crecimiento.PoblacionCaptacion` |
| `A(d)` | **Clientela actual**: pacientes que han venido dentro de `VentanaActividadDias` | Se mide del pool |
| `S(d)` | Recurrentes **activos y satisfechos** — los que hablan bien de la clínica | Se mide del pool |
| `p` | Coeficiente de **innovación** (llegan solos) | **Derivado** de `PacientesPorDiaMedio` |
| `q` | Coeficiente de **imitación** (boca a boca) | **Derivado** de `PacientesPorDiaObjetivo` |
| `aforo(d)` | Lo que cabe hoy en la consulta | `PacientesPorDiaObjetivo` × peso del día / peso medio |

### ⚠️ El error que había aquí, y lo que costó

El volumen del día se derivaba de las altas —`total = altas / (1 − PorcentajeRecurrentes/100)`— y los
recurrentes eran el **residuo**: un cupo fijo del 30 %. La demanda real del panel no entraba en la
ecuación. Como la clínica agenda control en ~2 de cada 3 visitas, llegaban ~20 citas al día a competir por
14 huecos, y el sobrante **vencía sin que nadie lo atendiera**. Medido en una corrida de 3,5 años:

- **14.025 citas `Missed`** contra 14.115 `Completed` — media agenda a la basura.
- El **65 % de los crónicos** (7.686 personas con HTA, DM2, VIH, EPOC) **no volvió jamás a un control**.
- El **79 % de los pacientes** vino una sola vez. Media: **1,45 visitas por paciente**.
- El mix de recurrentes, **clavado en el 31 %** el primer mes y el último.

`PorcentajeRecurrentes` **ya no existe**. La fracción de recurrentes **emerge** del panel y crece con él
(4 % el primer mes → ~60 % en el tercer año). Lo vigilan las **leyes de la simulación**
(`leyes_simulacion.md`), que se ejecutan en cada corrida y devuelven **exit code 3** si se rompen.

## Los tres mandos de la curva

**Tú no tocas `p` ni `q`.** Son coeficientes con los que nadie puede apuntar a ojo. Lo que configuras son
los tres números que sí significan algo:

| Mando | Parámetro | Qué hace |
|-------|-----------|----------|
| **Dónde arranca** | `PacientesPorDiaMedio` | **Altas**/día del primer día (el pool está vacío: no hay a quién controlar). De aquí sale `p`. |
| **Dónde acaba** | `Crecimiento.PacientesPorDiaObjetivo` | **Visitas totales**/día (altas + controles) en las que se estabiliza. Es **también el aforo** de la consulta. De aquí sale `q`. |
| **Cuánto tarda** | `Crecimiento.VentanaActividadDias` | Cuánto tiempo un paciente sigue siendo cliente (y recomendando la clínica). Estira o comprime la rampa. |

Con los valores por defecto (arranca en 6 altas/día, apunta a 25 visitas/día, ventana de 365 días) la
curva sale así — y fíjate en la **columna del mix**, que es la que dice si el panel está madurando:

```
              visitas/día              % recurrentes
  ene 2023   6,2   ██████                   4 %     ← recién abierta: casi todo son altas
  jun 2023  13,0   █████████████           47 %
  dic 2023  17,9   ██████████████████      56 %
  jun 2024  21,5   █████████████████████   57 %
  dic 2024  23,8   ███████████████████████ 59 %
  dic 2025  25,2   █████████████████████████ 60 %   ← meseta: la clínica vive de su panel
```

> **El suelo NO es `PacientesPorDiaMedio`.** Cada paciente captado vuelve ~3,3 veces, así que 6 altas/día
> ya producen ~14 visitas/día **sin nada de boca a boca**. Si pones un objetivo por debajo de ese suelo, la
> clínica no necesita crecer y el simulador te lo dice en la etapa 2/5.

Arranque lento (el primer trimestre apenas se mueve: aún no hay a quién oírle hablar bien de la clínica),
rampa sostenida durante los años 1 y 2, y estabilidad al final.

> ⚠️ **Por qué `q` no es un mando.** Antes lo era (`RecurrentesPorPacienteExtra`) y era **inservible**. El
> crecimiento es un lazo de realimentación positiva cuyo punto fijo vale `1/(1 − ganancia)`, y eso
> **explota** cuando la ganancia se acerca a 1. Medido, arrancando en 6 pacientes/día:
>
> | Boca a boca | Resultado a 3 años |
> |---|---|
> | 1 por cada 25 satisfechos | crece a 9,6/día y pasa **dos años en una línea plana** |
> | 1 por cada 12 | crece a 25/día — la curva que se busca |
> | 1 por cada 8 | **se estrella contra el techo duro en 12 meses** |
>
> Un factor 3 en el pomo separa "no crece" de "se dispara". Por eso ahora se apunta al **destino** y el
> simulador calcula el boca a boca que lo produce (por bisección, al arrancar). Lo verás en el informe de
> la etapa 2/5: *"boca a boca derivado del objetivo: 1 paciente nuevo por cada 12,6 recurrentes
> satisfechos"*.

### Los frenos (guardarraíles, no mecanismo principal)

**`(M − A(d))` — el techo de mercado.** El barrio tiene población finita: cuanta más gente ya sea paciente
de la clínica, menos queda por captar. ⚠️ **`A(d)` es la clientela ACTIVA, no el histórico**: quien lleva
más de `VentanaActividadDias` sin pisar la clínica ha vuelto al mercado. Es lo que permite que el sistema
alcance un **equilibrio** (altas = bajas) en vez de agotar el área y quedarse sin pacientes.

> A volúmenes de clínica real este freno **apenas actúa** (la clientela ronda el 5-10 % del área). Quien
> fija la meseta es el objetivo. `PoblacionCaptacion` y `PacientesPorDiaMax` están para que una
> configuración disparatada no reviente, no para gobernar la curva.

### Referencia de parámetros — sección `Crecimiento`

| Parámetro | Descripción |
|-----------|-------------|
| `Enabled` | `false` = el volumen diario vuelve a ser fijo (`PacientesPorDiaMedio`), como antes de esta feature. |
| **`PacientesPorDiaObjetivo`** | **Dónde se estabiliza la clínica.** De aquí se deriva el boca a boca. ≤ `PacientesPorDiaMedio` = no crece. |
| **`VentanaActividadDias`** | **Cuánto tarda la rampa.** Días que un paciente sigue contando como clientela activa (y recomendando). 90 → meseta en 1 año; 365 (def.) → crecimiento repartido por 3 años. |
| `PoblacionCaptacion` | `M`: habitantes del área. Debe ir **holgado**: si la clínica acaba registrando más gente de la que vive en el barrio, el área se agota y la curva se desinfla (el informe avisa). |
| `PacientesPorDiaMax` | Tope absoluto de pacientes en un día (red de seguridad). Debe ser ≥ `PacientesPorDiaObjetivo`. |
| `RecurrentesPorPacienteExtra` | **Override avanzado**: fija el boca a boca a mano en vez de derivarlo. **0 (def.) = derivar del objetivo**, que es lo que quieres casi siempre. |
| `MinVisitasRecurrente` | Visitas mínimas para contar como "recurrente" (2 = ya volvió una vez). |
| `AsistenciaProbInsatisfecho` | Prob. de que un paciente descontento acuda a su cita (0,20 frente al 0,75 de los contentos). |

---

## 15. Satisfacción del paciente — la nota de cada visita

Cada visita recibe una **calificación entera de 1 a 5**. **No es un dato clínico y NO se escribe en
OpenMRS**: es estado de simulación que decide si el paciente seguirá viniendo y si recomendará la
clínica (el `S(d)` de la fórmula de arriba). Se vuelca en los CSV de salida (§16).

```
nota = MediaBase
     + BonusMedicoCabecera      si le atendió SU médico de siempre  (continuidad asistencial)
     + BonusCitaCumplida        si vino a una cita agendada         (le esperaban)
     − PenalizacionCuadroGrave  si alguno de sus dx es grave
     − PenalizacionSaturacion × saturacion(d)
     + ruido N(0, Desviacion)

saturacion(d) = max(0, (pacientes del día − CapacidadComodaPorDia) / CapacidadComodaPorDia)
calificación  = redondear(nota), acotada a [1, 5]
```

**La penalización por saturación es el segundo freno del sistema, y es el que lo convierte en una
simulación y no en un contador:** crecer llena la consulta → la consulta llena atiende peor → peores
notas → menos satisfechos → menos boca a boca. Si la clínica no da abasto, **el crecimiento se estanca
antes de agotar el barrio**.

**El paciente descontento** (promedio ≤ `UmbralSatisfaccion`) deja de contar para el boca a boca, apenas
acude a sus citas (`Crecimiento.AsistenciaProbInsatisfecho`) — que acaban en `Missed` — y no vuelve por su
cuenta. Es el *churn*, y por eso una corrida con esta feature produce **más citas perdidas** que una sin
ella: es lo esperado, no un fallo.

### Referencia de parámetros — sección `Satisfaccion`

| Parámetro | Descripción |
|-----------|-------------|
| `Enabled` | `false` = no se califica ninguna visita → nadie se va descontento y no hay boca a boca. |
| `MediaBase` | Nota de una visita neutra, en la escala 1-5 (def. 4,0). |
| `Desviacion` | Dispersión de las notas entre pacientes (σ del ruido normal). 0 = todos puntúan igual. |
| `BonusMedicoCabecera` | Cuánto suma que le atienda su médico de siempre. |
| `BonusCitaCumplida` | Cuánto suma venir a una cita agendada en vez de llegar de improviso. |
| `PenalizacionCuadroGrave` | Cuánto resta consultar por algo serio. |
| `PenalizacionSaturacion` | Cuánto resta una saturación de 1,0 (el doble de pacientes que la capacidad cómoda). |
| `CapacidadComodaPorDia` | Pacientes que la clínica atiende sin que se note la espera. Por encima, las notas bajan. |
| `UmbralSatisfaccion` | Promedio **estricto** por encima del cual el paciente está contento (def. 3,0: empatar a 3 no basta). |

---

## 16. Archivos de salida — la evidencia de la corrida

Se escriben en `Salida.Carpeta` (def. `output`; si es relativa, cuelga de la carpeta del binario; vacía =
no se escribe nada). La ruta absoluta se imprime al final de la corrida. Es lo **único** que el simulador
escribe en disco.

**`output/crecimiento_diario.csv`** — una fila por día simulado. **Es la curva de Bass, lista para
graficar.** Se escribe día a día (se puede abrir a media corrida para ver cómo va), así que un Ctrl+C no
se lleva la evidencia.

```csv
fecha,activos_A,captados_total,satisfechos_activos_S,lambda_nuevos,media_efectiva,atendidos,nuevos,recurrentes,calificacion_media_dia,saturacion,pct_mercado
2023-03-15,398,412,26,11.30,16.14,15,10,5,4.20,0.000,1.99
```

| Columna | Qué es |
|---------|--------|
| `activos_A` | Clientela actual (el `A(d)` de la fórmula): pacientes que vinieron dentro de la ventana de actividad. |
| `captados_total` | Pacientes distintos que han pasado por la clínica alguna vez. |
| `satisfechos_activos_S` | El `S(d)`: los que sostienen el boca a boca. |
| `lambda_nuevos` | Pacientes nuevos esperados hoy (λ, antes del peso del día y del sorteo). |
| `media_efectiva` | Media diaria que implica ese λ. **Comparar su valor final con el inicial ES la medida del crecimiento.** |
| `atendidos` / `nuevos` / `recurrentes` | Lo que de verdad se sembró ese día. |
| `calificacion_media_dia` | Nota media de las visitas del día. **Debe bajar cuando `saturacion` sube** (si no, el freno está roto). |
| `saturacion` | Cuánto se pasó la clínica de su capacidad cómoda. |
| `pct_mercado` | Qué % del área es clientela activa. |

**`output/clientes_recurrentes.csv`** — instantánea del pool al cerrar la corrida, un paciente por fila:

```csv
identifier,uuid,visitas,calificaciones,promedio,satisfecho,activo,ultima_visita,proxima_cita,cronicas
SIM-000123,3f2a…,4,5|4|5|4,4.5,true,true,2024-08-03,2024-11-12,2
```

---

## Resumen: qué editar para cambiar el comportamiento

| Quiero cambiar... | Editar |
|-------------------|--------|
| Período de simulación | `appsettings.json` → `StartDate/EndDate` |
| **Dónde ARRANCA la clínica** (pac/día el primer día) | `appsettings.json` → `PacientesPorDiaMedio` |
| **Dónde ACABA** (pac/día en los que se estabiliza) | `appsettings.json` → `Crecimiento.PacientesPorDiaObjetivo` |
| **Cuánto TARDA en llegar** (duración de la rampa) | `appsettings.json` → `Crecimiento.VentanaActividadDias` (365 = repartido en 3 años; 90 = meseta en 1 año) |
| Que la clínica NO crezca (volumen fijo, como antes) | `appsettings.json` → `Crecimiento.Enabled: false` |
| Lo contentos que salen los pacientes | `appsettings.json` → `Satisfaccion.MediaBase` / `Desviacion` |
| A partir de cuántos pacientes/día se resiente la atención | `appsettings.json` → `Satisfaccion.CapacidadComodaPorDia` (ponlo ≈ el objetivo) |
| Cuántos pacientes abandonan la clínica descontentos | `appsettings.json` → `Satisfaccion.UmbralSatisfaccion` / `Crecimiento.AsistenciaProbInsatisfecho` |
| Dónde se escriben los CSV de la corrida | `appsettings.json` → `Salida.Carpeta` (vacío = no escribir) |
| Variación estadística diaria | Con crecimiento: es Poisson (varianza = λ). Sin él: `DailyScheduleGenerator.cs` → σ del Normal |
| Perfil pediátrico de la clínica | `appsettings.json` → `DemographicProfile.PediatricClinic` |
| Distribución etaria | `appsettings.json` → `DemographicProfile.AgeGroups` |
| Volumen por día de semana | `appsettings.json` → `WeekdayWeights` |
| Qué enfermedades predominan | `epidemiology-profile.csv` → `peso` por categoría |
| Qué enfermedades aparecen según edad | `diagnosticos.csv` → columnas `aplica_*` por diagnóstico |
| Peso de un dx entre hombres vs. mujeres | `diagnosticos.csv` → `peso_M` / `peso_F` |
| Qué medicamentos se prescriben | `medicamentos.csv` → `aplica_CATEGORIA` |
| Qué labs externos se piden | `laboratorios.csv` → `aplica_CATEGORIA` |
| Qué componentes trae un panel (hemograma) | `paneles.csv` → filas con su `panel_uuid` |
| Qué exámenes de consultorio aplican | `examenes_clinicos.csv` → `aplica_CATEGORIA` |
| Frases de motivo de consulta | `motivos_consulta.csv` → `texto` |
| % de visitas con labs externos | `appsettings.json` → `ReferralProbabilities.LabOrder` |
| % de visitas con examen en consultorio | `appsettings.json` → `ReferralProbabilities.ClinicalExam` |
| % de pacientes con alergias | `appsettings.json` → `Allergy.BaseProbabilityMin/Max` |
| Cuántas alergias por paciente alérgico | `appsettings.json` → `Allergy.SecondAllergyProbability` / `ThirdAllergyProbability` / `MaxAllergies` |
| Qué alérgenos pueden aparecer | `alergenos.csv` |
| Qué vital dispara una enfermedad concreta | `diagnosticos.csv` → `vital_fiebre` / `vital_imc` / `vital_pa` / `vital_fc` / `vital_spo2` |
| Qué exámenes hace la clínica y cuáles se mandan fuera (y cuánto tardan) | `catalogs/laboratorios.csv` → `se_realiza_en_clinica`, `dias_entrega_min/max` |
| % de muestras rechazadas / de resultados que se pierden | `appsettings.json` → `Laboratorio.ProbRechazo`, `Laboratorio.ProbResultadoLlega` |
| Quién toma la muestra y quién valida el resultado | `catalogs/personal_laboratorio.csv` |
| Que el paciente lleve teléfono / estado civil | `appsettings.json` → `Defaults.TelephoneAttributeTypeUuid` / `CivilStatusAttributeTypeUuid` (vacío = off) |
| UUIDs de OpenMRS (location, visita, encuentro) | `appsettings.json` → `OpenMRS.Defaults` |
| Reproducibilidad | `appsettings.json` → `RandomSeed` |
