# ============================================================================
# ampliar_medicamentos.ps1
# ----------------------------------------------------------------------------
# Añade los fármacos NUEVOS verificados en la instancia OpenMRS al catálogo
# medicamentos.csv, respetando el orden de columnas existente. Es idempotente:
# no duplica drug_uuid ya presentes.
#
# Uso:
#   pwsh scripts/ampliar_medicamentos.ps1
# ============================================================================

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$path = Join-Path $root 'openmrs_seeder_v1/openmrs_seeder_v1/catalogs/medicamentos.csv'

$existentes = @(Import-Csv $path | ForEach-Object { $_.drug_uuid })
$nuevos = @(
  # (drug_uuid, concept_uuid, nombre, strength, via, res,card,diab,dig,osteo,uro,infec,endo,neuro,derma,mental,gineco,trauma, grupo, requiere_ctx)
  @('ee3e761a-3d43-4c51-98a3-cc2bd94946b6','a4391c57-549b-4047-8971-c3fa72e140b5','Claritromicina','500mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','true','false','false','false','false','false','true','false','false','false','false','false','false','antibiotico_macrolido','true'),
  @('cbbc7d55-db59-4ea3-9f3b-d3709b1bb82d','bbd3baa3-64bc-443d-b794-d684b413d7dd','Eritromicina','500mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','true','false','false','false','false','false','true','false','false','true','false','false','false','antibiotico_macrolido','true'),
  @('1cd391d8-ed72-4c61-a203-faf40bee6921','a4615855-e041-4352-baae-339940d274f7','Amoxicilina/Clavulánico','625mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','true','false','false','false','false','false','true','false','false','false','false','false','true','antibiotico_beta_lactamico','true'),
  @('1a2354cb-3a85-4af9-8f36-d5146bda46a1','1b535529-68fb-4bb5-bb5a-202ec74efb1d','Cloxacilina','500mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','false','false','false','true','false','false','true','false','false','true','antibiotico_beta_lactamico','true'),
  @('39c32716-b384-4dd8-84c7-324786e015b3','98dbb9b2-9859-4b68-b08a-78f88f629d40','Levofloxacino','500mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','true','false','false','false','false','true','true','false','false','false','false','false','false','antibiotico_fluoroquinolona','true'),
  @('e6b91fc3-8961-493e-9acb-5853162ffec7','76178AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','Famotidina','20mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','true','false','false','false','false','false','false','false','false','false','supresor_acido','true'),
  @('ce6b9fe4-6cbc-495e-8744-20a9cb48f1cf','75875AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','Esomeprazol','20mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','true','false','false','false','false','false','false','false','false','false','supresor_acido','true'),
  @('7a563628-d9c4-4a25-8193-207199051785','fa2a3b41-e500-45cd-8f93-e7d77d5db398','Metoclopramida','10mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','true','false','false','false','false','false','false','false','false','false','antiemetico','true'),
  @('7104f6ed-8793-4b51-8425-5fcb9cf8a59e','72241AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','Bismuto subsalicilato','300mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','true','false','false','false','false','false','false','false','false','false','antidiarreico','false'),
  @('1fc3e5e1-85dd-4a1e-88fa-5f57e07d727d','5a26faba-ca4c-458c-9f41-2a75e242694e','Miralax (polietilenglicol)','17g','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','true','false','false','false','false','false','false','false','false','false','laxante','false'),
  @('e6bd712f-5fb7-4704-8cc3-f26b7f710d20','78945AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','Lisinopril','10mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','true','false','false','false','false','false','false','false','false','false','false','false','antihipertensivo_ieca','true'),
  @('864df3a3-cbda-48d7-86fb-6059b6db713b','f5e348b5-ecd5-47cf-9a37-78e7ef08978d','Captopril','25mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','true','false','false','false','false','false','false','false','false','false','false','false','antihipertensivo_ieca','true'),
  @('eff805bf-db0e-4adf-9a3d-9a5039083016','9f6c3927-2a31-485c-8735-f10b9951aef9','Furosemida','40mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','true','false','false','false','false','false','false','false','false','false','false','false','diuretico_asa','true'),
  @('1ea6b0b2-25d7-4279-b4ee-ccbacd60d17a','7dc18dde-ad3f-405a-be1e-8fc15a01cd91','Metoprolol','50mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','true','false','false','false','false','false','false','false','false','false','false','false','betabloqueador','true'),
  @('057862ef-9fe0-40c8-8955-3149b25c4a70','251a17f9-c229-4aee-9555-9bf7620b2a5a','Nifedipino','20mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','true','false','false','false','false','false','false','false','false','false','false','false','antihipertensivo_calcioantagonista','true'),
  @('f6f88eca-f59c-4859-8a7a-d005a59691ce','4d159957-5f5c-444c-9637-8ab41e564f1f','Ciclobenzaprina','10mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','false','true','false','false','false','false','false','false','false','false','relajante_muscular','true'),
  @('c6308b9c-00f1-476c-aa61-745bb26e0f71','0c0563de-cd06-49de-8b31-af79d668b81b','Gabapentina','300mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','false','true','false','false','false','true','false','false','false','false','anticonvulsivo_gaba','true'),
  @('92e5fe31-7eb4-4bdc-a245-e359715eee0e','77897AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','Ibuprofeno pediátrico','100mg/5ml','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','false','true','false','false','false','true','false','false','false','true','aines','false'),
  @('28dc1ebd-945e-4795-9060-5c60ffb5a577','85275AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','Tramadol','50mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','false','true','false','false','false','false','false','false','false','true','opioide','true'),
  @('c9544877-0a60-462f-9b7c-c8bb57380831','7e31f224-d756-41e6-9fd0-eb5ce3985460','Budesonida inhalada','180mcg','160241AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','true','false','false','false','false','false','false','false','false','false','false','false','false','corticoide_inhalado','true'),
  @('14af1c0c-decd-41c0-ba07-95059f167ed2','8f446491-b746-43ae-bb22-1511aa0c5e29','Fexofenadina','180mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','true','false','false','false','false','false','false','false','false','true','false','false','false','antihistaminico','false'),
  @('52b9f66b-cb47-487a-9452-00e036fe1dd8','59c4799f-151b-4479-a9c8-a05a27893d29','Fluticasona nasal','50mcg','160241AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','true','false','false','false','false','false','false','false','false','false','false','false','false','corticoide_nasal','true'),
  @('d07d5371-355c-4c61-9469-3f354f7edd5e','a2956625-0a07-48b7-9344-3cb707dcd542','Hidrocortisona crema','1%','162780AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','false','false','false','false','false','false','true','false','false','false','corticoide_topico','true'),
  @('a74fe93c-b023-4159-8bd6-5d40a0909636','70245AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','Aciclovir crema','3%','162780AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','false','false','false','true','false','false','true','false','false','false','antiviral_antiherpes','true'),
  @('92247357-ccd3-4f83-9362-f40a103f4cd4','73e9e187-7164-4e0a-bb36-0a7178406288','Ketoconazol crema','2%','162780AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','false','false','false','false','false','false','true','false','false','false','antifungico','true'),
  @('4125aa3e-4b43-41c2-99a3-00bb71978084','984a9a0b-13f6-4bb4-b55d-c51d5ce225c9','Nistatina','100000U/g','162780AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','false','false','false','true','false','false','true','false','true','false','antifungico','true'),
  @('9dcd95b2-afe2-4d51-8b88-17211966fd65','e28a653a-d7fc-4158-989f-9515963ea3db','Neosporina','crema','162780AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','false','false','false','true','false','false','true','false','false','true','antibiotico_topico','true'),
  @('1d153d70-219e-4683-96c2-c1337a2eea85','d08ec8fe-2d91-489f-a416-16af0a043b0b','Calamina','loción','162775AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','false','false','false','false','false','false','true','false','false','true','emoliente_topic','false'),
  @('114d5a9f-f369-4976-9eb7-1f78050c6a9b','d1274833-6c0e-480c-9c0a-b656156e6e7f','Hierro + Ácido fólico','60mg/400µg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','false','false','false','false','true','false','false','false','true','false','hierro_suplemento','false'),
  @('52128175-f648-4a4b-bb2d-4d7a97ea4b6e','836b7f4c-9e3c-4a53-bb08-ab8990b5b9d8','Vitamina B12','1000µg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','false','false','false','false','true','true','false','false','false','false','vitamina_suplemento','false'),
  @('fb601ad7-ffb6-4624-8f64-93522bb8d4ac','564a7a84-148f-4814-a520-d0351dbea6dc','Multivitamínico Prenatal','cap','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','false','false','false','false','true','false','false','false','true','false','vitamina_suplemento','false'),
  @('ab1a18bd-926d-48f3-8006-16314952698b','37466129-caab-4a63-b050-95f6d7024ef6','Gliburida','5mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','true','false','false','false','false','false','false','false','false','false','false','antidiabetico_sulfonilurea','true'),
  @('3543c654-0834-4c90-982c-5e277005c15d','c9c0c627-7ba6-4880-a702-cfc985a296fa','Griseofulvina','500mg','160240AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','false','false','false','false','false','false','false','false','false','true','false','false','false','antifungico','true')
)

# Cabecera del CSV en el orden exacto del archivo
$cols = @('drug_uuid','concept_uuid','nombre_generico','strength','via_uuid','aplica_respiratorio','aplica_cardiovascular','aplica_diabetes','aplica_digestivo','aplica_osteomuscular','aplica_urologico','aplica_infeccioso','aplica_endocrino','aplica_neurologico','aplica_dermatologico','aplica_salud_mental','aplica_ginecoobstetrico','aplica_trauma','dosis','unidad_dosis_uuid','frecuencia_uuid','dias_tratamiento','grupo_terapeutico','requiere_contexto')

$agregados = 0
$todas = @(Import-Csv $path)
foreach ($n in $nuevos) {
    if ($existentes -contains $n[0]) { continue }
    $row = [ordered]@{}
    # Construimos fila vacía y luego llenamos
    foreach ($c in $cols) { $row[$c] = '' }
    $row['drug_uuid'] = $n[0]
    $row['concept_uuid'] = $n[1]
    $row['nombre_generico'] = $n[2]
    $row['strength'] = $n[3]
    $row['via_uuid'] = $n[4]
    $row['aplica_respiratorio'] = $n[5]
    $row['aplica_cardiovascular'] = $n[6]
    $row['aplica_diabetes'] = $n[7]
    $row['aplica_digestivo'] = $n[8]
    $row['aplica_osteomuscular'] = $n[9]
    $row['aplica_urologico'] = $n[10]
    $row['aplica_infeccioso'] = $n[11]
    $row['aplica_endocrino'] = $n[12]
    $row['aplica_neurologico'] = $n[13]
    $row['aplica_dermatologico'] = $n[14]
    $row['aplica_salud_mental'] = $n[15]
    $row['aplica_ginecoobstetrico'] = $n[16]
    $row['aplica_trauma'] = $n[17]
    $row['grupo_terapeutico'] = $n[18]
    $row['requiere_contexto'] = $n[19]
    $todas += [pscustomobject]$row
    $agregados++
}
$todas | Export-Csv $path -NoTypeInformation -Encoding utf8
Write-Host "Fármacos añadidos: $agregados  (total ahora: $($todas.Count))"
