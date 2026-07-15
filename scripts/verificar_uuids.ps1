<#
.SYNOPSIS
  Verifica que cada UUID de los catálogos apunte al concepto correcto en ESTA instancia de OpenMRS.

.DESCRIPTION
  El CatalogValidator (fail-fast al arrancar) es puro: comprueba la forma de los CSV, pero no puede saber
  si un UUID apunta a lo que dice apuntar. Y ese es justo el fallo que no da ningún error:

    - "Hepatitis A"   apuntaba a *Hepatitis A preparation*  → la VACUNA (clase Drug)
    - "Gota"          apuntaba a *Gota*                     → la UNIDAD DE MEDIDA (¡la gota de líquido!)
    - "Zika"          apuntaba a *Zika virus RT-PCR*        → la PRUEBA DE LABORATORIO
    - "Urocultivo"    apuntaba a un concepto de clase Diagnosis, datatype N/A → nunca podía llevar resultado

  La API acepta cualquier concepto en `diagnoses[].coded`, así que la barbaridad entra en la historia
  clínica en silencio. Este script lo caza: para cada UUID comprueba que EXISTA, que no esté RETIRADO, que
  su CLASE sea la esperada para el uso que le da el catálogo, y que su DATATYPE admita un valor cuando el
  catálogo espera un resultado.

.EJEMPLOS
  pwsh scripts/verificar_uuids.ps1
  pwsh scripts/verificar_uuids.ps1 -BaseUrl http://localhost/openmrs/ws/rest/v1 -Usuario admin

.NOTAS
  La contraseña se pasa por -Password o por la variable de entorno OPENMRS_PASSWORD (nunca se embebe: va a git).
  Exit code 0 = sin hallazgos · 1 = hay UUIDs que corregir.
#>
param(
    [string]$BaseUrl   = 'http://localhost/openmrs/ws/rest/v1',
    [string]$Usuario   = 'admin',
    [string]$Password  = $env:OPENMRS_PASSWORD,
    [string]$Catalogos = (Join-Path $PSScriptRoot '..\openmrs_seeder_v1\openmrs_seeder_v1\catalogs')
)

if (-not $Password) { throw "Falta la contraseña: usa -Password o la variable de entorno OPENMRS_PASSWORD." }

$auth    = @{ Authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${Usuario}:${Password}")) }
$cache   = @{}
$hallazgos = [System.Collections.Generic.List[string]]::new()

# Qué clase de concepto es legítima para cada uso. Un diagnóstico DEBE ser un diagnóstico: si es un Drug,
# lo que se está registrando en la historia del paciente es un medicamento.
$clasesEsperadas = @{
    diagnostico = @('Diagnosis')
    laboratorio = @('Test', 'LabSet', 'Radiology/Imaging Procedure', 'Procedure')
    farmaco     = @('Drug')
    alergeno    = @('Drug', 'Misc', 'Pharmacologic Drug Class', 'Diagnosis')
    examen      = @('Test', 'Finding', 'Question', 'Procedure')
    componente  = @('Test', 'Finding')
    pregunta    = @('Question')   # concepto sobre el que se registra una obs (la referencia al hospital)
    respuesta   = @()             # una respuesta codificada puede ser de cualquier clase
}

# ⚠️ Conceptos que NO viven en ningún CSV: son constantes del código (Services/ReferenciaPolicy.cs).
# Sin esto quedarían fuera del arnés y un UUID equivocado fallaría en silencio, que es exactamente el
# fallo que este script existe para cazar.
$constantesDelCodigo = @(
    @{ uuid = '1272AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'; nombre = 'Remisiones solicitadas';   uso = 'pregunta';  valor = $true  }
    @{ uuid = '1589AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'; nombre = '→ Hospital';               uso = 'respuesta'; valor = $false }
    @{ uuid = '1788AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'; nombre = '¿Referido a hospital?';    uso = 'pregunta';  valor = $true  }
    @{ uuid = '1065AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'; nombre = '→ Sí';                     uso = 'respuesta'; valor = $false }
    @{ uuid = '1885AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'; nombre = 'Prioridad de referencia';  uso = 'pregunta';  valor = $true  }
    @{ uuid = '1882AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'; nombre = '→ Emergencia';             uso = 'respuesta'; valor = $false }
    @{ uuid = '1883AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'; nombre = '→ Urgente';                uso = 'respuesta'; valor = $false }
    @{ uuid = '164359AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'; nombre = 'Motivo de la referencia';  uso = 'pregunta';  valor = $true  }
)

function Get-Concepto([string]$uuid) {
    if ($cache.ContainsKey($uuid)) { return $cache[$uuid] }
    $url = "$BaseUrl/concept/$uuid" + '?v=custom:(uuid,display,retired,datatype:(name),conceptClass:(name))'
    try   { $c = Invoke-RestMethod -Uri $url -Headers $auth -Method Get -ErrorAction Stop }
    catch { $c = $null }
    $cache[$uuid] = $c
    return $c
}

# $esperaValor: el catálogo va a registrar una obs con valor sobre este concepto → datatype N/A lo rompería
function Revisar([string]$uuid, [string]$nombre, [string]$archivo, [string]$uso, [bool]$esperaValor = $false) {
    if ([string]::IsNullOrWhiteSpace($uuid)) { return }
    $c = Get-Concepto $uuid
    $donde = "$archivo ($nombre)"

    if ($null -eq $c)  { $hallazgos.Add("$donde : el UUID $uuid NO EXISTE en la instancia"); return }
    if ($c.retired)    { $hallazgos.Add("$donde : el concepto está RETIRADO — '$($c.display)'") }

    $clase = $c.conceptClass.name
    $ok    = $clasesEsperadas[$uso]
    if ($ok.Count -gt 0 -and $clase -notin $ok) {
        $hallazgos.Add("$donde : es de clase '$clase' (se esperaba $($ok -join '/')) — el UUID apunta a '$($c.display)'")
    }
    if ($esperaValor -and $c.datatype.name -eq 'N/A') {
        $hallazgos.Add("$donde : datatype N/A — no puede llevar un valor (la obs de resultado fallaría con 'ZZ')")
    }
}

function Leer($archivo) {
    $ruta = Join-Path $Catalogos $archivo
    if (-not (Test-Path $ruta)) { return @() }
    return @(Import-Csv $ruta)
}

Write-Host "Verificando los UUID de los catálogos contra $BaseUrl ..." -ForegroundColor Cyan

foreach ($d in Leer 'diagnosticos.csv')  { Revisar $d.ciel_uuid    $d.nombre_es       'diagnosticos.csv'     'diagnostico' }
foreach ($m in Leer 'medicamentos.csv')  { Revisar $m.concept_uuid $m.nombre_generico 'medicamentos.csv'     'farmaco' }
foreach ($a in Leer 'alergenos.csv')     { Revisar $a.concept_uuid $a.nombre_es       'alergenos.csv'        'alergeno' }
foreach ($e in Leer 'examenes_clinicos.csv') {
    Revisar $e.ciel_uuid $e.nombre_es 'examenes_clinicos.csv' 'examen' -esperaValor $true
}
foreach ($l in Leer 'laboratorios.csv') {
    # 'panel' e 'imagen' no registran un valor sobre el concepto padre; numeric/coded sí
    $conValor = $l.datatype -in @('numeric', 'coded')
    Revisar $l.ciel_uuid        $l.nombre_es 'laboratorios.csv' 'laboratorio' -esperaValor $conValor
    Revisar $l.res_normal_uuid  "$($l.nombre_es) → resultado normal"  'laboratorios.csv' 'respuesta'
    Revisar $l.res_anormal_uuid "$($l.nombre_es) → resultado anormal" 'laboratorios.csv' 'respuesta'
}
foreach ($p in Leer 'paneles.csv') {
    Revisar $p.componente_uuid $p.nombre 'paneles.csv' 'componente' -esperaValor $true
}

foreach ($k in $constantesDelCodigo) {
    Revisar $k.uuid $k.nombre 'ReferenciaPolicy.cs' $k.uso -esperaValor $k.valor
}

# Los fármacos se recetan por su producto del formulario (tabla drug), no solo por su concepto
foreach ($m in Leer 'medicamentos.csv') {
    if (-not $m.drug_uuid) { continue }
    try { $null = Invoke-RestMethod -Uri "$BaseUrl/drug/$($m.drug_uuid)" -Headers $auth -ErrorAction Stop }
    catch { $hallazgos.Add("medicamentos.csv ($($m.nombre_generico)) : el drug_uuid $($m.drug_uuid) no existe en el formulario") }
}

Write-Host ""
if ($hallazgos.Count -eq 0) {
    Write-Host "OK: $($cache.Count) conceptos verificados, 0 hallazgos." -ForegroundColor Green
    exit 0
}
Write-Host "$($hallazgos.Count) hallazgo(s) sobre $($cache.Count) conceptos verificados:" -ForegroundColor Yellow
$hallazgos | ForEach-Object { Write-Host "  - $_" }
exit 1
