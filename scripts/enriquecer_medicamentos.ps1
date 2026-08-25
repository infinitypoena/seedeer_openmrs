param(
    [string]$MedicamentosPath = "$PSScriptRoot/../openmrs_seeder_v1/openmrs_seeder_v1/catalogs/medicamentos.csv"
)

$grupos = @{
    'Amoxicilina' = 'antibiotico_beta_lactamico'
    'Enalapril' = 'antihipertensivo_ieca'
    'Metformina' = 'antidiabetico_biguanida'
    'Omeprazol' = 'supresor_acido'
    'Ibuprofeno' = 'aines'
    'Trimetoprim+Sulfametoxazol' = 'antibiotico_antifolato'
    'Azitromicina' = 'antibiotico_macrolido'
    'Losartán' = 'antihipertensivo_ara2'
    'Levotiroxina' = 'hormona_tiroidea'
    'Salbutamol' = 'broncodilatador_saba'
    'Paracetamol' = 'analgesico_antipiretico'
    'Metronidazol' = 'antimicrobiano_anaerobio'
    'Albendazol' = 'antiparasitario'
    'Prednisona' = 'corticoide_sistemico'
    'Diclofenaco' = 'aines'
    'Amlodipino' = 'antihipertensivo_calcioantagonista'
    'Hidroclorotiazida' = 'diuretico_tiazida'
    'Ciprofloxacino' = 'antibiotico_fluoroquinolona'
    'Aciclovir' = 'antiviral_antiherpes'
    'Insulina' = 'insulina'
    'Diazepam' = 'benzodiazepina'
    'Acido folico' = 'vitamina_suplemento'
    'Simvastatina' = 'estatina'
    'Aspirina (antiagregante)' = 'antiagregante'
    'Atenolol' = 'betabloqueador'
    'Glipizida' = 'antidiabetico_sulfonilurea'
    'Fluconazol' = 'antifungico_sistemico'
    'Ketoconazol' = 'antifungico'
    'Difenhidramina' = 'antihistaminico'
    'Doxiciclina' = 'antibiotico_tetraciclina'
}

$rows = Import-Csv $MedicamentosPath
$salida = foreach ($row in $rows) {
    $grupo = $grupos[$row.nombre_generico]
    if ([string]::IsNullOrWhiteSpace($grupo)) { throw "Medicamento sin grupo terapéutico: $($row.nombre_generico)" }
    $row | Add-Member -NotePropertyName grupo_terapeutico -NotePropertyValue $grupo -PassThru |
        Add-Member -NotePropertyName requiere_contexto -NotePropertyValue 'true' -PassThru
}
$salida | Export-Csv -Path $MedicamentosPath -NoTypeInformation -Encoding UTF8
Write-Host "Enriquecidos $($salida.Count) medicamentos"
