# Parametrización del Simulador Clínico OpenMRS

Este documento describe todos los archivos de configuración y catálogos del simulador, qué controla cada parámetro y cómo interactúan entre sí.

---

## 1. appsettings.json — Configuración central

Todo el comportamiento del simulador se controla desde aquí. Ningún valor está hardcodeado en el código.

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
      "DrugOrder": 0.65,
      "Urgent": 0.20,
      "FollowUp": 0.30
    },
    "WeekdayWeights": {
      "Monday":    1.20,
      "Tuesday":   1.20,
      "Wednesday": 1.00,
      "Thursday":  1.00,
      "Friday":    0.90,
      "Saturday":  0.50,
      "Sunday":    0.00
    }
  }
}
```

### Referencia de parámetros

| Parámetro | Tipo | Descripción |
|-----------|------|-------------|
| `SeedMode` | string | `"RestApi"` o `"DirectDb"`. Actualmente solo RestApi está implementado. |
| `StartDate` / `EndDate` | date | Rango temporal de la simulación. Todas las visitas caen dentro de este período. |
| `PacientesPorDiaMedio` | int | Promedio de pacientes atendidos por día hábil. El simulador aplica variación estadística (σ ≈ 20%). |
| `PorcentajeRecurrentes` | int (0-100) | % de visitas que corresponden a pacientes ya existentes en OpenMRS (controles, crónicos). |
| `Locale` | string | Locale de Bogus para generación de nombres/direcciones. `"es"` = español latinoamericano. |
| `RandomSeed` | int | Semilla para reproducibilidad. Mismo seed = misma simulación. `0` = aleatorio real. |
| `ClinicType` | string | Perfil del establecimiento. Afecta defaults de otras secciones. Ver tabla abajo. |
| `HorarioAtencion.PicoAM/PM` | objeto | Define horarios pico y su peso (% de atenciones que ocurren en ese bloque). El resto se distribuye uniformemente. |
| `DemographicProfile.AgeGroups` | array | Distribución etaria de los pacientes. Los `Weight` se normalizan al 100%. |
| `DemographicProfile.GenderRatio` | objeto | Proporción M/F. |
| `ReferralProbabilities.LabOrder` | float (0-1) | Probabilidad de que una visita genere una orden de laboratorio externo (testorder). |
| `ReferralProbabilities.ClinicalExam` | float (0-1) | Probabilidad de que una visita incluya un examen realizado en consultorio (obs inmediata). |
| `ReferralProbabilities.DrugOrder` | float (0-1) | Probabilidad de que una visita genere una prescripción de medicamento. |
| `ReferralProbabilities.Urgent` | float (0-1) | Probabilidad de que una orden de lab sea marcada como URGENTE (vs. ROUTINE). |
| `ReferralProbabilities.FollowUp` | float (0-1) | Probabilidad de que la visita genere una nota de seguimiento. |
| `ReferralProbabilities.AllergyOnNew` | float (0-1) | Probabilidad de que un paciente nuevo tenga alergias registradas. |
| `WeekdayWeights` | objeto | Multiplicador de volumen por día de semana. `1.0` = volumen normal. `0.0` = sin atención. |

### Perfiles de ClinicType y sus efectos

| ClinicType | Descripción | Efecto sobre defaults |
|------------|-------------|----------------------|
| `ConsultaExterna` | Clínica ambulatoria general | PacientesPorDiaMedio típico: 30-60. Diagnósticos: respiratorio, digestivo, preventivo. |
| `HospitalUrgencias` | Hospital con urgencias | Casos más graves. Mayor % de cardiovascular, trauma, infeccioso. LabOrder ~70%, mayor Urgent. |
| `CentroComunitario` | Atención primaria | Muchos crónicos (diabetes, HTA). Alto % de recurrentes (50%+). Preventivo y control. |

> **Nota:** `ClinicType` no fuerza valores — es una referencia semántica. Los parámetros individuales siempre tienen precedencia. Puedes poner `ClinicType: "ConsultaExterna"` y aun así configurar `LabOrder: 0.80`.

---

## 2. catalogs/epidemiology-profile.csv — Pesos por categoría/edad/género

Archivo simple. Una fila por combinación de categoría + grupo etario + género. Sin UUIDs, sin listas.

```csv
categoria,grupo_edad,genero,peso
respiratorio,0-14,M,35
respiratorio,0-14,F,32
respiratorio,15-29,Ambos,18
cardiovascular,45-64,M,28
cardiovascular,45-64,F,22
cardiovascular,65+,Ambos,35
diabetes,30-44,Ambos,12
diabetes,45-64,Ambos,20
diabetes,65+,Ambos,22
digestivo,0-14,Ambos,15
digestivo,15-29,Ambos,20
...
```

| Columna | Tipo | Descripción |
|---------|------|-------------|
| `categoria` | string | Sistema orgánico. Ver tabla de categorías abajo. |
| `grupo_edad` | string | `0-14`, `15-29`, `30-44`, `45-64`, `65+` |
| `genero` | string | `M`, `F`, o `Ambos` |
| `peso` | int | Probabilidad relativa. Se normaliza al 100% dentro de cada grupo edad+género. |

### Categorías de diagnóstico

| Categoría | Descripción | Grupos etarios más afectados |
|-----------|-------------|------------------------------|
| `respiratorio` | Infecciones vías aéreas, asma, EPOC | 0-14, 65+ |
| `cardiovascular` | HTA, ICC, angina, arritmias | 45-64, 65+ |
| `diabetes` | DM1, DM2, prediabetes, control glicémico | 30-44, 45-64, 65+ |
| `digestivo` | Gastritis, colitis, parasitosis, hígado | Todos |
| `osteomuscular` | Artritis, lumbalgia, fracturas, tendinitis | 30-44, 65+ |
| `neurologico` | Cefalea, migraña, vértigo, ACV | 15-29, 65+ |
| `urologico` | ITU, litiasis, IRC, próstata | F 15-44, M 45+ |
| `dermatologico` | Dermatitis, infecciones piel, psoriasis | 0-14, 15-29 |
| `infeccioso` | Fiebre, dengue, COVID-like, sepsis | Todos |
| `mental` | Ansiedad, depresión, insomnio | 15-44 |
| `preventivo` | Control prenatal, pediatría, vacunación | 0-14, F 15-44 |
| `endocrino` | Hipotiroidismo, obesidad, dislipidemia | F 30+ |

### Cómo funciona la selección de categoría

Para un paciente de 52 años, masculino:
1. Filtrar filas donde `grupo_edad = "45-64"` y `genero = "M"` o `"Ambos"`
2. Normalizar los `peso` → probabilidades (ej: cardiovascular 28%, diabetes 20%, respiratorio 15%...)
3. Elegir una `categoria` al azar según esas probabilidades

Luego el simulador va a `diagnosticos.csv` para elegir el diagnóstico específico dentro de esa categoría.

---

## 3. catalogs/diagnosticos.csv — Catálogo CIEL con booleanos por grupo etario

Extraído de OpenMRS + enriquecido con columnas booleanas. Cada fila es un diagnóstico. Las columnas `aplica_*` reemplazan las listas de UUIDs del diseño anterior — son simples `true`/`false` editables en Excel.

```csv
ciel_uuid,codigo_icd10,nombre_es,nombre_en,categoria,severidad,aplica_0_14,aplica_15_29,aplica_30_44,aplica_45_64,aplica_65mas,peso_M,peso_F,requiere_lab,requiere_rx
uuid_bronquitis,J20,Bronquitis aguda,Acute bronchitis,respiratorio,leve,true,true,true,false,true,10,10,false,true
uuid_asma,J45,Asma bronquial,Asthma,respiratorio,moderado,true,true,true,true,true,8,12,false,true
uuid_hta,I10,Hipertensión arterial,Hypertension,cardiovascular,moderado,false,false,false,true,true,28,22,true,true
uuid_icc,I50,Insuficiencia cardíaca,Heart failure,cardiovascular,grave,false,false,false,true,true,8,6,true,true
uuid_dm2,E11,Diabetes mellitus tipo 2,DM type 2,diabetes,moderado,false,false,true,true,true,15,18,true,true
uuid_prediabetes,R73,Prediabetes,Prediabetes,diabetes,leve,false,false,true,true,false,8,10,true,false
uuid_gastritis,K29,Gastritis,Gastritis,digestivo,leve,true,true,true,true,true,12,14,false,true
uuid_faringitis,J02,Faringitis aguda,Acute pharyngitis,respiratorio,leve,true,true,true,false,false,8,8,false,true
```

| Columna | Descripción |
|---------|-------------|
| `ciel_uuid` | UUID del concept OpenMRS — se usa en `POST /encounter` para el diagnóstico |
| `codigo_icd10` | Código ICD-10-WHO (referencia) |
| `nombre_es` | Nombre en español (FULLY_SPECIFIED) |
| `nombre_en` | Nombre en inglés |
| `categoria` | Categoría del sistema orgánico — enlaza con `epidemiology-profile.csv` |
| `severidad` | `leve`, `moderado`, `grave` — afecta probabilidad de lab urgente |
| `aplica_0_14` | `true`/`false` — ¿puede aparecer en niños 0-14? |
| `aplica_15_29` | `true`/`false` — ¿puede aparecer en adultos jóvenes 15-29? |
| `aplica_30_44` | `true`/`false` — ¿puede aparecer en adultos 30-44? |
| `aplica_45_64` | `true`/`false` — ¿puede aparecer en adultos mayores 45-64? |
| `aplica_65mas` | `true`/`false` — ¿puede aparecer en adultos 65+? |
| `peso_M` | Peso relativo dentro de la categoría para hombres (se normaliza) |
| `peso_F` | Peso relativo dentro de la categoría para mujeres (se normaliza) |
| `requiere_lab` | `true` si este dx típicamente siempre pide laboratorio (aumenta prob. base) |
| `requiere_rx` | `true` si este dx típicamente siempre recibe prescripción (aumenta prob. base) |

**Fuente**: Query SQL sobre `concept` + `concept_name` + `concept_reference_map` en la DB OpenMRS (ver `fases_implementacion.md` Fase 2). Las columnas `aplica_*` y `peso_*` se agregan manualmente después.

---

## 4. catalogs/medicamentos.csv — Catálogo de fármacos con booleanos por categoría

Extraído de la tabla `drug` de OpenMRS + columnas booleanas por categoría diagnóstica.

```csv
drug_uuid,nombre_generico,strength,via_uuid,aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,aplica_digestivo,aplica_osteomuscular,aplica_urologico,aplica_infeccioso,aplica_endocrino,aplica_neurologico,aplica_mental
uuid_amoxicilina,Amoxicilina,500mg,160240AAAAAAAAAAAAAAAAAA,true,false,false,false,false,true,true,false,false,false
uuid_salbutamol,Salbutamol,100mcg,160241AAAAAAAAAAAAAAAAAA,true,false,false,false,false,false,false,false,false,false
uuid_enalapril,Enalapril,10mg,160240AAAAAAAAAAAAAAAAAA,false,true,false,false,false,false,false,false,false,false
uuid_atenolol,Atenolol,50mg,160240AAAAAAAAAAAAAAAAAA,false,true,false,false,false,false,false,false,false,false
uuid_metformina,Metformina,850mg,160240AAAAAAAAAAAAAAAAAA,false,false,true,false,false,false,false,false,false,false
uuid_omeprazol,Omeprazol,20mg,160240AAAAAAAAAAAAAAAAAA,false,false,false,true,false,false,false,false,false,false
uuid_ibuprofeno,Ibuprofeno,400mg,160240AAAAAAAAAAAAAAAAAA,false,false,false,false,true,false,false,false,true,false
```

| Columna | Descripción |
|---------|-------------|
| `drug_uuid` | UUID del drug — se usa en `POST /order` tipo drugorder |
| `nombre_generico` | Nombre del medicamento |
| `strength` | Concentración (ej: "500mg", "10mg/5ml") |
| `via_uuid` | UUID CIEL de la vía de administración |
| `aplica_CATEGORIA` | `true`/`false` — si este medicamento es coherente para esa categoría diagnóstica |

**Vías de administración CIEL**:
- Oral: `160240AAAAAAAAAAAAAAAAAA`
- Inhalación: `160241AAAAAAAAAAAAAAAAAA`
- IV: `160242AAAAAAAAAAAAAAAAAA`
- IM: `160243AAAAAAAAAAAAAAAAAA`

---

## 5. catalogs/laboratorios.csv — Catálogo de exámenes con booleanos por categoría

Extraído de OpenMRS (concept class Test/LabSet) + columnas booleanas.

```csv
ciel_uuid,nombre_es,nombre_en,clase,aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,aplica_digestivo,aplica_osteomuscular,aplica_urologico,aplica_infeccioso,aplica_endocrino,aplica_neurologico
uuid_hemograma,Hemograma completo,Complete blood count,Test,true,true,true,true,false,true,true,false,false
uuid_glucemia,Glucemia en ayunas,Fasting glucose,Test,false,true,true,false,false,false,false,true,false
uuid_hba1c,Hemoglobina glicosilada,HbA1c,Test,false,false,true,false,false,false,false,true,false
uuid_lipidos,Perfil lipídico,Lipid panel,Test,false,true,true,false,false,false,false,true,false
uuid_ecg,Electrocardiograma,ECG,Test,false,true,false,false,false,false,false,false,false
uuid_rx_torax,Radiografía de tórax,Chest X-ray,Test,true,true,false,false,false,false,true,false,false
uuid_orina,Uroanálisis,Urinalysis,Test,false,false,true,false,false,true,true,false,false
```

| Columna | Descripción |
|---------|-------------|
| `ciel_uuid` | UUID del concept — se usa en `POST /order` tipo testorder |
| `nombre_es` | Nombre del examen en español |
| `nombre_en` | Nombre en inglés |
| `clase` | `Test`, `LabSet`, `Lab Findings` |
| `aplica_CATEGORIA` | `true`/`false` — si este lab es coherente para esa categoría diagnóstica |

---

## 6. catalogs/examenes_clinicos.csv — Exámenes realizados en consultorio

Diferencia fundamental con `laboratorios.csv`: estos exámenes los realiza el médico en el consultorio y el resultado se registra **inmediatamente** como una observación (`POST /obs`). No se genera una orden — el resultado queda en el encuentro ADULTINITIAL.

```csv
ciel_uuid,nombre_es,tipo_resultado,unidad,aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,aplica_digestivo,aplica_osteomuscular,aplica_urologico,aplica_infeccioso,aplica_endocrino,aplica_neurologico,aplica_preventivo
uuid_glucometria,Glucometría capilar,numerico,mg/dL,false,true,true,false,false,false,false,true,false,false
uuid_oximetria,Oximetría de pulso,numerico,%,true,true,false,false,false,false,true,false,false,false
uuid_ecg,Electrocardiograma,categorico,,false,true,false,false,false,false,false,false,false,false
uuid_espirometria,Espirometría,categorico,,true,false,false,false,false,false,false,false,false,false
uuid_test_embarazo,Test de embarazo en orina,categorico,,false,false,false,false,false,false,false,false,false,true
uuid_agudeza_visual,Agudeza visual,categorico,,false,false,false,false,false,false,false,false,true,true
uuid_pa_seriada,Toma de PA seriada,numerico,mmHg,false,true,false,false,false,false,false,false,false,false
```

| Columna | Descripción |
|---------|-------------|
| `ciel_uuid` | UUID del concept OpenMRS — se usa en `POST /obs` |
| `nombre_es` | Nombre del examen en español |
| `tipo_resultado` | `numerico` (registra valor + unidad) o `categorico` (normal / anormal) |
| `unidad` | Unidad de medida si es numérico (ej: mg/dL, %, mmHg) |
| `aplica_CATEGORIA` | `true`/`false` — si este examen es coherente para la categoría diagnóstica |

> Si el diagnóstico tiene `requiere_examen_clinico = true` en `diagnosticos.csv`, la probabilidad de realizar un examen sube al 90% (vs. la probabilidad base de `ClinicalExam`).

---

## 7. catalogs/alergenos.csv — Catálogo de alérgenos

Las alergias se registran **al momento de crear el paciente nuevo** (no en cada visita), usando `POST /patient/{uuid}/allergy`. OpenMRS requiere indicar el tipo de alérgeno, la severidad y al menos una reacción.

```csv
concept_uuid,nombre_es,tipo_alergeno,severidad_tipica,reaccion_tipica_uuid
uuid_penicilina,Penicilina,DRUG,moderada,uuid_urticaria
uuid_aspirina,Aspirina,DRUG,leve,uuid_nauseas
uuid_ibuprofeno,Ibuprofeno,DRUG,leve,uuid_gastritis
uuid_sulfas,Sulfamidas,DRUG,grave,uuid_anafilaxia
uuid_latex,Látex,ENVIRONMENT,moderada,uuid_dermatitis
uuid_polvo,Ácaros del polvo doméstico,ENVIRONMENT,leve,uuid_rinitis
uuid_mariscos,Mariscos,FOOD,grave,uuid_anafilaxia
uuid_nueces,Nueces y derivados,FOOD,grave,uuid_anafilaxia
uuid_mani,Maní,FOOD,moderada,uuid_urticaria
uuid_penicilium,Polen de gramíneas,ENVIRONMENT,leve,uuid_rinitis
```

| Columna | Descripción |
|---------|-------------|
| `concept_uuid` | UUID del concept OpenMRS del alérgeno |
| `nombre_es` | Nombre del alérgeno en español |
| `tipo_alergeno` | `DRUG`, `FOOD`, `ENVIRONMENT` — valores requeridos por OpenMRS |
| `severidad_tipica` | `leve`, `moderada`, `grave` — se usa como valor por defecto, con variación aleatoria |
| `reaccion_tipica_uuid` | UUID del concept de la reacción más común para ese alérgeno |

**Flujo de alergias en el pipeline**:
1. Al crear paciente nuevo: si `rand < AllergyOnNew` (ej: 15%) → registrar alergias
2. Elegir 1–3 alérgenos al azar del catálogo
3. Para cada alérgeno: `POST /patient/{uuid}/allergy` con `allergenType`, `codedAllergen.uuid`, `severity.uuid`, `reactions[0].reaction.uuid`

---

## 8. catalogs/motivos_consulta.csv — Frases de motivo de consulta

Creado manualmente. Son frases en español que se usan como texto libre en el encounter ADULTINITIAL.

```csv
id,descripcion,categoria_dx
1,"Dolor de cabeza intenso y persistente",neurologico
2,"Fiebre mayor de 38°C y malestar general",infeccioso
3,"Dolor abdominal en epigastrio",digestivo
4,"Tos seca de más de una semana",respiratorio
5,"Control de presión arterial alta",cardiovascular
6,"Control de glucemia en diabético conocido",diabetes
7,"Dolor lumbar que irradia a pierna derecha",osteomuscular
8,"Náuseas, vómitos y diarrea líquida",digestivo
9,"Dificultad para respirar al esfuerzo",cardiovascular|respiratorio
10,"Ardor al orinar y frecuencia urinaria aumentada",urologico
...
```

La columna `categoria_dx` permite seleccionar el motivo de consulta coherente con el diagnóstico ya elegido por el perfil epidemiológico.

---

## 9. Coherencia entre archivos — Cómo se conectan

```
appsettings.json
  └── Simulation.DemographicProfile  ──────────► edad/género del paciente generado
  └── Simulation.PacientesPorDiaMedio ─────────► cuántos pacientes por día
  └── Simulation.WeekdayWeights ───────────────► multiplicador por día de semana
  └── Simulation.ReferralProbabilities ────────► probabilidades base de cada acción

AL CREAR PACIENTE NUEVO:
alergenos.csv
  └── rand < AllergyOnNew (0.15) ──────────────► elegir 1-3 al azar
  └── por cada alérgeno ────────────────────────► POST /patient/{uuid}/allergy

POR CADA VISITA:

PASO 1 — elegir categoría:
epidemiology-profile.csv
  └── filtrar por [grupo_edad + genero] ───────► filas candidatas
  └── normalizar [peso] ────────────────────────► elegir categoria (ej: "cardiovascular")

PASO 2 — elegir diagnóstico:
diagnosticos.csv
  └── filtrar por [categoria = elegida] ───────► filas del sistema orgánico
  └── filtrar por [aplica_GRUPO = true] ───────► solo dx válidos para la edad
  └── normalizar [peso_M] o [peso_F] ──────────► elegir diagnóstico específico
  └── [requiere_lab = true] ───────────────────► aumentar prob. de lab externo
  └── [requiere_rx = true] ────────────────────► aumentar prob. de prescripción
  └── [requiere_examen_clinico = true] ────────► aumentar prob. de examen en consultorio
  └── [severidad = "grave"] ────────────────────► aumentar prob. de lab urgente

PASO 3 — vitales (siempre):
VitalsSeeder ajusta rangos según categoría del diagnóstico elegido

PASO 4 — examen en consultorio (si aplica):
examenes_clinicos.csv
  └── rand < ClinicalExam (0.35) o requiere_examen_clinico ──► filtrar por [aplica_CATEGORIA = true]
  └── elegir 1 al azar ─────────────────────────► POST /obs en encounter ADULTINITIAL

PASO 5 — lab externo (si aplica):
laboratorios.csv
  └── rand < LabOrder (0.40) o requiere_lab ──► filtrar por [aplica_CATEGORIA = true]
  └── elegir 1-2 al azar ───────────────────────► POST /order testorder

PASO 6 — prescripción (si aplica):
medicamentos.csv
  └── rand < DrugOrder (0.65) o requiere_rx ──► filtrar por [aplica_CATEGORIA = true]
  └── elegir 1-3 al azar ───────────────────────► POST /order drugorder

PASO 7 — motivo de consulta (siempre):
motivos_consulta.csv
  └── filtrar por [categoria_dx = categoria] ──► frases coherentes
  └── elegir 1 al azar ─────────────────────────► texto libre en encounter ADULTINITIAL
```

---

## 8. Vitales coherentes con diagnóstico

Los signos vitales del encuentro VITALS se ajustan según la categoría del diagnóstico elegido:

| Categoría dx | Ajuste en vitales |
|--------------|-------------------|
| `cardiovascular` (HTA) | PA sistólica: 140-180 mmHg, diastólica: 90-110 |
| `cardiovascular` normal | PA dentro de rango normal |
| `infeccioso` / fiebre | Temperatura: 37.5-39.5°C, pulso elevado: 90-110 |
| `respiratorio` grave | SpO2: 88-94%, FR elevada |
| `diabetes` | Peso tendencia alta (BMI 25-35) |
| Resto | Rangos normales con variación estadística |

Rangos base (sin ajuste por dx):
- Peso: 45-120 kg
- Talla: 145-195 cm
- PA sistólica: 100-130 mmHg
- PA diastólica: 60-85 mmHg
- Temperatura: 36.0-37.4°C
- Pulso: 60-100 lpm
- SpO2: 96-100%

---

## 10. Idempotencia y limpieza

Todos los registros creados por el simulador son identificables:
- **Pacientes**: identificador con prefijo `SIM-` (ej: `SIM-00042`)
- **Visitas/Encounters**: campo `description` contiene `SEEDED_BY_SIMULATOR`

Esto permite:
- `DELETE /api/seed/clear` → busca pacientes `SIM-*` → void lógico en cascada (visitas, encounters, obs, orders)
- Re-ejecuciones seguras: el simulador detecta pacientes existentes con `SIM-` y los usa como recurrentes

---

## Resumen: qué editar para cambiar el comportamiento

| Quiero cambiar... | Editar |
|-------------------|--------|
| Período de simulación | `appsettings.json` → `StartDate/EndDate` |
| Volumen de pacientes | `appsettings.json` → `PacientesPorDiaMedio` |
| Tipo de clínica | `appsettings.json` → `ClinicType` |
| Distribución etaria | `appsettings.json` → `DemographicProfile.AgeGroups` |
| Qué enfermedades predominan | `epidemiology-profile.csv` → `peso` por categoría |
| Qué enfermedades aparecen según edad | `diagnosticos.csv` → columnas `aplica_*` por diagnóstico |
| Peso de un dx entre hombres vs. mujeres | `diagnosticos.csv` → `peso_M` / `peso_F` |
| Qué medicamentos se prescriben para una categoría | `medicamentos.csv` → `aplica_CATEGORIA` |
| Qué labs externos se piden para una categoría | `laboratorios.csv` → `aplica_CATEGORIA` |
| Qué exámenes de consultorio aplican a una categoría | `examenes_clinicos.csv` → `aplica_CATEGORIA` |
| Frases de motivo de consulta | `motivos_consulta.csv` |
| % de visitas con labs externos | `appsettings.json` → `ReferralProbabilities.LabOrder` |
| % de visitas con examen en consultorio | `appsettings.json` → `ReferralProbabilities.ClinicalExam` |
| % de pacientes con alergias | `appsettings.json` → `ReferralProbabilities.AllergyOnNew` |
| Qué alérgenos pueden aparecer | `alergenos.csv` |
| Reproducibilidad | `appsettings.json` → `RandomSeed` |
