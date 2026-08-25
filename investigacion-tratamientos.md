# Investigación de tratamientos del catálogo

## Alcance

El catálogo actual contiene 604 diagnósticos, 208 marcados como crónicos y 82 con `ambito=referencia`. El simulador representa una clínica de primer nivel, por lo que no debe crear recetas definitivas para cuadros quirúrgicos, emergencias, cánceres que requieren tratamiento especializado ni diagnósticos enviados al hospital.

La matriz se construye en dos niveles:

1. Política por categoría: garantiza que todo diagnóstico tenga una clasificación segura aunque todavía no exista una regla individual.
2. Regla por diagnóstico: permite recetar solo cuando la indicación fue revisada y existe un producto válido en OpenMRS.

Los diagnósticos sin regla individual no quedan sin clasificación: usan `manejo_sintomatico`, `tratamiento_condicionado`, `seguimiento` o `referencia`, pero no reciben una receta automática.

## Fuentes consultadas

- OMS, WHO PEN para enfermedades cardiovasculares, diabetes y respiratorias crónicas en atención primaria: <https://www.who.int/publications/i/item/9789240009226>
- OMS, lista modelo de medicamentos esenciales 2023: <https://www.who.int/publications/i/item/WHO-MHP-HPS-EML-2023.02>
- OPS/OMS, diagnóstico y tratamiento de dengue, chikungunya y Zika: <https://www.paho.org/en/documents/guidelines-clinical-diagnosis-and-treatment-dengue-chikungunya-and-zika>
- OMS, tratamiento de sífilis: <https://www.who.int/publications/i/item/9789241549714>
- CDC, guías de tratamiento de infecciones de transmisión sexual: <https://www.cdc.gov/std/treatment-guidelines/default.htm>
- NICE, prescripción antimicrobiana para infección urinaria baja: <https://www.nice.org.uk/guidance/ng109>
- OMS, resistencia antimicrobiana: <https://www.who.int/news-room/fact-sheets/detail/antimicrobial-resistance>
- OMS, guía mhGAP para salud mental, neurológica y consumo de sustancias: <https://www.who.int/publications/i/item/9789241549790>

Estas fuentes orientan la clasificación y las familias de tratamiento. No se copiaron dosis sin verificar la guía local, el contexto del paciente y los UUID de la instancia OpenMRS.

## Reglas iniciales implementadas

| Diagnóstico | Acción | Producto | Justificación de simulación |
|---|---|---|---|
| Influenza (gripe) | Manejo sintomático | Paracetamol | La gripe no complicada no debe activar antibiótico; continúa siendo episodio agudo. |
| Hipertensión arterial | Tratamiento específico | Enalapril | Manejo crónico de primer nivel, sujeto a evaluación clínica. |
| Diabetes mellitus tipo 2 | Tratamiento específico | Metformina | Manejo crónico de primer nivel, sujeto a control metabólico. |
| Asma bronquial | Tratamiento específico | Salbutamol | Tratamiento de rescate para enfermedad estable; exacerbación grave sigue la política de referencia. |
| ERGE | Tratamiento específico | Omeprazol | Manejo sintomático/específico de primer nivel, sujeto a signos de alarma. |
| Tiña corporal | Tratamiento específico | Ketoconazol | Dermatomicosis superficial sin datos de extensión o complicación. |
| Parasitosis intestinal | Tratamiento específico | Albendazol | Tratamiento antiparasitario condicionado a diagnóstico y contexto. |
| Lumbalgia / osteoartrosis | Manejo sintomático | Paracetamol | Evita convertir antiinflamatorios en elección universal; la seguridad se ampliará en reglas futuras. |
| Cistitis / ITU | Tratamiento condicionado | Sin receta automática todavía | La selección depende de sexo, embarazo, edad, recurrencia, resistencia local y localización. |

## Exclusiones deliberadas

No se receta automáticamente para:

- Diagnósticos con `ambito=referencia`.
- Influenza, resfriado, bronquitis viral, dengue, chikungunya o Zika no complicados con antibióticos.
- Síntomas aislados como cefalea, vértigo, fiebre sin foco, disnea o malestar general.
- Cáncer, melanoma, demencia, enfermedades neurodegenerativas y lesiones que requieren biopsia o especialista.
- Embarazo normal y control prenatal como si fueran una enfermedad farmacológica.
- Tuberculosis, VIH, hepatitis crónica, salud mental grave y pie diabético sin modelar antes el programa y el protocolo correspondiente.
- Antibióticos, corticoides sistémicos, benzodiacepinas, insulina y antifúngicos sistémicos sin contexto suficiente.

## Próxima ampliación

1. Revisar diagnósticos comunes por frecuencia observada en las corridas y crear reglas específicas con fuente.
2. Añadir contexto de edad, sexo, embarazo, lactancia, severidad, función renal/hepática y alergias.
3. Verificar `drug_uuid`, `concept_uuid`, vía, unidad y frecuencia contra la instancia OpenMRS.
4. Incorporar alternativas y prioridades sin convertir la selección en polifarmacia aleatoria.
5. Hacer que los controles crónicos renueven tratamientos activos en vez de reseleccionar productos.

La falta de regla individual se considera una decisión segura y visible, no una autorización para usar el fallback antiguo de selección aleatoria por categoría.

## Productos adicionales verificados en OpenMRS

La instancia OpenMRS local devolvió estas presentaciones adicionales mediante `GET /drug?v=full`:

| Producto | `drug_uuid` | Concepto | Uso previsto |
|---|---|---|---|
| Amoxicilina 400 mg/5 ml | `9e99eb20-5172-433a-ba7e-669b0f2af50b` | `71160AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` | Presentación pediátrica; requiere cálculo por peso y contexto infeccioso. |
| Amoxicilina 250 mg/5 ml | `0e99b832-3cad-4039-bfb5-0c2f55906f8a` | `71160AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` | Presentación pediátrica; requiere cálculo por peso y contexto infeccioso. |
| Salbutamol 4 mg | `0f4627ca-7bb4-414f-a07e-5d24c0d3a442` | `9088359a-587f-4dbc-b13e-b3c38a07cd85` | Alternativa oral; no sustituye automáticamente al inhalador. |
| Salbutamol 2 mg/5 ml | `d39b535c-9a32-49c1-b339-e512d811c44d` | `9088359a-587f-4dbc-b13e-b3c38a07cd85` | Presentación líquida; requiere edad, peso y contexto respiratorio. |

También se verificaron unidades de la instancia: vía oral `160240...`, inhalación `160241...`, comprimido
`1513...`, mililitro `162263...` y jarabe `1515...`; y frecuencias una/dos/tres veces al día.
Las presentaciones nuevas se agregaron al inventario, pero no se enlazaron a `tratamientos.csv` todavía:
la selección actual no calcula dosis por kg y conectarlas sin esa capacidad sería introducir un valor por
defecto disfrazado de tratamiento real.

## Estado de esta investigación

- `medicamentos.csv`: 67 productos, todos con `drug_uuid`, `concept_uuid`, `grupo_terapeutico`, `requiere_contexto` y posología opcional; verificados contra OpenMRS.
- `tratamientos.csv`: 617 reglas, una por cada diagnóstico actual (604) más 13 fallbacks de categoría.
- Reglas farmacológicas específicas actuales: 118 reglas con producto válido y fuente explícita (81 `tratamiento_especifico` + 37 `manejo_sintomatico`); incluyen influenza sintomática, hipertensión, diabetes tipo 2, asma, ERGE, tiñas, parasitosis, herpes, hipotiroidismo, dislipidemia, ITS y cuadros bacterianos condicionados.
- Diagnósticos restantes: clasificación explícita sin receta automática, seguimiento o referencia.
- Referencias cruzadas: 0 UUIDs de diagnóstico o medicamento huérfanos.
- Prueba de tres meses: pendiente de autorización del usuario; todavía no se ejecutó `dotnet test` después de esta ampliación ni la simulación.
