# OpenMRS Clinical Simulator

Aplicación de consola en **C# .NET 10** que actúa como **simulador clínico** para una instancia [OpenMRS 3.x](https://openmrs.org/): se ejecuta una vez, genera el historial completo de una clínica de consulta externa durante el período configurado, y termina.

Lo que genera (en español, epidemiológica y clínicamente coherente):

- **Pacientes** con nombres y direcciones centroamericanos realistas, edad y género según distribución configurable.
- **Visitas completas**: vitales coherentes con la enfermedad, consulta con diagnóstico (por edad/sexo/estación, con comorbilidades), órdenes de laboratorio **con resultado**, prescripciones, alergias y cierre de visita.
- **Continuidad asistencial**: crónicos que vuelven al control de su enfermedad con su médico de cabecera, problem list, inscripción en programas (VIH, diabetes) y **citas reales en la agenda de O3** con no-shows.

> **El sembrado va 100 % por la REST API** (`/ws/rest/v1`) — no se escribe SQL. La única excepción es la etapa 5/5 (*corrección de fechas de auditoría*), **desactivada por defecto**: existe porque OpenMRS sella `date_created` con el reloj real del servidor y por REST no hay forma de mandarlo. Ver [`correccion_fechas.md`](correccion_fechas.md).

## Inicio rápido

Requisitos: [.NET SDK 10+](https://dotnet.microsoft.com/download) y una instancia OpenMRS 3.x con su REST API accesible.

```bash
# 1. Configurar credenciales y ventana de simulación
cp openmrs_seeder_v1/openmrs_seeder_v1/appsettings.example.json \
   openmrs_seeder_v1/openmrs_seeder_v1/appsettings.json
# Editar: OpenMRS.RestApi (URL/usuario/contraseña) y Simulation.StartDate/EndDate

# 2. Ejecutar — corre la simulación completa y termina
dotnet run --project openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj
```

> `appsettings.json` y `docker/.env` están en `.gitignore` — las credenciales nunca se suben al repositorio.

## Comandos

Todos se lanzan con `--project openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj` (omitido aquí por brevedad):

| Comando | Qué hace |
|---------|----------|
| `dotnet run` | **Corrida completa**: las 5 etapas y termina |
| `dotnet run -- clear` | **Anula** (void) todos los pacientes `SIM-` y **cancela** sus citas pendientes. Pide confirmación `s/N` |
| `dotnet run -- fechas --dry-run` | Informa cuántas filas de auditoría corregiría, **sin escribir nada** |
| `dotnet run -- fechas` | Retrofecha `date_created` de datos **ya sembrados** (la etapa 5/5 por separado) |
| `dotnet test openmrs_seeder_v1/openmrs_seeder_v1.Tests/openmrs_seeder_v1.Tests.csproj` | Suite de tests (279) |

**Exit codes**: `0` completado · `1` fallo del proceso · `2` OpenMRS inaccesible, argumento inválido o **catálogos inválidos** — en los tres casos **no se toca ningún dato**.

**Ctrl+C** cancela limpiamente: lo ya insertado persiste y se imprime el resumen parcial.

### Con Docker (sin instalar .NET)

```bash
cd docker && cp .env.example .env   # editar URL y contraseña
docker compose -f docker/docker-compose.yml run --rm seeder          # simulación
docker compose -f docker/docker-compose.yml run --rm -i seeder clear # limpieza
```

> OpenMRS en el mismo host: usar `host.docker.internal` (Windows/Mac) o `172.17.0.1` (Linux) en `OPENMRS_URL`.

### Utilidades

| Script | Para qué |
|--------|----------|
| [`scripts/backup_openmrs.ps1`](scripts/backup_openmrs.ps1) | Respaldo de la BD (dump + gzip + retención). **Correr antes de la etapa de fechas** |
| [`scripts/agregar_nombres_es.ps1`](scripts/agregar_nombres_es.ps1) | Añade el nombre en español a los conceptos CIEL que no lo traen (idempotente) |
| [`scripts/ajustar_diagnosticos.ps1`](scripts/ajustar_diagnosticos.ps1) | Normaliza las columnas por reglas de `diagnosticos.csv` (`vital_*`, `cronica`, `sexo`…) |
| [`scripts/verificar_uuids.ps1`](scripts/verificar_uuids.ps1) | **Contrasta cada UUID de los catálogos contra la instancia**: que exista, no esté retirado, sea de la clase esperada (un diagnóstico debe ser `Diagnosis`, no la vacuna) y su datatype admita resultado. Correr tras tocar cualquier catálogo |

| Query (QA sobre la BD) | Para qué |
|------------------------|----------|
| [`querys/visita_detalle.sql`](querys/visita_detalle.sql) | Radiografía de una visita + detección de datos imposibles (vitales fuera de rango, paneles huérfanos) |
| [`querys/coherencia_seguimiento.sql`](querys/coherencia_seguimiento.sql) | Audita el control: mismo médico, mismo diagnóstico, 0 citas vencidas sin resolver |
| [`querys/sp_fechas_auditoria.sql`](querys/sp_fechas_auditoria.sql) | Los stored procedures de la etapa 5/5 (también ejecutables a mano) |
| [`querys/borrar_simulacion.sql`](querys/borrar_simulacion.sql) | Borrado **físico** de los datos `SIM-` (el `clear` solo los anula) |

## Las 5 etapas de una corrida

| Etapa | Qué hace |
|:-----:|----------|
| **1/5** Validación de catálogos | Carga los 15 CSV, informa de las filas de cada uno y **valida**: dominios, bandas, filas inseleccionables, cruce perfil ↔ diagnósticos. Con errores **aborta (exit 2) antes de tocar OpenMRS** |
| **2/5** Días a simular | Ventana, días con atención vs. cerrados, volumen previsto (nuevos/recurrentes) y desglose por día o por mes. Es el plan **exacto** que se va a ejecutar, no una estimación |
| **3/5** Ejecución | **Pausa de 5 s** para abortar con Ctrl+C tras leer el informe; luego siembra, con progreso cada ~15 s |
| **4/5** Resumen final | Visitas (nuevos vs. recurrentes), pacientes distintos y cuántos volvieron, visitas por semana, **top-5 diagnósticos**, y errores separados en *de proceso* y *de operación* (una obs rechazada no es lo mismo que una corrida caída) |
| **5/5** Fechas de auditoría | *Opcional, desactivada por defecto.* Retrofecha `date_created` derivándolo de la fecha de negocio. Idempotente, acotada a `SIM-` y reversible |

## Diseño

### Pipeline de una visita

`SeedOrchestrator` planifica los días y, por cada paciente, encadena seeders autocontenidos. Este es el orden real (`ProcesarVisitaAsync`), y cada uno escribe en OpenMRS por REST:

| Seeder | Qué escribe |
|--------|-------------|
| `PatientSeeder` | Paciente: nombre (2 + 2 apellidos), dirección salvadoreña, teléfono, estado civil, identificador `SIM-` + OpenMRS ID válido (Luhn) |
| `AllergySeeder` | Alergias — **solo al dar de alta** al paciente (prevalencia y nº por corrida) |
| `VisitSeeder` | La visita (`OPD Visit`), con recuperación si OpenMRS reporta solapamiento |
| `VitalsSeeder` | 8 obs de signos vitales **derivadas de la enfermedad** y de la peor severidad |
| `AppointmentSeeder` *(resolver)* | Marca `Completed` la cita a la que el paciente acude, o `Missed` la que se le pasó |
| `ConsultaSeeder` | Encuentro de consulta: motivo, **diagnósticos** (primario + comorbilidades), exámenes en consultorio, nota de seguimiento |
| `ConditionSeeder` | Problem list: cada diagnóstico crónico (HTA, diabetes, EPOC…) |
| `ProgramEnrollmentSeeder` | Inscripción en programas de atención (VIH, diabetes) |
| `LabOrderSeeder` | Órdenes de laboratorio **y su resultado**; los paneles (hemograma) como obs-group. Los resultados diferidos llegan en la visita siguiente |
| `PrescriptionSeeder` | Prescripciones con posología del catálogo |
| `AppointmentSeeder` *(agendar)* | La **cita real** en la agenda de O3, con el médico reservado para ese día |
| `VisitCloseSeeder` | Cierra la visita (1–4 h después de la llegada) |

### Servicios de decisión

Aquí vive la inteligencia del simulador. El patrón que gobierna todo el proyecto: son **seams puros** (estáticos, con el RNG inyectado), lo que permite testear las reglas clínicas **sin red ni base de datos** — de ahí que los 279 tests corran en ~120 ms.

| Servicio | Decide |
|----------|--------|
| `EpidemiologySelector` | El diagnóstico: categoría por edad/sexo/estación, sesgo hacia enfermedades comunes, *damping* anti-repetición, comorbilidades por afinidad clínica, y si el crónico viene a su control |
| `ClinicResourceAssigner` | Consultorio y médico: roster diario (2-3 médicos), médico de cabecera, y qué médico atenderá una cita futura |
| `RecurrentSelector` · `RecurrenceScheduler` | Quién vuelve hoy (prioridad a quien tiene cita) y cuándo será elegible de nuevo (agudo 7–21 d, crónico 30–120 d) |
| `SeguimientoPolicy` | Si se agenda control, con probabilidad condicionada al cuadro (crónico > grave > resto) |
| `LabResultGenerator` | El valor del laboratorio: banda normal o anormal según la enfermedad; los componentes del panel, cada uno por su cuenta |
| `VitalsSeeder.ComputeVitals` | Los vitales: IMC acoplado a la talla, fiebre, taquicardia, SpO2 — con overrides por enfermedad |
| `PatientProfileGenerator` | Demografía: nombre, edad, sexo, dirección, teléfono, estado civil |
| `DailyScheduleGenerator` | Cuántos pacientes atiende cada día y a qué hora llegan |
| `ClimateResolver` | La estación de cada fecha (dengue en lluvia, gripe en invierno) |
| `RunStats` · `ErrorTally` · `SeedProgressTracker` | El resumen final y el conteo **preciso** de errores por componente |
| `AuditDateFixer` | La etapa 5/5 — única pieza que habla con MariaDB |

### Validación fail-fast

El proceso **no arranca** con configuración o catálogos inválidos, y aborta **antes de escribir nada** (exit 2):

- `SettingsValidator` — rangos y coherencia de `appsettings.json` (probabilidades en [0,1], bandas `Min ≤ Max`, fechas, volúmenes). Además avisa de las claves del JSON que el binding ignoraría **en silencio**.
- `CatalogValidator` — los 15 CSV. `CatalogLoader` es deliberadamente mudo (un CSV ausente da lista vacía; un booleano mal escrito, `false`), así que una errata se manifestaba como *una feature apagada sin que nadie lo notara*. Ahora se caza al arrancar.

## De dónde sale la coherencia clínica

No es ruido aleatorio: cada dato tiene una razón. El detalle, con ejemplos, en [`manual_usuario.md` §2](manual_usuario.md).

- **Perfil epidemiológico** — la enfermedad se sortea por categoría según edad y género (`epidemiology-profile.csv`), y el diagnóstico concreto se filtra **duro** por sexo biológico (ningún hombre con preeclampsia).
- **Sesgo a enfermedades comunes** — cada corrida sortea su propia proporción de casos comunes (75–95 %), así que lo frecuente domina pero lo raro aparece.
- **Comorbilidad** — con la edad crece la probabilidad de llevar 1-2 diagnósticos extra, y estos se agrupan en *clusters* clínicamente afines (diabetes ↔ cardiovascular).
- **Estacionalidad** — el clima de cada semana ISO empuja las enfermedades de temporada (dengue en lluvia, gripe en invierno) y sube algo la temperatura corporal en las semanas calurosas.
- **Vitales derivados de la enfermedad** — el peso sale del IMC objetivo acoplado a la talla (nunca un IMC absurdo), la fiebre y la taquicardia acompañan a la infección, la SpO2 baja con la gravedad respiratoria. La talla se fija en la primera visita y **es constante entre visitas**; la edad avanza con el tiempo simulado.
- **Laboratorios con resultado** — la orden se cierra con su valor, coherente con el cuadro. El 10 % de los resultados **se difiere de verdad**: se generan con el contexto de la visita que los pidió y llegan en la siguiente.
- **Continuidad longitudinal** — el crónico vuelve a control **de su enfermedad**, no de una al azar; el agudo vuelve por el mismo episodio; y la cita de control transporta el motivo y el médico, así que le atiende **el mismo médico** que se lo ordenó.
- **La agenda gobierna el retorno** — quien tiene cita hoy es atendido primero; quien no aparece, deja un `Missed` real en la agenda.
- **Reproducibilidad** — misma `RandomSeed` + misma configuración = exactamente la misma corrida.

## Catálogos

Todo el contenido clínico vive en CSV editables (`openmrs_seeder_v1/openmrs_seeder_v1/catalogs/`), no en el código. Esquema completo en [`parametrizacion_archivos.md`](parametrizacion_archivos.md).

| Catálogo | Contenido |
|----------|-----------|
| `epidemiology-profile.csv` | Peso de cada categoría por grupo de edad y género |
| `diagnosticos.csv` | 875 diagnósticos CIEL (**uno por concepto**: los duplicados son un error que aborta el arranque) en 13 categorías, con peso por edad/sexo, `cronica`, `comun`, estación y pistas de vitales |
| `medicamentos.csv` | ~30 fármacos del formulario, con posología y las categorías a las que aplican |
| `laboratorios.csv` | 27 exámenes con sus **bandas de resultado** normal/anormal y qué las dispara |
| `paneles.csv` | Componentes de los paneles (hemograma y perfil lipídico) — *opcional* |
| `examenes_clinicos.csv` | 10 exámenes hechos en consultorio (Glasgow, escala de dolor, FC fetal…) |
| `alergenos.csv` | Alérgenos (fármaco / alimento / ambiente) |
| `motivos_consulta.csv` | Frases de motivo de consulta por categoría |
| `nombres.csv` · `apellidos.csv` | Nombres y apellidos centroamericanos |
| `direcciones.csv` | Direcciones de El Salvador, ponderadas por cercanía a la clínica — *opcional* |
| `consultorios.csv` | Consultorios y su médico — *opcional* |
| `programas.csv` | Programas de atención de OpenMRS y qué diagnóstico los dispara — *opcional* |
| `comorbilidad_afinidades.csv` | Clusters de comorbilidad — *opcional* |
| `clima.csv` | Estación y temperatura por semana ISO — *opcional* |

> *Opcional* = si el archivo falta o está vacío, esa función simplemente **se apaga**; el simulador no falla.

## Estructura del repositorio

```
openmrs_seeder_v1/
  openmrs_seeder_v1/          # la aplicación
    Program.cs                #   las 5 etapas y los subcomandos
    Seeders/                  #   el pipeline (11 seeders + SeedOrchestrator)
    Services/                 #   las decisiones (seams puros y testeables)
    Configuration/            #   settings + validación fail-fast
    Clients/                  #   OpenMrsRestClient
    catalogs/                 #   los 15 CSV
  openmrs_seeder_v1.Tests/    # 279 tests
querys/                       # QA sobre la BD + los stored procedures
scripts/                      # backup y utilidades de mantenimiento
docker/                       # ejecutar el seeder sin instalar .NET
```

## Tests

```bash
dotnet test openmrs_seeder_v1/openmrs_seeder_v1.Tests/openmrs_seeder_v1.Tests.csproj
```

Los tests son rápidos (~120 ms) porque prueban **seams puros**, no la red: las reglas clínicas se ejercitan con un RNG determinista. Incluyen un test de humo que valida **los CSV reales del repositorio**, así una edición futura de un catálogo no puede romper el simulador en silencio.

## Documentación

| Documento | Contenido |
|-----------|-----------|
| [`manual_usuario.md`](manual_usuario.md) | **Empieza aquí** — tour de capacidades con ejemplos, configuración, casos de uso, solución de problemas |
| [`parametrizacion_archivos.md`](parametrizacion_archivos.md) | Referencia completa de parámetros (`appsettings.json`) y esquemas de los catálogos CSV |
| [`correccion_fechas.md`](correccion_fechas.md) | La etapa 5/5: por qué `date_created` salía con la fecha de la corrida, las reglas de derivación, cómo se verifica y se revierte |
| [`CLAUDE.md`](CLAUDE.md) | Notas técnicas: arquitectura, UUIDs verificados de la instancia, restricciones de la REST API |
| [`fases_implementacion.md`](fases_implementacion.md) | Historia del proyecto: fases y registro de cambios |
| [`enfermedades-centroamerica.md`](enfermedades-centroamerica.md) | Investigación: 300 enfermedades de Centroamérica con metodología y referencias (insumo del catálogo) |

## Identificación de los datos simulados

Todo paciente generado lleva el identificador **`SIM-XXXXXXXX`**, sus visitas la marca `SEEDED_BY_SIMULATOR` y los médicos generados el prefijo `SIM-MED-`. Los datos reales de la instancia **nunca se tocan**: `clear` solo anula lo simulado, y la etapa de fechas solo alcanza a las filas de pacientes `SIM-`.
