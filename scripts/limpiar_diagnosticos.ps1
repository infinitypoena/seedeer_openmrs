# ============================================================================
# limpiar_diagnosticos.ps1 — Poda el catálogo para que parezca una CONSULTA EXTERNA
# ----------------------------------------------------------------------------
# ⚠️ OJO: a diferencia de ajustar_diagnosticos.ps1 (que solo AÑADE y nunca pisa),
#    este script BORRA FILAS y SOBRESCRIBE valores. Es idempotente por convergencia
#    (aplicarlo dos veces da el mismo resultado), pero no es reversible: haz commit antes.
#
# El catálogo cosechado de CIEL metía en la historia clínica cosas que ningún médico
# de consulta externa reconocería: "faringitis flegmonosa", "diarrea nerviosa",
# "gastroenteritis por radiación", "cistitis submucosa", "paludismo simiesco". No era
# relleno muerto: en el 5-25 % de las visitas el sorteo se restringe al pool comun=false,
# donde esas filas eran el 78 %, y la amortiguación anti-repetición empujaba HACIA ellas.
#
# Lo que hace, en orden:
#   1. BORRA la cosecha cruda de CIEL. Señal limpia: los nombres cosechados vienen en
#      minúscula (son sinónimos crudos del diccionario); los curados a mano están
#      capitalizados. 466 filas en minúscula vs 409 capitalizadas.
#   2. BORRA lo capitalizado que igual es ruido: no endémico (malaria — El Salvador está
#      certificado LIBRE DE MALARIA por la OMS desde 2021), neonatal de UCI, iatrogenias.
#   3. RESCATA lo que solo existía en minúscula y la clínica ve a diario (deshidratación).
#   4. MARCA ambito=referencia lo quirúrgico y lo agudo de emergencia: el primer nivel no
#      opera un abdomen agudo, lo detecta, lo estabiliza y lo REFIERE al hospital.
#      ⚠️ severidad=grave NO significa referir: VIH, tuberculosis (DOTS), pie diabético o
#      trastorno bipolar son graves y los maneja el primer nivel (tienen sus programas).
# ============================================================================
param(
    [string]$Csv = "$PSScriptRoot/../openmrs_seeder_v1/openmrs_seeder_v1/catalogs/diagnosticos.csv"
)

# ── 2. Ruido capitalizado: se borra por nombre exacto ────────────────────────────────────
$borrar = @(
    # No endémicas: El Salvador es libre de malaria (certificación OMS, 2021)
    'Paludismo (malaria)', 'Paludismo, otras formas', 'Paludismo grave',
    'Malaria por P. falciparum', 'Malaria por P. vivax',
    # Neonatal de UCI: no es consulta externa
    'Meningitis neonatal', 'Sepsis neonatal', 'Neumonía neonatal', 'Bocio neonatal',
    'Hipoglucemia neonatal iatrogénica',
    # Iatrogenias y rarezas de exposición
    'Gota causada por plomo', 'Quemadura con electrocauterio',
    # Artefactos del diccionario (no son diagnósticos activos)
    'Historia del infarto de miocardio', 'Insomnio - hallazgo', 'Hipertiroidismo T>3<',
    'Tuberculosis presunta',
    # Duplicado peligroso: "Toxemia con convulsiones" ES eclampsia (emergencia obstétrica que mata),
    # pero venía como neurologico/LEVE. Ya hay tres filas de Eclampsia bien etiquetadas y referidas.
    'Toxemia con convulsiones'
)

# ── 3. Rescate: filas en minúscula que SÍ se quedan (y se promueven) ─────────────────────
# deshidratación es pan de cada día en una clínica (EDA pediátrica) y estaba con peso 4,
# comun=false y categoría 'endocrino' (!). Solo existe en minúscula: la poda la perdería.
$rescatar = @{
    'deshidratación' = @{
        nombre = 'Deshidratación'; categoria = 'digestivo'; severidad = 'moderado'
        comun = 'true'; peso = 22
    }
}

# ── 4. ambito=referencia: la clínica lo detecta, estabiliza y REFIERE al hospital ────────
# Curado a mano. Quirúrgico + agudo de emergencia. Nunca crónicas de primer nivel.
$referencia = @(
    # Abdomen quirúrgico / digestivo
    'Apendicitis aguda', 'Colecistitis aguda', 'Pancreatitis aguda',
    'Hemorragia digestiva', 'Hemorragia digestiva alta',
    # Cardiovascular
    'Infarto agudo de miocardio', 'Infarto de miocardio subsecuente',
    'Accidente cerebrovascular hemorrágico', 'Crisis hipertensiva',
    'Embolia pulmonar', 'Endocarditis infecciosa',
    # Neurológico
    'Accidente cerebrovascular isquémico', 'Hemorragia intracraneal',
    'Estado epiléptico', 'Encefalitis',
    # Infeccioso
    'Sepsis', 'Sepsis grave', 'Sepsis puerperal',
    'Meningitis', 'Meningitis bacteriana', 'Meningitis viral', 'Meningitis por varicela',
    'Dengue con signos de alarma',          # protocolo ES: signos de alarma → hospitalizar
    # Respiratorio
    'Neumotórax', 'Insuficiencia respiratoria aguda', 'Crisis asmática',
    # Metabólico
    'Cetoacidosis diabética', 'Estado hiperosmolar diabetico',
    'Estado hiperosmolar hiperglucémico',
    # Renal
    'Insuficiencia renal aguda', 'Glomerulonefritis aguda',
    # Obstétrico
    'Eclampsia', 'Eclampsia durante el embarazo', 'Eclampsia en el trabajo de parto',
    'Preeclampsia', 'Preeclampsia (no especificada)', 'Preeclampsia grave estabilizada',
    'Preeclampsia moderada', 'Hemorragia posparto', 'Parto pretérmino',
    'Ruptura prematura de membranas', 'Aborto espontáneo',
    # Trauma
    'Politraumatismo por accidente de tránsito', 'Traumatismo craneoencefálico',
    'Trauma abdominal', 'Trauma de tórax', 'Fractura de cadera',
    'Fractura de miembro inferior', 'Herida por arma blanca',
    'Quemadura grave', 'Quemadura eléctrica', 'Cuerpo extraño en vía aérea',
    'Mordedura de serpiente (ofidismo)', 'Intoxicación por plaguicidas',
    'Luxación atlantoaxial', 'Ahogamiento / casi ahogamiento', 'Caída de altura',
    # Otras urgencias agudas que el primer nivel no resuelve
    'Epiglotitis',                  # urgencia de vía aérea
    'Artritis séptica',             # necesita drenaje articular
    'Derrame pleural',              # necesita toracocentesis
    'Trombosis venosa profunda',    # necesita eco-doppler y anticoagulación
    'Hiperémesis gravídica',        # deshidratación grave: hidratación IV
    # Salud mental: riesgo vital inmediato
    'Intento de suicidio', 'Conducta suicida / autolesión'
)

# Correcciones puntuales de filas mal etiquetadas (nombre exacto → campo → valor).
$corregir = @{
    # Un ictus agudo no es una condición crónica: lo marcó por keyword el script hermano.
    'Accidente cerebrovascular isquémico' = @{ cronica = 'false' }
    # Una luxación atlantoaxial es una urgencia neuroquirúrgica, no un cuadro "leve".
    'Luxación atlantoaxial'               = @{ severidad = 'grave' }
}

$rows = Import-Csv -Path $Csv
$total = $rows.Count
$chg = [ordered]@{ cosecha = 0; ruido = 0; rescatadas = 0; referencia = 0; corregidas = 0 }

$salida = New-Object System.Collections.Generic.List[object]

foreach ($r in $rows) {
    $nombre = $r.nombre_es

    # 1. Cosecha cruda de CIEL: nombre en minúscula. -cmatch = comparación SENSIBLE a mayúsculas.
    if ($nombre -cmatch '^[a-záéíóúñü]') {
        if ($rescatar.ContainsKey($nombre)) {
            $x = $rescatar[$nombre]
            $r.nombre_es = $x.nombre
            $r.categoria = $x.categoria
            $r.severidad = $x.severidad
            $r.comun     = $x.comun
            $r.peso_M    = $x.peso
            $r.peso_F    = $x.peso
            # La clínica la ve en todas las edades (sobre todo en niños con EDA).
            foreach ($c in 'aplica_0_14','aplica_15_29','aplica_30_44','aplica_45_64','aplica_65mas') {
                $r.$c = 'true'
            }
            $chg.rescatadas++
        }
        else {
            $chg.cosecha++
            continue        # se descarta
        }
    }
    # 2. Ruido capitalizado
    elseif ($borrar -contains $nombre) {
        $chg.ruido++
        continue            # se descarta
    }

    # Correcciones puntuales
    if ($corregir.ContainsKey($r.nombre_es)) {
        foreach ($kv in $corregir[$r.nombre_es].GetEnumerator()) {
            $r.($kv.Key) = $kv.Value
        }
        $chg.corregidas++
    }

    # 4. Columna nueva 'ambito' (vacío = 'clinica', la clínica lo trata)
    if ($null -eq $r.PSObject.Properties['ambito']) {
        $r | Add-Member -NotePropertyName ambito -NotePropertyValue ''
    }
    if ($referencia -contains $r.nombre_es) {
        $r.ambito = 'referencia'
        $chg.referencia++
    }
    elseif ([string]::IsNullOrWhiteSpace($r.ambito)) {
        $r.ambito = 'clinica'
    }

    $salida.Add($r)
}

$salida | Export-Csv -Path $Csv -NoTypeInformation -Encoding utf8 -UseQuotes AsNeeded

"Filas: $total -> $($salida.Count)"
"  cosecha CIEL borrada (nombre en minúscula) : $($chg.cosecha)"
"  ruido capitalizado borrado (malaria, neonatal, iatrogenias) : $($chg.ruido)"
"  rescatadas y promovidas : $($chg.rescatadas)"
"  marcadas ambito=referencia : $($chg.referencia)"
"  correcciones puntuales : $($chg.corregidas)"

# Red de seguridad: el validador aborta el arranque si una categoría se queda sin diagnósticos.
"`nDiagnósticos por categoría (ninguna puede quedar en 0):"
$salida | Group-Object categoria | Sort-Object Name | ForEach-Object {
    $com = ($_.Group | Where-Object { $_.comun -eq 'true' }).Count
    "  {0,-18} {1,4}   comunes: {2}" -f $_.Name, $_.Count, $com
}
