# Corrección de las fechas de auditoría (etapa 5/5)

## El problema

El simulador escribe en OpenMRS **exclusivamente por REST**. Cada fila que crea la API lleva dos clases
de fecha:

- **Fechas de negocio** — cuándo ocurrió el hecho clínico: `visit.date_started`, `encounter.encounter_datetime`,
  `obs.obs_datetime`, `orders.date_activated`, `conditions.onset_date`, `patient_program.date_enrolled`,
  `patient_appointment.start_date_time`. El seeder **las manda explícitamente** y OpenMRS las respeta. Están
  bien: caen dentro de la ventana simulada.
- **Fechas de auditoría** — cuándo se insertó la fila: `date_created`, `date_changed`. Estas **no se pueden
  mandar por REST**: OpenMRS las sella con el **reloj real del servidor**. Quedan todas con el día en que se
  corrió el sembrado.

El efecto es discreto pero envenena cualquier análisis: para un ETL, un cuadro de mando o una auditoría que
ingiera "por fecha de inserción", **años de historia clínica ocurrieron en una sola tarde**. Medido en la
corrida 2023-01-01 → 2026-06-30 (11.227 pacientes): las fechas de negocio abarcaban los tres años y medio, y
las 434.420 filas de auditoría decían todas `2026-07-12`.

## La solución

Un proceso que **retrofecha** `date_created` (y los pocos `date_changed` con valor) **derivándolo de la fecha
de negocio** de la propia fila o de la de su padre. Vive en stored procedures
(`querys/sp_fechas_auditoria.sql`) y lo ejecuta la app (`Services/AuditDateFixer.cs`). Es la **única vía
no-REST** del proyecto, y está **desactivada por defecto**.

Tres propiedades que lo hacen seguro:

- **Idempotente**: cada `UPDATE` recalcula el destino desde la fecha de negocio, nunca desde el valor actual.
  Ejecutarlo dos veces da el mismo resultado (la segunda pasada cambia 0 filas); una ejecución interrumpida a
  medias se arregla re-ejecutándola. Por eso los lotes van en transacciones cortas en vez de una transacción
  gigante: la seguridad la dan la idempotencia y el snapshot, no la atomicidad global.
- **Acotado**: todo se une contra el conjunto `patient_identifier.identifier LIKE 'SIM-%'` (el prefijo es
  configurable). Ninguna sentencia puede tocar pacientes demo, conceptos, fármacos ni ubicaciones.
- **Reversible**: antes de escribir guarda la pre-imagen de las filas que cambia en `sim_fecha_backup`
  (`CALL sp_sim_fechas_revertir('<ejecución>')` las restaura).

## Análisis: qué tablas hay que tocar y por qué

Las 20 tablas se reparten en tres situaciones. La única no trivial es la cita.

### a) Tienen fecha de negocio propia → `date_created` sale de ella

| Tabla | `date_created` ← | `date_changed` ← |
|---|---|---|
| `visit` | `date_started` (llega el paciente) | `date_stopped` (se cierra la visita al final) |
| `encounter` | `encounter_datetime` | — |
| `obs` | `obs_datetime` | — (la tabla no tiene la columna) |
| `orders` | `date_activated` | — (la tabla no tiene la columna) |
| `conditions` | `onset_date` | — |
| `patient_program` | `date_enrolled` | — |

### b) No tienen fecha de negocio → se deriva del padre

| Tabla | `date_created` ← |
|---|---|
| `person`, `patient`, `person_name`, `person_address`, `person_attribute`, `patient_identifier` | **instante de registro** del paciente |
| `allergy` | instante de registro (las alergias solo se siembran al dar de alta al paciente nuevo) |
| `encounter_provider`, `encounter_diagnosis` | `encounter.encounter_datetime` del encuentro padre |
| `patient_state` | `patient_program.date_enrolled` |
| `patient_appointment_provider` | fecha de creación de la cita |
| `provider` (`SIM-MED-*`) y su `person` / `person_name` | `StartDate 07:00` — son datos de referencia: los médicos existen antes de abrir el día 1 |

El **instante de registro** es la primera visita del paciente **menos un desfase de 5–20 minutos**,
determinista por `patient_id` (`5 + (patient_id * 7) MOD 16`, replicado en
`AuditDateFixer.DesfaseRegistroMinutos` para poder fijarlo en un test). El paciente pasa por recepción y
luego entra a consulta: así `registro < primera visita`, que es lo que esperaría una auditoría, y las altas
del día no forman una fila artificial en el mismo minuto.

### c) La cita (`patient_appointment`) — la única con derivación no trivial

- **Cuándo se agendó** (`date_created` y `date_appointment_scheduled`): el instante de la **consulta que la
  creó**. No hace falta heurística: la cita nace en la consulta que registró la obs *"Return visit date"*
  (`5096AAAA…`) con `value_datetime` igual al día de la cita — el mismo join exacto que usa
  `querys/coherencia_seguimiento.sql`. Fallback (no se dio en la práctica): la última visita anterior a la
  cita.
- **Cuándo se resolvió** (`date_changed`):
  - `Completed` → `date_started` de la visita a la que el paciente acudió (±`Appointments.ToleranciaDias`,
    que es justo cuando el simulador la marca).
  - `Missed` → la primera visita **posterior** a la ventana de gracia (el paciente reapareció tarde) y, si no
    volvió nunca, el **barrido de cierre** al final de la ventana (`EndDate 23:59`).
  - `Scheduled` → `NULL`: nunca se modificó, que es lo correcto.
- `patient_appointment_audit`: la fila del estado inicial hereda la fecha de creación de la cita; las de los
  cambios, la de su resolución.

### Lo que queda fuera

`allergy_reaction` y `note` no tienen columnas de fecha (o están vacías). Los 5.464 obs hijas de panel
(hemograma) tienen `encounter_id` NULL —al postear el padre con `groupMembers` las hijas no heredan el
encuentro—, pero **sí** tienen `obs_datetime`, así que se retrofechan igual que las demás. (Ese NULL es un
asunto aparte, del seeder, no de este proceso.)

## Cómo se ejecuta

**Interruptor** (`appsettings.json`). Con `CorregirFechas: false` **o** la cadena de conexión vacía, la etapa
5/5 se salta y el simulador sigue siendo REST puro:

```json
"OpenMRS": {
  "Database": {
    "CorregirFechas": true,
    "ConnectionString": "Server=localhost;Port=3306;Database=openmrs;User Id=openmrs;Password=…;",
    "PrefijoPaciente": "SIM-",
    "TamanoLote": 20000,
    "PedirConfirmacion": true
  }
}
```

**Tres vías**, todas sobre los mismos SPs:

```bash
# 1. Etapa 5/5 automática, al terminar de sembrar (si CorregirFechas = true)
dotnet run

# 2. Proceso suelto, sobre datos ya sembrados (p. ej. una corrida anterior)
dotnet run -- fechas --dry-run   # informa cuántas filas cambiaría, sin escribir
dotnet run -- fechas             # aplica (pide confirmación si PedirConfirmacion = true)

# 3. A mano, con el cliente de MariaDB
docker exec -i openmrs-distro-referenceapplication-360-db-1 \
  mariadb -uopenmrs -p<PASSWORD> openmrs < querys/sp_fechas_auditoria.sql
#   CALL sp_sim_fechas_preparar('SIM-', 3, '2026-06-30 23:59:00', '2023-01-01 07:00:00', 'manual');
#   CALL sp_sim_fechas_aplicar(1, 20000, 'manual');   -- 1 = dry-run
#   CALL sp_sim_fechas_aplicar(0, 20000, 'manual');   -- 0 = aplicar
#   CALL sp_sim_fechas_verificar('2023-01-01', '2026-06-30 23:59:59', 'manual');
```

⚠️ **Después de aplicar hay que reiniciar el backend** (`docker compose restart backend`): Hibernate cachea
las entidades y seguiría sirviendo las fechas viejas. Si el gateway responde 502, `docker compose restart
gateway` (gotcha conocido de esta instancia).

## Log de progreso

Los SP escriben cada lote y cada tabla en **`sim_fecha_log`**, que es persistente: el detalle de cualquier
ejecución se puede consultar después con SQL. La app va volcando esas filas a la consola conforme avanza, con
el mismo estilo que las otras etapas:

```
══ Etapa 5/5 · Fechas de auditoría ══
Conectado a MariaDB (localhost) | ejecución 20260712-222333
Stored procedures instalados (15 sentencias desde querys/sp_fechas_auditoria.sql).
   [preparar] sim_fecha_ref                     11227  pacientes en alcance
   [preparar] sim_fecha_cita                     9492  citas en alcance
   [preparar] sim_fecha_plan                   434420  filas en alcance (destino calculado)
Filas por corregir: 434420 en 20 tablas
   person                            11230
   …
Aplicando por lotes de 20000 filas (snapshot reversible en sim_fecha_backup)…
   [aplicar] obs                              168451  filas corregidas
Corregidas 434420 filas.
```

```sql
-- el detalle de una ejecución, después
SELECT fase, tabla, filas, mensaje FROM sim_fecha_log WHERE ejecucion = '20260712-222333' ORDER BY id;
```

## Verificación

`sp_sim_fechas_verificar` cierra el proceso con un informe por tabla y **tres invariantes que deben dar 0**:

| Invariante | Qué cazaría |
|---|---|
| `pendientes` = 0 | filas del alcance que aún difieren del plan (la corrección no se aplicó entera) |
| `fuera_ventana` = 0 | filas cuyo `date_created` cae fuera de la ventana simulada |
| pacientes registrados **después** de su primera visita = 0 | derivación del registro invertida |
| obs cuya fecha de creación ≠ su `obs_datetime` = 0 | la tabla más grande, sin excepciones |
| visitas cuya `date_changed` ≠ su `date_stopped` = 0 | el cierre de visita quedó descolgado |

Resultado real de la corrida 2023-01-01 → 2026-06-30 (434.420 filas, 20 tablas):

```
Verificación (ventana 2023-01-01 → 2026-06-30):
   person                            11230 filas | 2023-01-01 → 2026-06-30
   obs                              168451 filas | 2023-01-02 → 2026-06-30
   visit                             15915 filas | 2023-01-02 → 2026-06-30
   provider                              3 filas | 2023-01-01 → 2023-01-01
   …
   [verificar] person   0  pacientes registrados DESPUÉS de su primera visita (debe ser 0)
   [verificar] obs      0  obs cuya fecha de creación no es su obs_datetime (debe ser 0)
   [verificar] visit    0  visitas cuya fecha de cierre no es su date_stopped (debe ser 0)
Invariantes OK: 0 filas pendientes y 0 fuera de la ventana simulada.
```

Y lo de fuera del alcance quedó intacto: los 52 pacientes demo, los 59.270 conceptos, las 22 ubicaciones y
los 323 fármacos conservan sus fechas originales.

## Reversión

```sql
CALL sp_sim_fechas_revertir('20260712-222333');   -- el id de ejecución que imprimió la app
```

Restaura desde `sim_fecha_backup` (pre-imagen de **solo** las filas que se cambiaron). Es la vuelta atrás
fina; la gruesa sigue siendo el dump de `scripts/backup_openmrs.ps1`, que conviene tener antes de aplicar.

## Objetos que crea en la BD

Todos con prefijo `sim_` / `sp_sim_` para no colisionar con el esquema de OpenMRS. No los borra el
subcomando `clear`.

| Objeto | Qué es |
|---|---|
| `sim_fecha_tabla` | metadatos de las 20 tablas: nombre de su PK y qué columnas se tocan |
| `sim_fecha_ref` | por paciente: su primera visita y su instante de registro derivado |
| `sim_fecha_cita` | por cita: cuándo se agendó y cuándo se resolvió |
| `sim_fecha_plan` | **el plan**: toda fila en alcance con su fecha destino ya calculada |
| `sim_fecha_backup` | snapshot reversible (pre-imagen de las filas cambiadas) |
| `sim_fecha_log` | log de progreso persistente, una fila por lote |
| `sim_fecha_check` | resultado de la última verificación |
