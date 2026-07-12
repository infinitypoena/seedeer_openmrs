# ============================================================================
# agregar_nombres_es.ps1 — Nombres en español para conceptos CIEL del catálogo
# ----------------------------------------------------------------------------
# Algunos conceptos CIEL usados como diagnóstico NO tienen nombre en español en
# ESTA instancia de OpenMRS, por lo que la UI en español los muestra en inglés
# (p.ej. "Heart failure", "Dengue with warning signs"). Este script empuja el
# `nombre_es` del catálogo como nombre en español del concepto, vía REST.
#
#   • ADITIVO: solo AGREGA un nombre 'es'; nunca borra ni modifica los existentes.
#   • IDEMPOTENTE: si el concepto ya tiene un nombre 'es', lo omite.
#   • No cambia el UUID ni el código CIEL: solo la etiqueta de visualización.
#
# Uso:
#   pwsh scripts/agregar_nombres_es.ps1                 # aplica
#   pwsh scripts/agregar_nombres_es.ps1 -DryRun         # solo muestra qué haría
# ============================================================================
param(
    [string]$BaseUrl = "http://localhost/openmrs/ws/rest/v1",
    [string]$User    = "admin",
    [string]$Pass    = 'Prueba01$$xD',
    [string]$Csv     = "$PSScriptRoot/../openmrs_seeder_v1/openmrs_seeder_v1/catalogs/diagnosticos.csv",
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$auth = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${User}:${Pass}"))
$headers = @{ Authorization = $auth }

# Un nombre_es por UUID (primer aparición); se ignoran vacíos y PENDIENTE.
$porUuid = [ordered]@{}
foreach ($r in Import-Csv -Path $Csv) {
    $u = $r.ciel_uuid.Trim()
    if ([string]::IsNullOrWhiteSpace($u) -or $u -eq 'PENDIENTE') { continue }
    if (-not $porUuid.Contains($u)) { $porUuid[$u] = $r.nombre_es.Trim() }
}
"Conceptos únicos en el catálogo: $($porUuid.Count)"

$agregados = 0; $yaTenian = 0; $errores = 0; $i = 0
foreach ($u in $porUuid.Keys) {
    $i++
    $nombre = $porUuid[$u]
    try {
        $c = Invoke-RestMethod -Uri "$BaseUrl/concept/$u`?v=custom:(names:(name,locale))" -Headers $headers -Method Get
        $tieneEs = @($c.names | Where-Object { $_.locale -eq 'es' }).Count -gt 0
        if ($tieneEs) { $yaTenian++; continue }

        if ($DryRun) {
            "[DRY] $u  +es='$nombre'"
            $agregados++
            continue
        }

        # Sinónimo preferido en 'es' (sin conceptNameType): se muestra en la UI española y evita el
        # choque de unicidad de los nombres FULLY_SPECIFIED (p.ej. "Preeclampsia" ya existe en otro concepto).
        $body = @{ name = $nombre; locale = 'es'; localePreferred = $true } | ConvertTo-Json
        Invoke-RestMethod -Uri "$BaseUrl/concept/$u/name" -Headers $headers -Method Post -ContentType 'application/json' -Body $body | Out-Null
        "[OK]  $u  +es='$nombre'"
        $agregados++
    }
    catch {
        "[ERR] $u ('$nombre'): $($_.Exception.Message)"
        $errores++
    }
    if ($i % 100 -eq 0) { "  ...procesados $i/$($porUuid.Count)" }
}

""
"Resumen -> agregados:$agregados  ya_tenian_es:$yaTenian  errores:$errores  (total $($porUuid.Count))"
if ($DryRun) { "(DryRun: no se escribió nada en OpenMRS)" }
