-- ============================================================================
-- generales.sql — Vistas GENERALES de la clínica simulada, listas para mirar
-- ----------------------------------------------------------------------------
-- Una consulta por dominio, cada una con valor real por sí sola: el padrón de
-- pacientes con sus visitas, el volumen mes a mes, qué se diagnostica, qué
-- exámenes se piden (y cómo salen), qué se receta, cómo va la agenda y qué
-- alergias hay documentadas. Todas acotadas a los pacientes SIM-.
--
-- Uso:
--   docker exec -i <db> mariadb -uroot -p<pass> openmrs < querys/generales.sql
-- ============================================================================

-- Las tablas de OpenMRS son utf8mb4_general_ci; un cliente que conecte en unicode_ci
-- hace fallar el LIKE con "Illegal mix of collations". Se fija la sesion a la de las tablas.
SET NAMES utf8mb4 COLLATE utf8mb4_general_ci;

SET @prefijo = 'SIM-';

-- ── 1. PACIENTES · el padrón completo con su cantidad de visitas ─────────────
-- De más a menos visitas: arriba los recurrentes de verdad, abajo los de una
-- sola vez.
SELECT
    pi.identifier                                           AS identificador,
    CONCAT_WS(' ', pn.given_name, pn.middle_name,
                   pn.family_name, pn.family_name2)          AS nombre_completo,
    p.birthdate                                             AS nacimiento,
    p.gender                                                AS sexo,
    COUNT(v.visit_id)                                       AS visitas,
    MIN(DATE(v.date_started))                               AS primera_visita,
    MAX(DATE(v.date_started))                               AS ultima_visita
FROM patient_identifier pi
JOIN person p       ON p.person_id = pi.patient_id AND p.voided = 0
JOIN person_name pn ON pn.person_id = pi.patient_id AND pn.voided = 0
LEFT JOIN visit v   ON v.patient_id = pi.patient_id AND v.voided = 0
WHERE pi.voided = 0 AND pi.identifier LIKE CONCAT(@prefijo, '%')
GROUP BY pi.identifier, nombre_completo, p.birthdate, p.gender
ORDER BY visitas DESC, pi.identifier;

-- ── 2. VISITAS · volumen mes a mes ───────────────────────────────────────────
-- La curva de la clínica: visitas, pacientes distintos del mes y media por día
-- con atención. En una corrida con crecimiento, la media diaria tiene que subir.
SELECT
    DATE_FORMAT(v.date_started, '%Y-%m')                    AS mes,
    COUNT(*)                                                AS visitas,
    COUNT(DISTINCT v.patient_id)                            AS pacientes_distintos,
    COUNT(DISTINCT DATE(v.date_started))                    AS dias_con_atencion,
    ROUND(COUNT(*) / COUNT(DISTINCT DATE(v.date_started)), 1) AS visitas_por_dia
FROM visit v
JOIN patient_identifier pi ON pi.patient_id = v.patient_id AND pi.voided = 0
                          AND pi.identifier LIKE CONCAT(@prefijo, '%')
WHERE v.voided = 0
GROUP BY mes
ORDER BY mes;

-- ── 3. DIAGNÓSTICOS · qué se diagnostica y a cuántos ─────────────────────────
-- Frecuencia real de cada diagnóstico: veces total, cuántas como motivo
-- principal (rank 1) vs comorbilidad (rank 2), pacientes distintos y certeza.
SELECT
    cn.name                                                 AS diagnostico,
    COUNT(*)                                                AS veces,
    SUM(ed.dx_rank = 1)                                     AS como_primario,
    SUM(ed.dx_rank = 2)                                     AS como_comorbilidad,
    COUNT(DISTINCT ed.patient_id)                           AS pacientes_distintos,
    SUM(ed.certainty = 'CONFIRMED')                         AS confirmados,
    SUM(ed.certainty = 'PROVISIONAL')                       AS provisionales
FROM encounter_diagnosis ed
JOIN concept c       ON c.concept_id = ed.diagnosis_coded
JOIN concept_name cn ON cn.concept_id = c.concept_id AND cn.locale = 'es'
                    AND cn.locale_preferred = 1 AND cn.voided = 0
JOIN patient_identifier pi ON pi.patient_id = ed.patient_id AND pi.voided = 0
                          AND pi.identifier LIKE CONCAT(@prefijo, '%')
WHERE ed.voided = 0
GROUP BY cn.name
ORDER BY veces DESC;

-- ── 4. EXÁMENES DE LABORATORIO · qué se pide y cómo sale ─────────────────────
-- Por examen: órdenes, urgentes (STAT), el ciclo de la orden (completadas /
-- rechazadas / en curso) y cuántas tienen resultado registrado.
SELECT
    cn.name                                                 AS examen,
    COUNT(*)                                                AS ordenes,
    SUM(o.urgency = 'STAT')                                 AS urgentes,
    SUM(o.fulfiller_status = 'COMPLETED')                   AS completadas,
    SUM(o.fulfiller_status = 'DECLINED')                    AS rechazadas,
    SUM(o.fulfiller_status = 'IN_PROGRESS')                 AS en_curso,
    SUM(res.n IS NOT NULL)                                  AS con_resultado,
    COUNT(DISTINCT o.patient_id)                            AS pacientes_distintos
FROM orders o
JOIN order_type ot ON ot.order_type_id = o.order_type_id
                  AND ot.java_class_name = 'org.openmrs.TestOrder'
JOIN concept c       ON c.concept_id = o.concept_id
JOIN concept_name cn ON cn.concept_id = c.concept_id AND cn.locale = 'es'
                    AND cn.locale_preferred = 1 AND cn.voided = 0
JOIN patient_identifier pi ON pi.patient_id = o.patient_id AND pi.voided = 0
                          AND pi.identifier LIKE CONCAT(@prefijo, '%')
LEFT JOIN (SELECT ob.order_id, COUNT(*) n FROM obs ob WHERE ob.voided = 0
           GROUP BY ob.order_id) res ON res.order_id = o.order_id
WHERE o.voided = 0
GROUP BY cn.name
ORDER BY ordenes DESC;

-- ── 5. MEDICAMENTOS · qué se receta ──────────────────────────────────────────
SELECT
    d.name                                                  AS medicamento,
    COUNT(*)                                                AS prescripciones,
    COUNT(DISTINCT o.patient_id)                            AS pacientes_distintos
FROM orders o
JOIN order_type ot ON ot.order_type_id = o.order_type_id
                  AND ot.java_class_name = 'org.openmrs.DrugOrder'
JOIN drug_order dor ON dor.order_id = o.order_id
JOIN drug d         ON d.drug_id = dor.drug_inventory_id
JOIN patient_identifier pi ON pi.patient_id = o.patient_id AND pi.voided = 0
                          AND pi.identifier LIKE CONCAT(@prefijo, '%')
WHERE o.voided = 0
GROUP BY d.name
ORDER BY prescripciones DESC;

-- ── 6. AGENDA · las citas por estado ─────────────────────────────────────────
-- Una agenda sana: mayoría Completed, no-shows moderados (Missed ≤ 25 %, ley
-- L2) y ninguna Scheduled vencida al cierre de la corrida.
SELECT
    pa.status                                               AS estado,
    COUNT(*)                                                AS citas,
    ROUND(100 * COUNT(*) / SUM(COUNT(*)) OVER (), 1)        AS pct
FROM patient_appointment pa
JOIN patient_identifier pi ON pi.patient_id = pa.patient_id AND pi.voided = 0
                          AND pi.identifier LIKE CONCAT(@prefijo, '%')
WHERE COALESCE(pa.voided, 0) = 0
GROUP BY pa.status
ORDER BY citas DESC;

-- ── 7. ALERGIAS · qué hay documentado ────────────────────────────────────────
SELECT
    cn.name                                                 AS alergeno,
    a.allergen_type                                         AS tipo,
    COUNT(*)                                                AS pacientes
FROM allergy a
JOIN concept c       ON c.concept_id = a.coded_allergen
JOIN concept_name cn ON cn.concept_id = c.concept_id AND cn.locale = 'es'
                    AND cn.locale_preferred = 1 AND cn.voided = 0
JOIN patient_identifier pi ON pi.patient_id = a.patient_id AND pi.voided = 0
                          AND pi.identifier LIKE CONCAT(@prefijo, '%')
WHERE a.voided = 0
GROUP BY cn.name, a.allergen_type
ORDER BY pacientes DESC;

-- ── 8. RESUMEN · la clínica en una fila ──────────────────────────────────────
SELECT
    (SELECT COUNT(DISTINCT pi.patient_id) FROM patient_identifier pi
      WHERE pi.voided = 0 AND pi.identifier LIKE CONCAT(@prefijo, '%')) AS pacientes,
    (SELECT COUNT(*) FROM visit v
      JOIN patient_identifier pi ON pi.patient_id = v.patient_id AND pi.voided = 0
       AND pi.identifier LIKE CONCAT(@prefijo, '%')
      WHERE v.voided = 0)                                               AS visitas,
    (SELECT COUNT(*) FROM encounter_diagnosis ed
      JOIN patient_identifier pi ON pi.patient_id = ed.patient_id AND pi.voided = 0
       AND pi.identifier LIKE CONCAT(@prefijo, '%')
      WHERE ed.voided = 0)                                              AS diagnosticos,
    (SELECT COUNT(*) FROM orders o
      JOIN order_type ot ON ot.order_type_id = o.order_type_id
       AND ot.java_class_name = 'org.openmrs.TestOrder'
      JOIN patient_identifier pi ON pi.patient_id = o.patient_id AND pi.voided = 0
       AND pi.identifier LIKE CONCAT(@prefijo, '%')
      WHERE o.voided = 0)                                               AS ordenes_lab,
    (SELECT COUNT(*) FROM orders o
      JOIN order_type ot ON ot.order_type_id = o.order_type_id
       AND ot.java_class_name = 'org.openmrs.DrugOrder'
      JOIN patient_identifier pi ON pi.patient_id = o.patient_id AND pi.voided = 0
       AND pi.identifier LIKE CONCAT(@prefijo, '%')
      WHERE o.voided = 0)                                               AS prescripciones,
    (SELECT COUNT(*) FROM patient_appointment pa
      JOIN patient_identifier pi ON pi.patient_id = pa.patient_id AND pi.voided = 0
       AND pi.identifier LIKE CONCAT(@prefijo, '%')
      WHERE COALESCE(pa.voided, 0) = 0)                                 AS citas;
