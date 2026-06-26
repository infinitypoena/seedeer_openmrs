# Parametrización del Simulador Clínico OpenMRS

Este documento describe todos los archivos de configuración y catálogos del simulador, qué controla cada parámetro y cómo interactúan entre sí.

---

## 1. appsettings.json — Configuración central

Todo el comportamiento del simulador se controla desde aquí.

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
      "Password": "openmrs"
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
| `PacientesPorDiaMedio` | int | Promedio de pacientes por día hábil. Se aplica variación σ ≈ 20% con distribución normal (Box-Muller). |
| `PorcentajeRecurrentes` | int (0-100) | % de visitas de pacientes ya existentes (controles, crónicos). |
| `Locale` | string | Locale de Bogus. `"es"` = español latinoamericano. |
| `RandomSeed` | int | Semilla para reproducibilidad. Mismo seed = misma simulación. |
| `CommonProbMin` / `CommonProbMax` | float (0-1) | Factor inicial: cada corrida sortea su P(común) en `[min,max]` (def. 0.75–0.95) → el principal cae mayormente en el pool `comun=true`, variando entre corridas. |
| `MedicoCabeceraProbMin` / `MedicoCabeceraProbMax` | float (0-1) | Médico de cabecera: cada corrida sortea en `[min,max]` (def. 0.70–0.90) la prob. de que un recurrente vuelva con el mismo médico/consultorio de su primera visita; si no, cae con otro. Requiere `catalogs/consultorios.csv`. |
| `ClinicType` | string | Perfil del establecimiento: `ConsultaExterna`, `HospitalUrgencias`, `CentroComunitario`. Referencia semántica, no fuerza valores. |
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
| `ReferralProbabilities.FollowUp` | float (0-1) | Probabilidad de registrar una cita de control: obs fecha "Return visit date" (`5096`) 7–30 días después de la visita. |
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
| `LocationUuid` | Ubicación de **respaldo** si no hay `catalogs/consultorios.csv` | `GET /ws/rest/v1/location` |
| `RegistrationLocationUuid` | Ubicación de registro/admisión (Recepción) del identificador del paciente | `GET /ws/rest/v1/location` |
| `VisitTypeUuid` | UUID del tipo de visita "OPD Visit" (consulta externa; `287463d3-…`) | `GET /ws/rest/v1/visittype` |
| `VitalsEncounterTypeUuid` | UUID del tipo de encuentro "Vitals" | `GET /ws/rest/v1/encountertype` |
| `ConsultaEncounterTypeUuid` | UUID del tipo de encuentro "Consultation" | `GET /ws/rest/v1/encountertype` |
| `ProviderUuid` | Médico de **respaldo** si no hay `catalogs/consultorios.csv` | `GET /ws/rest/v1/provider` |

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

> **Fuente**: Query SQL sobre `concept` + `concept_name` en la DB OpenMRS. Las columnas `aplica_*`, `peso_*`, `requiere_*` y `vital_*` se agregan manualmente. Las columnas `vital_*` son **opcionales** (el loader tolera su ausencia → comportamiento neutro gobernado por la categoría). Ver queries en `fases_implementacion.md` Fase 2.

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

> **Nota**: Los valores actuales usan prefijo `SAMPLE_` en los UUIDs. Deben reemplazarse con UUIDs reales extraídos de la DB OpenMRS (Query 2 en `fases_implementacion.md`).

---

## 5. catalogs/laboratorios.csv — Catálogo de exámenes externos con booleanos

Extraído de OpenMRS (concept class Test/LabSet) + columnas booleanas.

```csv
ciel_uuid,nombre_es,clase,aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,aplica_digestivo,aplica_osteomuscular,aplica_urologico,aplica_infeccioso,aplica_endocrino
1019AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA,Hemograma completo,Test,true,true,true,true,true,true,true,false
887AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA,Glucemia en ayunas,Test,false,true,true,false,false,false,false,true
159799AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA,Hemoglobina glicosilada HbA1c,Test,false,false,true,false,false,false,false,true
```

| Columna | Descripción |
|---------|-------------|
| `ciel_uuid` | UUID del concept — se usa en `POST /order` tipo testorder |
| `nombre_es` | Nombre del examen en español |
| `clase` | `Test`, `LabSet`, `Lab Findings` |
| `aplica_CATEGORIA` | `true`/`false` — si este lab es coherente para esa categoría diagnóstica |

---

## 6. catalogs/examenes_clinicos.csv — Exámenes realizados en consultorio

Diferencia fundamental con `laboratorios.csv`: estos exámenes los realiza el médico en el consultorio y el resultado se registra **inmediatamente** como observación (`POST /obs`). No se genera una orden.

```csv
ciel_uuid,nombre_es,tipo_resultado,unidad,aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,...
887AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA,Glucometría capilar,numerico,mg/dL,false,true,true,...
5242AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA,Oximetría de pulso,numerico,%,true,true,false,...
159568AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA,Electrocardiograma,categorico,,false,true,false,...
```

| Columna | Descripción |
|---------|-------------|
| `ciel_uuid` | UUID del concept OpenMRS — se usa en `POST /obs` dentro del encounter ADULTINITIAL |
| `nombre_es` | Nombre del examen en español |
| `tipo_resultado` | `numerico` (registra valor + unidad) o `categorico` (normal / anormal) |
| `unidad` | Unidad de medida si es numérico (ej: `mg/dL`, `%`, `mmHg`). Vacío si categorico. |
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

> Los médicos son **datos de referencia** (personal): se reutilizan entre corridas y `DELETE
> /api/seed/clear` **no** los elimina. El registro del paciente va a `Defaults.RegistrationLocationUuid`
> (Recepción), no a un consultorio. Los recurrentes vuelven a su médico de cabecera según
> `MedicoCabeceraProbMin/Max`.

> **Fail-fast:** si un médico del catálogo no se puede crear ni encontrar al iniciar la corrida, el
> seeder **aborta** con un error claro (estado `error` en `GET /api/seed/progress/{runId}`, listando los
> identificadores) **antes** de crear datos — así no quedan encuentros firmados por "Unknown Provider".

---

## 10. catalogs/comorbilidad_afinidades.csv — Clusters de comorbilidad

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

PASO 5 — lab externo (si aplica):
laboratorios.csv
  └── rand < LabOrder (0.40) o requiere_lab
        → filtrar por [aplica_CATEGORIA = true]
        → elegir 1-2 al azar → POST /order testorder

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
los overrides opcionales por enfermedad (`vital_fiebre`, `vital_imc`). El override gana sobre
la categoría.

| Condición | Ajuste en vitales |
|-----------|-------------------|
| `cardiovascular` | PA 140-180 / 90-110 mmHg; pulso 80-110 |
| `infeccioso`, `respiratorio` (≥moderado) o `vital_fiebre=true` | Temperatura 37.5-39.5°C (hasta 40 si grave); pulso 90-120; FR elevada |
| `respiratorio` grave / moderado | SpO2 88-93 / 92-96% |
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
- `DELETE /api/seed/clear` → busca pacientes `SIM-*` → void lógico en cascada (visitas, encounters, obs, orders)
- Re-ejecuciones seguras: pacientes `SIM-` existentes se usan como "recurrentes"

---

## Resumen: qué editar para cambiar el comportamiento

| Quiero cambiar... | Editar |
|-------------------|--------|
| Período de simulación | `appsettings.json` → `StartDate/EndDate` |
| Volumen de pacientes | `appsettings.json` → `PacientesPorDiaMedio` |
| Variación estadística diaria | `DailyScheduleGenerator.cs` → parámetro σ del Normal |
| Tipo de clínica / perfil | `appsettings.json` → `ClinicType` (referencia semántica) |
| Distribución etaria | `appsettings.json` → `DemographicProfile.AgeGroups` |
| Volumen por día de semana | `appsettings.json` → `WeekdayWeights` |
| Qué enfermedades predominan | `epidemiology-profile.csv` → `peso` por categoría |
| Qué enfermedades aparecen según edad | `diagnosticos.csv` → columnas `aplica_*` por diagnóstico |
| Peso de un dx entre hombres vs. mujeres | `diagnosticos.csv` → `peso_M` / `peso_F` |
| Qué medicamentos se prescriben | `medicamentos.csv` → `aplica_CATEGORIA` |
| Qué labs externos se piden | `laboratorios.csv` → `aplica_CATEGORIA` |
| Qué exámenes de consultorio aplican | `examenes_clinicos.csv` → `aplica_CATEGORIA` |
| Frases de motivo de consulta | `motivos_consulta.csv` → `texto` |
| % de visitas con labs externos | `appsettings.json` → `ReferralProbabilities.LabOrder` |
| % de visitas con examen en consultorio | `appsettings.json` → `ReferralProbabilities.ClinicalExam` |
| % de pacientes con alergias | `appsettings.json` → `Allergy.BaseProbabilityMin/Max` |
| Cuántas alergias por paciente alérgico | `appsettings.json` → `Allergy.SecondAllergyProbability` / `ThirdAllergyProbability` / `MaxAllergies` |
| Qué alérgenos pueden aparecer | `alergenos.csv` |
| UUIDs de OpenMRS (location, visita, encuentro) | `appsettings.json` → `OpenMRS.Defaults` |
| Reproducibilidad | `appsettings.json` → `RandomSeed` |
