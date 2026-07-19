-- ============================================================================
-- laboratorio.sql — QA del ciclo de vida de la orden de laboratorio
-- ----------------------------------------------------------------------------
-- Antes, el simulador creaba la orden y no tocaba nunca `fulfiller_status`: todas
-- se quedaban en "Tests ordered" (la cola de la app de laboratorio de O3 no las veía
-- avanzar nunca). Ahora la muestra se toma (IN_PROGRESS), el resultado se registra en
-- un encuentro PROPIO del laboratorio y la orden se cierra (COMPLETED) — o se rechaza
-- (DECLINED). Lo que no hace la clínica se refiere a un laboratorio externo y vuelve
-- días después.
--
-- Estas consultas comprueban que eso pasó de verdad en la BD.
--
-- Uso:
--   docker exec -i <db> mariadb -uroot -p<pass> openmrs < querys/laboratorio.sql
-- ============================================================================

-- Las tablas de OpenMRS son utf8mb4_general_ci; un cliente que conecte en unicode_ci
-- hace fallar el LIKE con "Illegal mix of collations". Se fija la sesion a la de las tablas.
SET NAMES utf8mb4 COLLATE utf8mb4_general_ci;

SET @prefijo = 'SIM-';

-- ── 1. Reparto por estado (lo que se ve en la cola de la app) ────────────────
-- Un reparto sano: la mayoría COMPLETED, un puñado DECLINED (~4 %), unas pocas
-- IN_PROGRESS (los resultados que se perdieron) y NINGUNA sin estado.
SELECT
    COALESCE(o.fulfiller_status, '(sin tomar)') AS estado,
    COUNT(*)                                    AS ordenes,
    ROUND(100 * COUNT(*) / SUM(COUNT(*)) OVER (), 1) AS pct
FROM orders o
JOIN order_type ot ON ot.order_type_id = o.order_type_id
                  AND ot.java_class_name = 'org.openmrs.TestOrder'
JOIN patient_identifier pi ON pi.patient_id = o.patient_id AND pi.voided = 0
WHERE o.voided = 0 AND pi.identifier LIKE CONCAT(@prefijo, '%')
GROUP BY estado
ORDER BY ordenes DESC;

-- ── 2. Interno vs. externo: ¿tarda lo que dice el catálogo? ──────────────────
-- Días reales entre la orden y su resultado, según la instrucción que lleva la orden.
-- Lo que se procesa en la clínica debe salir el MISMO día (0); lo externo, varios días.
SELECT
    o.comment_to_fulfiller                                   AS instruccion,
    COUNT(*)                                                 AS resultados,
    MIN(DATEDIFF(DATE(ob.obs_datetime), DATE(o.date_activated))) AS dias_min,
    ROUND(AVG(DATEDIFF(DATE(ob.obs_datetime), DATE(o.date_activated))), 1) AS dias_medio,
    MAX(DATEDIFF(DATE(ob.obs_datetime), DATE(o.date_activated))) AS dias_max
FROM orders o
JOIN obs ob ON ob.order_id = o.order_id AND ob.voided = 0
JOIN patient_identifier pi ON pi.patient_id = o.patient_id AND pi.voided = 0
WHERE o.voided = 0 AND pi.identifier LIKE CONCAT(@prefijo, '%')
GROUP BY o.comment_to_fulfiller;

-- ── 3. INVARIANTE: el resultado lo firma el LABORATORIO, no el médico ───────
-- Toda obs de resultado (la que va ligada a una orden) debe colgar de un encuentro
-- de tipo "Lab Results". Debe dar 0.
SELECT COUNT(*) AS resultados_fuera_del_encuentro_de_laboratorio
FROM obs ob
JOIN orders o     ON o.order_id = ob.order_id AND o.voided = 0
JOIN encounter e  ON e.encounter_id = ob.encounter_id
JOIN encounter_type et ON et.encounter_type_id = e.encounter_type
JOIN patient_identifier pi ON pi.patient_id = ob.person_id AND pi.voided = 0
WHERE ob.voided = 0
  AND pi.identifier LIKE CONCAT(@prefijo, '%')
  AND et.name <> 'Lab Results';

-- ── 4. INVARIANTE: ningún componente de panel sin encuentro ─────────────────
-- La REST API NO propaga el `encounter` del padre a los groupMembers. Debe dar 0.
SELECT COUNT(*) AS obs_de_panel_sin_encuentro
FROM obs hija
JOIN obs padre ON padre.obs_id = hija.obs_group_id AND padre.voided = 0
JOIN patient_identifier pi ON pi.patient_id = hija.person_id AND pi.voided = 0
WHERE hija.voided = 0
  AND hija.encounter_id IS NULL
  AND pi.identifier LIKE CONCAT(@prefijo, '%');

-- ── 5. INVARIANTE: nada que se haga en la clínica queda sin resolver ────────
-- Una orden interna se toma y se resuelve el mismo día. Si quedan muchas en curso,
-- algo falló (lo normal es solo la fracción de resultados que se pierde).
SELECT
    o.fulfiller_status,
    COUNT(*) AS ordenes_internas_sin_completar
FROM orders o
JOIN patient_identifier pi ON pi.patient_id = o.patient_id AND pi.voided = 0
WHERE o.voided = 0
  AND pi.identifier LIKE CONCAT(@prefijo, '%')
  AND o.comment_to_fulfiller LIKE '%clínica%'
  AND (o.fulfiller_status IS NULL OR o.fulfiller_status = 'IN_PROGRESS')
GROUP BY o.fulfiller_status;

-- ── 6. Quién trabaja en el laboratorio (los encuentros los firma el técnico) ─
SELECT
    CONCAT(pn.given_name, ' ', pn.family_name) AS profesional,
    pr.identifier                              AS codigo,
    COUNT(DISTINCT e.encounter_id)             AS encuentros_de_laboratorio
FROM encounter e
JOIN encounter_type et ON et.encounter_type_id = e.encounter_type AND et.name = 'Lab Results'
JOIN encounter_provider ep ON ep.encounter_id = e.encounter_id AND COALESCE(ep.voided, 0) = 0
JOIN provider pr    ON pr.provider_id = ep.provider_id
JOIN person_name pn ON pn.person_id = pr.person_id AND pn.voided = 0
WHERE e.voided = 0
GROUP BY profesional, codigo
ORDER BY encuentros_de_laboratorio DESC;

-- ── 7. Los resultados que llegan días después NO tienen visita ──────────────
-- El paciente no está delante: la muestra se procesa sin él. Es lo esperado, no un fallo.
SELECT
    CASE WHEN e.visit_id IS NULL THEN 'sin visita (llegó después)'
         ELSE 'dentro de la visita (se hizo aquí)' END AS contexto,
    COUNT(*) AS encuentros
FROM encounter e
JOIN encounter_type et ON et.encounter_type_id = e.encounter_type AND et.name = 'Lab Results'
JOIN patient_identifier pi ON pi.patient_id = e.patient_id AND pi.voided = 0
WHERE e.voided = 0 AND pi.identifier LIKE CONCAT(@prefijo, '%')
GROUP BY contexto;

-- ── 8. Muestra de órdenes con su historia completa ──────────────────────────
SELECT
    o.order_number, o.accession_number, cn.name AS examen,
    o.comment_to_fulfiller AS instruccion,
    o.fulfiller_status     AS estado,
    o.fulfiller_comment    AS nota,
    DATE(o.date_activated) AS fecha_orden,
    DATE(ob.obs_datetime)  AS fecha_resultado
FROM orders o
JOIN concept_name cn ON cn.concept_id = o.concept_id AND cn.locale = 'es'
                    AND cn.voided = 0 AND cn.locale_preferred = 1
LEFT JOIN obs ob ON ob.order_id = o.order_id AND ob.voided = 0 AND ob.obs_group_id IS NULL
JOIN patient_identifier pi ON pi.patient_id = o.patient_id AND pi.voided = 0
WHERE o.voided = 0 AND pi.identifier LIKE CONCAT(@prefijo, '%')
ORDER BY o.order_id DESC
LIMIT 25;
