-- ============================================================================
-- borrar_simulacion.sql — Borra SOLO los datos simulados (pacientes SIM-)
-- ----------------------------------------------------------------------------
-- Elimina de forma definitiva los PACIENTES con identificador 'SIM-%' y TODO su
-- rastro clínico: visitas, encuentros (consultas/vitales), diagnósticos,
-- observaciones, órdenes (labs/medicamentos), condiciones y alergias.
--
-- NO toca nada más: médicos (providers SIM-MED-*), ubicaciones/consultorios,
-- conceptos, fármacos del formulario, ni los pacientes demo de OpenMRS.
--
-- Uso:
--   docker exec -i openmrs-distro-referenceapplication-360-db-1 \
--     mysql -uopenmrs -p<MYSQL_PASSWORD> openmrs < querys/borrar_simulacion.sql
--
-- Equivale (más rápido y definitivo) al endpoint DELETE /api/seed/clear, que solo
-- "anula" (voided=1). Este script BORRA físicamente. Es transaccional (todo o nada).
-- ============================================================================

SET NAMES utf8mb4 COLLATE utf8mb4_general_ci;

-- ── 1) Capturar los IDs objetivo: SOLO pacientes con identificador 'SIM-%' ──
DROP TEMPORARY TABLE IF EXISTS _sim_pat;
CREATE TEMPORARY TABLE _sim_pat (patient_id INT PRIMARY KEY);
INSERT INTO _sim_pat
    SELECT DISTINCT patient_id
    FROM patient_identifier
    WHERE identifier LIKE 'SIM-%';   -- incluye anulados (voided) para limpieza total

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

-- ── 2) Vista previa: qué se va a borrar ─────────────────────────────────────
SELECT
    (SELECT COUNT(*) FROM _sim_pat)                                              AS pacientes_sim,
    (SELECT COUNT(*) FROM _sim_vis)                                              AS visitas,
    (SELECT COUNT(*) FROM _sim_enc)                                              AS encuentros,
    (SELECT COUNT(*) FROM encounter_diagnosis WHERE patient_id IN (SELECT patient_id FROM _sim_pat)) AS diagnosticos,
    (SELECT COUNT(*) FROM obs WHERE person_id IN (SELECT patient_id FROM _sim_pat)) AS observaciones,
    (SELECT COUNT(*) FROM _sim_ord)                                              AS ordenes,
    (SELECT COUNT(*) FROM conditions WHERE patient_id IN (SELECT patient_id FROM _sim_pat)) AS condiciones,
    (SELECT COUNT(*) FROM _sim_alg)                                              AS alergias;

-- ── 3) Borrado, hijo → padre, dentro de una transacción ─────────────────────
SET FOREIGN_KEY_CHECKS = 0;
START TRANSACTION;

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

-- ── 4) Verificación: SIM en 0, el resto intacto ─────────────────────────────
SELECT
    (SELECT COUNT(*) FROM patient_identifier WHERE identifier LIKE 'SIM-%')      AS sim_restantes,
    (SELECT COUNT(*) FROM patient)                                               AS pacientes_totales,
    (SELECT COUNT(*) FROM provider WHERE identifier LIKE 'SIM-MED-%')            AS medicos_intactos,
    (SELECT COUNT(*) FROM location)                                              AS ubicaciones_intactas;

-- Limpieza de tablas temporales
DROP TEMPORARY TABLE IF EXISTS _sim_pat;
DROP TEMPORARY TABLE IF EXISTS _sim_enc;
DROP TEMPORARY TABLE IF EXISTS _sim_vis;
DROP TEMPORARY TABLE IF EXISTS _sim_ord;
DROP TEMPORARY TABLE IF EXISTS _sim_alg;
