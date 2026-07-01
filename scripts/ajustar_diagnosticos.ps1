# ============================================================================
# ajustar_diagnosticos.ps1 — Normalización por reglas del catálogo de diagnósticos
# ----------------------------------------------------------------------------
# Mejora la PRECISIÓN clínica de la simulación con reglas explícitas y revisables
# (no reescribe categoría/pesos/edad a ciegas). Ajusta, por nombre de enfermedad:
#   • vital_imc   = bajo (caquexia/adelgazamiento) | alto (obesidad/retención)
#   • vital_fiebre= true para entidades febriles agudas (sepsis, abscesos, etc.)
#   • cronica     = true para enfermedades claramente crónicas no marcadas
#   • severidad   = grave para entidades inequívocamente letales mal marcadas
# Solo AÑADE/eleva; nunca quita una cronica ni baja una severidad ya puesta.
# Idempotente: re-ejecutar no cambia nada nuevo.
# ============================================================================
param(
    [string]$Csv = "$PSScriptRoot/../openmrs_seeder_v1/openmrs_seeder_v1/catalogs/diagnosticos.csv"
)

function Fold([string]$s) {
    if ($null -eq $s) { return "" }
    $n = $s.ToLowerInvariant().Normalize([Text.NormalizationForm]::FormD)
    -join ($n.ToCharArray() | Where-Object {
        [Globalization.CharUnicodeInfo]::GetUnicodeCategory($_) -ne 'NonSpacingMark' })
}
function Hit([string]$n, [string[]]$kw) { foreach ($k in $kw) { if ($n.Contains($k)) { return $true } } return $false }

$imcBajo = @('tuberculosis','vih','sida','cancer','carcinoma','neoplasia maligna','tumor maligno',
    'leucemia','linfoma','hipertiroidismo','tirotoxicosis','caquexia','desnutricion','anorexia nerviosa',
    'malabsorcion','celiaca','cirrosis')
$imcAlto = @('obesidad','sobrepeso','sindrome metabolico','hipotiroidismo','cushing','ovario poliquistico')
$fiebre  = @('sepsis','septic','absceso','flemon','celulitis','erisipela','colecistitis aguda','colangitis',
    'pielonefritis','apendicitis','diverticulitis','peritonitis','meningitis','encefalitis','osteomielitis',
    'endocarditis','fiebre reumatica','enfermedad inflamatoria pelvica','salpingitis','mastitis',
    'pancreatitis aguda','corioamnionitis','empiema','prostatitis aguda','fiebre')
$cronList = @('epoc','enfisema','cirrosis','vih','sida','artritis reumatoide','lupus eritematoso',
    'esclerosis multiple','enfermedad de parkinson','epilepsia','hipotiroidismo','hipertiroidismo',
    'osteoporosis','insuficiencia renal cronica','enfermedad renal cronica','nefropatia diabetica',
    'retinopatia diabetica','hiperplasia prostatica','psoriasis','glaucoma','fibromialgia','demencia','alzheimer')
$grave = @('sepsis','septic','shock','choque','infarto agudo','hemorragia','embolia pulmonar',
    'tromboembolia pulmonar','perforacion','peritonitis','meningitis bacteriana','estado epileptico',
    'status epileptic','falciparum','cetoacidosis','insuficiencia respiratoria aguda',
    'edema agudo de pulmon','diseccion','eclampsia','anafilaxia','politraumatismo','paro card')

# Sexo-específicos (acento-insensible, substring). Femeninos: embarazo/parto/ginecológicos.
$sexoF = @('embaraz','gestacion','gestacional','preeclamp','eclampsia','parto','posparto','postparto',
    'puerper','aborto','placenta','corioamnionitis','obstetric','ginecolog','vaginal','vaginitis',
    'vaginosis','vulv','cervicitis','cervicouterin','cuello uterino','endometr','menstrual','menstruacion',
    'dismenorrea','amenorrea','menopaus','ovaric','ovario','ovulacion','salping','anexial','mastitis',
    'utero','uterin','miomatosis','leucorrea','trompa','lactancia','galactorrea','bartholin',
    'cervical intraepitelial','mama')
# Masculinos: próstata/testículos/etc. (se evita 'pene' suelto por falsos positivos como 'penetrante').
$sexoM = @('prostat','testicul','escrot','epididim','orquitis','varicocele','hidrocele','espermat',
    'prepucio','fimosis','balanitis','priapismo','peneana','del pene')

$rows = Import-Csv -Path $Csv
$chg = [ordered]@{ imc = 0; fiebre = 0; cronica = 0; severidad = 0; sexo = 0 }

foreach ($r in $rows) {
    $n = Fold $r.nombre_es

    # sexo (columna nueva): asegurar que exista y solo asignar si está vacía (respeta overrides).
    if ($null -eq $r.PSObject.Properties['sexo']) { $r | Add-Member -NotePropertyName sexo -NotePropertyValue '' }
    if ([string]::IsNullOrWhiteSpace($r.sexo)) {
        if     (Hit $n $sexoF) { $r.sexo = 'F'; $chg.sexo++ }
        elseif (Hit $n $sexoM) { $r.sexo = 'M'; $chg.sexo++ }
    }

    # vital_imc (bajo gana sobre alto); no pisar un valor ya puesto a mano
    if ([string]::IsNullOrWhiteSpace($r.vital_imc)) {
        if     (Hit $n $imcBajo) { $r.vital_imc = 'bajo'; $chg.imc++ }
        elseif (Hit $n $imcAlto) { $r.vital_imc = 'alto'; $chg.imc++ }
    }
    # vital_fiebre
    if ($r.vital_fiebre -ne 'true' -and (Hit $n $fiebre)) { $r.vital_fiebre = 'true'; $chg.fiebre++ }
    # cronica (solo añadir)
    if ($r.cronica -ne 'true' -and ($n.Contains('cronic') -or (Hit $n $cronList))) { $r.cronica = 'true'; $chg.cronica++ }
    # severidad (solo elevar a grave)
    if ($r.severidad -ne 'grave' -and (Hit $n $grave)) { $r.severidad = 'grave'; $chg.severidad++ }
}

$rows | Export-Csv -Path $Csv -NoTypeInformation -Encoding utf8 -UseQuotes AsNeeded
"Filas: $($rows.Count)"
"Cambios -> vital_imc:$($chg.imc)  vital_fiebre:$($chg.fiebre)  cronica:$($chg.cronica)  severidad:$($chg.severidad)  sexo:$($chg.sexo)"
