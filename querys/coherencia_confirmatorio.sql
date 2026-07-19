-- ============================================================================
-- coherencia_confirmatorio.sql — QA de la coherencia diagnóstico ↔ examen
-- ----------------------------------------------------------------------------
-- Jul 2026: (A) un dx con examen confirmatorio (res_trigger_dx en laboratorios.csv)
-- fuerza la ORDEN de ese examen (LabOrderSelector) y condiciona la certainty del
-- dx (CertaintyPolicy: confirmatorio interno → CONFIRMED, externo → PROVISIONAL);
-- (B) el chequeo voluntario: visitas SIN diagnóstico con solo labs chequeo=true y
-- resultados normales, y el examen extra "Solicitado por el paciente".
--
-- El mapeo dx→lab del bloque 1 es un ESPEJO de res_trigger_dx (laboratorios.csv):
-- si se edita el catálogo hay que actualizarlo aquí.
--
-- Uso:
--   docker exec -i <db> mariadb -uroot -p<pass> openmrs < querys/coherencia_confirmatorio.sql
-- ============================================================================

-- Las tablas de OpenMRS son utf8mb4_general_ci; un cliente que conecte en unicode_ci
-- hace fallar el LIKE con "Illegal mix of collations". Se fija la sesion a la de las tablas.
SET NAMES utf8mb4 COLLATE utf8mb4_general_ci;

SET @prefijo = 'SIM-';

-- ── 1. INVARIANTE: el dx confirmable lleva SIEMPRE la orden de su examen ─────
-- Por cada encuentro de consulta con un dx confirmable, ¿está la orden del lab
-- confirmatorio en ese encuentro? pct_con_orden debe rondar el 100 % — la única
-- excepción legítima es una orden AÚN VIGENTE de una visita anterior (control a
-- <7 días, raro). Un % bajo = el confirmatorio ha dejado de forzarse.
WITH mapeo (dx_nombre, dx_uuid, lab_uuid) AS (
    SELECT 'Dengue sin signos',   '61304dd2-7f0c-4933-9f31-c1a909200a71', '58b969e7-77ef-4941-a0ec-72372a2fa716' UNION ALL
    SELECT 'Dengue con signos',   '67acf134-be8a-49ae-b90b-e8bb4a158494', '58b969e7-77ef-4941-a0ec-72372a2fa716' UNION ALL
    SELECT 'ITU/cistitis',        '111633AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', '302AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' UNION ALL
    SELECT 'Cistitis',            '119685AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', '302AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' UNION ALL
    SELECT 'Sífilis',             '112492AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', '299AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' UNION ALL
    SELECT 'VIH',                 '149197AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', '1042AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' UNION ALL
    SELECT 'DM2',                 '119481AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', '160912AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' UNION ALL
    SELECT 'Hipotiroidismo',      '117321AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', '161505AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' UNION ALL
    SELECT 'Gota',                '117762AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', '159825AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' UNION ALL
    SELECT 'Anemia',              '121629AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', '1019AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' UNION ALL
    SELECT 'Dislipidemia',        '141623AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', '1010AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' UNION ALL
    SELECT 'ERC',                 '120574AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', '164364AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'
)
SELECT
    m.dx_nombre,
    COUNT(*)                                                        AS encuentros_con_dx,
    SUM(orden.order_id IS NOT NULL)                                 AS con_orden_confirmatoria,
    ROUND(100 * SUM(orden.order_id IS NOT NULL) / COUNT(*), 1)      AS pct_con_orden
FROM encounter_diagnosis ed
JOIN concept cdx        ON cdx.concept_id = ed.diagnosis_coded AND cdx.uuid = (SELECT dx_uuid FROM mapeo m2 WHERE m2.dx_uuid = cdx.uuid LIMIT 1)
JOIN mapeo m            ON m.dx_uuid = cdx.uuid
JOIN encounter e        ON e.encounter_id = ed.encounter_id AND e.voided = 0
JOIN patient_identifier pi ON pi.patient_id = e.patient_id AND pi.voided = 0
                          AND pi.identifier LIKE CONCAT(@prefijo, '%')
LEFT JOIN (
    SELECT o.order_id, o.encounter_id, c.uuid AS lab_uuid
    FROM orders o JOIN concept c ON c.concept_id = o.concept_id
    WHERE o.voided = 0
) orden ON orden.encounter_id = e.encounter_id AND orden.lab_uuid = m.lab_uuid
WHERE ed.voided = 0
GROUP BY m.dx_nombre
ORDER BY encuentros_con_dx DESC;

-- ── 2. Certainty coherente con el confirmatorio ──────────────────────────────
-- Un dx cuyo confirmatorio es INTERNO (orina, glucemia, NS1…) sale CONFIRMED en
-- la visita que lo estrena; uno EXTERNO (VIH, TSH, lipídico…) sale PROVISIONAL
-- (pendiente de confirmación) — y CONFIRMED en sus visitas de CONTROL.
SELECT
    cn.name                    AS diagnostico,
    ed.certainty,
    COUNT(*)                   AS veces
FROM encounter_diagnosis ed
JOIN concept c   ON c.concept_id = ed.diagnosis_coded
JOIN concept_name cn ON cn.concept_id = c.concept_id AND cn.locale = 'es'
                    AND cn.locale_preferred = 1 AND cn.voided = 0
JOIN encounter e ON e.encounter_id = ed.encounter_id AND e.voided = 0
JOIN patient_identifier pi ON pi.patient_id = e.patient_id AND pi.voided = 0
                          AND pi.identifier LIKE CONCAT(@prefijo, '%')
WHERE ed.voided = 0
  AND c.uuid IN ('1042AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',  -- (no aplica: es lab) —
                 '149197AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',  -- VIH (externo → PROVISIONAL)
                 '117321AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',  -- Hipotiroidismo (externo)
                 '141623AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',  -- Dislipidemia (externo)
                 '111633AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',  -- ITU (interno → CONFIRMED)
                 '119481AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',  -- DM2 (interno)
                 '61304dd2-7f0c-4933-9f31-c1a909200a71')  -- Dengue (interno)
GROUP BY cn.name, ed.certainty
ORDER BY cn.name, ed.certainty;

-- ── 3. INVARIANTE: la visita de chequeo va limpia ────────────────────────────
-- Encuentros de consulta SIN diagnóstico = chequeos voluntarios. Deben tener:
-- 0 drug orders, solo test orders del pool de chequeo, y motivo de consulta.
-- (encounter type Consultation dd528487-82a5-4082-9c72-ed246bd49591)
SELECT
    COUNT(DISTINCT e.encounter_id)                            AS chequeos,
    SUM(dord.n_drug_orders IS NOT NULL)                       AS con_recetas_DEBE_SER_0,
    ROUND(AVG(tord.n_test_orders), 1)                         AS labs_medios_por_chequeo
FROM encounter e
JOIN encounter_type et ON et.encounter_type_id = e.encounter_type
                      AND et.uuid = 'dd528487-82a5-4082-9c72-ed246bd49591'
JOIN patient_identifier pi ON pi.patient_id = e.patient_id AND pi.voided = 0
                          AND pi.identifier LIKE CONCAT(@prefijo, '%')
LEFT JOIN encounter_diagnosis ed ON ed.encounter_id = e.encounter_id AND ed.voided = 0
LEFT JOIN (
    SELECT o.encounter_id, COUNT(*) n_drug_orders
    FROM orders o JOIN order_type ot ON ot.order_type_id = o.order_type_id
                                    AND ot.java_class_name = 'org.openmrs.DrugOrder'
    WHERE o.voided = 0 GROUP BY o.encounter_id
) dord ON dord.encounter_id = e.encounter_id
LEFT JOIN (
    SELECT o.encounter_id, COUNT(*) n_test_orders
    FROM orders o JOIN order_type ot ON ot.order_type_id = o.order_type_id
                                    AND ot.java_class_name = 'org.openmrs.TestOrder'
    WHERE o.voided = 0 GROUP BY o.encounter_id
) tord ON tord.encounter_id = e.encounter_id
WHERE e.voided = 0 AND ed.diagnosis_id IS NULL;

-- ── 4. El examen a petición del paciente queda trazado ───────────────────────
-- Órdenes cuyo comment_to_fulfiller empieza por "Solicitado por el paciente":
-- las de chequeos + las extra de enfermos. Todas ROUTINE (nunca STAT).
SELECT
    o.urgency,
    COUNT(*) AS ordenes
FROM orders o
JOIN patient_identifier pi ON pi.patient_id = o.patient_id AND pi.voided = 0
                          AND pi.identifier LIKE CONCAT(@prefijo, '%')
WHERE o.voided = 0
  AND o.comment_to_fulfiller LIKE 'Solicitado por el paciente%'
GROUP BY o.urgency;
