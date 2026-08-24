# ============================================================================
# fusionar_reglas_tratamiento.ps1
# ----------------------------------------------------------------------------
# Fusiona las reglas específicas generadas (/tmp_reglas_tratamiento.salida.csv)
# en tratamientos.csv:
#   - Preserva las políticas de categoría (filas sin diagnostico_uuid).
#   - Para cada regla generada: si el diagnóstico ya tenía regla específica
#     (mismo diagnostico_uuid) → la REEMPLAZA; si no → la AÑADE.
#   - Garantiza una única regla por diagnóstico (el validador aborta con duplicados).
#
# Uso:
#   pwsh scripts/fusionar_reglas_tratamiento.ps1
# ============================================================================

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$path = Join-Path $root 'openmrs_seeder_v1/openmrs_seeder_v1/catalogs/tratamientos.csv'
$generadasPath = Join-Path $root 'tmp_reglas_tratamiento.salida.csv'

$existentes   = @(Import-Csv $path)
$generadas    = @(Import-Csv $generadasPath)

# Políticas de categoría (sin diagnostico_uuid) se conservan intactas
$politicas = @($existentes | Where-Object { [string]::IsNullOrWhiteSpace($_.diagnostico_uuid) })

# Reglas específicas actuales por diagnostico_uuid
$especificas = @($existentes | Where-Object { -not [string]::IsNullOrWhiteSpace($_.diagnostico_uuid) })
$porDx = @{}
foreach ($e in $especificas) { $porDx[$e.diagnostico_uuid] = $e }

# Aplicar las generadas (reemplazan si el dx ya existía)
$reemplazadas = 0
$nuevas = 0
foreach ($g in $generadas) {
    if ($porDx.ContainsKey($g.diagnostico_uuid)) { $reemplazadas++ } else { $nuevas++ }
    $porDx[$g.diagnostico_uuid] = $g
}

$final = @($politicas) + @($porDx.Values | Sort-Object categoria)
$final | Export-Csv $path -NoTypeInformation -Encoding utf8
Write-Host "Reglas específicas: reemplazadas=$reemplazadas, nuevas=$nuevas"
Write-Host "Total filas tratamientos.csv ahora: $($final.Count)"
Write-Host "Filas que recetan (manejo_sintomatico/tratamiento_especifico con medicamento): $(($final | Where-Object { ($_.accion -in @('manejo_sintomatico','tratamiento_especifico')) -and $_.medicamento_uuid }).Count)"
