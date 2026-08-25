param(
    [string]$DiagnosticosPath = "$PSScriptRoot/../openmrs_seeder_v1/openmrs_seeder_v1/catalogs/diagnosticos.csv",
    [string]$TratamientosPath = "$PSScriptRoot/../openmrs_seeder_v1/openmrs_seeder_v1/catalogs/tratamientos.csv"
)

$diagnosticos = Import-Csv $DiagnosticosPath
$existentes = if (Test-Path $TratamientosPath) { Import-Csv $TratamientosPath } else { @() }
$especificas = @{}
foreach ($regla in $existentes | Where-Object { -not [string]::IsNullOrWhiteSpace($_.diagnostico_uuid) }) {
    $especificas[$regla.diagnostico_uuid.ToLowerInvariant()] = $regla
}

# Reglas revisadas contra guías institucionales. Los UUID de medicamentos provienen de la instancia
# OpenMRS local; no se agregan dosis aquí porque requieren contexto clínico y cálculo de posología.
$investigadas = @{
    '117321AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' = @{ accion = 'tratamiento_condicionado'; medicamento_uuid = '762b4267-6ce6-4de7-8342-7d415fedda6b'; fuente = 'WHO EML 2023: hipotiroidismo confirmado'; version = '2023' }
    '117543AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' = @{ accion = 'tratamiento_especifico'; medicamento_uuid = '123c4342-552a-4db9-bb0b-a7d0bd2e5849'; fuente = 'WHO EML 2023: herpes zoster'; version = '2023' }
    '119895AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' = @{ accion = 'manejo_sintomatico'; medicamento_uuid = '33558e37-627d-475f-b18f-5e5baf0ac0a6'; fuente = 'WHO EML 2023: prurito/alergia condicionada'; version = '2023' }
    '121832AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' = @{ accion = 'tratamiento_condicionado'; medicamento_uuid = 'a8355c83-31db-4089-a962-512e35097ef0'; fuente = 'WHO antimicrobial stewardship: sinusitis bacteriana'; version = '2023' }
    '123384AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' = @{ accion = 'tratamiento_condicionado'; medicamento_uuid = '24de32d2-6d1f-4e53-b047-93e6729228c4'; fuente = 'WHO STI guidelines: tricomoniasis'; version = '2024' }
    '123468AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' = @{ accion = 'tratamiento_condicionado'; medicamento_uuid = '33558e37-627d-475f-b18f-5e5baf0ac0a6'; fuente = 'WHO EML 2023: urticaria sintomatica'; version = '2023' }
    '136614AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' = @{ accion = 'tratamiento_condicionado'; medicamento_uuid = '37302e59-9824-4ec3-a8f7-6b491a5ee183'; fuente = 'WHO EML 2023: parasitosis intestinal'; version = '2023' }
    '138706AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' = @{ accion = 'tratamiento_especifico'; medicamento_uuid = '123c4342-552a-4db9-bb0b-a7d0bd2e5849'; fuente = 'WHO EML 2023: herpes simple'; version = '2023' }
    '141623AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' = @{ accion = 'tratamiento_condicionado'; medicamento_uuid = 'e5e8b45b-83db-4a9d-8cd4-398c4e8081fa'; fuente = 'WHO PEN/EML: dislipidemia evaluada'; version = '2023' }
    '1449AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' = @{ accion = 'tratamiento_condicionado'; medicamento_uuid = 'a80acf74-a85c-4fef-8dc4-d763e364663f'; fuente = 'WHO PEN: diabetes gestacional requiere protocolo obstetrico'; version = '2020' }
    '148002AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' = @{ accion = 'tratamiento_condicionado'; medicamento_uuid = '24de32d2-6d1f-4e53-b047-93e6729228c4'; fuente = 'WHO STI guidelines: vaginosis bacteriana'; version = '2024' }
    '149579AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' = @{ accion = 'tratamiento_condicionado'; medicamento_uuid = 'a8355c83-31db-4089-a962-512e35097ef0'; fuente = 'WHO antimicrobial stewardship: faringitis bacteriana'; version = '2023' }
    '149609AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' = @{ accion = 'tratamiento_condicionado'; medicamento_uuid = 'a8355c83-31db-4089-a962-512e35097ef0'; fuente = 'WHO antimicrobial stewardship: otitis bacteriana'; version = '2023' }
    '166533AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' = @{ accion = 'tratamiento_condicionado'; medicamento_uuid = 'a8355c83-31db-4089-a962-512e35097ef0'; fuente = 'WHO antimicrobial stewardship: faringitis estreptococica'; version = '2023' }
}

$salida = [System.Collections.Generic.List[object]]::new()
foreach ($dx in $diagnosticos) {
    $uuid = $dx.ciel_uuid
    $investigada = $investigadas[$uuid]
    if ($null -ne $investigada) {
        $salida.Add([pscustomobject]@{
            diagnostico_uuid = $uuid
            categoria = $dx.categoria
            accion = $investigada.accion
            medicamento_uuid = $investigada.medicamento_uuid
            max_medicamentos = 1
            fuente = $investigada.fuente
            version_guia = $investigada.version
        })
        continue
    }
    $existente = $especificas[$uuid.ToLowerInvariant()]
    if ($null -ne $existente) {
        $salida.Add([pscustomobject]@{
            diagnostico_uuid = $uuid
            categoria = $dx.categoria
            accion = $existente.accion
            medicamento_uuid = $existente.medicamento_uuid
            max_medicamentos = if ($existente.max_medicamentos) { $existente.max_medicamentos } else { 1 }
            fuente = $existente.fuente
            version_guia = $existente.version_guia
        })
        continue
    }

    if ($dx.ambito -eq 'referencia') {
        $accion = 'referencia'
        $fuente = 'ReferenciaPolicy / catalogo ambito'
    } elseif ($dx.cronica -eq 'true') {
        $accion = 'seguimiento'
        $fuente = 'Clasificacion conservadora: requiere plan clinico'
    } else {
        $accion = 'sin_receta'
        $fuente = 'Clasificacion conservadora: indicacion no investigada'
    }

    $salida.Add([pscustomobject]@{
        diagnostico_uuid = $uuid
        categoria = $dx.categoria
        accion = $accion
        medicamento_uuid = ''
        max_medicamentos = 1
        fuente = $fuente
        version_guia = 'pendiente_revision'
    })
}

# Las filas por categoria documentan el fallback para diagnósticos futuros.
foreach ($regla in $existentes | Where-Object { [string]::IsNullOrWhiteSpace($_.diagnostico_uuid) }) {
    $salida.Add([pscustomobject]@{
        diagnostico_uuid = ''
        categoria = $regla.categoria
        accion = $regla.accion
        medicamento_uuid = $regla.medicamento_uuid
        max_medicamentos = if ($regla.max_medicamentos) { $regla.max_medicamentos } else { 1 }
        fuente = $regla.fuente
        version_guia = $regla.version_guia
    })
}

$salida | Export-Csv -Path $TratamientosPath -NoTypeInformation -Encoding UTF8
Write-Host "Generadas $($salida.Count) reglas en $TratamientosPath"
