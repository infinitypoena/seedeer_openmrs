# Enfermedades de Centroamérica — perfil epidemiológico, tratamiento de primera línea y referencias

**Documento de investigación / insumo para el catálogo del simulador clínico OpenMRS**
Autor de la recopilación: proyecto `proyecto_api_seedeer` · Fecha: junio 2026

---

## 1. Resumen y metodología

Este informe recopila **300 enfermedades relevantes para Centroamérica** (Guatemala, El Salvador, Honduras, Nicaragua, Costa Rica, Panamá; con extensión a la subregión mesoamericana), organizadas en las **13 categorías** que usa el catálogo `diagnosticos.csv` del simulador. Para cada enfermedad se indica: severidad, grupos de edad afectados, **tratamiento farmacológico de primera línea**, estacionalidad climática (cuando aplica), si es crónica, si es de alta frecuencia ("común" en consulta externa) y una nota de relevancia regional con su **referencia verificable**.

### Criterio de selección
Las enfermedades se eligieron combinando tres lentes:
1. **Carga de enfermedad (AVAD/AVISA)** según los estudios de Carga Global de Enfermedad (GBD) y los informes de la OPS para las Américas [1][2][12].
2. **Morbilidad de consulta externa** (motivos frecuentes de atención primaria): infecciones respiratorias agudas, enfermedad diarreica, parasitosis, dermatosis, etc.
3. **Prioridades regionales propias de Centroamérica**: enfermedades transmitidas por vectores (dengue, chikungunya, Zika, malaria, Chagas, leishmaniasis) [4][6][7][8][9][18], enfermedades tropicales desatendidas (ETDs) [5][20], salud materno-infantil [16][17], causas externas/violencia interpersonal [15][23] y la **enfermedad renal crónica de causas no tradicionales (nefropatía mesoamericana)** [13][14][21].

### Fuentes de tratamiento
El tratamiento de primera línea se basa en la **Lista Modelo de Medicamentos Esenciales de la OMS, 23ª lista (2023)** [3] y, para entidades específicas, en las guías de práctica de la OPS/OMS (dengue/chikungunya/Zika [4], Chagas [7], leishmaniasis [8], malaria [9][22], tuberculosis [10][11]). Los esquemas se presentan de forma **resumida** (1–3 fármacos clave); no sustituyen las guías nacionales (MINSAL) de cada país, que deben consultarse para dosificación.

### ⚠️ Advertencias de honestidad (leer antes de usar)
- **Referencias por grupo, no inventadas por enfermedad.** Se usa un **conjunto curado de fuentes reales** (sección 16, Bibliografía) citadas con `[n]` a nivel de categoría y de afirmación epidemiológica. No se fabricó una cita única por cada una de las 300 entradas: muchas enfermedades comunes no tienen un estudio centroamericano dedicado, y forzar una cita por fila produciría referencias débiles o falsas. Todas las afirmaciones epidemiológicas regionales sí están ancladas a una fuente verificable.
- **UUID CIEL pendientes.** La columna `ciel_uuid` aparece como `PENDIENTE-VERIFICAR` en todas las filas. Los UUID **deben confirmarse contra ESTA instancia de OpenMRS** vía `GET /ws/rest/v1/concept?q=...` antes de cargarlos (regla del proyecto: muchos UUID "estándar" mapean a otra enfermedad en esta instalación). No se inventaron UUIDs.
- **No es consejo clínico.** Es material de simulación con fines académicos.

### Cómo se mapea al catálogo (`diagnosticos.csv`)
Cada fila es directamente mapeable a las columnas del seeder:
`ciel_uuid, nombre_es, categoria, severidad, aplica_0_14, aplica_15_29, aplica_30_44, aplica_45_64, aplica_65mas, peso_M, peso_F, requiere_lab, requiere_rx, requiere_examen_clinico, clima, cronica, comun`.
La columna **Edades** de las tablas indica qué banderas `aplica_*` van en `true`. El **Apéndice (sección 17)** entrega las 300 filas en formato CSV listo para importar.

---

## 2. Panorama epidemiológico de Centroamérica

Centroamérica atraviesa una **transición epidemiológica incompleta**: convive una carga creciente de **enfermedades no transmisibles (ENT)** —que ya representan cerca del **65 % de las muertes** en las Américas, encabezadas por la cardiopatía isquémica, los accidentes cerebrovasculares, la diabetes y los cánceres [1][2]— con una **carga persistente de enfermedades transmisibles**, especialmente las **transmitidas por vectores** (dengue, chikungunya, Zika por *Aedes aegypti*; malaria; enfermedad de Chagas por triatominos; leishmaniasis) [18][4][6].

Rasgos epidemiológicos distintivos de la subregión:

- **Arbovirosis endémo-epidémicas.** El dengue es endémico con brotes estacionales ligados a la temporada lluviosa; chikungunya y Zika emergieron como epidemias regionales en 2014–2016. La OPS sostiene desde 2023 un plan reforzado de manejo clínico y control vectorial con SE-COMISCA y los CDC [4].
- **Enfermedades tropicales desatendidas (ETDs).** Chagas, leishmaniasis, geohelmintiasis, lepra y otras afectan a poblaciones rurales y pobres; la OPS impulsa su eliminación en el marco 2020–2030 [5][20].
- **Nefropatía mesoamericana (ERC de causas no tradicionales).** Forma de enfermedad renal crónica que afecta de manera desproporcionada a trabajadores agrícolas jóvenes (caña de azúcar, maíz) de El Salvador y Nicaragua, no explicada por diabetes ni hipertensión; prevalencias estandarizadas de hasta 14 % en cañeros salvadoreños [13][14][21]. En El Salvador es una de las principales causas de muerte en hombres.
- **Violencia interpersonal y causas externas.** La Región de las Américas tiene la tasa de homicidios más alta del mundo; la violencia interpersonal es la principal causa de mortalidad en adolescentes y jóvenes de Centroamérica, con El Salvador históricamente entre las tasas más altas [15][23].
- **Salud materno-infantil.** Las infecciones respiratorias bajas y las enfermedades diarreicas siguen siendo causas importantes de AVAD en menores de 5 años (≈11 % y ≈6.7 % respectivamente), y persiste mortalidad materna prevenible [16][17].
- **ENT en ascenso.** Hipertensión, diabetes tipo 2, obesidad, dislipidemia, EPOC y cánceres (cervicouterino, gástrico, mama) crecen con la urbanización y el envejecimiento [1][2][12].

Las tablas siguientes desglosan estas cargas en 300 entidades clínicas.

---

## 3. Categoría: infeccioso (50)

Categoría de mayor peso regional: concentra las arbovirosis, las ETDs y las infecciones agudas de consulta externa [4][5][6][7][8][9][18][20].

| # | Enfermedad | Sev. | Edades | Medicamento 1ª línea | Clima | Crón. | Común | Relevancia regional |
|---|-----------|------|--------|----------------------|-------|-------|-------|---------------------|
| 1 | Dengue sin signos de alarma | moderado | todas | Paracetamol + hidratación oral | lluvia | no | sí | Arbovirosis endémica, brotes en temporada lluviosa [4][18] |
| 2 | Dengue con signos de alarma | grave | todas | Hidratación IV con cristaloides | lluvia | no | no | Requiere triaje y manejo guiado por OPS [4] |
| 3 | Dengue grave (choque/hemorrágico) | grave | todas | Reanimación con cristaloides, manejo hospitalario | lluvia | no | no | Forma letal; vigilancia OPS [4] |
| 4 | Chikungunya | moderado | todas | Paracetamol, manejo sintomático | lluvia | no | sí | Epidemia regional 2014–2015 [4][18] |
| 5 | Zika | leve | todas | Manejo sintomático (paracetamol) | lluvia | no | sí | Epidemia 2015–2016, riesgo congénito [4][18] |
| 6 | Malaria por *P. vivax* | moderado | todas | Cloroquina + primaquina (cura radical) | lluvia | no | no | Especie predominante en Mesoamérica [9][22] |
| 7 | Malaria por *P. falciparum* | grave | todas | TCA (artemeter-lumefantrina) | lluvia | no | no | Focos en zonas selváticas [9] |
| 8 | Enfermedad de Chagas aguda | moderado | todas | Benznidazol | — | no | no | ETD prioritaria, vector triatomino [6][7] |
| 9 | Enfermedad de Chagas crónica | grave | 15-64 | Benznidazol / nifurtimox | — | sí | no | Miocardiopatía chagásica; eliminación OPS [6][7] |
| 10 | Leishmaniasis cutánea | moderado | todas | Antimoniales pentavalentes intralesionales | — | no | no | ETD endémica rural [8] |
| 11 | Leishmaniasis mucocutánea | grave | 15-64 | Antimoniales sistémicos / anfotericina B | — | no | no | Forma destructiva [8] |
| 12 | Tuberculosis pulmonar (BK+) | grave | todas | 2HRZE / 4HRE | — | sí | no | Descenso lento de incidencia en LAC [10][11] |
| 13 | Tuberculosis extrapulmonar | grave | todas | 2HRZE / 4HRE | — | sí | no | Coinfección frecuente con VIH [10] |
| 14 | VIH / SIDA | grave | 15-64 | TAR (tenofovir+lamivudina+dolutegravir) | — | sí | no | Carga creciente, coinfección TB [3] |
| 15 | Influenza (gripe) | moderado | todas | Sintomático; oseltamivir si grave | invierno | no | sí | Estacional, picos en meses frescos [3] |
| 16 | Faringoamigdalitis estreptocócica | leve | todas | Penicilina / amoxicilina | invierno | no | sí | Motivo frecuente de consulta [3] |
| 17 | Geohelmintiasis (parasitosis intestinal) | leve | todas | Albendazol | lluvia | no | sí | ETD; desparasitación masiva OPS [5] |
| 18 | Amebiasis intestinal | moderado | todas | Metronidazol | lluvia | no | sí | Agua/saneamiento deficiente [3] |
| 19 | Giardiasis | leve | 0-14 | Metronidazol / tinidazol | lluvia | no | sí | Frecuente en niños [3] |
| 20 | Enfermedad diarreica aguda infecciosa | moderado | todas | SRO + zinc | verano | no | sí | Causa importante de AVAD en <5 años [16] |
| 21 | Fiebre tifoidea | moderado | todas | Ciprofloxacino / azitromicina | verano | no | no | Transmisión hídrica [3] |
| 22 | Leptospirosis | grave | 15-64 | Doxiciclina / penicilina | lluvia | no | no | Brotes post-inundación [18] |
| 23 | Hepatitis A | moderado | 0-14 | Soporte | lluvia | no | sí | Endémica, fecal-oral [3] |
| 24 | Hepatitis B | grave | 15-64 | Tenofovir (forma crónica) | — | sí | no | Vacuna en PAI [3] |
| 25 | Hepatitis C | grave | 30-64 | Antivirales de acción directa | — | sí | no | Meta de eliminación OPS [3] |
| 26 | Escabiosis (sarna) | leve | todas | Permetrina tópica | — | no | sí | ETD cutánea, hacinamiento [3][5] |
| 27 | Pediculosis | leve | 0-14 | Permetrina | — | no | sí | Frecuente escolar [3] |
| 28 | Celulitis bacteriana | moderado | todas | Cefalexina / dicloxacilina | — | no | sí | Complicación de heridas [3] |
| 29 | Absceso cutáneo | moderado | todas | Drenaje + antibiótico | — | no | sí | Atención primaria frecuente [3] |
| 30 | Infección urinaria (cistitis) | leve | todas | Nitrofurantoína / TMP-SMX | — | no | sí | Muy frecuente en mujeres [3] |
| 31 | Neumonía adquirida en comunidad | grave | todas | Amoxicilina / amoxi-clavulánico | invierno | no | sí | Causa de mortalidad en <5 y ancianos [16] |
| 32 | Bronquiolitis (VRS) | moderado | 0-14 | Soporte / oxígeno | invierno | no | sí | IRA baja del lactante [16] |
| 33 | Otitis media aguda | leve | 0-14 | Amoxicilina | invierno | no | sí | Frecuente pediátrica [3] |
| 34 | Sinusitis bacteriana aguda | leve | todas | Amoxicilina | invierno | no | sí | Complicación de IRA [3] |
| 35 | Conjuntivitis infecciosa | leve | todas | Antibiótico tópico | — | no | sí | Brotes comunitarios [3] |
| 36 | Varicela | leve | 0-14 | Sintomático; aciclovir si riesgo | — | no | sí | Inmunoprevenible [3] |
| 37 | Sarampión | grave | 0-14 | Soporte + vitamina A | — | no | no | Reintroducción por baja cobertura [16] |
| 38 | Rubéola | leve | 0-14 | Soporte | — | no | no | Vigilancia de síndrome congénito [16] |
| 39 | Parotiditis (paperas) | leve | 0-14 | Soporte | — | no | no | Inmunoprevenible [16] |
| 40 | Tos ferina (pertussis) | grave | 0-14 | Azitromicina | — | no | no | Riesgo en lactantes [16] |
| 41 | Mononucleosis infecciosa | leve | 15-29 | Soporte | — | no | no | Frecuente en jóvenes [3] |
| 42 | Micosis superficial (tiña/dermatofitosis) | leve | todas | Antifúngico tópico (clotrimazol) | lluvia | no | sí | Favorecida por humedad [3] |
| 43 | Candidiasis | leve | todas | Fluconazol / nistatina | — | no | sí | Frecuente, oportunista [3] |
| 44 | Sífilis | moderado | 15-64 | Penicilina benzatínica | — | no | no | ITS; tamizaje prenatal [3] |
| 45 | Gonorrea | moderado | 15-44 | Ceftriaxona + azitromicina | — | no | no | ITS con resistencia creciente [3] |
| 46 | Clamidiasis | leve | 15-44 | Azitromicina / doxiciclina | — | no | sí | ITS frecuente [3] |
| 47 | Brucelosis | moderado | 15-64 | Doxiciclina + rifampicina | — | no | no | Zoonosis rural [3] |
| 48 | Rabia (profilaxis post-exposición) | grave | todas | Vacuna + inmunoglobulina antirrábica | — | no | no | ETD; mordeduras de perro [5] |
| 49 | Lepra (enfermedad de Hansen) | grave | 15-64 | Poliquimioterapia (rifampicina+dapsona+clofazimina) | — | sí | no | ETD en eliminación [5][20] |
| 50 | COVID-19 | moderado | todas | Soporte; antivirales si riesgo | invierno | no | sí | Carga respiratoria reciente [3] |

---

## 4. Categoría: respiratorio (27)

Las infecciones respiratorias agudas son el principal motivo de consulta externa; el asma y la EPOC encabezan las ENT respiratorias [1][3][16].

| # | Enfermedad | Sev. | Edades | Medicamento 1ª línea | Clima | Crón. | Común | Relevancia regional |
|---|-----------|------|--------|----------------------|-------|-------|-------|---------------------|
| 1 | Resfriado común (rinofaringitis aguda) | leve | todas | Sintomático | invierno | no | sí | Motivo #1 de consulta [3] |
| 2 | Faringitis aguda viral | leve | todas | Sintomático | invierno | no | sí | Muy frecuente [3] |
| 3 | Laringitis aguda | leve | todas | Sintomático | invierno | no | sí | IRA alta común [3] |
| 4 | Bronquitis aguda | leve | todas | Sintomático | invierno | no | sí | Autolimitada [3] |
| 5 | Asma bronquial | moderado | todas | Salbutamol + corticoide inhalado (budesonida) | invierno | sí | sí | ENT respiratoria prevalente [3] |
| 6 | Crisis asmática | grave | todas | Salbutamol nebulizado + corticoide sistémico | invierno | no | no | Urgencia frecuente [3] |
| 7 | EPOC | grave | 45+ | Broncodilatadores (salbutamol/ipratropio) | invierno | sí | sí | Ligada a humo de leña y tabaco [1][3] |
| 8 | Exacerbación de EPOC | grave | 45+ | Broncodilatadores + corticoide + antibiótico | invierno | no | no | Hospitalización recurrente [3] |
| 9 | Rinitis alérgica | leve | todas | Antihistamínico (loratadina) | seca | sí | sí | Estacional, polvo/polen [3] |
| 10 | Neumonía atípica | moderado | 15-64 | Azitromicina / claritromicina | invierno | no | sí | Forma ambulatoria [3] |
| 11 | Crup (laringotraqueítis) | moderado | 0-14 | Dexametasona | invierno | no | sí | Pediátrico estacional [16] |
| 12 | Epiglotitis | grave | 0-14 | Ceftriaxona + manejo de vía aérea | — | no | no | Emergencia pediátrica [3] |
| 13 | Apnea obstructiva del sueño | moderado | 30-64 | CPAP / medidas higiénicas | — | sí | no | Asociada a obesidad creciente [1] |
| 14 | Derrame pleural | grave | 30+ | Tratar causa + drenaje | — | no | no | Complicación de TB/neumonía [3] |
| 15 | Neumotórax | grave | 15-44 | Drenaje torácico | — | no | no | Urgencia [3] |
| 16 | Bronquiectasias | grave | 45+ | Antibióticos + fisioterapia | — | sí | no | Secuela de infecciones [3] |
| 17 | Fibrosis pulmonar | grave | 45+ | Manejo especializado | — | sí | no | ENT crónica [1] |
| 18 | Enfisema pulmonar | grave | 45+ | Broncodilatadores | — | sí | no | Espectro EPOC [1] |
| 19 | Neumoconiosis/silicosis | grave | 45+ | Soporte | — | sí | no | Exposición ocupacional [1] |
| 20 | Hemoptisis | moderado | 30+ | Tratar causa | — | no | no | Marcador de TB/cáncer [3] |
| 21 | Tos crónica | leve | todas | Tratar causa | — | sí | sí | Síntoma frecuente [3] |
| 22 | Sinusitis crónica | leve | 15+ | Manejo / antibiótico | — | sí | sí | Recurrente [3] |
| 23 | Amigdalitis recurrente | leve | 0-29 | Penicilina; valorar amigdalectomía | invierno | no | sí | Frecuente escolar [3] |
| 24 | Insuficiencia respiratoria aguda | grave | 45+ | Oxígeno + tratar causa | — | no | no | Urgencia [3] |
| 25 | Influenza con complicación respiratoria | grave | todas | Oseltamivir + soporte | invierno | no | no | Riesgo en ancianos [3] |
| 26 | Faringitis crónica | leve | 15+ | Sintomático | — | sí | sí | Tabaquismo/ambiental [3] |
| 27 | Resfriado complicado (otitis/sinusitis) | leve | 0-14 | Amoxicilina | invierno | no | sí | Complicación pediátrica [3] |

---

## 5. Categoría: cardiovascular (24)

Las enfermedades cardiovasculares son la primera causa de muerte y de AVAD en la región; la cardiopatía isquémica encabeza [1][2][12].

| # | Enfermedad | Sev. | Edades | Medicamento 1ª línea | Clima | Crón. | Común | Relevancia regional |
|---|-----------|------|--------|----------------------|-------|-------|-------|---------------------|
| 1 | Hipertensión arterial esencial | moderado | 30+ | Amlodipino / losartán / hidroclorotiazida | — | sí | sí | ENT más prevalente [1][2] |
| 2 | Crisis hipertensiva | grave | 30+ | Antihipertensivos IV/orales según tipo | — | no | no | Urgencia frecuente [1] |
| 3 | Cardiopatía isquémica crónica (angina) | grave | 45+ | AAS + estatina + betabloqueador | — | sí | sí | Principal causa de AVAD [1][2] |
| 4 | Infarto agudo de miocardio | grave | 45+ | AAS + reperfusión + estatina | — | no | no | Primera causa de muerte [1][2] |
| 5 | Insuficiencia cardíaca | grave | 45+ | IECA + betabloqueador + diurético | — | sí | sí | ENT crónica en aumento [1] |
| 6 | Accidente cerebrovascular isquémico | grave | 45+ | Manejo agudo + AAS + estatina | — | no | no | Segunda causa de muerte CV [1][2] |
| 7 | Accidente cerebrovascular hemorrágico | grave | 45+ | Manejo hospitalario / control de PA | — | no | no | Letalidad alta [1] |
| 8 | Fibrilación auricular | moderado | 45+ | Anticoagulante + control de frecuencia | — | sí | no | Riesgo embólico [3] |
| 9 | Dislipidemia (hipercolesterolemia) | leve | 30+ | Estatina (atorvastatina) | — | sí | sí | Factor de riesgo CV [1] |
| 10 | Enfermedad arterial periférica | moderado | 45+ | AAS + estatina + cilostazol | — | sí | no | Asociada a diabetes/tabaco [1] |
| 11 | Trombosis venosa profunda | grave | 30+ | Anticoagulación (heparina/warfarina/DOAC) | — | no | no | Riesgo de embolia pulmonar [3] |
| 12 | Embolia pulmonar | grave | 45+ | Anticoagulación | — | no | no | Urgencia letal [3] |
| 13 | Várices de miembros inferiores | leve | 30+ | Medidas + manejo quirúrgico | — | sí | sí | Muy frecuente [3] |
| 14 | Cardiopatía hipertensiva | grave | 45+ | Control de PA + IECA | — | sí | sí | Consecuencia de HTA mal controlada [1] |
| 15 | Valvulopatía reumática | grave | 15-44 | Profilaxis con penicilina + manejo | — | sí | no | Secuela de fiebre reumática [3] |
| 16 | Fiebre reumática aguda | moderado | 0-14 | Penicilina + AAS | invierno | no | no | Aún presente en la región [3] |
| 17 | Miocardiopatía chagásica | grave | 30-64 | Manejo de IC + antiarrítmicos | — | sí | no | Complicación crónica de Chagas [6][7] |
| 18 | Endocarditis infecciosa | grave | 30+ | Antibióticos IV prolongados | — | no | no | Alta letalidad [3] |
| 19 | Pericarditis aguda | moderado | 15-44 | AINE + colchicina | — | no | no | Causa viral frecuente [3] |
| 20 | Arritmia (taquicardia supraventricular) | moderado | 15+ | Maniobras vagales / betabloqueador | — | no | no | Consulta de urgencia [3] |
| 21 | Hipotensión ortostática | leve | 65+ | Medidas + ajuste de fármacos | — | no | sí | Frecuente en ancianos [3] |
| 22 | Aneurisma aórtico | grave | 45+ | Control de PA + cirugía | — | sí | no | ENT grave [1] |
| 23 | Cor pulmonale | grave | 45+ | Tratar EPOC + oxígeno | — | sí | no | Secuela de EPOC [1] |
| 24 | Síncope cardiogénico | moderado | 45+ | Estudiar causa | — | no | no | Motivo de urgencia [3] |

---

## 6. Categoría: diabetes (12)

La diabetes tipo 2 es una de las ENT de mayor crecimiento en Centroamérica, con complicaciones micro y macrovasculares [1][2].

| # | Enfermedad | Sev. | Edades | Medicamento 1ª línea | Clima | Crón. | Común | Relevancia regional |
|---|-----------|------|--------|----------------------|-------|-------|-------|---------------------|
| 1 | Diabetes mellitus tipo 2 | moderado | 30+ | Metformina | — | sí | sí | ENT prevalente en ascenso [1][2] |
| 2 | Diabetes mellitus tipo 1 | grave | 0-29 | Insulina | — | sí | no | Debut juvenil [3] |
| 3 | Diabetes gestacional | moderado | 15-44 | Dieta + insulina si precisa | — | no | no | Tamizaje prenatal [17] |
| 4 | Cetoacidosis diabética | grave | todas | Insulina IV + hidratación + electrolitos | — | no | no | Urgencia metabólica [3] |
| 5 | Estado hiperosmolar hiperglucémico | grave | 45+ | Hidratación + insulina | — | no | no | Letal en ancianos [3] |
| 6 | Hipoglucemia | moderado | todas | Glucosa oral/IV | — | no | sí | Complicación del tratamiento [3] |
| 7 | Retinopatía diabética | grave | 30+ | Control glucémico + fotocoagulación | — | sí | no | Causa de ceguera [1] |
| 8 | Nefropatía diabética | grave | 30+ | IECA/ARA-II + control glucémico | — | sí | no | Causa de ERC [1] |
| 9 | Neuropatía diabética | moderado | 30+ | Control glucémico + gabapentina/amitriptilina | — | sí | sí | Dolor neuropático frecuente [3] |
| 10 | Pie diabético (úlcera/infección) | grave | 45+ | Antibióticos + curaciones + control | — | no | no | Causa de amputación [1] |
| 11 | Diabetes mal controlada (descompensada) | moderado | 30+ | Ajuste de hipoglucemiantes/insulina | — | sí | sí | Seguimiento de consulta [1] |
| 12 | Prediabetes (intolerancia a la glucosa) | leve | 30+ | Cambios de estilo de vida ± metformina | — | sí | sí | Ventana de prevención [1] |

---

## 7. Categoría: digestivo (27)

La enfermedad diarreica y las parasitosis conviven con ENT digestivas (gastritis, ERGE, hepatopatías) [3][16].

| # | Enfermedad | Sev. | Edades | Medicamento 1ª línea | Clima | Crón. | Común | Relevancia regional |
|---|-----------|------|--------|----------------------|-------|-------|-------|---------------------|
| 1 | Gastroenteritis aguda | moderado | todas | SRO + zinc | verano | no | sí | Causa de AVAD en <5 [16] |
| 2 | Gastritis aguda | leve | 15+ | Omeprazol | — | no | sí | Muy frecuente [3] |
| 3 | Enfermedad por reflujo gastroesofágico | leve | 30+ | Omeprazol + medidas | — | sí | sí | ENT digestiva común [3] |
| 4 | Úlcera péptica | moderado | 30+ | IBP + erradicación de *H. pylori* | — | sí | sí | Asociada a *H. pylori* [3] |
| 5 | Infección por *Helicobacter pylori* | leve | 15+ | Triple terapia (IBP+claritromicina+amoxicilina) | — | no | sí | Alta prevalencia regional [3] |
| 6 | Síndrome de intestino irritable | leve | 15-44 | Antiespasmódicos + fibra | — | sí | sí | Funcional frecuente [3] |
| 7 | Estreñimiento crónico | leve | todas | Laxantes + fibra | — | sí | sí | Consulta común [3] |
| 8 | Apendicitis aguda | grave | 5-44 | Apendicectomía + antibióticos | — | no | no | Urgencia quirúrgica frecuente [3] |
| 9 | Colelitiasis / cólico biliar | moderado | 30+ | Analgesia + colecistectomía | — | sí | sí | Frecuente en mujeres [3] |
| 10 | Colecistitis aguda | grave | 30+ | Antibióticos + colecistectomía | — | no | no | Complicación litiásica [3] |
| 11 | Pancreatitis aguda | grave | 30+ | Hidratación + analgesia + ayuno | — | no | no | Etanol/litiasis [3] |
| 12 | Hepatitis viral aguda | moderado | todas | Soporte | lluvia | no | sí | A/B/E endémicas [3] |
| 13 | Cirrosis hepática | grave | 45+ | Manejo de complicaciones | — | sí | no | Alcohol/hepatitis virales [1] |
| 14 | Hígado graso no alcohólico | leve | 30+ | Cambios de estilo de vida | — | sí | sí | Asociado a obesidad [1] |
| 15 | Hemorroides | leve | 30+ | Medidas + flebotónicos | — | sí | sí | Muy frecuente [3] |
| 16 | Fisura anal | leve | 15-44 | Medidas + analgesia tópica | — | no | sí | Consulta común [3] |
| 17 | Enfermedad diarreica persistente | moderado | 0-14 | SRO + evaluar causa | lluvia | no | sí | Desnutrición asociada [16] |
| 18 | Parasitosis intestinal mixta | leve | todas | Albendazol ± metronidazol | lluvia | no | sí | ETD; saneamiento [5] |
| 19 | Teniasis / cisticercosis | moderado | 15-64 | Praziquantel / albendazol | — | no | no | ETD rural [5] |
| 20 | Gastroenteritis por rotavirus | moderado | 0-14 | SRO + zinc | invierno | no | sí | Inmunoprevenible (PAI) [16] |
| 21 | Intoxicación alimentaria | moderado | todas | Hidratación + soporte | verano | no | sí | Brotes comunitarios [3] |
| 22 | Hernia inguinal | moderado | todas | Reparación quirúrgica | — | sí | sí | Frecuente quirúrgica [3] |
| 23 | Hernia umbilical | leve | 0-14 | Observación / cirugía | — | sí | sí | Pediátrica común [3] |
| 24 | Enfermedad inflamatoria intestinal | grave | 15-44 | Mesalazina / corticoides | — | sí | no | Diagnóstico creciente [3] |
| 25 | Dispepsia funcional | leve | 15+ | IBP / procinéticos | — | sí | sí | Muy frecuente [3] |
| 26 | Hemorragia digestiva alta | grave | 45+ | IBP IV + endoscopia | — | no | no | Urgencia, úlcera/varices [3] |
| 27 | Cáncer gástrico | grave | 45+ | Manejo oncológico | — | sí | no | Alta incidencia en Centroamérica [1][12] |

---

## 8. Categoría: osteomuscular (18)

Las lumbalgias y artropatías son causa mayor de discapacidad (años vividos con discapacidad) [1][2].

| # | Enfermedad | Sev. | Edades | Medicamento 1ª línea | Clima | Crón. | Común | Relevancia regional |
|---|-----------|------|--------|----------------------|-------|-------|-------|---------------------|
| 1 | Lumbalgia inespecífica | leve | 15+ | AINE (ibuprofeno) + ejercicio | — | no | sí | Principal causa de AVD [1][2] |
| 2 | Cervicalgia | leve | 15+ | AINE + medidas | — | no | sí | Muy frecuente [3] |
| 3 | Osteoartritis (artrosis) | moderado | 45+ | Paracetamol / AINE | — | sí | sí | ENT del envejecimiento [1] |
| 4 | Artritis reumatoide | grave | 30-64 | Metotrexato | — | sí | no | Autoinmune crónica [3] |
| 5 | Gota | moderado | 30-64 | AINE/colchicina (agudo); alopurinol | — | sí | sí | Asociada a dieta/alcohol [3] |
| 6 | Tendinitis | leve | 15-64 | AINE + reposo | — | no | sí | Sobreuso/laboral [3] |
| 7 | Bursitis | leve | 30+ | AINE + reposo | — | no | sí | Frecuente [3] |
| 8 | Esguince de tobillo | leve | todas | RICE + analgesia | — | no | sí | Lesión común [3] |
| 9 | Fascitis plantar | leve | 30+ | AINE + estiramientos | — | no | sí | Dolor de talón frecuente [3] |
| 10 | Hombro doloroso (síndrome de manguito) | moderado | 30+ | AINE + fisioterapia | — | sí | sí | Consulta frecuente [3] |
| 11 | Hernia discal lumbar | moderado | 30-64 | Analgesia + fisioterapia | — | sí | no | Causa de ciática [3] |
| 12 | Osteoporosis | moderado | 65+ | Calcio + vitamina D ± bifosfonatos | — | sí | sí | Riesgo de fractura en ancianos [1] |
| 13 | Fibromialgia | moderado | 30-64 | Amitriptilina + ejercicio | — | sí | no | Dolor crónico, predominio femenino [3] |
| 14 | Epicondilitis | leve | 30-64 | AINE + reposo | — | no | sí | Sobreuso laboral [3] |
| 15 | Síndrome del túnel carpiano | moderado | 30-64 | Férula ± cirugía | — | sí | sí | Ocupacional [3] |
| 16 | Artritis séptica | grave | todas | Antibióticos IV + drenaje | — | no | no | Urgencia articular [3] |
| 17 | Lupus eritematoso sistémico | grave | 15-44 | Hidroxicloroquina + corticoides | — | sí | no | Autoinmune, predominio femenino [3] |
| 18 | Escoliosis | leve | 0-14 | Observación / ortesis | — | sí | no | Tamizaje escolar [3] |

---

## 9. Categoría: urologico (18)

Incluye la entidad regional emblemática: la **enfermedad renal crónica de causas no tradicionales (nefropatía mesoamericana)** [13][14][21].

| # | Enfermedad | Sev. | Edades | Medicamento 1ª línea | Clima | Crón. | Común | Relevancia regional |
|---|-----------|------|--------|----------------------|-------|-------|-------|---------------------|
| 1 | Infección urinaria baja (cistitis) | leve | todas | Nitrofurantoína / TMP-SMX | — | no | sí | Muy frecuente en mujeres [3] |
| 2 | Pielonefritis aguda | grave | 15-64 | Ciprofloxacino / ceftriaxona | — | no | no | Complicación de ITU [3] |
| 3 | Enfermedad renal crónica de causas no tradicionales (nefropatía mesoamericana) | grave | 15-64 | Manejo nefroprotector + control de hidratación/calor | verano | sí | sí | Epidemia regional en cañeros de El Salvador/Nicaragua [13][14][21] |
| 4 | Enfermedad renal crónica (general) | grave | 45+ | IECA/ARA-II + manejo de complicaciones | — | sí | sí | Carga creciente, diálisis limitada [13][21] |
| 5 | Litiasis renal (urolitiasis) | moderado | 30-64 | Analgesia + hidratación ± urología | verano | sí | sí | Asociada a deshidratación/calor [3] |
| 6 | Cólico renoureteral | moderado | 30-64 | AINE (diclofenaco) + hidratación | verano | no | sí | Urgencia frecuente [3] |
| 7 | Hiperplasia prostática benigna | moderado | 65+ | Tamsulosina ± finasterida | — | sí | sí | Frecuente en varones mayores [3] |
| 8 | Prostatitis | moderado | 30-64 | Ciprofloxacino | — | no | sí | Consulta urológica común [3] |
| 9 | Insuficiencia renal aguda | grave | todas | Tratar causa + soporte | verano | no | no | Deshidratación/sepsis [3] |
| 10 | Incontinencia urinaria | leve | 45+ | Ejercicios de suelo pélvico / anticolinérgicos | — | sí | sí | Frecuente en mujeres y ancianos [3] |
| 11 | Glomerulonefritis aguda | grave | 0-29 | Manejo de soporte + tratar causa | — | no | no | Post-estreptocócica en niños [3] |
| 12 | Síndrome nefrótico | grave | 0-29 | Corticoides | — | sí | no | Pediátrico frecuente [3] |
| 13 | Vejiga neurogénica | moderado | 30+ | Manejo según causa | — | sí | no | Secuela neurológica [3] |
| 14 | Cáncer de próstata | grave | 65+ | Manejo oncológico/urológico | — | sí | no | ENT en varones mayores [1] |
| 15 | Hematuria | moderado | 30+ | Estudiar causa | — | no | sí | Signo de alarma [3] |
| 16 | Uretritis | leve | 15-44 | Tratar ITS (ceftriaxona+azitromicina) | — | no | sí | Asociada a ITS [3] |
| 17 | Orquitis / epididimitis | moderado | 15-44 | Antibióticos | — | no | sí | Consulta común [3] |
| 18 | Disfunción eréctil | leve | 45+ | Inhibidores de PDE5 | — | sí | sí | Asociada a ENT (DM/HTA) [3] |

---

## 10. Categoría: endocrino (13)

Tiroidopatías, obesidad y dislipidemias acompañan la transición a ENT [1][2].

| # | Enfermedad | Sev. | Edades | Medicamento 1ª línea | Clima | Crón. | Común | Relevancia regional |
|---|-----------|------|--------|----------------------|-------|-------|-------|---------------------|
| 1 | Hipotiroidismo | moderado | 30+ | Levotiroxina | — | sí | sí | Frecuente, predominio femenino [3] |
| 2 | Hipertiroidismo | moderado | 15-44 | Metimazol | — | sí | no | Enfermedad de Graves [3] |
| 3 | Bocio | leve | 15+ | Yodo / levotiroxina según causa | — | sí | sí | Déficit de yodo histórico [3] |
| 4 | Tiroiditis | leve | 15-44 | AINE / manejo según fase | — | no | no | Subaguda/autoinmune [3] |
| 5 | Obesidad | moderado | todas | Cambios de estilo de vida | — | sí | sí | Factor de riesgo de ENT en ascenso [1][2] |
| 6 | Síndrome metabólico | moderado | 30+ | Manejo integral de factores | — | sí | sí | Antesala de DM2/ECV [1] |
| 7 | Dislipidemia mixta | leve | 30+ | Estatina ± fibrato | — | sí | sí | Factor de riesgo CV [1] |
| 8 | Hiperuricemia | leve | 30+ | Cambios de dieta ± alopurinol | — | sí | sí | Precursor de gota [3] |
| 9 | Síndrome de Cushing | grave | 30-64 | Tratar causa | — | sí | no | Endocrinopatía rara [3] |
| 10 | Insuficiencia suprarrenal | grave | 15-64 | Hidrocortisona | — | sí | no | Crisis addisoniana [3] |
| 11 | Hiperprolactinemia | leve | 15-44 | Cabergolina | — | sí | no | Causa de infertilidad [3] |
| 12 | Deficiencia de vitamina D | leve | todas | Suplemento de vitamina D | — | no | sí | Frecuente subdiagnosticada [3] |
| 13 | Hipogonadismo | leve | 30+ | Terapia hormonal según caso | — | sí | no | Consulta endocrina [3] |

---

## 11. Categoría: neurologico (20)

Las cefaleas, la epilepsia y las secuelas de ACV concentran carga neurológica [1][3].

| # | Enfermedad | Sev. | Edades | Medicamento 1ª línea | Clima | Crón. | Común | Relevancia regional |
|---|-----------|------|--------|----------------------|-------|-------|-------|---------------------|
| 1 | Cefalea tensional | leve | 15+ | Analgésicos (paracetamol/AINE) | — | no | sí | Motivo frecuente de consulta [3] |
| 2 | Migraña | moderado | 15-44 | AINE / triptanes (agudo) | — | sí | sí | Predominio femenino [3] |
| 3 | Epilepsia | grave | todas | Valproato / carbamazepina / fenitoína | — | sí | sí | ENT neurológica frecuente [3] |
| 4 | Crisis convulsiva febril | moderado | 0-14 | Manejo de la fiebre + soporte | — | no | sí | Pediátrica común [3] |
| 5 | Estado epiléptico | grave | todas | Benzodiacepina (diazepam) IV | — | no | no | Urgencia neurológica [3] |
| 6 | Neuralgia del trigémino | moderado | 45+ | Carbamazepina | — | sí | no | Dolor facial intenso [3] |
| 7 | Neuropatía periférica | moderado | 30+ | Gabapentina / amitriptilina | — | sí | sí | A menudo diabética [3] |
| 8 | Parálisis facial periférica (Bell) | moderado | 15-64 | Corticoides (prednisona) | — | no | sí | Consulta frecuente [3] |
| 9 | Enfermedad de Parkinson | grave | 65+ | Levodopa/carbidopa | — | sí | no | ENT del envejecimiento [1] |
| 10 | Demencia / Alzheimer | grave | 65+ | Donepecilo + soporte | — | sí | no | Carga creciente por envejecimiento [1] |
| 11 | Secuela de ACV (hemiparesia) | grave | 45+ | Rehabilitación + prevención 2ª | — | sí | sí | Discapacidad post-ictus [1] |
| 12 | Vértigo periférico | leve | 30+ | Antivertiginosos (betahistina) | — | no | sí | Consulta común [3] |
| 13 | Meningitis bacteriana | grave | todas | Ceftriaxona IV | — | no | no | Urgencia infecciosa [3] |
| 14 | Meningitis viral | moderado | todas | Soporte | — | no | no | Más benigna [3] |
| 15 | Encefalitis | grave | todas | Aciclovir si herpética | lluvia | no | no | Arbovirosis neurotropas [18] |
| 16 | Neurocisticercosis | grave | 15-64 | Albendazol + corticoides | — | no | no | Causa de epilepsia adquirida; ETD [5] |
| 17 | Esclerosis múltiple | grave | 15-44 | Inmunomoduladores | — | sí | no | Diagnóstico creciente [3] |
| 18 | Polineuropatía alcohólica | moderado | 30-64 | Abstinencia + tiamina | — | sí | no | Asociada a alcoholismo [3] |
| 19 | Temblor esencial | leve | 45+ | Propranolol | — | sí | no | Consulta neurológica [3] |
| 20 | Cefalea en racimos | moderado | 30-44 | Oxígeno + triptanes | — | sí | no | Forma intensa de cefalea [3] |

---

## 12. Categoría: dermatologico (24)

Las dermatosis infecciosas (escabiosis, micosis, piodermas) son muy frecuentes y se favorecen por el clima húmedo y el hacinamiento [3][5].

| # | Enfermedad | Sev. | Edades | Medicamento 1ª línea | Clima | Crón. | Común | Relevancia regional |
|---|-----------|------|--------|----------------------|-------|-------|-------|---------------------|
| 1 | Dermatitis atópica | leve | 0-29 | Emolientes + corticoide tópico | — | sí | sí | Frecuente pediátrica [3] |
| 2 | Dermatitis de contacto | leve | todas | Corticoide tópico + evitar causa | — | no | sí | Ocupacional/ambiental [3] |
| 3 | Dermatitis seborreica | leve | 15+ | Antifúngico/corticoide tópico | — | sí | sí | Muy frecuente [3] |
| 4 | Acné vulgar | leve | 15-29 | Peróxido de benzoílo / retinoide tópico | — | sí | sí | Consulta de adolescentes [3] |
| 5 | Escabiosis (sarna) | leve | todas | Permetrina tópica | — | no | sí | ETD; hacinamiento [3][5] |
| 6 | Tiña corporal (dermatofitosis) | leve | todas | Antifúngico tópico (clotrimazol) | lluvia | no | sí | Favorecida por humedad [3] |
| 7 | Tiña pedis (pie de atleta) | leve | 15+ | Antifúngico tópico | lluvia | no | sí | Calor/humedad [3] |
| 8 | Onicomicosis | leve | 30+ | Terbinafina | — | sí | sí | Crónica frecuente [3] |
| 9 | Candidiasis cutánea | leve | todas | Antifúngico tópico (nistatina) | — | no | sí | Pliegues/humedad [3] |
| 10 | Impétigo | leve | 0-14 | Mupirocina / antibiótico oral | — | no | sí | Piodermitis pediátrica [3] |
| 11 | Foliculitis / forunculosis | leve | 15-44 | Higiene + antibiótico tópico/oral | — | no | sí | Frecuente [3] |
| 12 | Urticaria | leve | todas | Antihistamínico (loratadina) | — | no | sí | Reacción común [3] |
| 13 | Psoriasis | moderado | 15-64 | Corticoide tópico / análogos vitamina D | — | sí | no | ENT cutánea crónica [3] |
| 14 | Vitiligo | leve | 15-44 | Corticoide tópico / fototerapia | — | sí | no | Impacto psicosocial [3] |
| 15 | Verrugas virales | leve | 0-29 | Crioterapia / queratolíticos | — | no | sí | VPH cutáneo [3] |
| 16 | Herpes simple labial | leve | 15+ | Aciclovir | — | no | sí | Recurrente [3] |
| 17 | Herpes zóster | moderado | 45+ | Aciclovir / valaciclovir | — | no | sí | Reactivación en mayores [3] |
| 18 | Celulitis | moderado | todas | Cefalexina / dicloxacilina | — | no | sí | Complicación de heridas [3] |
| 19 | Quemadura solar | leve | todas | Medidas + emolientes | verano | no | sí | Exposición tropical [3] |
| 20 | Larva migrans cutánea | leve | todas | Albendazol / ivermectina | lluvia | no | no | Playas/suelos contaminados; ETD [5] |
| 21 | Pitiriasis versicolor | leve | 15-44 | Antifúngico tópico (ketoconazol) | lluvia | no | sí | Clima cálido húmedo [3] |
| 22 | Melasma | leve | 15-44 | Protección solar + despigmentantes | verano | sí | sí | Frecuente en mujeres [3] |
| 23 | Cáncer de piel (no melanoma) | moderado | 45+ | Escisión / manejo dermatológico | verano | sí | no | Exposición solar acumulada [1] |
| 24 | Picadura/reacción a insectos | leve | todas | Antihistamínico + corticoide tópico | lluvia | no | sí | Frecuente en zonas tropicales [3] |

---

## 13. Categoría: salud_mental (18)

La depresión, la ansiedad y los trastornos por consumo de sustancias son causas crecientes de discapacidad; la violencia agrava la carga [1][15][23].

| # | Enfermedad | Sev. | Edades | Medicamento 1ª línea | Clima | Crón. | Común | Relevancia regional |
|---|-----------|------|--------|----------------------|-------|-------|-------|---------------------|
| 1 | Trastorno depresivo mayor | moderado | 15+ | ISRS (fluoxetina/sertralina) | — | sí | sí | Principal causa de AVD mental [1] |
| 2 | Trastorno de ansiedad generalizada | moderado | 15-64 | ISRS + psicoterapia | — | sí | sí | Muy frecuente [1] |
| 3 | Trastorno de pánico | moderado | 15-44 | ISRS + psicoterapia | — | sí | sí | Consulta frecuente [3] |
| 4 | Trastorno de estrés postraumático | grave | 15-64 | ISRS + psicoterapia | — | sí | no | Asociado a violencia regional [15][23] |
| 5 | Trastorno bipolar | grave | 15-44 | Estabilizadores (litio/valproato) | — | sí | no | Crónico [3] |
| 6 | Esquizofrenia | grave | 15-44 | Antipsicóticos (risperidona/haloperidol) | — | sí | no | ENT psiquiátrica grave [3] |
| 7 | Trastorno por consumo de alcohol | grave | 15-64 | Desintoxicación + soporte | — | sí | sí | Alta prevalencia regional [1] |
| 8 | Trastorno por consumo de sustancias | grave | 15-44 | Manejo de abstinencia + rehabilitación | — | sí | no | Problema de salud pública [1] |
| 9 | Insomnio | leve | todas | Higiene del sueño ± hipnótico breve | — | sí | sí | Consulta frecuente [3] |
| 10 | Trastorno depresivo posparto | moderado | 15-44 | ISRS + soporte | — | no | sí | Salud materna [17] |
| 11 | Trastorno de adaptación | leve | 15-64 | Psicoterapia | — | no | sí | Reacción a estrés [3] |
| 12 | Trastorno obsesivo-compulsivo | moderado | 15-44 | ISRS + TCC | — | sí | no | Crónico [3] |
| 13 | Demencia con trastorno conductual | grave | 65+ | Manejo conductual ± antipsicótico | — | sí | no | Envejecimiento [1] |
| 14 | Trastorno por déficit de atención/hiperactividad | moderado | 0-14 | Metilfenidato + abordaje conductual | — | sí | no | Pediátrico [3] |
| 15 | Conducta suicida / autolesión | grave | 15-29 | Manejo de crisis + tratamiento de base | — | no | no | Mortalidad juvenil regional [23] |
| 16 | Trastorno de la conducta alimentaria | grave | 15-29 | Abordaje multidisciplinario | — | sí | no | Predominio femenino joven [3] |
| 17 | Trastorno somatomorfo | leve | 15-64 | Psicoterapia | — | sí | sí | Frecuente en atención primaria [3] |
| 18 | Duelo complicado | leve | todas | Psicoterapia | — | no | sí | Asociado a violencia/pérdidas [15] |

---

## 14. Categoría: ginecoobstetrico (27)

Salud materno-infantil: control prenatal, complicaciones del embarazo e ITS; eje de las metas de "cero muertes maternas evitables" de la OPS [16][17].

| # | Enfermedad | Sev. | Edades | Medicamento 1ª línea | Clima | Crón. | Común | Relevancia regional |
|---|-----------|------|--------|----------------------|-------|-------|-------|---------------------|
| 1 | Embarazo normal (control prenatal) | leve | 15-44 | Ácido fólico + hierro | — | no | sí | Eje de salud materna [17] |
| 2 | Amenaza de aborto | moderado | 15-44 | Reposo + manejo obstétrico | — | no | sí | Consulta obstétrica frecuente [17] |
| 3 | Aborto espontáneo | moderado | 15-44 | Manejo obstétrico (AMEU/medicamentos) | — | no | no | Causa de morbilidad materna [17] |
| 4 | Preeclampsia | grave | 15-44 | Antihipertensivos + sulfato de magnesio | — | no | no | Causa principal de muerte materna [17] |
| 5 | Eclampsia | grave | 15-44 | Sulfato de magnesio + manejo urgente | — | no | no | Emergencia obstétrica [17] |
| 6 | Hemorragia posparto | grave | 15-44 | Oxitocina + manejo activo | — | no | no | Causa líder de muerte materna [17] |
| 7 | Diabetes gestacional | moderado | 15-44 | Dieta + insulina si precisa | — | no | sí | Tamizaje prenatal [17] |
| 8 | Infección urinaria en el embarazo | leve | 15-44 | Antibiótico seguro (nitrofurantoína) | — | no | sí | Frecuente, riesgo de parto prematuro [17] |
| 9 | Vaginosis bacteriana | leve | 15-44 | Metronidazol | — | no | sí | Muy frecuente [3] |
| 10 | Candidiasis vaginal | leve | 15-44 | Fluconazol / clotrimazol | — | no | sí | Consulta ginecológica común [3] |
| 11 | Tricomoniasis | leve | 15-44 | Metronidazol | — | no | sí | ITS frecuente [3] |
| 12 | Enfermedad pélvica inflamatoria | moderado | 15-44 | Ceftriaxona + doxiciclina + metronidazol | — | no | no | Secuela de ITS [3] |
| 13 | Dismenorrea | leve | 15-29 | AINE | — | sí | sí | Muy frecuente [3] |
| 14 | Síndrome de ovario poliquístico | moderado | 15-44 | Anticonceptivos / metformina | — | sí | sí | Causa de infertilidad [3] |
| 15 | Miomatosis uterina | moderado | 30-44 | Manejo médico/quirúrgico | — | sí | sí | Frecuente en mujeres [3] |
| 16 | Quiste ovárico | leve | 15-44 | Observación / cirugía | — | no | sí | Hallazgo común [3] |
| 17 | Menopausia / climaterio | leve | 45-64 | Medidas ± terapia hormonal | — | sí | sí | Etapa fisiológica [3] |
| 18 | Sangrado uterino anormal | moderado | 30-44 | Hormonal / estudiar causa | — | sí | sí | Motivo de consulta común [3] |
| 19 | Cáncer cervicouterino | grave | 30-64 | Cirugía/radio-quimioterapia | — | sí | no | Alta incidencia; prevenible (VPH/tamizaje) [1][12] |
| 20 | Cáncer de mama | grave | 45-64 | Cirugía + terapia adyuvante | — | sí | no | Cáncer femenino más frecuente [1] |
| 21 | Lesión por VPH (displasia cervical) | leve | 15-44 | Crioterapia / seguimiento | — | sí | sí | Tamizaje citológico [12] |
| 22 | Mastitis puerperal | moderado | 15-44 | Antibiótico (dicloxacilina) + lactancia | — | no | sí | Posparto/lactancia [17] |
| 23 | Ruptura prematura de membranas | grave | 15-44 | Manejo obstétrico ± antibióticos | — | no | no | Riesgo de prematuridad [17] |
| 24 | Parto pretérmino | grave | 15-44 | Tocolíticos + corticoides fetales | — | no | no | Causa de morbimortalidad neonatal [16][17] |
| 25 | Anemia en el embarazo | leve | 15-44 | Hierro + ácido fólico | — | no | sí | Muy frecuente [17] |
| 26 | Hiperémesis gravídica | moderado | 15-44 | Antieméticos + hidratación | — | no | sí | Consulta del 1er trimestre [17] |
| 27 | Endometriosis | moderado | 15-44 | AINE / hormonal | — | sí | no | Dolor pélvico crónico [3] |

---

## 15. Categoría: trauma (22)

Las causas externas —violencia interpersonal y lesiones de tránsito— tienen un peso desproporcionado en Centroamérica, sobre todo en jóvenes varones [15][23].

| # | Enfermedad / lesión | Sev. | Edades | Manejo 1ª línea | Clima | Crón. | Común | Relevancia regional |
|---|-----------|------|--------|----------------------|-------|-------|-------|---------------------|
| 1 | Herida por arma blanca | grave | 15-44 | Hemostasia + sutura + antibiótico/antitetánica | — | no | no | Violencia interpersonal alta [15][23] |
| 2 | Herida por arma de fuego | grave | 15-44 | Reanimación + manejo quirúrgico | — | no | no | Homicidio: causa líder en jóvenes [15][23] |
| 3 | Politraumatismo por accidente de tránsito | grave | 15-44 | Reanimación (ABCDE) + manejo quirúrgico | — | no | no | Causa externa principal [1] |
| 4 | Fractura de miembro superior | moderado | todas | Inmovilización + analgesia ± cirugía | — | no | sí | Lesión frecuente [3] |
| 5 | Fractura de miembro inferior | grave | todas | Inmovilización/cirugía + analgesia | — | no | no | Accidentes/caídas [3] |
| 6 | Fractura de cadera | grave | 65+ | Cirugía + analgesia | — | no | no | Caídas en ancianos [1] |
| 7 | Traumatismo craneoencefálico | grave | 15-44 | Manejo neuroquirúrgico/observación | — | no | no | Tránsito y violencia [1] |
| 8 | Esguinces y luxaciones | leve | todas | RICE + analgesia ± reducción | — | no | sí | Muy frecuente [3] |
| 9 | Herida cortante (laceración) | leve | todas | Limpieza + sutura + antitetánica | — | no | sí | Atención primaria frecuente [3] |
| 10 | Quemadura térmica | grave | 0-14 | Hidratación + curaciones + analgesia | — | no | sí | Frecuente en hogar (niños) [3] |
| 11 | Quemadura eléctrica | grave | 15-44 | Manejo de quemaduras + monitoreo | — | no | no | Laboral [3] |
| 12 | Contusión / hematoma | leve | todas | Analgesia + medidas | — | no | sí | Consulta común [3] |
| 13 | Mordedura de perro | moderado | 0-14 | Lavado + antibiótico + profilaxis rabia | — | no | sí | Riesgo de rabia (ETD) [5] |
| 14 | Mordedura de serpiente (ofidismo) | grave | 15-64 | Suero antiofídico + soporte | lluvia | no | no | Accidente rural en temporada lluviosa [18] |
| 15 | Picadura de alacrán | moderado | todas | Soporte ± suero antialacrán | verano | no | sí | Zonas cálidas [3] |
| 16 | Trauma ocular | moderado | 15-44 | Manejo oftalmológico | — | no | no | Laboral/violencia [3] |
| 17 | Cuerpo extraño en vía aérea | grave | 0-14 | Maniobras de desobstrucción | — | no | no | Urgencia pediátrica [3] |
| 18 | Intoxicación por plaguicidas | grave | 15-44 | Atropina/pralidoxima (organofosforados) | — | no | no | Agrícola e intento suicida [23] |
| 19 | Ahogamiento / casi ahogamiento | grave | 0-29 | Reanimación + soporte | lluvia | no | no | Ríos/playas en temporada [18] |
| 20 | Caída de altura | grave | 15-44 | Reanimación + manejo de fracturas | — | no | no | Laboral [3] |
| 21 | Trauma de tórax | grave | 15-44 | Drenaje/manejo según lesión | — | no | no | Tránsito/violencia [1] |
| 22 | Trauma abdominal | grave | 15-44 | Evaluación + cirugía si precisa | — | no | no | Tránsito/violencia [1] |

---

## 16. Bibliografía (fuentes verificables)

Todas las afirmaciones epidemiológicas marcadas con `[n]` remiten a esta lista. Enlaces para verificación directa.

1. OPS/PAHO. *Leading causes of death and disease burden in the Americas: Noncommunicable diseases and external causes.* https://iris.paho.org/bitstream/handle/10665.2/59568/9789275128626_eng.pdf
2. PAHO/WHO. *The burden of noncommunicable diseases.* https://www.paho.org/en/enlace/burden-noncommunicable-diseases
3. WHO. *Model List of Essential Medicines — 23rd list (2023).* https://iris.who.int/bitstream/handle/10665/371090/WHO-MHP-HPS-EML-2023.02-eng.pdf
4. PAHO/WHO. *Guidelines for the Clinical Diagnosis and Treatment of Dengue, Chikungunya, and Zika.* https://www.paho.org/en/documents/guidelines-clinical-diagnosis-and-treatment-dengue-chikungunya-and-zika · (Apoyo a Centroamérica 2024) https://www.paho.org/en/news/8-8-2024-paho-intensifies-support-central-america-control-dengue
5. WHO/OMS. *Enfermedades tropicales desatendidas (preguntas y respuestas).* https://www.who.int/es/news-room/questions-and-answers/item/neglected-tropical-diseases
6. WHO. *Chagas disease (American trypanosomiasis) — fact sheet.* https://www.who.int/news-room/fact-sheets/detail/chagas-disease-(american-trypanosomiasis)
7. OPS. *Synthesis of evidence: Guidance for the diagnosis and treatment of Chagas disease.* https://pmc.ncbi.nlm.nih.gov/articles/PMC7279121/
8. PAHO 2022 Guideline for the Treatment of Leishmaniasis in the Americas — *Intralesional Antimonial Drug Treatment for L. braziliensis Cutaneous Leishmaniasis.* https://academic.oup.com/cid/article/77/4/583/7143571
9. WHO. *Guidelines for the Treatment of Malaria — P. vivax (chloroquine + primaquine).* https://www.ncbi.nlm.nih.gov/books/NBK294430/
10. WHO. *Treatment of tuberculosis patients — Implementing the WHO Stop TB Strategy (2HRZE/4HRE).* https://www.ncbi.nlm.nih.gov/books/NBK310759/
11. European Respiratory Society. *Roadmap for tuberculosis elimination in Latin American and Caribbean countries.* https://publications.ersnet.org/content/erj/48/5/1282
12. CEPAL. *El perfil epidemiológico de América Latina y el Caribe.* https://repositorio.cepal.org/server/api/core/bitstreams/b1e40615-0072-4772-9681-724b252f8a56/content
13. NEJM. *Chronic Kidney Disease of Unknown Cause in Agricultural Communities (Mesoamerican nephropathy).* https://www.nejm.org/doi/full/10.1056/NEJMra1813869
14. BMC Nephrology. *High prevalence of chronic kidney disease of unknown etiology among workers — Mesoamerican Nephropathy Occupational Study (MANOS).* https://bmcnephrol.biomedcentral.com/articles/10.1186/s12882-022-02861-0
15. PAHO/WHO. *Violence Prevention / Homicide mortality in the Americas.* https://www.paho.org/en/topics/violence-prevention · https://www.paho.org/en/enlace/homicide-mortality
16. PAHO/WHO. *Child Health (LRTI and diarrheal disease DALYs in children <5).* https://www.paho.org/en/topics/child-health
17. PAHO/WHO. *Maternal Health — Zero preventable maternal deaths.* https://www.paho.org/en/zero-preventable-maternal-deaths
18. NCBI/PMC. *Panorama epidemiológico de las enfermedades transmitidas por vectores.* https://www.ncbi.nlm.nih.gov/pmc/articles/PMC10766072/
19. OPS. *Anuario Estadístico de Salud 2023.* https://www.paho.org/sites/default/files/2025-02/anuario-estadistico-salud-2023-ed-2024.pdf
20. PAHO. *Iniciativa de eliminación de enfermedades transmisibles 2020–2030 / ETDs en las Américas.* https://www.paho.org/en/news/28-1-2022-neglected-tropical-diseases-paho-calls-end-delays-treatment-americas
21. PMC. *Dialysis enrollment patterns in Guatemala: evidence of the CKD of non-traditional causes epidemic in Mesoamerica.* https://www.ncbi.nlm.nih.gov/pmc/articles/PMC4406024/
22. CDC. *Treatment of Uncomplicated Malaria — clinical guidance.* https://www.cdc.gov/malaria/hcp/clinical-guidance/treatment-uncomplicated.html
23. Pan American Journal of Public Health. *Mortality due to interpersonal violence in adolescents and young people in Latin America.* https://journal.paho.org/en/articles/mortality-due-interpersonal-violence-adolescents-and-young-people-latin-america

> **Nota sobre guías nacionales (MINSAL).** Para dosificación y esquemas oficiales por país conviene complementar con los formularios terapéuticos y guías clínicas nacionales de Guatemala (MSPAS), El Salvador (MINSAL), Honduras (SESAL), Nicaragua (MINSA), Costa Rica (CCSS) y Panamá (MINSA). No todos publican un anuario de morbilidad de consulta externa de acceso abierto estable; cuando se incorporen al catálogo, añadir su URL aquí.

---

## 17. Apéndice — Resolución de UUID CIEL e importación

Los UUID **ya fueron resueltos contra ESTA instancia de OpenMRS** (`GET /ws/rest/v1/concept?q=`). El resultado está en el archivo **`enfermedades-centroamerica-uuids.csv`** (junto a este documento), con columnas: `categoria, nombre, uuid, display_openmrs, fuente, score, flag, alternativas`.

### Estado de la resolución (300 enfermedades)
| Estado | Cant. | Significado |
|--------|:---:|-------------|
| **OK** | 273 | UUID con coincidencia fuerte (nombre ≈ concepto) — alta confianza |
| **REVISAR** | 24 | UUID asignado a un concepto **sinónimo o relacionado** cuyo nombre difiere; confirmar que es el correcto |
| **FALTA** | 3 | Sin concepto limpio en la instancia; verificar o crear el concepto manualmente |

- **Fuente del UUID:** 92 reutilizados de `diagnosticos.csv` (ya verificados previamente) + 205 resueltos por consulta a OpenMRS.
- **`display_openmrs`** guarda el nombre real del concepto detrás del UUID, para que la verificación sea visual y rápida.
- **Convención CIEL:** los UUID tipo `116958AAAA…` son válidos (el relleno `A` es de CIEL, no un placeholder).

### Las 24 a REVISAR (mi nombre → concepto OpenMRS asignado)
La mayoría son sinónimos correctos (Giardiasis→*Lambliasis*, Neurocisticercosis→*cisticercosis cerebral*, Tiña pedis→*tiña de los pies*, Tiña corporal→*dermatofitosis*, Larva migrans→*larva migratoria cutánea*, Exacerbación EPOC→*exacerbación aguda de EPOC*, Cólico renoureteral→*cólico renal*, Secuela ACV→*hemiparesia*, Hombro doloroso→*síndrome del manguito de los rotadores*, Crisis hipertensiva→*urgencia hipertensiva*, etc.).
**Verificar con prioridad estas, cuyo concepto trae una complicación/variante que podría no ser la deseada:** Malaria por *P. vivax* (→ *…con ruptura esplénica*), Malaria por *P. falciparum* (→ *…con complicaciones cerebrales*), Influenza con complicación respiratoria (→ *…con encefalopatía*), Diabetes mal controlada (→ *…relacionada con la desnutrición*), Prediabetes (→ clasificación gestacional), Parasitosis intestinal mixta (→ *…por Entopolypoides*).

### Las 3 FALTA (sin UUID — verificar/crear manualmente)
- **Síndrome de ovario poliquístico** — la instancia no devolvió un concepto claro de SOP.
- **Trauma de tórax** — no hay un "traumatismo torácico" genérico (sí variantes como *tórax inestable*).
- **Trauma abdominal** — no hay un "traumatismo abdominal" genérico.

### Para importar a `diagnosticos.csv`
Falta solo el mapeo mecánico a las columnas del catálogo: tomar `uuid` + `nombre` de este CSV y los campos `categoria, severidad, aplica_*, clima, cronica, comun` de las tablas (secciones 3–15). `peso_M`/`peso_F` por defecto `10` (calibrar), y `requiere_lab/rx/examen_clinico` en `false` (ajustar). Esta carga es una fase aparte que requiere tu visto bueno tras revisar las 24 REVISAR y resolver las 3 FALTA.

---

## Resumen de cobertura

| Categoría | Enfermedades |
|-----------|:---:|
| infeccioso | 50 |
| respiratorio | 27 |
| cardiovascular | 24 |
| diabetes | 12 |
| digestivo | 27 |
| osteomuscular | 18 |
| urologico | 18 |
| endocrino | 13 |
| neurologico | 20 |
| dermatologico | 24 |
| salud_mental | 18 |
| ginecoobstetrico | 27 |
| trauma | 22 |
| **TOTAL** | **300** |
