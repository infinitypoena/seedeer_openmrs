-- ============================================================================
-- sp_fechas_auditoria.sql — Corrección de las FECHAS DE AUDITORÍA de los datos SIM-
-- ----------------------------------------------------------------------------
-- EL PROBLEMA
--   El simulador escribe en OpenMRS solo por REST, y OpenMRS sella cada fila con el
--   reloj real del servidor. Por eso las fechas de NEGOCIO son correctas (la visita, la
--   obs, la orden llevan la fecha simulada, que el seeder manda explícitamente) pero las
--   de AUDITORÍA no: `date_created` queda con el día en que se corrió el sembrado.
--   Resultado: para cualquier ETL o auditoría que ingiera "por fecha de inserción", años
--   de historia clínica ocurrieron en una sola tarde.
--
-- LA SOLUCIÓN
--   Retrofechar `date_created` (y los pocos `date_changed` con valor) DERIVÁNDOLO de la
--   fecha de negocio de la propia fila o de la de su padre. Ver la tabla de reglas en
--   `correccion_fechas.md`.
--
-- ALCANCE (invariante de seguridad)
--   TODO se acota a los pacientes con identificador '<prefijo>%' (por defecto 'SIM-') y a
--   los médicos '<prefijo>MED-%'. Ninguna sentencia toca pacientes demo, conceptos,
--   fármacos ni ubicaciones: el conjunto se materializa una sola vez en `sim_fecha_ref` /
--   `sim_fecha_plan` y todo lo demás se une contra él.
--
-- IDEMPOTENTE
--   Cada UPDATE recalcula el destino desde la fecha de negocio, no desde el valor actual.
--   Ejecutarlo dos veces da el mismo resultado (la segunda cambia 0 filas); una ejecución
--   interrumpida a medias se arregla re-ejecutándola. La seguridad la dan la idempotencia
--   y el snapshot `sim_fecha_backup`, no una transacción gigante.
--
-- USO
--   Normalmente lo ejecuta la app (`dotnet run -- fechas`, o la etapa 5/5 si
--   OpenMRS:Database:CorregirFechas = true), que instala estos SPs y los llama en orden.
--   A mano:
--     docker exec -i openmrs-distro-referenceapplication-360-db-1 \
--       mariadb -uopenmrs -p<PASSWORD> openmrs < querys/sp_fechas_auditoria.sql
--     CALL sp_sim_fechas_preparar('SIM-', 3, '2026-06-30 23:59:00', '2023-01-01 07:00:00', 'manual');
--     CALL sp_sim_fechas_aplicar(1, 20000, 'manual');   -- 1 = dry-run (no escribe)
--     CALL sp_sim_fechas_aplicar(0, 20000, 'manual');   -- 0 = aplicar
--     CALL sp_sim_fechas_verificar('2023-01-01', '2026-06-30 23:59:59', 'manual');
--     CALL sp_sim_fechas_revertir('manual');            -- deshacer desde el snapshot
--
--   Tras aplicar: reiniciar el backend (`docker compose restart backend`) para vaciar la
--   caché de Hibernate; si el gateway responde 502, `docker compose restart gateway`.
-- ============================================================================

-- ── Tablas de trabajo (prefijo sim_ para no colisionar con el esquema de OpenMRS) ────────

-- Log de progreso persistente: una fila por lote y por fase. Sobrevive a la ejecución, así
-- que el detalle de cualquier corrida se puede consultar después con SQL.
CREATE TABLE IF NOT EXISTS sim_fecha_log (
    id        BIGINT AUTO_INCREMENT PRIMARY KEY,
    ejecucion VARCHAR(40)  NOT NULL,
    ts        DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    fase      VARCHAR(24)  NOT NULL,
    tabla     VARCHAR(48)  NULL,
    filas     BIGINT       NULL,
    mensaje   VARCHAR(255) NULL,
    KEY ix_ejecucion (ejecucion, id)
) ENGINE=InnoDB;

-- Metadatos de las 20 tablas: cómo se llama su PK y qué columnas se tocan.
--   set_changed = 1  → además de date_created se escribe date_changed (solo visit y cita)
--   col_extra        → tercera columna de tipo "fecha de creación" (solo la cita)
CREATE TABLE IF NOT EXISTS sim_fecha_tabla (
    orden       INT         NOT NULL,
    tabla       VARCHAR(48) NOT NULL PRIMARY KEY,
    pk_col      VARCHAR(48) NOT NULL,
    set_changed TINYINT     NOT NULL DEFAULT 0,
    col_extra   VARCHAR(48) NULL
) ENGINE=InnoDB;

-- Referencia por paciente: su primera visita y su instante de registro (derivado).
CREATE TABLE IF NOT EXISTS sim_fecha_ref (
    patient_id     INT      NOT NULL PRIMARY KEY,
    primera_visita DATETIME NOT NULL,
    registro       DATETIME NOT NULL
) ENGINE=InnoDB;

-- Referencia por cita: cuándo se agendó (la consulta que la creó) y cuándo se resolvió.
CREATE TABLE IF NOT EXISTS sim_fecha_cita (
    patient_appointment_id INT      NOT NULL PRIMARY KEY,
    creada                 DATETIME NOT NULL,
    cambiada               DATETIME NULL
) ENGINE=InnoDB;

-- El plan: TODAS las filas en alcance con su fecha destino ya calculada. El aplicador solo
-- escribe las que difieren del valor actual (de ahí la idempotencia).
CREATE TABLE IF NOT EXISTS sim_fecha_plan (
    tabla         VARCHAR(48) NOT NULL,
    pk            BIGINT      NOT NULL,
    nueva_created DATETIME    NOT NULL,
    nueva_changed DATETIME    NULL,
    PRIMARY KEY (tabla, pk)
) ENGINE=InnoDB;

-- Snapshot reversible: pre-imagen de SOLO las filas que se cambian.
CREATE TABLE IF NOT EXISTS sim_fecha_backup (
    tabla            VARCHAR(48) NOT NULL,
    pk               BIGINT      NOT NULL,
    ejecucion        VARCHAR(40) NOT NULL,
    date_created_old DATETIME    NULL,
    date_changed_old DATETIME    NULL,
    extra_old        DATETIME    NULL,
    PRIMARY KEY (tabla, pk)
) ENGINE=InnoDB;

-- Resultado de la verificación (una fila por tabla).
CREATE TABLE IF NOT EXISTS sim_fecha_check (
    orden        INT         NOT NULL,
    tabla        VARCHAR(48) NOT NULL PRIMARY KEY,
    filas        BIGINT      NOT NULL,
    pendientes   BIGINT      NOT NULL,
    fuera_ventana BIGINT     NOT NULL,
    min_created  DATETIME    NULL,
    max_created  DATETIME    NULL
) ENGINE=InnoDB;

DELIMITER $$

-- ════════════════════════════════════════════════════════════════════════════
-- sp_sim_fechas_preparar — calcula el destino de cada fila (no escribe en OpenMRS)
--   p_prefijo       prefijo del identificador de los pacientes simulados (p.ej. 'SIM-')
--   p_tolerancia    Appointments.ToleranciaDias (días de gracia para dar una cita por atendida)
--   p_barrido       instante del barrido de cierre: las citas nunca resueltas se marcan
--                   Missed al terminar la corrida (normalmente EndDate 23:59)
--   p_fecha_medicos instante en que "existen" los médicos: datos de referencia creados antes
--                   de abrir el día 1 (normalmente StartDate 07:00)
-- ════════════════════════════════════════════════════════════════════════════
DROP PROCEDURE IF EXISTS sp_sim_fechas_preparar $$
CREATE PROCEDURE sp_sim_fechas_preparar(
    IN p_prefijo       VARCHAR(32),
    IN p_tolerancia    INT,
    IN p_barrido       DATETIME,
    IN p_fecha_medicos DATETIME,
    IN p_ejecucion     VARCHAR(40)
)
BEGIN
    DECLARE v_retorno  INT DEFAULT NULL;   -- concepto "Return visit date" (5096…)
    DECLARE v_personal VARCHAR(48);

    -- Personal de referencia: médicos (SIM-MED-*) y laboratorio (SIM-LAB-*). En la tabla
    -- provider solo viven ellos (un paciente nunca es provider), así que basta el prefijo.
    SET v_personal = CONCAT(p_prefijo, '%');
    SELECT concept_id INTO v_retorno FROM concept
     WHERE uuid = '5096AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' LIMIT 1;

    -- ── Metadatos de las tablas (padre → hijo) ──────────────────────────────
    DELETE FROM sim_fecha_tabla;
    INSERT INTO sim_fecha_tabla (orden, tabla, pk_col, set_changed, col_extra) VALUES
        ( 1, 'person',                       'person_id',                      0, NULL),
        ( 2, 'patient',                      'patient_id',                     0, NULL),
        ( 3, 'person_name',                  'person_name_id',                 0, NULL),
        ( 4, 'person_address',               'person_address_id',              0, NULL),
        ( 5, 'person_attribute',             'person_attribute_id',            0, NULL),
        ( 6, 'patient_identifier',           'patient_identifier_id',          0, NULL),
        ( 7, 'allergy',                      'allergy_id',                     0, NULL),
        ( 8, 'visit',                        'visit_id',                       1, NULL),
        ( 9, 'encounter',                    'encounter_id',                   0, NULL),
        (10, 'encounter_provider',           'encounter_provider_id',          0, NULL),
        (11, 'encounter_diagnosis',          'diagnosis_id',                   0, NULL),
        (12, 'obs',                          'obs_id',                         0, NULL),   -- sin date_changed
        (13, 'orders',                       'order_id',                       0, NULL),   -- sin date_changed
        (14, 'conditions',                   'condition_id',                   0, NULL),
        (15, 'patient_program',              'patient_program_id',             0, NULL),
        (16, 'patient_state',                'patient_state_id',               0, NULL),
        (17, 'patient_appointment',          'patient_appointment_id',         1, 'date_appointment_scheduled'),
        (18, 'patient_appointment_provider', 'patient_appointment_provider_id',0, NULL),
        (19, 'patient_appointment_audit',    'patient_appointment_audit_id',   0, NULL),
        (20, 'provider',                     'provider_id',                    0, NULL);

    -- ── Pacientes en alcance + su instante de registro ──────────────────────
    -- El desfase (5-20 min antes de la primera visita) es determinista por paciente: pasa por
    -- recepción y luego entra a consulta. Garantiza registro < primera visita.
    DELETE FROM sim_fecha_ref;
    INSERT INTO sim_fecha_ref (patient_id, primera_visita, registro)
    SELECT v.patient_id,
           MIN(v.date_started),
           DATE_SUB(MIN(v.date_started), INTERVAL (5 + (v.patient_id * 7) MOD 16) MINUTE)
      FROM visit v
     WHERE v.patient_id IN (SELECT patient_id FROM patient_identifier
                             WHERE identifier LIKE CONCAT(p_prefijo, '%'))
     GROUP BY v.patient_id;

    INSERT INTO sim_fecha_log (ejecucion, fase, tabla, filas, mensaje)
    SELECT p_ejecucion, 'preparar', 'sim_fecha_ref', COUNT(*), 'pacientes en alcance' FROM sim_fecha_ref;

    -- ── Citas: cuándo se agendó y cuándo se resolvió ────────────────────────
    -- Agendada: la cita nace en la consulta que registró la obs "Return visit date" con
    -- value_datetime = el día de la cita (join exacto, el mismo de coherencia_seguimiento.sql).
    -- Fallback (no debería darse): la consulta de la última visita anterior a la cita.
    DELETE FROM sim_fecha_cita;
    INSERT INTO sim_fecha_cita (patient_appointment_id, creada, cambiada)
    SELECT a.patient_appointment_id,
           COALESCE(
               (SELECT MIN(o.obs_datetime) FROM obs o
                 WHERE o.person_id  = a.patient_id
                   AND o.concept_id = v_retorno
                   AND o.voided     = 0
                   AND DATE(o.value_datetime) = DATE(a.start_date_time)),
               (SELECT DATE_ADD(MAX(v.date_started), INTERVAL 30 MINUTE) FROM visit v
                 WHERE v.patient_id = a.patient_id
                   AND v.date_started < a.start_date_time),
               a.start_date_time),
           NULL
      FROM patient_appointment a
      JOIN sim_fecha_ref r ON r.patient_id = a.patient_id;

    -- Resuelta: Completed = la visita a la que el paciente acudió (±tolerancia, que es cuando
    -- el simulador la marca). Missed = la primera visita POSTERIOR a la ventana de gracia y, si
    -- el paciente no volvió, el barrido de cierre. Scheduled = nunca se tocó → NULL.
    UPDATE sim_fecha_cita c
      JOIN patient_appointment a ON a.patient_appointment_id = c.patient_appointment_id
       SET c.cambiada = CASE a.status
           WHEN 'Completed' THEN GREATEST(c.creada, COALESCE(
               (SELECT MIN(v.date_started) FROM visit v
                 WHERE v.patient_id = a.patient_id
                   AND ABS(DATEDIFF(v.date_started, a.start_date_time)) <= p_tolerancia),
               a.start_date_time))
           WHEN 'Missed' THEN GREATEST(c.creada, COALESCE(
               (SELECT MIN(v.date_started) FROM visit v
                 WHERE v.patient_id = a.patient_id
                   AND DATEDIFF(v.date_started, a.start_date_time) > p_tolerancia),
               p_barrido))
           ELSE NULL
       END;

    INSERT INTO sim_fecha_log (ejecucion, fase, tabla, filas, mensaje)
    SELECT p_ejecucion, 'preparar', 'sim_fecha_cita', COUNT(*), 'citas en alcance' FROM sim_fecha_cita;

    -- ── El plan: toda fila en alcance con su fecha destino ──────────────────
    -- TRUNCATE, no DELETE: el plan de una corrida grande ronda las 800k filas y un DELETE
    -- completo tardaba >1 h (undo row a row) — saltaba el timeout del driver y dejaba a
    -- MariaDB otra hora haciendo rollback. TRUNCATE es DDL (instantáneo) y aquí es
    -- equivalente: vaciado total de una tabla de trabajo sin FKs. El backup reversible
    -- (sim_fecha_backup) NO se toca.
    TRUNCATE TABLE sim_fecha_plan;

    -- 1-6 · Registro del paciente: no tienen fecha de negocio, se derivan de la primera visita
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'person', t.person_id, r.registro
      FROM person t JOIN sim_fecha_ref r ON r.patient_id = t.person_id;
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'patient', t.patient_id, r.registro
      FROM patient t JOIN sim_fecha_ref r ON r.patient_id = t.patient_id;
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'person_name', t.person_name_id, r.registro
      FROM person_name t JOIN sim_fecha_ref r ON r.patient_id = t.person_id;
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'person_address', t.person_address_id, r.registro
      FROM person_address t JOIN sim_fecha_ref r ON r.patient_id = t.person_id;
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'person_attribute', t.person_attribute_id, r.registro
      FROM person_attribute t JOIN sim_fecha_ref r ON r.patient_id = t.person_id;
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'patient_identifier', t.patient_identifier_id, r.registro
      FROM patient_identifier t JOIN sim_fecha_ref r ON r.patient_id = t.patient_id;

    -- 7 · Alergias: se siembran al registrar al paciente nuevo, en su primera visita
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'allergy', t.allergy_id, r.registro
      FROM allergy t JOIN sim_fecha_ref r ON r.patient_id = t.patient_id;

    -- 8 · Visita: nace al llegar el paciente y se "modifica" al cerrarla (stopDatetime)
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created, nueva_changed)
    SELECT 'visit', t.visit_id, t.date_started, t.date_stopped
      FROM visit t JOIN sim_fecha_ref r ON r.patient_id = t.patient_id;

    -- 9-13 · Filas con fecha de negocio propia
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'encounter', t.encounter_id, t.encounter_datetime
      FROM encounter t JOIN sim_fecha_ref r ON r.patient_id = t.patient_id;
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'obs', t.obs_id, t.obs_datetime
      FROM obs t JOIN sim_fecha_ref r ON r.patient_id = t.person_id;
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'orders', t.order_id, t.date_activated
      FROM orders t JOIN sim_fecha_ref r ON r.patient_id = t.patient_id
     WHERE t.date_activated IS NOT NULL;
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'conditions', t.condition_id, t.onset_date
      FROM conditions t JOIN sim_fecha_ref r ON r.patient_id = t.patient_id
     WHERE t.onset_date IS NOT NULL;
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'patient_program', t.patient_program_id, t.date_enrolled
      FROM patient_program t JOIN sim_fecha_ref r ON r.patient_id = t.patient_id
     WHERE t.date_enrolled IS NOT NULL;

    -- 10-11, 16 · Hijas sin fecha propia: heredan la del padre
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'encounter_provider', t.encounter_provider_id, e.encounter_datetime
      FROM encounter_provider t
      JOIN encounter e ON e.encounter_id = t.encounter_id
      JOIN sim_fecha_ref r ON r.patient_id = e.patient_id;
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'encounter_diagnosis', t.diagnosis_id, e.encounter_datetime
      FROM encounter_diagnosis t
      JOIN encounter e ON e.encounter_id = t.encounter_id
      JOIN sim_fecha_ref r ON r.patient_id = e.patient_id;
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'patient_state', t.patient_state_id, pp.date_enrolled
      FROM patient_state t
      JOIN patient_program pp ON pp.patient_program_id = t.patient_program_id
      JOIN sim_fecha_ref r ON r.patient_id = pp.patient_id
     WHERE pp.date_enrolled IS NOT NULL;

    -- 17-19 · Citas (y sus hijas), desde sim_fecha_cita
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created, nueva_changed)
    SELECT 'patient_appointment', c.patient_appointment_id, c.creada, c.cambiada
      FROM sim_fecha_cita c;
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'patient_appointment_provider', t.patient_appointment_provider_id, c.creada
      FROM patient_appointment_provider t
      JOIN sim_fecha_cita c ON c.patient_appointment_id = t.patient_appointment_id;
    -- La fila de auditoría del estado inicial nace con la cita; las de los cambios, al cambiarla.
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'patient_appointment_audit', t.patient_appointment_audit_id,
           CASE WHEN t.status = 'Scheduled' THEN c.creada ELSE COALESCE(c.cambiada, c.creada) END
      FROM patient_appointment_audit t
      JOIN sim_fecha_cita c ON c.patient_appointment_id = t.appointment_id;

    -- 20 · Personal (médicos + laboratorio): datos de referencia, existen antes de abrir
    -- el día 1 de la ventana. Si no, el técnico que firma un resultado de 2023 constaría
    -- como creado el día de la corrida.
    INSERT INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'provider', t.provider_id, p_fecha_medicos
      FROM provider t WHERE t.identifier LIKE v_personal;
    INSERT IGNORE INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'person', t.person_id, p_fecha_medicos
      FROM person t
     WHERE t.person_id IN (SELECT person_id FROM provider WHERE identifier LIKE v_personal);
    INSERT IGNORE INTO sim_fecha_plan (tabla, pk, nueva_created)
    SELECT 'person_name', t.person_name_id, p_fecha_medicos
      FROM person_name t
     WHERE t.person_id IN (SELECT person_id FROM provider WHERE identifier LIKE v_personal);

    INSERT INTO sim_fecha_log (ejecucion, fase, tabla, filas, mensaje)
    SELECT p_ejecucion, 'preparar', 'sim_fecha_plan', COUNT(*), 'filas en alcance (destino calculado)'
      FROM sim_fecha_plan;

    SELECT tabla, COUNT(*) AS filas
      FROM sim_fecha_plan p
      JOIN sim_fecha_tabla t USING (tabla)
     GROUP BY tabla, t.orden
     ORDER BY t.orden;
END $$

-- ════════════════════════════════════════════════════════════════════════════
-- sp_sim_fechas_aplicar — escribe (o cuenta, si p_dry=1) las filas que difieren del plan
--   Recorre las tablas en orden padre→hijo, por lotes de PK: transacciones cortas, sin
--   undo log gigante ni bloqueos largos. Guarda la pre-imagen antes de tocar cada fila.
-- ════════════════════════════════════════════════════════════════════════════
DROP PROCEDURE IF EXISTS sp_sim_fechas_aplicar $$
CREATE PROCEDURE sp_sim_fechas_aplicar(
    IN p_dry       TINYINT,
    IN p_lote      INT,
    IN p_ejecucion VARCHAR(40)
)
BEGIN
    DECLARE v_fin         TINYINT DEFAULT 0;
    DECLARE v_tabla       VARCHAR(48);
    DECLARE v_pk          VARCHAR(48);
    DECLARE v_set_changed TINYINT;
    DECLARE v_extra       VARCHAR(48);
    DECLARE v_min         BIGINT;
    DECLARE v_max         BIGINT;
    DECLARE v_lo          BIGINT;
    DECLARE v_paso        BIGINT;
    DECLARE v_filas       BIGINT;
    DECLARE v_total       BIGINT;
    DECLARE v_difiere     TEXT;

    DECLARE cur CURSOR FOR
        SELECT tabla, pk_col, set_changed, col_extra FROM sim_fecha_tabla ORDER BY orden;
    DECLARE CONTINUE HANDLER FOR NOT FOUND SET v_fin = 1;

    IF p_dry = 0 THEN
        DELETE FROM sim_fecha_backup WHERE ejecucion = p_ejecucion;
    END IF;

    INSERT INTO sim_fecha_log (ejecucion, fase, mensaje)
    VALUES (p_ejecucion, IF(p_dry = 1, 'dry-run', 'aplicar'),
            CONCAT('inicio (lote ', p_lote, ')'));

    OPEN cur;
    bucle: LOOP
        FETCH cur INTO v_tabla, v_pk, v_set_changed, v_extra;
        IF v_fin = 1 THEN LEAVE bucle; END IF;

        SELECT MIN(pk), MAX(pk) INTO v_min, v_max FROM sim_fecha_plan WHERE tabla = v_tabla;
        IF v_min IS NULL THEN ITERATE bucle; END IF;

        -- Predicado "esta fila aún no está corregida" (lo que hace idempotente al proceso)
        SET v_difiere = CONCAT('t.date_created <> p.nueva_created');
        IF v_set_changed = 1 THEN
            SET v_difiere = CONCAT(v_difiere, ' OR NOT (t.date_changed <=> p.nueva_changed)');
        END IF;
        IF v_extra IS NOT NULL THEN
            SET v_difiere = CONCAT(v_difiere, ' OR NOT (t.', v_extra, ' <=> p.nueva_created)');
        END IF;

        -- Tope de 1000 lotes por tabla: con PKs muy dispersas el paso crece en vez de iterar en vano
        SET v_paso  = GREATEST(p_lote, CEIL((v_max - v_min + 1) / 1000));
        SET v_lo    = v_min;
        SET v_total = 0;

        WHILE v_lo <= v_max DO
            IF p_dry = 1 THEN
                SET @sql = CONCAT(
                    'SELECT COUNT(*) INTO @filas FROM ', v_tabla, ' t ',
                    'JOIN sim_fecha_plan p ON p.tabla = ''', v_tabla, ''' AND p.pk = t.', v_pk, ' ',
                    'WHERE t.', v_pk, ' >= ', v_lo, ' AND t.', v_pk, ' < ', v_lo + v_paso,
                    ' AND (', v_difiere, ')');
                PREPARE st FROM @sql; EXECUTE st; DEALLOCATE PREPARE st;
                SET v_filas = @filas;
            ELSE
                -- Pre-imagen SOLO de las filas que se van a tocar (snapshot reversible)
                SET @sql = CONCAT(
                    'INSERT IGNORE INTO sim_fecha_backup ',
                    '(tabla, pk, ejecucion, date_created_old, date_changed_old, extra_old) ',
                    'SELECT ''', v_tabla, ''', t.', v_pk, ', ''', p_ejecucion, ''', t.date_created, ',
                    IF(v_set_changed = 1, 't.date_changed', 'NULL'), ', ',
                    IF(v_extra IS NULL, 'NULL', CONCAT('t.', v_extra)), ' ',
                    'FROM ', v_tabla, ' t ',
                    'JOIN sim_fecha_plan p ON p.tabla = ''', v_tabla, ''' AND p.pk = t.', v_pk, ' ',
                    'WHERE t.', v_pk, ' >= ', v_lo, ' AND t.', v_pk, ' < ', v_lo + v_paso,
                    ' AND (', v_difiere, ')');
                PREPARE st FROM @sql; EXECUTE st; DEALLOCATE PREPARE st;

                SET @sql = CONCAT(
                    'UPDATE ', v_tabla, ' t ',
                    'JOIN sim_fecha_plan p ON p.tabla = ''', v_tabla, ''' AND p.pk = t.', v_pk, ' ',
                    'SET t.date_created = p.nueva_created',
                    IF(v_set_changed = 1, ', t.date_changed = p.nueva_changed', ''),
                    IF(v_extra IS NULL, '', CONCAT(', t.', v_extra, ' = p.nueva_created')), ' ',
                    'WHERE t.', v_pk, ' >= ', v_lo, ' AND t.', v_pk, ' < ', v_lo + v_paso,
                    ' AND (', v_difiere, ')');
                PREPARE st FROM @sql; EXECUTE st;
                SET v_filas = ROW_COUNT();
                DEALLOCATE PREPARE st;
            END IF;

            SET v_total = v_total + v_filas;
            IF v_filas > 0 THEN
                INSERT INTO sim_fecha_log (ejecucion, fase, tabla, filas, mensaje)
                VALUES (p_ejecucion, IF(p_dry = 1, 'dry-run', 'aplicar'), v_tabla, v_filas,
                        CONCAT('lote pk [', v_lo, ', ', v_lo + v_paso, ')'));
            END IF;
            SET v_lo = v_lo + v_paso;
        END WHILE;

        INSERT INTO sim_fecha_log (ejecucion, fase, tabla, filas, mensaje)
        VALUES (p_ejecucion, IF(p_dry = 1, 'dry-run', 'aplicar'), v_tabla, v_total,
                IF(p_dry = 1, 'filas que se corregirían', 'filas corregidas'));
    END LOOP;
    CLOSE cur;

    -- Resumen por tabla de ESTA fase (lo que la app imprime por consola)
    SELECT t.orden, l.tabla, l.filas
      FROM sim_fecha_log l
      JOIN sim_fecha_tabla t ON t.tabla = l.tabla
     WHERE l.ejecucion = p_ejecucion
       AND l.fase      = IF(p_dry = 1, 'dry-run', 'aplicar')
       AND l.mensaje IN ('filas que se corregirían', 'filas corregidas')
     ORDER BY t.orden;
END $$

-- ════════════════════════════════════════════════════════════════════════════
-- sp_sim_fechas_verificar — invariantes tras aplicar
--   pendientes    = filas del alcance que AÚN difieren del plan  → debe ser 0
--   fuera_ventana = filas cuyo date_created cae fuera de [inicio, fin] → debe ser 0
--   Más tres coherencias fuertes, que van al log como mensajes.
-- ════════════════════════════════════════════════════════════════════════════
DROP PROCEDURE IF EXISTS sp_sim_fechas_verificar $$
CREATE PROCEDURE sp_sim_fechas_verificar(
    IN p_inicio    DATETIME,
    IN p_fin       DATETIME,
    IN p_ejecucion VARCHAR(40)
)
BEGIN
    DECLARE v_fin         TINYINT DEFAULT 0;
    DECLARE v_orden       INT;
    DECLARE v_tabla       VARCHAR(48);
    DECLARE v_pk          VARCHAR(48);
    DECLARE v_set_changed TINYINT;
    DECLARE v_extra       VARCHAR(48);
    DECLARE v_difiere     TEXT;

    DECLARE cur CURSOR FOR
        SELECT orden, tabla, pk_col, set_changed, col_extra FROM sim_fecha_tabla ORDER BY orden;
    DECLARE CONTINUE HANDLER FOR NOT FOUND SET v_fin = 1;

    DELETE FROM sim_fecha_check;

    OPEN cur;
    bucle: LOOP
        FETCH cur INTO v_orden, v_tabla, v_pk, v_set_changed, v_extra;
        IF v_fin = 1 THEN LEAVE bucle; END IF;

        SET v_difiere = CONCAT('t.date_created <> p.nueva_created');
        IF v_set_changed = 1 THEN
            SET v_difiere = CONCAT(v_difiere, ' OR NOT (t.date_changed <=> p.nueva_changed)');
        END IF;
        IF v_extra IS NOT NULL THEN
            SET v_difiere = CONCAT(v_difiere, ' OR NOT (t.', v_extra, ' <=> p.nueva_created)');
        END IF;

        -- COALESCE: una tabla puede no tener NINGUNA fila en el alcance (p.ej. patient_state
        -- si ningún paciente entró a un programa con workflow). El join sale vacío y SUM()
        -- devuelve NULL, que no cabe en las columnas NOT NULL del check.
        SET @sql = CONCAT(
            'INSERT INTO sim_fecha_check (orden, tabla, filas, pendientes, fuera_ventana, min_created, max_created) ',
            'SELECT ', v_orden, ', ''', v_tabla, ''', COUNT(*), ',
            'COALESCE(SUM(CASE WHEN ', v_difiere, ' THEN 1 ELSE 0 END), 0), ',
            'COALESCE(SUM(CASE WHEN t.date_created < ''', p_inicio, ''' OR t.date_created > ''', p_fin, ''' THEN 1 ELSE 0 END), 0), ',
            'MIN(t.date_created), MAX(t.date_created) ',
            'FROM ', v_tabla, ' t ',
            'JOIN sim_fecha_plan p ON p.tabla = ''', v_tabla, ''' AND p.pk = t.', v_pk);
        PREPARE st FROM @sql; EXECUTE st; DEALLOCATE PREPARE st;
    END LOOP;
    CLOSE cur;

    -- Los médicos son datos de referencia: su fecha es anterior a la ventana a propósito, no cuenta
    UPDATE sim_fecha_check SET fuera_ventana = 0 WHERE tabla = 'provider';

    -- Coherencias fuertes (0 = correcto)
    INSERT INTO sim_fecha_log (ejecucion, fase, tabla, filas, mensaje)
    SELECT p_ejecucion, 'verificar', 'person', COUNT(*), 'pacientes registrados DESPUÉS de su primera visita (debe ser 0)'
      FROM person t JOIN sim_fecha_ref r ON r.patient_id = t.person_id
     WHERE t.date_created >= r.primera_visita;

    INSERT INTO sim_fecha_log (ejecucion, fase, tabla, filas, mensaje)
    SELECT p_ejecucion, 'verificar', 'obs', COUNT(*), 'obs cuya fecha de creación no es su obs_datetime (debe ser 0)'
      FROM obs t JOIN sim_fecha_ref r ON r.patient_id = t.person_id
     WHERE t.date_created <> t.obs_datetime;

    INSERT INTO sim_fecha_log (ejecucion, fase, tabla, filas, mensaje)
    SELECT p_ejecucion, 'verificar', 'visit', COUNT(*), 'visitas cuya fecha de cierre no es su date_stopped (debe ser 0)'
      FROM visit t JOIN sim_fecha_ref r ON r.patient_id = t.patient_id
     WHERE NOT (t.date_changed <=> t.date_stopped);

    SELECT orden, tabla, filas, pendientes, fuera_ventana, min_created, max_created
      FROM sim_fecha_check ORDER BY orden;
END $$

-- ════════════════════════════════════════════════════════════════════════════
-- sp_sim_fechas_revertir — deshace una ejecución desde su snapshot
-- ════════════════════════════════════════════════════════════════════════════
DROP PROCEDURE IF EXISTS sp_sim_fechas_revertir $$
CREATE PROCEDURE sp_sim_fechas_revertir(IN p_ejecucion VARCHAR(40))
BEGIN
    DECLARE v_fin         TINYINT DEFAULT 0;
    DECLARE v_tabla       VARCHAR(48);
    DECLARE v_pk          VARCHAR(48);
    DECLARE v_set_changed TINYINT;
    DECLARE v_extra       VARCHAR(48);
    DECLARE v_filas       BIGINT;

    DECLARE cur CURSOR FOR
        SELECT tabla, pk_col, set_changed, col_extra FROM sim_fecha_tabla ORDER BY orden DESC;
    DECLARE CONTINUE HANDLER FOR NOT FOUND SET v_fin = 1;

    OPEN cur;
    bucle: LOOP
        FETCH cur INTO v_tabla, v_pk, v_set_changed, v_extra;
        IF v_fin = 1 THEN LEAVE bucle; END IF;

        SET @sql = CONCAT(
            'UPDATE ', v_tabla, ' t ',
            'JOIN sim_fecha_backup b ON b.tabla = ''', v_tabla, ''' AND b.pk = t.', v_pk,
            ' AND b.ejecucion = ''', p_ejecucion, ''' ',
            'SET t.date_created = b.date_created_old',
            IF(v_set_changed = 1, ', t.date_changed = b.date_changed_old', ''),
            IF(v_extra IS NULL, '', CONCAT(', t.', v_extra, ' = b.extra_old')));
        PREPARE st FROM @sql; EXECUTE st;
        SET v_filas = ROW_COUNT();
        DEALLOCATE PREPARE st;

        INSERT INTO sim_fecha_log (ejecucion, fase, tabla, filas, mensaje)
        VALUES (p_ejecucion, 'revertir', v_tabla, v_filas, 'filas restauradas');
    END LOOP;
    CLOSE cur;

    SELECT tabla, filas FROM sim_fecha_log
     WHERE ejecucion = p_ejecucion AND fase = 'revertir' AND mensaje = 'filas restauradas';
END $$

DELIMITER ;
