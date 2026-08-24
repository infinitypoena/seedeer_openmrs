# Plan de continuidad clínica y terapias

## Estado

- Rama de trabajo: `develop6`
- Corrida de validación durante el desarrollo: 1 mes
- Corrida longitudinal de 18 meses: solo validación final de L1, L4 y L7
- Estados: `[ ]` pendiente, `[>]` en curso, `[x]` realizado, `[!]` bloqueado

## Objetivo

Mejorar el simulador para que los retornos tengan un motivo clínico explícito, que las condiciones persistentes no se confundan con episodios agudos y que los tratamientos se seleccionen a partir de investigación clínica aplicable a los diagnósticos presentes en `catalogs/diagnosticos.csv`.

## Decisiones clínicas

- `[x]` La gripe y otros diagnósticos `cronica=false` son episodios temporales.
- `[x]` Solo `cronica=true` define persistencia.
- `[x]` Un diagnóstico provisional puede generar condición y tratamiento.
- `[x]` Los retornos espontáneos tendrán motivo simulado: mismo episodio, crónica activa o nueva consulta.
- `[x]` Una cita conserva prioridad y el diagnóstico que la originó.
- `[x]` En controles se atiende el motivo y las crónicas activas relacionadas.
- `[x]` El estado no persistirá entre ejecuciones.
- `[x]` La validación operativa principal será de un mes.
- `[x]` Se investigarán tratamientos reales para las enfermedades del catálogo, distinguiendo primer nivel y referencia hospitalaria.

## Fases

### 1. Estado clínico y condiciones `[x]`

- `[x]` Confirmar el flujo de `SimulatedPatient`, `SeedOrchestrator`, `RecurrentSelector`, `ConditionSeeder` y `MiniClinica`.
- `[x]` Actualizar `ProblemListConcepts` solo después de un `POST /condition` exitoso.
- `[x]` Añadir prueba de fallo y reintento en condiciones.
- `[x]` Añadir prueba específica de deduplicación después de un POST exitoso.
- `[ ]` Mantener separados episodios agudos, crónicas y comorbilidades agudas.
- `[ ]` Añadir motivos textuales específicos para controles.

### 2. Motivo explícito de retorno `[>]`

- `[ ]` Crear un selector puro para decidir el motivo del retorno.
- `[ ]` Mantener la prioridad de citas agendadas.
- `[ ]` Añadir probabilidades configurables para control agudo, control crónico y consulta nueva.
- `[ ]` Validar rangos y suma de probabilidades.
- `[x]` Propagar el tipo de control como estado por visita (`TipoRetorno`).
- `[ ]` Sincronizar `MiniClinica` con el orquestador.

### 3. Investigación y catálogo de medicamentos `[>]`

- `[x]` Inventariar todos los diagnósticos de `diagnosticos.csv` por categoría, cronicidad, severidad y ámbito.
- `[!]` La documentación declara 407 diagnósticos, pero el catálogo actual contiene 604; la cifra deberá corregirse durante la auditoría documental.
- `[>]` Investigar tratamientos de primer nivel por familias y comenzar con diagnósticos prioritarios.
- `[x]` Documentar fuentes y distinguir manejo sintomático, tratamiento condicionado, seguimiento y referencia.
- `[x]` Clasificar todas las categorías para impedir recetas aleatorias en diagnósticos aún no investigados.
- `[x]` Diseñar y cargar `tratamientos.csv` separado del catálogo maestro de medicamentos.
- `[x]` Documentar fuentes consultadas y reglas iniciales en `investigacion-tratamientos.md`.
- `[x]` Ampliar `medicamentos.csv` con cuatro presentaciones verificadas en OpenMRS.
- `[x]` Añadir 24 relaciones diagnóstico-medicamento investigadas con fuentes explícitas.
- `[ ]` Completar la matriz específica de los diagnósticos restantes; mientras tanto usan políticas conservadoras sin receta automática.
- `[ ]` Auditar `medicamentos.csv` y verificar productos reales de OpenMRS.
- `[ ]` Completar posologías y metadatos de seguridad.

### 4. Selector diagnóstico-terapia `[>]`

- `[x]` Crear `TreatmentSelector` como seam puro y determinista.
- `[x]` Priorizar reglas por diagnóstico y usar categoría solo como fallback.
- `[ ]` Filtrar por edad, sexo, embarazo, severidad y ámbito.
- `[ ]` Evitar duplicidades terapéuticas.
- `[x]` Integrar el selector en `PrescriptionSeeder`.
- `[ ]` Mantener referencias sin tratamiento definitivo en la clínica.

### 5. Continuidad farmacológica `[ ]`

- `[ ]` Añadir tratamientos activos a `SimulatedPatient`.
- `[ ]` Separar estado clínico de `OrderedConcepts`.
- `[ ]` Renovar tratamientos activos al vencer.
- `[ ]` Registrar estado solo después de crear la orden correctamente.
- `[ ]` Mantener tratamiento coherente para el motivo y crónicas activas.

### 6. Validación y documentación `[ ]`

- `[ ]` Validar UUIDs de fármacos, conceptos, vías, frecuencias y unidades.
- `[ ]` Añadir pruebas de condiciones, retornos y tratamientos.
- `[ ]` Extender `MiniClinica` con farmacoterapia ligera.
- `[ ]` Ejecutar pruebas de integración y una corrida de tres meses con cinco pacientes diarios, previa autorización; aún no ejecutado.
- `[ ]` Revisar las leyes L1-L9 en la validación final longitudinal.
- `[ ]` Actualizar documentación, consultas QA y scripts de verificación.

## Verificación por fase

1. Ejecutar primero la prueba enfocada del cambio.
2. Ejecutar la suite completa después de cada fase de código.
3. Ejecutar validación de catálogos y UUIDs antes de sembrar datos.
4. Ejecutar una corrida de un mes para validación operativa.
5. Ejecutar una corrida de 18 meses únicamente al finalizar, para las leyes longitudinales.

## Cambios realizados

- `ConditionSeeder` ya no marca una condición como creada cuando el POST REST falla.
- Añadida prueba de regresión para el reintento de condiciones.
- Añadido `tratamientos.csv` con políticas para las 13 categorías y reglas específicas iniciales.
- `TreatmentSelector` reemplaza la selección aleatoria amplia por categoría.
- Añadidas validaciones de referencias internas y pruebas del selector.
- La suite completa queda en 521 pruebas correctas.

## Corrida de observación de tres meses

- Fecha solicitada: `2026-07-01` a `2026-09-30`.
- Configuración usada: `PacientesPorDiaMedio=1`, `RandomSeed=4242`, crecimiento desactivado y salida aislada en `output/corrida-3-meses-2026`.
- Resultado: 92 días calendario, 75 días con atención, 75 pacientes distintos y 95 visitas.
- Retornos: 20 visitas recurrentes; 18 pacientes volvieron al menos una vez; padrón final con 57 pacientes de 1 visita, 16 de 2 y 2 de 3.
- Agenda: 20 citas cumplidas y 6 no-show (23,1 %).
- Aforo: 0 retornos desplazados y 0 altas no captadas.
- Referencias: 6 en total y 0 remisiones repetidas en controles post-alta.
- L1 no se cumplió: 24 % de 25 pacientes crónicos volvieron durante esta ventana corta.
- Importante: el sistema limpia el episodio agudo interno después de su control, pero `ConditionSeeder` solo crea condiciones crónicas con estado `ACTIVE`; todavía no existe un cierre/inactivación clínica explícita en OpenMRS. Esa mejora sigue pendiente.
- La etapa de fechas de auditoría se ejecutó porque `OpenMRS.Database.CorregirFechas=true`; encontró 585600 filas fuera de la ventana por datos SIM- previos y reportó verificación fallida aunque no dejó filas pendientes.
