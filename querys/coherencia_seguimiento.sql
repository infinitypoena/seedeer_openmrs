-- ============================================================================
-- coherencia_seguimiento.sql — QA de la consulta de seguimiento
-- ----------------------------------------------------------------------------
-- Verifica las dos invariantes de la coherencia del control:
--   A) MISMO MÉDICO: el médico con el que quedó agendada la cita es el que
--      firma el encounter de la visita en la que el paciente acude a ella.
--   B) MISMO DIAGNÓSTICO: el dx primario (rank=1) del control es el mismo que
--      el de la visita que lo agendó (el motivo de la cita).
--
-- El vínculo entre las dos visitas NO se adivina por proximidad: se usa la obs
-- "Return visit date" (5096AAAA…) que la consulta registra con la fecha del
-- control. Esa fecha es la de la cita, y el paciente puede acudir dentro de
-- ±ToleranciaDias (def. 3) → ese margen es el que se usa para emparejar.
--
-- Conexión:
--   docker exec -i openmrs-distro-referenceapplication-360-db-1 \
--     mariadb -uopenmrs -p<OMRS_DB_PASSWORD> -t openmrs < querys/coherencia_seguimiento.sql
--
-- Los datos sembrados ANTES de esta feature divergen por diseño: acota la
-- ventana con @desde/@hasta a las fechas de la corrida que quieres auditar.
--
-- ⚠️ El módulo de citas (Bahmni) inserta patient_appointment_provider.voided
--    como NULL, no 0: filtrar con `voided = 0` deja la tabla fuera y la
--    sección 2 devuelve 0 filas. Hay que usar COALESCE(voided, 0) = 0.
-- ============================================================================

SET NAMES utf8mb4 COLLATE utf8mb4_general_ci;

SET @desde     = '2025-10-01';   -- ← rango de la corrida a auditar
SET @hasta     = '2025-11-30';
SET @tolerancia = 3;             -- Simulation.Appointments.ToleranciaDias


-- ============================================================================
-- 0) PARES (visita que agendó el control → visita de control atendida)
--    Se materializan una vez y se reutilizan en las secciones 1-3.
-- ============================================================================
DROP TEMPORARY TABLE IF EXISTS pares_seguimiento;
CREATE TEMPORARY TABLE pares_seguimiento AS
SELECT
    pi.identifier                          AS paciente,
    DATE(e1.encounter_datetime)            AS fecha_indice,
    DATE(o.value_datetime)                 AS fecha_citada,
    DATE(e2.encounter_datetime)            AS fecha_control,
    prov1.identifier                       AS medico_indice,
    prov2.identifier                       AS medico_control,
    dx1.diagnosis_coded                    AS dx_indice,
    dx2.diagnosis_coded                    AS dx_control,
    cn1.name                               AS dx_indice_nombre,
    cn2.name                               AS dx_control_nombre
FROM encounter e1
-- La consulta índice dejó la fecha de retorno como obs (Return visit date)
JOIN obs o                 ON o.encounter_id = e1.encounter_id AND o.voided = 0
                          AND o.concept_id = (SELECT concept_id FROM concept
                                               WHERE uuid = '5096AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA')
JOIN patient_identifier pi ON pi.patient_id = e1.patient_id AND pi.voided = 0
                          AND pi.identifier LIKE 'SIM-%'
-- La visita de control: consulta del MISMO paciente en la fecha citada (±tolerancia)
JOIN encounter e2          ON e2.patient_id = e1.patient_id AND e2.voided = 0
                          AND e2.encounter_type = e1.encounter_type
                          AND e2.encounter_id <> e1.encounter_id
                          AND ABS(DATEDIFF(e2.encounter_datetime, o.value_datetime)) <= @tolerancia
JOIN encounter_provider ep1 ON ep1.encounter_id = e1.encounter_id AND ep1.voided = 0
JOIN provider prov1         ON prov1.provider_id = ep1.provider_id
JOIN encounter_provider ep2 ON ep2.encounter_id = e2.encounter_id AND ep2.voided = 0
JOIN provider prov2         ON prov2.provider_id = ep2.provider_id
LEFT JOIN encounter_diagnosis dx1 ON dx1.encounter_id = e1.encounter_id AND dx1.voided = 0 AND dx1.dx_rank = 1
LEFT JOIN encounter_diagnosis dx2 ON dx2.encounter_id = e2.encounter_id AND dx2.voided = 0 AND dx2.dx_rank = 1
LEFT JOIN concept_name cn1 ON cn1.concept_id = dx1.diagnosis_coded AND cn1.locale = 'es'
                          AND cn1.locale_preferred = 1 AND cn1.voided = 0
LEFT JOIN concept_name cn2 ON cn2.concept_id = dx2.diagnosis_coded AND cn2.locale = 'es'
                          AND cn2.locale_preferred = 1 AND cn2.voided = 0
WHERE e1.voided = 0
  AND DATE(e1.encounter_datetime) BETWEEN @desde AND @hasta
  AND DATE(e2.encounter_datetime) BETWEEN @desde AND @hasta;


-- ============================================================================
-- 1) INVARIANTE B — MISMO DIAGNÓSTICO EN EL CONTROL
--    Esperado: coinciden = total, distintos = 0.
-- ============================================================================
SELECT 'B) dx del control = dx que motivó la cita' AS invariante,
       COUNT(*)                                             AS controles,
       SUM(dx_indice = dx_control)                          AS coinciden,
       SUM(dx_indice <> dx_control)                         AS distintos,
       CONCAT(ROUND(100 * SUM(dx_indice = dx_control) / NULLIF(COUNT(*), 0), 1), '%') AS pct_coherente
FROM pares_seguimiento;


-- ============================================================================
-- 2) INVARIANTE A — MISMO MÉDICO EN EL CONTROL
--    (a) el médico del control vs. el de la visita índice: pueden diferir si el
--        médico índice no estaba de turno el día de la cita (se reserva otro).
--    (b) lo que NO puede fallar: el médico de la CITA en la agenda = el médico
--        que firma el encounter del control. Esperado: divergencias = 0.
-- ============================================================================
SELECT 'A) médico de la cita = médico que atiende el control' AS invariante,
       COUNT(*)                                                       AS citas_completadas,
       SUM(prov_cita.identifier = ps.medico_control)                  AS coinciden,
       SUM(prov_cita.identifier <> ps.medico_control)                 AS divergencias
FROM pares_seguimiento ps
JOIN patient_identifier pi ON pi.identifier = ps.paciente AND pi.voided = 0
JOIN patient_appointment pa ON pa.patient_id = pi.patient_id AND pa.voided = 0
                           AND DATE(pa.start_date_time) = ps.fecha_citada
JOIN patient_appointment_provider pap ON pap.patient_appointment_id = pa.patient_appointment_id
                                     AND COALESCE(pap.voided, 0) = 0   -- ⚠️ Bahmni lo deja en NULL
JOIN provider prov_cita ON prov_cita.provider_id = pap.provider_id;


-- ============================================================================
-- 3) DETALLE — las primeras 20 filas (para inspección visual)
-- ============================================================================
SELECT paciente, fecha_indice, fecha_citada, fecha_control,
       medico_indice, medico_control,
       IF(medico_indice = medico_control, 'sí', 'no')  AS mismo_medico,
       dx_indice_nombre, dx_control_nombre,
       IF(dx_indice = dx_control, 'sí', 'NO ⚠')        AS mismo_dx
FROM pares_seguimiento
ORDER BY paciente, fecha_control
LIMIT 20;


-- ============================================================================
-- 4) QA — citas Scheduled vencidas al cierre (debe ser 0: SweepMissedAsync)
--    Se acota a las citas creadas por LA CORRIDA auditada (date_created = el día
--    en que se ejecutó el seeder): las de corridas anteriores tienen su propio
--    EndDate y contarlas aquí da falsos positivos.
-- ============================================================================
SET @dia_corrida = CURDATE();   -- ← día real en que se ejecutó el seeder

SELECT COUNT(*) AS citas_scheduled_vencidas
FROM patient_appointment pa
JOIN patient_identifier pi ON pi.patient_id = pa.patient_id AND pi.voided = 0
                          AND pi.identifier LIKE 'SIM-%'
WHERE pa.voided = 0
  AND pa.status = 'Scheduled'
  AND DATE(pa.date_created) = @dia_corrida
  AND DATE(pa.start_date_time) < DATE_SUB(@hasta, INTERVAL @tolerancia DAY);

-- ============================================================================
-- 5) QA — la referencia se resuelve (ley L9): el control post-alta NO re-refiere
--    El bug del bucle (jul 2026): la decisión de referir era sin estado y cada
--    control re-emitía la remisión y re-agendaba el episodio → un paciente llegó
--    a 47 encuentros de "Apendicitis aguda" y 6,8 remisiones por referido.
-- ============================================================================

-- 5a) Remisiones por paciente: la media debe rondar 1,0-1,2 (un episodio, una
--     remisión; algún paciente con dos episodios distintos es normal).
SELECT
    COUNT(*)                                              AS remisiones_totales,
    COUNT(DISTINCT ob.person_id)                          AS pacientes_referidos,
    ROUND(COUNT(*) / COUNT(DISTINCT ob.person_id), 2)     AS remisiones_por_referido
FROM obs ob
JOIN concept c ON c.concept_id = ob.concept_id
              AND c.uuid = '1272AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'   -- Remisiones solicitadas
JOIN patient_identifier pi ON pi.patient_id = ob.person_id AND pi.voided = 0
                          AND pi.identifier LIKE 'SIM-%'
WHERE ob.voided = 0
  AND DATE(ob.obs_datetime) BETWEEN @desde AND @hasta;

-- 5b) Histograma: encuentros con el MISMO dx de referencia por paciente. Con el
--     episodio cerrándose en su control, el máximo esperado es 2 (episodio +
--     control post-alta); 3-4 solo si el mismo cuadro reaparece como episodio
--     nuevo meses después. El 47 de la corrida rota no puede volver.
SELECT veces_mismo_dx, COUNT(*) AS pacientes
FROM (
    SELECT ed.patient_id, ed.diagnosis_coded, COUNT(*) AS veces_mismo_dx
    FROM encounter_diagnosis ed
    JOIN encounter e ON e.encounter_id = ed.encounter_id AND e.voided = 0
    JOIN patient_identifier pi ON pi.patient_id = ed.patient_id AND pi.voided = 0
                              AND pi.identifier LIKE 'SIM-%'
    WHERE ed.voided = 0
      AND DATE(e.encounter_datetime) BETWEEN @desde AND @hasta
      -- Solo los dx de referencia: los que alguna vez motivaron una remisión en
      -- el mismo encuentro (aproximación sin catálogo dentro de la BD).
      AND ed.diagnosis_coded IN (
          SELECT DISTINCT ed2.diagnosis_coded
          FROM encounter_diagnosis ed2
          JOIN obs r ON r.encounter_id = ed2.encounter_id AND r.voided = 0
          JOIN concept c2 ON c2.concept_id = r.concept_id
                         AND c2.uuid = '1272AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'
          WHERE ed2.voided = 0 AND ed2.dx_rank = 1)
    GROUP BY ed.patient_id, ed.diagnosis_coded
) t
GROUP BY veces_mismo_dx
ORDER BY veces_mismo_dx;
