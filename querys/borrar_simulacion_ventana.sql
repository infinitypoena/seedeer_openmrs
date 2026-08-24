-- ============================================================================
-- borrar_simulacion_ventana.sql — Borrado FÍSICO de SOLO los pacientes SIM- que
-- tuvieron ALGUNA visita dentro de una ventana de fechas (ambos extremos incluidos).
-- ----------------------------------------------------------------------------
-- Hermano de borrar_simulacion.sql (que borra TODO el SIM-), pero ACOTADO a una
-- ventana para poder re-ejecutar una corrida sin arrastrar historial previo de
-- esas fechas, dejando intacto el resto (corridas históricas de otras fechas).
--
-- ⚠️ Edita las dos fechas de la ventana (V_INICIO y V_FIN) antes de ejecutar.
--
-- ⚠️ Un paciente con visitas dentro Y fuera de la ventana se borra COMPLETO
-- (no se deja a medio camino): es la elección deliberada para que la prueba de
-- la ventana arranque con panel limpio, sin crónicas/citas que arrastrar.
--
-- Cubre todo lo que siembra el simulador: visitas, encuentros, obs, diagnósticos,
-- órdenes, condiciones, alergias, citas Bahmni y programas de atención.
--
-- Uso (dentro del contenedor que tiene MariaDB):
--   Get-Content querys/borrar_simulacion_ventana.sql -Raw | docker exec -i \
--     openmrs-distro-referenceapplication-360-db-1 \
--     mysql -uopenmrs -p<CLAVE> openmrs
-- (El usuario openmrs basta; no necesita root.)
--
-- Ejemplo real usado (ago 2026): limpiar mayo-jul 2026 para re-ejecutar la prueba
-- de 3 meses con ~5 pacientes diarios.
-- ============================================================================

SET NAMES utf8mb4 COLLATE utf8mb4_general_ci;
SET @v_inicio = '2026-05-01';
SET @v_fin    = '2026-08-01';   -- EXCLUSIVO: equivale a "hasta el 31-07 incluido"

-- ── 1) Pacientes SIM- con al menos una visita en la ventana ──
DROP TEMPORARY TABLE IF EXISTS _sim_pat;
CREATE TEMPORARY TABLE _sim_pat (patient_id INT PRIMARY KEY);
INSERT INTO _sim_pat
    SELECT DISTINCT v.patient_id
    FROM visit v
    JOIN patient_identifier pi ON pi.patient_id = v.patient_id
    WHERE pi.identifier LIKE 'SIM-%'
      AND v.date_started >= @v_inicio
      AND v.date_started <  @v_fin;

DROP TEMPORARY TABLE IF EXISTS _sim_enc;
CREATE TEMPORARY TABLE _sim_enc (id INT PRIMARY KEY);
INSERT INTO _sim_enc
    SELECT encounter_id FROM encounter WHERE patient_id IN (SELECT patient_id FROM _sim_pat);

DROP TEMPORARY TABLE IF EXISTS _sim_vis;
CREATE TEMPORARY TABLE _sim_vis (id INT PRIMARY KEY);
INSERT INTO _sim_vis
    SELECT visit_id FROM visit WHERE patient_id IN (SELECT patient_id FROM _sim_pat);

DROP TEMPORARY TABLE IF EXISTS _sim_ord;
CREATE TEMPORARY TABLE _sim_ord (id INT PRIMARY KEY);
INSERT INTO _sim_ord
    SELECT order_id FROM orders WHERE patient_id IN (SELECT patient_id FROM _sim_pat);

DROP TEMPORARY TABLE IF EXISTS _sim_alg;
CREATE TEMPORARY TABLE _sim_alg (id INT PRIMARY KEY);
INSERT INTO _sim_alg
    SELECT allergy_id FROM allergy WHERE patient_id IN (SELECT patient_id FROM _sim_pat);

-- Citas Bahmni: patient_appointment.patient_appointment_id es la PK que referencia
-- patient_appointment_provider.patient_appointment_id.
DROP TEMPORARY TABLE IF EXISTS _sim_appt;
CREATE TEMPORARY TABLE _sim_appt (id INT PRIMARY KEY);
INSERT INTO _sim_appt
    SELECT patient_appointment_id FROM patient_appointment WHERE patient_id IN (SELECT patient_id FROM _sim_pat);

-- ── 2) Vista previa ──
SELECT
    (SELECT COUNT(*) FROM _sim_pat)                                              AS pacientes_ventana,
    (SELECT COUNT(*) FROM _sim_vis)                                              AS visitas,
    (SELECT COUNT(*) FROM _sim_enc)                                              AS encuentros,
    (SELECT COUNT(*) FROM obs WHERE person_id IN (SELECT patient_id FROM _sim_pat)) AS observaciones,
    (SELECT COUNT(*) FROM _sim_ord)                                              AS ordenes,
    (SELECT COUNT(*) FROM _sim_alg)                                              AS alergias,
    (SELECT COUNT(*) FROM _sim_appt)                                             AS citas;

-- ── 3) Borrado, hijo → padre, dentro de una transacción ──
SET FOREIGN_KEY_CHECKS = 0;
START TRANSACTION;

-- Citas Bahmni
DELETE FROM patient_appointment_provider WHERE patient_appointment_id IN (SELECT id FROM _sim_appt);
DELETE FROM patient_appointment          WHERE patient_appointment_id IN (SELECT id FROM _sim_appt);

DELETE FROM allergy_reaction    WHERE allergy_id   IN (SELECT id FROM _sim_alg);
DELETE FROM allergy             WHERE patient_id   IN (SELECT patient_id FROM _sim_pat);

DELETE FROM obs                 WHERE person_id    IN (SELECT patient_id FROM _sim_pat);
DELETE FROM encounter_diagnosis WHERE patient_id   IN (SELECT patient_id FROM _sim_pat);
DELETE FROM encounter_provider  WHERE encounter_id IN (SELECT id FROM _sim_enc);
DELETE FROM note                WHERE patient_id   IN (SELECT patient_id FROM _sim_pat);

DELETE FROM drug_order          WHERE order_id     IN (SELECT id FROM _sim_ord);
DELETE FROM test_order          WHERE order_id     IN (SELECT id FROM _sim_ord);
DELETE FROM order_attribute     WHERE order_id     IN (SELECT id FROM _sim_ord);
DELETE FROM orders              WHERE patient_id   IN (SELECT patient_id FROM _sim_pat);

DELETE FROM conditions          WHERE patient_id   IN (SELECT patient_id FROM _sim_pat);

-- Programas de atención (ProgramEnrollmentSeeder): estados y atributos antes que la inscripción
DELETE FROM patient_state WHERE patient_program_id IN
    (SELECT patient_program_id FROM patient_program WHERE patient_id IN (SELECT patient_id FROM _sim_pat));
DELETE FROM patient_program_attribute WHERE patient_program_id IN
    (SELECT patient_program_id FROM patient_program WHERE patient_id IN (SELECT patient_id FROM _sim_pat));
DELETE FROM patient_program WHERE patient_id   IN (SELECT patient_id FROM _sim_pat);

DELETE FROM encounter           WHERE patient_id   IN (SELECT patient_id FROM _sim_pat);
DELETE FROM visit_attribute     WHERE visit_id     IN (SELECT id FROM _sim_vis);
DELETE FROM visit               WHERE patient_id   IN (SELECT patient_id FROM _sim_pat);

DELETE FROM patient_identifier  WHERE patient_id   IN (SELECT patient_id FROM _sim_pat);
DELETE FROM patient             WHERE patient_id   IN (SELECT patient_id FROM _sim_pat);

DELETE FROM person_name         WHERE person_id    IN (SELECT patient_id FROM _sim_pat);
DELETE FROM person_address      WHERE person_id    IN (SELECT patient_id FROM _sim_pat);
DELETE FROM person_attribute    WHERE person_id    IN (SELECT patient_id FROM _sim_pat);
DELETE FROM person              WHERE person_id    IN (SELECT patient_id FROM _sim_pat);

COMMIT;
SET FOREIGN_KEY_CHECKS = 1;

-- ── 4) Verificación ──
SELECT
    (SELECT COUNT(*) FROM patient_identifier WHERE identifier LIKE 'SIM-%')      AS sim_restantes_total,
    (SELECT COUNT(*) FROM patient_identifier pi
        JOIN visit v ON v.patient_id = pi.patient_id
        WHERE pi.identifier LIKE 'SIM-%' AND v.date_started >= @v_inicio AND v.date_started < @v_fin) AS sim_ventana_restantes,
    (SELECT COUNT(*) FROM provider WHERE identifier LIKE 'SIM-MED-%')            AS medicos_intactos;

DROP TEMPORARY TABLE IF EXISTS _sim_pat;
DROP TEMPORARY TABLE IF EXISTS _sim_enc;
DROP TEMPORARY TABLE IF EXISTS _sim_vis;
DROP TEMPORARY TABLE IF EXISTS _sim_ord;
DROP TEMPORARY TABLE IF EXISTS _sim_alg;
DROP TEMPORARY TABLE IF EXISTS _sim_appt;
