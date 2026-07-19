-- ============================================================================
-- clientes_repetidos.sql — ¿Pacientes repetidos, o solo homónimos?
-- ----------------------------------------------------------------------------
-- En las listas de la UI (agenda, búsqueda de pacientes) O3 muestra solo
-- PRIMER NOMBRE + PRIMER APELLIDO, así que dos pacientes distintos pueden
-- verse "repetidos" aunque difieran en segundo nombre, segundo apellido,
-- identificador y fecha de nacimiento (medido jul-2026: 61 pacientes en 30
-- grupos sobre 2.542 — homónimos plausibles, como en una clínica real).
--
-- Estas consultas separan las dos cosas:
--   §1 lista los grupos de "nombre corto" repetido CON todos los datos que la
--      UI no enseña (para comprobar que son personas distintas),
--   §2 y §3 son INVARIANTES de duplicado real (deben dar 0 filas): nombre
--      completo + misma fecha de nacimiento, y persona con >1 identificador SIM-.
--
-- Uso:
--   docker exec -i <db> mariadb -uroot -p<pass> openmrs < querys/clientes_repetidos.sql
-- ============================================================================

SET @prefijo = 'SIM-';

-- ── 1. Grupos que la UI muestra "repetidos" (nombre corto) — el detalle ──────
-- Cada fila es un paciente; los grupos comparten nombre_corto. Mirar las
-- columnas de la derecha: si difieren (segundo nombre/apellido, nacimiento,
-- identificador), son homónimos y no hay nada que arreglar.
SELECT
    CONCAT(pn.given_name, ' ', pn.family_name)              AS nombre_corto,
    pi.identifier                                           AS identificador,
    CONCAT_WS(' ', pn.given_name, pn.middle_name,
                   pn.family_name, pn.family_name2)          AS nombre_completo,
    p.birthdate                                             AS nacimiento,
    p.gender                                                AS sexo,
    (SELECT COUNT(*) FROM visit v
      WHERE v.patient_id = pn.person_id AND v.voided = 0)   AS visitas
FROM person_name pn
JOIN person p              ON p.person_id = pn.person_id AND p.voided = 0
JOIN patient_identifier pi ON pi.patient_id = pn.person_id AND pi.voided = 0
                          AND pi.identifier LIKE CONCAT(@prefijo, '%')
WHERE pn.voided = 0
  AND CONCAT(pn.given_name, ' ', pn.family_name) IN (
        SELECT nombre_corto FROM (
            SELECT CONCAT(pn2.given_name, ' ', pn2.family_name) AS nombre_corto
            FROM person_name pn2
            JOIN person p2 ON p2.person_id = pn2.person_id AND p2.voided = 0
            JOIN patient_identifier pi2 ON pi2.patient_id = pn2.person_id AND pi2.voided = 0
                                       AND pi2.identifier LIKE CONCAT(@prefijo, '%')
            WHERE pn2.voided = 0
            GROUP BY nombre_corto HAVING COUNT(*) > 1
        ) repetidos)
ORDER BY nombre_corto, pi.identifier;

-- ── 2. INVARIANTE: duplicado REAL de identidad (debe dar 0 filas) ────────────
-- Mismo nombre COMPLETO (4 componentes) + misma fecha de nacimiento = con toda
-- probabilidad el mismo "paciente" sembrado dos veces. Si esto devuelve filas,
-- hay un bug en el alta de pacientes (o se re-sembró una ventana sin limpiar).
SELECT
    CONCAT_WS(' ', pn.given_name, pn.middle_name,
                   pn.family_name, pn.family_name2)          AS nombre_completo,
    p.birthdate                                             AS nacimiento,
    COUNT(*)                                                AS pacientes,
    GROUP_CONCAT(pi.identifier ORDER BY pi.identifier SEPARATOR ' | ') AS identificadores
FROM person_name pn
JOIN person p              ON p.person_id = pn.person_id AND p.voided = 0
JOIN patient_identifier pi ON pi.patient_id = pn.person_id AND pi.voided = 0
                          AND pi.identifier LIKE CONCAT(@prefijo, '%')
WHERE pn.voided = 0
GROUP BY nombre_completo, p.birthdate
HAVING COUNT(*) > 1;

-- ── 3. INVARIANTE: una persona con más de un identificador SIM- (debe dar 0) ─
-- El seeder da exactamente UN "Old Identification Number" SIM- por paciente.
SELECT
    pn.given_name, pn.family_name,
    COUNT(DISTINCT pi.identifier)                           AS identificadores_sim,
    GROUP_CONCAT(DISTINCT pi.identifier SEPARATOR ' | ')    AS cuales
FROM patient_identifier pi
JOIN person p       ON p.person_id = pi.patient_id AND p.voided = 0
JOIN person_name pn ON pn.person_id = pi.patient_id AND pn.voided = 0
WHERE pi.voided = 0 AND pi.identifier LIKE CONCAT(@prefijo, '%')
GROUP BY pi.patient_id, pn.given_name, pn.family_name
HAVING COUNT(DISTINCT pi.identifier) > 1;

-- ── 4. Resumen en un vistazo ─────────────────────────────────────────────────
SELECT
    (SELECT COUNT(*) FROM (
        SELECT 1 FROM person_name pn
        JOIN person p ON p.person_id = pn.person_id AND p.voided = 0
        JOIN patient_identifier pi ON pi.patient_id = pn.person_id AND pi.voided = 0
                                  AND pi.identifier LIKE CONCAT(@prefijo, '%')
        WHERE pn.voided = 0
        GROUP BY CONCAT(pn.given_name, ' ', pn.family_name) HAVING COUNT(*) > 1) t)
                                                            AS grupos_nombre_corto,
    (SELECT COUNT(*) FROM (
        SELECT 1 FROM person_name pn
        JOIN person p ON p.person_id = pn.person_id AND p.voided = 0
        JOIN patient_identifier pi ON pi.patient_id = pn.person_id AND pi.voided = 0
                                  AND pi.identifier LIKE CONCAT(@prefijo, '%')
        WHERE pn.voided = 0
        GROUP BY CONCAT_WS(' ', pn.given_name, pn.middle_name, pn.family_name, pn.family_name2),
                 p.birthdate
        HAVING COUNT(*) > 1) t)                             AS duplicados_reales_debe_ser_0;
