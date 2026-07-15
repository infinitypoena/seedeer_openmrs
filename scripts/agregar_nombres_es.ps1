# ============================================================================
# agregar_nombres_es.ps1 — Nombres en español para los conceptos CIEL del catálogo
# ----------------------------------------------------------------------------
# Muchos conceptos CIEL usados por el simulador NO tienen nombre en español en
# ESTA instancia de OpenMRS, así que la UI en español los muestra en inglés
# (p.ej. "Heart failure", o la creatinina como "Serum creatinine (mg/dL)", que no
# tiene NINGÚN nombre 'es'). Este script empuja el nombre del catálogo como
# nombre en español del concepto, vía REST.
#
# Cubre lo que el usuario ve en la historia y en la cola del laboratorio:
#   • diagnosticos.csv      → el diagnóstico de la consulta
#   • laboratorios.csv      → el nombre del examen en la orden y en la cola
#   • examenes_clinicos.csv → los exámenes de consultorio
#   • paneles.csv           → cada componente del panel (Hb, Hto, LDL…)
#   • alergenos.csv         → el alérgeno en la lista de alergias
#   • respuestas codificadas (Normal/Anormal, Positivo/Negativo…), que no salen
#     de ningún catálogo: van en el mapa $Respuestas de abajo.
#
#   • ADITIVO: solo AGREGA un nombre 'es'; nunca borra ni modifica los existentes.
#   • IDEMPOTENTE: si el concepto ya tiene un nombre 'es', lo omite.
#   • No cambia el UUID ni el código CIEL: solo la etiqueta de visualización.
#
# ⚠️ La UI de la app de laboratorio de O3 trae su propio es.json a medio traducir
#    DE FÁBRICA (~35 de 70 claves). Eso es del frontend y no lo arregla este
#    script: aquí solo se traducen los CONCEPTOS.
#
# Uso:
#   pwsh scripts/agregar_nombres_es.ps1                 # aplica
#   pwsh scripts/agregar_nombres_es.ps1 -DryRun         # solo muestra qué haría
# ============================================================================
param(
    [string]$BaseUrl   = "http://localhost/openmrs/ws/rest/v1",
    [string]$User      = "admin",
    [string]$Pass      = $env:OPENMRS_PASSWORD,
    [string]$Catalogos = "$PSScriptRoot/../openmrs_seeder_v1/openmrs_seeder_v1/catalogs",
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
if (-not $Pass) { throw "Falta la contraseña: usa -Pass o la variable de entorno OPENMRS_PASSWORD." }

$auth    = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${User}:${Pass}"))
$headers = @{ Authorization = $auth }

# Fuentes: cada catálogo con la columna del UUID y la del nombre en español.
$fuentes = @(
    @{ Archivo = 'diagnosticos.csv';      Uuid = 'ciel_uuid';       Nombre = 'nombre_es' },
    @{ Archivo = 'laboratorios.csv';      Uuid = 'ciel_uuid';       Nombre = 'nombre_es' },
    @{ Archivo = 'examenes_clinicos.csv'; Uuid = 'ciel_uuid';       Nombre = 'nombre_es' },
    @{ Archivo = 'paneles.csv';           Uuid = 'componente_uuid'; Nombre = 'nombre'    },
    @{ Archivo = 'alergenos.csv';         Uuid = 'concept_uuid';    Nombre = 'nombre_es' }
)

# Respuestas codificadas de los laboratorios: son las que se leen en el resultado
# ("Positivo", "Sin crecimiento"...) y no viven en ningún catálogo.
$Respuestas = [ordered]@{
    '1115AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'   = 'Normal'
    '1116AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'   = 'Anormal'
    '703AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'   = 'Positivo'
    '664AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'   = 'Negativo'
    '1228AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'   = 'Reactivo'
    '1229AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'   = 'No reactivo'
    '165390AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'   = 'Cultivo positivo'
    '165393AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'   = 'Sin crecimiento'
}

# Un nombre por UUID (primera aparición); se ignoran vacíos y PENDIENTE.
$porUuid = [ordered]@{}

function Agregar([string]$uuid, [string]$nombre) {
    $u = $uuid.Trim()
    if ([string]::IsNullOrWhiteSpace($u) -or $u -eq 'PENDIENTE') { return }
    if ([string]::IsNullOrWhiteSpace($nombre)) { return }
    if (-not $porUuid.Contains($u)) { $porUuid[$u] = $nombre.Trim() }
}

foreach ($f in $fuentes) {
    $ruta = Join-Path $Catalogos $f.Archivo
    if (-not (Test-Path $ruta)) { "  (sin $($f.Archivo))"; continue }
    $filas = @(Import-Csv -Path $ruta)
    foreach ($r in $filas) { Agregar $r.($f.Uuid) $r.($f.Nombre) }
    "  $($f.Archivo): $($filas.Count) filas"
}
foreach ($u in $Respuestas.Keys) { Agregar $u $Respuestas[$u] }

"Conceptos únicos a revisar: $($porUuid.Count)"
""

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
