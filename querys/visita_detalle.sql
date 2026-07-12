-- ============================================================================
-- visita_detalle.sql — Consultas de inspección para el simulador clínico OpenMRS
-- ----------------------------------------------------------------------------
-- Base de datos: openmrs (MariaDB 10.11, contenedor *-db-1).
-- Conexión rápida (desde el host):
--   docker exec -it openmrs-distro-referenceapplication-360-db-1 \
--     mysql -uopenmrs -p<MYSQL_PASSWORD> openmrs
-- Pegar luego la consulta deseada, o ejecutar todo el archivo:
--   docker exec -i openmrs-distro-referenceapplication-360-db-1 \
--     mysql -uopenmrs -p<MYSQL_PASSWORD> -t openmrs < querys/visita_detalle.sql
--
-- Todas las consultas filtran voided=0 (ignoran datos anulados por /clear).
-- Las secciones 2 y 3 usan @visita_uuid, que la Sección 0 fija automáticamente
-- a la visita SIM más reciente (puedes sobreescribirlo con un UUID literal).
-- ============================================================================

-- IMPORTANTE: igualar la collation de la conexión a la de las columnas (utf8mb4_general_ci).
-- Evita el error "Illegal mix of collations" al comparar @visita_uuid con las columnas uuid.
SET NAMES utf8mb4 COLLATE utf8mb4_general_ci;


-- ============================================================================
-- 0) SELECCIÓN AUTOMÁTICA DE VISITA  (para las secciones 2 y 3)
--    Toma por defecto la visita SIM más reciente. Para inspeccionar OTRA,
--    descomenta la segunda línea y pega el UUID que quieras (de la Sección 1).
-- ============================================================================
SET @visita_uuid = (
    SELECT v.uuid FROM visit v
      JOIN patient_identifier pi ON pi.patient_id = v.patient_id
                                AND pi.voided = 0 AND pi.identifier LIKE 'SIM-%'
     WHERE v.voided = 0
     ORDER BY v.date_started DESC LIMIT 1);
-- SET @visita_uuid = 'PEGA-AQUI-EL-UUID-DE-LA-VISITA';
SELECT @visita_uuid AS visita_inspeccionada;


-- ============================================================================
-- 1) RESUMEN POR VISITA  (una fila por visita)
--    Quién fue el paciente, su nombre, edad, dónde, qué médico lo atendió y
--    sus diagnósticos. Lo más útil para una vista general.
-- ============================================================================
SELECT
    v.visit_id,
    v.uuid                                              AS visita_uuid,
    pi_sim.identifier                                   AS sim_id,
    TRIM(CONCAT(pn.given_name, ' ', COALESCE(pn.family_name, ''))) AS paciente,
    p.gender                                            AS sexo,
    TIMESTAMPDIFF(YEAR, p.birthdate, v.date_started)    AS edad,
    vt.name                                             AS tipo_visita,
    l.name                                              AS consultorio,
    DATE(v.date_started)                                AS fecha,
    TIME(v.date_started)                                AS hora_inicio,
    TIME(v.date_stopped)                                AS hora_fin,
    -- Médico(s) que firmaron algún encuentro de la visita
    (SELECT GROUP_CONCAT(DISTINCT TRIM(CONCAT(mpn.given_name, ' ', COALESCE(mpn.family_name, ''))) SEPARATOR ', ')
       FROM encounter e2
       JOIN encounter_provider ep ON ep.encounter_id = e2.encounter_id AND ep.voided = 0
       JOIN provider prov         ON prov.provider_id = ep.provider_id
       LEFT JOIN person_name mpn  ON mpn.person_id = prov.person_id AND mpn.voided = 0 AND mpn.preferred = 1
      WHERE e2.visit_id = v.visit_id AND e2.voided = 0)            AS medico,
    -- Diagnósticos de la visita (con certeza), ordenados por rango (1 = principal)
    (SELECT GROUP_CONCAT(DISTINCT CONCAT(dxr.dx_name, ' (', dxr.certainty, ')')
                         ORDER BY dxr.dx_rank SEPARATOR ' | ')
       FROM encounter e3
       JOIN (
            SELECT ed.encounter_id, ed.dx_rank, ed.certainty,
                   COALESCE(
                       (SELECT cn.name FROM concept_name cn
                         WHERE cn.concept_id = ed.diagnosis_coded AND cn.voided = 0
                         ORDER BY (cn.locale='es' AND cn.locale_preferred=1) DESC,
                                  (cn.locale='es') DESC,
                                  (cn.locale='en' AND cn.locale_preferred=1) DESC,
                                  (cn.locale='en') DESC LIMIT 1),
                       ed.diagnosis_non_coded
                   ) AS dx_name
              FROM encounter_diagnosis ed WHERE ed.voided = 0
       ) dxr ON dxr.encounter_id = e3.encounter_id
      WHERE e3.visit_id = v.visit_id AND e3.voided = 0)            AS diagnosticos,
    -- Conteos rápidos de actividad clínica
    (SELECT COUNT(*) FROM obs o JOIN encounter eo ON eo.encounter_id = o.encounter_id
       WHERE eo.visit_id = v.visit_id AND o.voided = 0)            AS n_obs,
    (SELECT COUNT(*) FROM orders ord JOIN encounter er ON er.encounter_id = ord.encounter_id
       WHERE er.visit_id = v.visit_id AND ord.voided = 0 AND ord.order_type_id = 2) AS n_meds,
    (SELECT COUNT(*) FROM orders ord JOIN encounter er ON er.encounter_id = ord.encounter_id
       WHERE er.visit_id = v.visit_id AND ord.voided = 0 AND ord.order_type_id = 3) AS n_labs,
    (SELECT COUNT(*) FROM conditions c WHERE c.patient_id = v.patient_id AND c.voided = 0) AS n_problemas,
    (SELECT COUNT(*) FROM allergy a WHERE a.patient_id = v.patient_id)                     AS n_alergias
FROM visit v
JOIN person p                   ON p.person_id = v.patient_id
LEFT JOIN person_name pn        ON pn.person_id = v.patient_id AND pn.voided = 0 AND pn.preferred = 1
LEFT JOIN patient_identifier pi_sim ON pi_sim.patient_id = v.patient_id AND pi_sim.voided = 0
                                   AND pi_sim.identifier LIKE 'SIM-%'
LEFT JOIN visit_type vt         ON vt.visit_type_id = v.visit_type_id
LEFT JOIN location l            ON l.location_id = v.location_id
WHERE v.voided = 0
  -- AND pi_sim.identifier LIKE 'SIM-%'      -- <- descomenta para ver SOLO pacientes simulados
ORDER BY v.date_started DESC
LIMIT 50;


-- ============================================================================
-- 2) DETALLE DE LA VISITA EN @visita_uuid (fijada en la Sección 0)
-- ============================================================================

-- 2a) Diagnósticos de la visita
SELECT
    ed.dx_rank                                          AS rango,
    ed.certainty                                        AS certeza,
    COALESCE(
        (SELECT cn.name FROM concept_name cn
          WHERE cn.concept_id = ed.diagnosis_coded AND cn.voided = 0
          ORDER BY (cn.locale='es' AND cn.locale_preferred=1) DESC,
                   (cn.locale='es') DESC,
                   (cn.locale='en' AND cn.locale_preferred=1) DESC,
                   (cn.locale='en') DESC LIMIT 1),
        ed.diagnosis_non_coded
    )                                                   AS diagnostico
FROM visit v
JOIN encounter e            ON e.visit_id = v.visit_id AND e.voided = 0
JOIN encounter_diagnosis ed ON ed.encounter_id = e.encounter_id AND ed.voided = 0
WHERE v.uuid = @visita_uuid
ORDER BY ed.dx_rank;

-- 2b) Signos vitales / observaciones (nombre del concepto priorizando español)
SELECT
    et.name                                             AS encuentro,
    (SELECT cn.name FROM concept_name cn
       WHERE cn.concept_id = o.concept_id AND cn.voided = 0
       ORDER BY (cn.locale='es' AND cn.locale_preferred=1) DESC,
                (cn.locale='es') DESC,
                (cn.locale='en' AND cn.locale_preferred=1) DESC,
                (cn.locale='en') DESC LIMIT 1)          AS observacion,
    COALESCE(
        CAST(o.value_numeric AS CHAR),
        (SELECT cn2.name FROM concept_name cn2
           WHERE cn2.concept_id = o.value_coded AND cn2.voided = 0
           ORDER BY (cn2.locale='es' AND cn2.locale_preferred=1) DESC,
                    (cn2.locale='es') DESC,
                    (cn2.locale='en') DESC LIMIT 1),
        CAST(o.value_datetime AS CHAR),
        o.value_text
    )                                                   AS valor,
    o.obs_datetime                                      AS momento
FROM visit v
JOIN encounter o_e          ON o_e.visit_id = v.visit_id AND o_e.voided = 0
JOIN encounter_type et      ON et.encounter_type_id = o_e.encounter_type
JOIN obs o                  ON o.encounter_id = o_e.encounter_id AND o.voided = 0
WHERE v.uuid = @visita_uuid
ORDER BY et.name, observacion;

-- 2c) Órdenes: medicamentos (con posología legible) y laboratorios
--     Los FK de drug_order (dose_units, route, frequency, *_units) se resuelven a nombre.
SELECT
    ot.name                                             AS tipo_orden,
    (SELECT cn.name FROM concept_name cn
       WHERE cn.concept_id = ord.concept_id AND cn.voided = 0
       ORDER BY (cn.locale='es' AND cn.locale_preferred=1) DESC,
                (cn.locale='es') DESC, (cn.locale='en') DESC LIMIT 1)  AS concepto,
    d.name                                              AS producto,
    -- Posología compuesta y legible (solo para órdenes de medicamento)
    CASE WHEN ord.order_type_id = 2 THEN TRIM(CONCAT_WS(' ',
        CONCAT(do.dose, ' ',
            (SELECT cn.name FROM concept_name cn WHERE cn.concept_id = do.dose_units AND cn.voided = 0
              ORDER BY (cn.locale='es') DESC LIMIT 1)),
        CONCAT('vía ',
            (SELECT cn.name FROM concept_name cn WHERE cn.concept_id = do.route AND cn.voided = 0
              ORDER BY (cn.locale='es') DESC LIMIT 1)),
        (SELECT cn.name FROM concept_name cn JOIN order_frequency ofq ON ofq.concept_id = cn.concept_id
           WHERE ofq.order_frequency_id = do.frequency AND cn.voided = 0
           ORDER BY (cn.locale='es') DESC LIMIT 1),
        CONCAT('x ', do.duration, ' ',
            (SELECT cn.name FROM concept_name cn WHERE cn.concept_id = do.duration_units AND cn.voided = 0
              ORDER BY (cn.locale='es') DESC LIMIT 1)),
        CONCAT('(cant. ', do.quantity, ' ',
            (SELECT cn.name FROM concept_name cn WHERE cn.concept_id = do.quantity_units AND cn.voided = 0
              ORDER BY (cn.locale='es') DESC LIMIT 1), ')')
    )) END                                              AS posologia,
    ord.urgency                                         AS urgencia,
    TRIM(CONCAT(opn.given_name, ' ', COALESCE(opn.family_name, ''))) AS ordenado_por
FROM visit v
JOIN encounter oe           ON oe.visit_id = v.visit_id AND oe.voided = 0
JOIN orders ord             ON ord.encounter_id = oe.encounter_id AND ord.voided = 0
JOIN order_type ot          ON ot.order_type_id = ord.order_type_id
LEFT JOIN drug_order do      ON do.order_id = ord.order_id
LEFT JOIN drug d             ON d.drug_id = do.drug_inventory_id
LEFT JOIN provider oprov     ON oprov.provider_id = ord.orderer
LEFT JOIN person_name opn    ON opn.person_id = oprov.person_id AND opn.voided = 0 AND opn.preferred = 1
WHERE v.uuid = @visita_uuid
ORDER BY ot.name, concepto;


-- ============================================================================
-- 3) LISTA DE PROBLEMAS (condiciones crónicas) del paciente de @visita_uuid
-- ============================================================================
SELECT
    c.clinical_status                                   AS estado,
    COALESCE(
        (SELECT cn.name FROM concept_name cn
          WHERE cn.concept_id = c.condition_coded AND cn.voided = 0
          ORDER BY (cn.locale='es' AND cn.locale_preferred=1) DESC,
                   (cn.locale='es') DESC, (cn.locale='en') DESC LIMIT 1),
        c.condition_non_coded
    )                                                   AS problema,
    c.onset_date                                        AS inicio
FROM visit v
JOIN conditions c ON c.patient_id = v.patient_id AND c.voided = 0
WHERE v.uuid = @visita_uuid
ORDER BY c.clinical_status, problema;


-- ============================================================================
-- 4) CONTEOS GLOBALES (estado de la simulación)
-- ============================================================================
SELECT
    (SELECT COUNT(*) FROM patient_identifier WHERE voided = 0 AND identifier LIKE 'SIM-%') AS pacientes_sim,
    (SELECT COUNT(*) FROM visit WHERE voided = 0)                                          AS visitas,
    (SELECT COUNT(*) FROM encounter WHERE voided = 0)                                      AS encuentros,
    (SELECT COUNT(*) FROM encounter_diagnosis WHERE voided = 0)                            AS diagnosticos,
    (SELECT COUNT(*) FROM orders WHERE voided = 0 AND order_type_id = 2)                   AS medicamentos,
    (SELECT COUNT(*) FROM orders WHERE voided = 0 AND order_type_id = 3)                   AS laboratorios,
    (SELECT COUNT(*) FROM conditions WHERE voided = 0)                                     AS problemas;


-- ============================================================================
-- 5) QA — ANOMALÍAS DE SIGNOS VITALES  (detección de datos imposibles)
--    Caza errores de mapeo de concepto, como mandar SpO2 a "frecuencia
--    respiratoria". Idealmente debe devolver 0 filas sobre datos nuevos.
-- ============================================================================

-- 5a) Obs vitales fuera de rango fisiológico plausible
--     El nombre se resuelve por subconsulta (no por JOIN) para no duplicar filas
--     cuando el concepto tiene varios sinónimos en español.
SELECT
    pi.identifier                                       AS sim_id,
    (SELECT cn.name FROM concept_name cn WHERE cn.concept_id = c.concept_id
       AND cn.voided = 0 ORDER BY (cn.locale='es') DESC, (cn.locale='en') DESC LIMIT 1)  AS vital,
    o.value_numeric                                     AS valor,
    o.obs_datetime                                      AS momento
FROM obs o
JOIN concept c       ON c.concept_id = o.concept_id
JOIN encounter e     ON e.encounter_id = o.encounter_id
JOIN patient_identifier pi ON pi.patient_id = e.patient_id AND pi.voided = 0 AND pi.identifier LIKE 'SIM-%'
WHERE o.voided = 0 AND o.value_numeric IS NOT NULL
  AND (
       (c.uuid LIKE '5242AAAA%' AND (o.value_numeric < 8   OR o.value_numeric > 40))   -- FR respiratoria
    OR (c.uuid LIKE '5092AAAA%' AND (o.value_numeric < 70  OR o.value_numeric > 100))  -- SpO2
    OR (c.uuid LIKE '5087AAAA%' AND (o.value_numeric < 30  OR o.value_numeric > 200))  -- Pulso
    OR (c.uuid LIKE '5088AAAA%' AND (o.value_numeric < 34  OR o.value_numeric > 43))   -- Temperatura
    OR (c.uuid LIKE '5085AAAA%' AND (o.value_numeric < 60  OR o.value_numeric > 260))  -- PA sistólica
    OR (c.uuid LIKE '5086AAAA%' AND (o.value_numeric < 30  OR o.value_numeric > 160))  -- PA diastólica
  )
ORDER BY momento DESC
LIMIT 50;

-- 5b) Cobertura de vitales: cada concepto debe estar presente y con rango sano.
--     Tras el fix deben aparecer TANTO SpO2 (5092) como Frecuencia respiratoria (5242).
SELECT
    (SELECT cn.name FROM concept_name cn WHERE cn.concept_id = c.concept_id
       AND cn.voided = 0 ORDER BY (cn.locale='es') DESC, (cn.locale='en') DESC LIMIT 1)  AS vital,
    c.uuid                                              AS concepto_uuid,
    COUNT(DISTINCT o.obs_id)                            AS n_obs,
    MIN(o.value_numeric)                                AS minimo,
    ROUND(AVG(o.value_numeric), 1)                      AS promedio,
    MAX(o.value_numeric)                                AS maximo
FROM obs o
JOIN concept c       ON c.concept_id = o.concept_id
JOIN encounter e     ON e.encounter_id = o.encounter_id
JOIN patient_identifier pi ON pi.patient_id = e.patient_id AND pi.voided = 0 AND pi.identifier LIKE 'SIM-%'
WHERE o.voided = 0
  AND (c.uuid LIKE '5089AAAA%' OR c.uuid LIKE '5090AAAA%' OR c.uuid LIKE '5085AAAA%'
    OR c.uuid LIKE '5086AAAA%' OR c.uuid LIKE '5088AAAA%' OR c.uuid LIKE '5087AAAA%'
    OR c.uuid LIKE '5242AAAA%' OR c.uuid LIKE '5092AAAA%')
GROUP BY c.concept_id, c.uuid
ORDER BY vital;
