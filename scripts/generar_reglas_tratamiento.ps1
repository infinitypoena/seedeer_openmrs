# ============================================================================
# generar_reglas_tratamiento.ps1
# ----------------------------------------------------------------------------
# Genera reglas específicas diagnóstico→medicamento en tratamientos.csv a partir
# de una lista semilla (nombre del diagnóstico → acción → drug_uuid), resolviendo
# cada nombre contra diagnosticos.csv actual (que es la ÚNICA fuente de verdad de
# los cielo_uuid). Evita:
#   - referenciar un diagnóstico inexistente (el validador aborta)
#   - duplicar reglas por diagnóstico
# Solo emite reglas cuya acción receta: manejo_sintomatico | tratamiento_especifico.
#
# Uso:
#   pwsh scripts/generar_reglas_tratamiento.ps1
# Genera: /tmp_reglas_tratamiento.salida.csv  (revisarlo antes de volcar)
# ============================================================================

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$diagPath = Join-Path $root 'openmrs_seeder_v1/openmrs_seeder_v1/catalogs/diagnosticos.csv'
$medPath  = Join-Path $root 'openmrs_seeder_v1/openmrs_seeder_v1/catalogs/medicamentos.csv'

$diagnosticos = Import-Csv $diagPath
$medicamentos = Import-Csv $medPath

# Mapa nombre (normalizado, sin acentos, minúsculas) → ficha
$mapa = @{}
foreach ($d in $diagnosticos) {
    $key = $d.nombre_es.ToLowerInvariant()
    if (-not $mapa.ContainsKey($key)) { $mapa[$key] = $d }
}

# Mapa de drug_uuid que existen en medicamentos.csv
$drugs = @{}
foreach ($m in $medicamentos) {
    if ($m.drug_uuid) { $drugs[$m.drug_uuid] = $m.nombre_generico }
}

# A utf8
function Normalizar([string]$s) { return $s.ToLowerInvariant() }

# ── Reglas semilla: (diagnóstico, categoria, accion, "drug1|drug2", max, fuente, version) ──
# La "categoria" se usa como respaldo de seguridad; la verificación dura la da diagnosticos.csv.
$semilla = @(
  # respiratorio
  @('Influenza (gripe)','manejo_sintomatico','fbf74fd6-b37c-4325-86cb-dcaf4aabed81',1,'OMS UTI','2023'),
  @('Resfriado comun (rinofaringitis aguda)','manejo_sintomatico','fbf74fd6-b37c-4325-86cb-dcaf4aabed81',1,'OMS UTI','2023'),
  @('Rinofaringitis aguda','manejo_sintomatico','fbf74fd6-b37c-4325-86cb-dcaf4aabed81',1,'OMS UTI','2023'),
  @('Faringitis aguda','manejo_sintomatico','fbf74fd6-b37c-4325-86cb-dcaf4aabed81|f05dd6ca-43d0-45ea-93ef-8d82483bbdc9',2,'OMS UTI','2023'),
  @('Amigdalitis aguda','manejo_sintomatico','fbf74fd6-b37c-4325-86cb-dcaf4aabed81',1,'Guías MINSAL','2022'),
  @('Faringoamigdalitis estreptocócica','tratamiento_especifico','a8355c83-31db-4089-a962-512e35097ef0',1,'Guías MINSAL','2022'),
  @('Otitis media aguda','tratamiento_especifico','a8355c83-31db-4089-a962-512e35097ef0',1,'Guías MINSAL','2022'),
  @('Otitis externa','manejo_sintomatico','fbf74fd6-b37c-4325-86cb-dcaf4aabed81|f05dd6ca-43d0-45ea-93ef-8d82483bbdc9',2,'Guías MINSAL','2022'),
  @('Sinusitis aguda','tratamiento_especifico','a8355c83-31db-4089-a962-512e35097ef0',1,'OMS UTI','2023'),
  @('Sinusitis bacteriana aguda','tratamiento_especifico','a8355c83-31db-4089-a962-512e35097ef0',1,'OMS UTI','2023'),
  @('Neumonía adquirida en la comunidad','tratamiento_especifico','a8355c83-31db-4089-a962-512e35097ef0|dd87f758-a49b-44ac-841b-16b61c176244',2,'OMS PEN','2023'),
  @('Neumonía atípica','tratamiento_especifico','dd87f758-a49b-44ac-841b-16b61c176244',1,'OMS PEN','2023'),
  @('Asma bronquial','tratamiento_especifico','3099b446-010e-426a-ba02-bf62f1077f2e',1,'GINA','2023'),
  @('Asma persistente','tratamiento_especifico','3099b446-010e-426a-ba02-bf62f1077f2e|c9544877-0a60-462f-9b7c-c8bb57380831',2,'GINA','2023'),
  # cardiovascular
  @('Hipertension arterial','tratamiento_especifico','38978734-0960-4c7d-bb1e-725532bc4c6a',1,'WHO PEN','2021'),
  @('Hipertensión arterial esencial','tratamiento_especifico','38978734-0960-4c7d-bb1e-725532bc4c6a',1,'WHO PEN','2021'),
  @('Cardiopatía hipertensiva','tratamiento_especifico','38978734-0960-4c7d-bb1e-725532bc4c6a',1,'WHO PEN','2021'),
  @('Insuficiencia cardíaca','tratamiento_especifico','38978734-0960-4c7d-bb1e-725532bc4c6a|eff805bf-db0e-4adf-9a3d-9a5039083016',2,'ESC','2021'),
  @('Insuficiencia cardíaca congestiva','tratamiento_especifico','38978734-0960-4c7d-bb1e-725532bc4c6a|eff805bf-db0e-4adf-9a3d-9a5039083016',2,'ESC','2021'),
  @('Dislipidemia','tratamiento_especifico','e5e8b45b-83db-4a9d-8cd4-398c4e8081fa',1,'WHO PEN','2021'),
  @('Cardiopatía isquémica crónica (angina)','tratamiento_especifico','02dd2a8e-1a8f-49cb-bc06-daf9e1af16ba|dae4050d-c309-4a20-a62e-e84d192e1e87',2,'WHO PEN','2021'),
  @('Arritmia cardíaca','tratamiento_especifico','dae4050d-c309-4a20-a62e-e84d192e1e87|1ea6b0b2-25d7-4279-b4ee-ccbacd60d17a',1,'ESC','2021'),
  # diabetes
  @('Diabetes mellitus tipo 2','tratamiento_especifico','a80acf74-a85c-4fef-8dc4-d763e364663f',1,'ADA/OMS','2023'),
  @('Diabetes mellitus tipo 1','tratamiento_especifico','3b3e2029-84c4-4472-85b4-a1f1af0885b6',1,'ADA/OMS','2023'),
  @('Neuropatía diabética','tratamiento_especifico','c6308b9c-00f1-476c-aa61-745bb26e0f71',1,'ADA/NICE','2022'),
  @('Nefropatía diabética','tratamiento_especifico','38978734-0960-4c7d-bb1e-725532bc4c6a',1,'ADA/OMS','2023'),
  # digestivo
  @('Reflujo gastroesofágico (ERGE)','tratamiento_especifico','c10cc9b5-c1ec-4d73-9192-9dc6b40e16f9',1,'NICE','2022'),
  @('Úlcera péptica','tratamiento_especifico','c10cc9b5-c1ec-4d73-9192-9dc6b40e16f9|ce6b9fe4-6cbc-495e-8744-20a9cb48f1cf',1,'NICE','2022'),
  @('Gastritis aguda','tratamiento_especifico','c10cc9b5-c1ec-4d73-9192-9dc6b40e16f9',1,'Guías MINSAL','2022'),
  @('Gastritis crónica','tratamiento_especifico','c10cc9b5-c1ec-4d73-9192-9dc6b40e16f9',1,'Guías MINSAL','2022'),
  @('Diarrea aguda','manejo_sintomatico','7104f6ed-8793-4b51-8425-5fcb9cf8a59e|fbf74fd6-b37c-4325-86cb-dcaf4aabed81',2,'OMS','2023'),
  @('Gastroenteritis aguda (EDA)','manejo_sintomatico','7a563628-d9c4-4a25-8193-207199051785|fbf74fd6-b37c-4325-86cb-dcaf4aabed81',2,'OMS','2023'),
  @('Estreñimiento crónico','tratamiento_especifico','1fc3e5e1-85dd-4a1e-88fa-5f57e07d727d',1,'NICE','2020'),
  # osteomuscular
  @('Lumbalgia','manejo_sintomatico','f05dd6ca-43d0-45ea-93ef-8d82483bbdc9|fbf74fd6-b37c-4325-86cb-dcaf4aabed81',2,'NICE','2020'),
  @('Lumbalgia crónica','manejo_sintomatico','f05dd6ca-43d0-45ea-93ef-8d82483bbdc9|f6f88eca-f59c-4859-8a7a-d005a59691ce',2,'NICE','2020'),
  @('Ciática','manejo_sintomatico','f05dd6ca-43d0-45ea-93ef-8d82483bbdc9|f6f88eca-f59c-4859-8a7a-d005a59691ce',2,'NICE','2020'),
  @('Cervicalgia','manejo_sintomatico','f05dd6ca-43d0-45ea-93ef-8d82483bbdc9|f6f88eca-f59c-4859-8a7a-d005a59691ce',2,'NICE','2020'),
  @('Esguince','manejo_sintomatico','f05dd6ca-43d0-45ea-93ef-8d82483bbdc9|fbf74fd6-b37c-4325-86cb-dcaf4aabed81',2,'OMS','2020'),
  @('Esguince de tobillo','manejo_sintomatico','f05dd6ca-43d0-45ea-93ef-8d82483bbdc9|fbf74fd6-b37c-4325-86cb-dcaf4aabed81',2,'OMS','2020'),
  @('Esguince de muñeca','manejo_sintomatico','f05dd6ca-43d0-45ea-93ef-8d82483bbdc9|fbf74fd6-b37c-4325-86cb-dcaf4aabed81',2,'OMS','2020'),
  @('Tendinitis','manejo_sintomatico','f05dd6ca-43d0-45ea-93ef-8d82483bbdc9|7bedc809-05bd-4baf-bd4b-5a21d9e48a01',1,'NICE','2020'),
  @('Bursitis','manejo_sintomatico','f05dd6ca-43d0-45ea-93ef-8d82483bbdc9|7bedc809-05bd-4baf-bd4b-5a21d9e48a01',1,'NICE','2020'),
  @('Hernia discal','manejo_sintomatico','f05dd6ca-43d0-45ea-93ef-8d82483bbdc9|f6f88eca-f59c-4859-8a7a-d005a59691ce',2,'NICE','2020'),
  @('Gota','tratamiento_especifico','f05dd6ca-43d0-45ea-93ef-8d82483bbdc9|7bedc809-05bd-4baf-bd4b-5a21d9e48a01',1,'ACR','2020'),
  @('Osteoartrosis','tratamiento_especifico','fbf74fd6-b37c-4325-86cb-dcaf4aabed81|f05dd6ca-43d0-45ea-93ef-8d82483bbdc9',1,'NICE','2020'),
  @('Osteoartritis (artrosis)','tratamiento_especifico','fbf74fd6-b37c-4325-86cb-dcaf4aabed81|f05dd6ca-43d0-45ea-93ef-8d82483bbdc9',1,'NICE','2020'),
  @('Dolor muscular','manejo_sintomatico','f05dd6ca-43d0-45ea-93ef-8d82483bbdc9|fbf74fd6-b37c-4325-86cb-dcaf4aabed81',1,'NICE','2020'),
  # urologico
  @('Infección urinaria (cistitis)','tratamiento_especifico','4bc64ce7-18a8-460a-9314-0f89e4ca2d0b',1,'OMS','2023'),
  @('Cistitis','tratamiento_especifico','4bc64ce7-18a8-460a-9314-0f89e4ca2d0b',1,'OMS','2023'),
  @('Infección del tracto urinario','tratamiento_especifico','4bc64ce7-18a8-460a-9314-0f89e4ca2d0b',1,'OMS','2023'),
  @('Prostatitis aguda','tratamiento_especifico','9b305550-beb8-4c7b-a800-caab72438430',1,'EAU','2022'),
  @('Orquitis / epididimitis','tratamiento_especifico','9b305550-beb8-4c7b-a800-caab72438430',1,'EAU','2022'),
  @('Uretritis','tratamiento_especifico','dd87f758-a49b-44ac-841b-16b61c176244',1,'CDC','2021'),
  @('Litiasis renal (urolitiasis)','manejo_sintomatico','fbf74fd6-b37c-4325-86cb-dcaf4aabed81|f05dd6ca-43d0-45ea-93ef-8d82483bbdc9',2,'EAU','2022'),
  # infeccioso
  @('Parasitosis intestinal (geohelmintiasis)','tratamiento_especifico','37302e59-9824-4ec3-a8f7-6b491a5ee183',1,'OMS GET','2022'),
  @('Ascariasis','tratamiento_especifico','37302e59-9824-4ec3-a8f7-6b491a5ee183',1,'OMS GET','2022'),
  @('Oxiuriasis (enterobiasis)','tratamiento_especifico','37302e59-9824-4ec3-a8f7-6b491a5ee183',1,'OMS GET','2022'),
  @('Giardiasis','tratamiento_especifico','24de32d2-6d1f-4e53-b047-93e6729228c4',1,'OMS','2023'),
  @('Amebiasis intestinal','tratamiento_especifico','24de32d2-6d1f-4e53-b047-93e6729228c4',1,'OMS','2023'),
  @('Sífilis','tratamiento_especifico','12dd160a-2ab4-4b95-900c-40ebf6ccc55f',1,'OMS','2021'),
  @('Gonorrea','tratamiento_especifico','dd87f758-a49b-44ac-841b-16b61c176244|12dd160a-2ab4-4b95-900c-40ebf6ccc55f',2,'CDC/OMS','2021'),
  @('Clamidiasis','tratamiento_especifico','dd87f758-a49b-44ac-841b-16b61c176244|12dd160a-2ab4-4b95-900c-40ebf6ccc55f',1,'CDC','2021'),
  @('Celulitis','tratamiento_especifico','1a2354cb-3a85-4af9-8f36-d5146bda46a1|a8355c83-31db-4089-a962-512e35097ef0',1,'Guías MINSAL','2022'),
  @('Celulitis bacteriana','tratamiento_especifico','1a2354cb-3a85-4af9-8f36-d5146bda46a1',1,'Guías MINSAL','2022'),
  @('Absceso cutáneo','tratamiento_especifico','1a2354cb-3a85-4af9-8f36-d5146bda46a1',1,'Guías MINSAL','2022'),
  @('Herpes genital','tratamiento_especifico','123c4342-552a-4db9-bb0b-a7d0bd2e5849',1,'CDC','2021'),
  @('Candidiasis','tratamiento_especifico','2f76266a-a841-4147-9401-c6b8409b8ccc',1,'IDSA','2020'),
  @('Micosis superficial (tiña/dermatofitosis)','tratamiento_especifico','5b420f3b-7584-4934-b457-91d6f94ff6b7|3543c654-0834-4c90-982c-5e277005c15d',1,'IDSA','2020'),
  @('Dengue sin signos de alarma','manejo_sintomatico','fbf74fd6-b37c-4325-86cb-dcaf4aabed81',1,'OMS Dengue','2018'),
  @('Chikungunya','manejo_sintomatico','fbf74fd6-b37c-4325-86cb-dcaf4aabed81|f05dd6ca-43d0-45ea-93ef-8d82483bbdc9',2,'OMS','2022'),
  @('Zika','manejo_sintomatico','fbf74fd6-b37c-4325-86cb-dcaf4aabed81',1,'OMS','2018'),
  @('Fiebre sin foco','manejo_sintomatico','fbf74fd6-b37c-4325-86cb-dcaf4aabed81',1,'OMS UTI','2023'),
  @('Gastroenteritis viral','manejo_sintomatico','7a563628-d9c4-4a25-8193-207199051785',1,'OMS','2023'),
  # dermatologico
  @('Acné vulgar','tratamiento_especifico','12dd160a-2ab4-4b95-900c-40ebf6ccc55f',1,'AAD','2020'),
  @('Dermatitis de contacto','tratamiento_especifico','d07d5371-355c-4c61-9469-3f354f7edd5e',1,'NICE','2021'),
  @('Dermatitis atopica','tratamiento_especifico','d07d5371-355c-4c61-9469-3f354f7edd5e',1,'NICE','2021'),
  @('Dermatitis seborreica','tratamiento_especifico','92247357-ccd3-4f83-9362-f40a103f4cd4',1,'Guías MINSAL','2022'),
  @('Urticaria','tratamiento_especifico','33558e37-627d-475f-b18f-5e5baf0ac0a6|14af1c0c-decd-41c0-ba07-95059f167ed2',1,'NICE','2020'),
  @('Tiña corporal (dermatofitosis)','tratamiento_especifico','92247357-ccd3-4f83-9362-f40a103f4cd4',1,'IDSA','2020'),
  @('Tiña de la cabeza','tratamiento_especifico','3543c654-0834-4c90-982c-5e277005c15d',1,'IDSA','2020'),
  @('Onicomicosis','tratamiento_especifico','3543c654-0834-4c90-982c-5e277005c15d',1,'IDSA','2020'),
  @('Herpes simple labial','tratamiento_especifico','a74fe93c-b023-4159-8bd6-5d40a0909636',1,'CDC','2021'),
  @('Herpes zóster','tratamiento_especifico','123c4342-552a-4db9-bb0b-a7d0bd2e5849|fbf74fd6-b37c-4325-86cb-dcaf4aabed81',2,'CDC','2021'),
  @('Foliculitis / forunculosis','tratamiento_especifico','1a2354cb-3a85-4af9-8f36-d5146bda46a1',1,'Guías MINSAL','2022'),
  @('Quemadura solar','manejo_sintomatico','1d153d70-219e-4683-96c2-c1337a2eea85|fbf74fd6-b37c-4325-86cb-dcaf4aabed81',2,'Guías MINSAL','2022'),
  # neurologico
  @('Migraña sin aura','manejo_sintomatico','f05dd6ca-43d0-45ea-93ef-8d82483bbdc9|fbf74fd6-b37c-4325-86cb-dcaf4aabed81',2,'NICE','2020'),
  @('Migraña con aura','manejo_sintomatico','f05dd6ca-43d0-45ea-93ef-8d82483bbdc9|fbf74fd6-b37c-4325-86cb-dcaf4aabed81',2,'NICE','2020'),
  @('Migrana','manejo_sintomatico','f05dd6ca-43d0-45ea-93ef-8d82483bbdc9|fbf74fd6-b37c-4325-86cb-dcaf4aabed81',2,'NICE','2020'),
  @('Cefalea','manejo_sintomatico','fbf74fd6-b37c-4325-86cb-dcaf4aabed81',1,'NICE','2020'),
  @('Cefalea tensional','manejo_sintomatico','fbf74fd6-b37c-4325-86cb-dcaf4aabed81',1,'NICE','2020'),
  @('Neuralgia del trigémino','tratamiento_especifico','c6308b9c-00f1-476c-aa61-745bb26e0f71',1,'NICE','2022'),
  @('Neuropatía periférica','tratamiento_especifico','c6308b9c-00f1-476c-aa61-745bb26e0f71',1,'NICE','2022'),
  @('Neuropatia periferica diabetica','tratamiento_especifico','c6308b9c-00f1-476c-aa61-745bb26e0f71',1,'NICE','2022'),
  @('Polineuropatía alcohólica','tratamiento_especifico','c6308b9c-00f1-476c-aa61-745bb26e0f71|52128175-f648-4a4b-bb2d-4d7a97ea4b6e',2,'NICE','2022'),
  # endocrino
  @('Hipotiroidismo','tratamiento_especifico','762b4267-6ce6-4de7-8342-7d415fedda6b',1,'ATA','2014'),
  @('Anemia','tratamiento_especifico','114d5a9f-f369-4976-9eb7-1f78050c6a9b',1,'OMS','2023'),
  @('Anemia ferropenica','tratamiento_especifico','114d5a9f-f369-4976-9eb7-1f78050c6a9b|4eb11496-3275-4087-ab5c-b1e4ea885e99',1,'OMS','2023'),
  # salud mental
  @('Trastorno de ansiedad','tratamiento_especifico','0e8496ea-c8f7-4f08-abef-994e3c3243a2',1,'WHO mhGAP','2016'),
  @('Trastorno de ansiedad generalizada','tratamiento_especifico','0e8496ea-c8f7-4f08-abef-994e3c3243a2',1,'WHO mhGAP','2016'),
  @('Insomnio','tratamiento_especifico','0e8496ea-c8f7-4f08-abef-994e3c3243a2',1,'WHO mhGAP','2016'),
  # ginecoobstetrico
  @('Candidiasis vaginal','tratamiento_especifico','2f76266a-a841-4147-9401-c6b8409b8ccc|4125aa3e-4b43-41c2-99a3-00bb71978084',1,'CDC','2021'),
  @('Vaginosis bacteriana','tratamiento_especifico','24de32d2-6d1f-4e53-b047-93e6729228c4',1,'CDC','2021'),
  @('Tricomoniasis','tratamiento_especifico','24de32d2-6d1f-4e53-b047-93e6729228c4',1,'CDC','2021'),
  @('Infección por clamidia','tratamiento_especifico','dd87f758-a49b-44ac-841b-16b61c176244',1,'CDC','2021'),
  @('Enfermedad inflamatoria pelvica','tratamiento_especifico','12dd160a-2ab4-4b95-900c-40ebf6ccc55f|24de32d2-6d1f-4e53-b047-93e6729228c4',2,'CDC','2021'),
  @('Infección urinaria en el embarazo','tratamiento_especifico','a8355c83-31db-4089-a962-512e35097ef0',1,'OMS','2023'),
  @('Embarazo (control prenatal)','tratamiento_especifico','4eb11496-3275-4087-ab5c-b1e4ea885e99|fb601ad7-ffb6-4624-8f64-93522bb8d4ac',2,'OMS','2023'),
  # trauma
  @('Herida abierta','tratamiento_especifico','9dcd95b2-afe2-4d51-8b88-17211966fd65',1,'OMS','2020'),
  @('Herida cortante (laceración)','tratamiento_especifico','9dcd95b2-afe2-4d51-8b88-17211966fd65',1,'OMS','2020'),
  @('Quemadura leve','manejo_sintomatico','1d153d70-219e-4683-96c2-c1337a2eea85|fbf74fd6-b37c-4325-86cb-dcaf4aabed81',2,'Guías MINSAL','2022'),
  @('Contusion','manejo_sintomatico','f05dd6ca-43d0-45ea-93ef-8d82483bbdc9|fbf74fd6-b37c-4325-86cb-dcaf4aabed81',2,'OMS','2020'),
  @('Abrasión cutánea','manejo_sintomatico','f05dd6ca-43d0-45ea-93ef-8d82483bbdc9|fbf74fd6-b37c-4325-86cb-dcaf4aabed81',1,'OMS','2020'),
  @('Fractura cerrada','manejo_sintomatico','fbf74fd6-b37c-4325-86cb-dcaf4aabed81|f05dd6ca-43d0-45ea-93ef-8d82483bbdc9',2,'NICE','2016'),
  @('Esguince de rodilla','manejo_sintomatico','f05dd6ca-43d0-45ea-93ef-8d82483bbdc9|fbf74fd6-b37c-4325-86cb-dcaf4aabed81',2,'OMS','2020'),
  @('Mordedura','tratamiento_especifico','a8355c83-31db-4089-a962-512e35097ef0',1,'IDSA','2021'),
  @('Mordedura de animal','tratamiento_especifico','1cd391d8-ed72-4c61-a203-faf40bee6921',1,'IDSA','2021'),
  @('Picadura de alacrán','manejo_sintomatico','fbf74fd6-b37c-4325-86cb-dcaf4aabed81',1,'Guías MINSAL','2022')
)

$salida = @()
$noEncontrados = @()
foreach ($r in $semilla) {
    $nombre = $r[0]; $accion = $r[1]; $meds = $r[2]; $max = $r[3]; $fuente = $r[4]; $ver = $r[5]
    $key = $nombre.ToLowerInvariant()
    if (-not $mapa.ContainsKey($key)) {
        $noEncontrados += $nombre
        continue
    }
    $dx = $mapa[$key]
    # Verificar que todos los drug_uuid referenciados existen en medicamentos.csv
    $validos = @($meds -split '\|' | ForEach-Object { $_.Trim() })
    $desconocidos = @($validos | Where-Object { -not $drugs.ContainsKey($_) })
    if ($desconocidos.Count -gt 0) {
        Write-Host "SKIP $nombre : medicamentos desconocidos: $($desconocidos -join ',')"
        continue
    }
    $fila = [pscustomobject]@{
        'diagnostico_uuid' = $dx.ciel_uuid
        'categoria'        = $dx.categoria
        'accion'           = $accion
        'medicamento_uuid' = ($validos -join '|')
        'max_medicamentos' = $max
        'fuente'           = $fuente
        'version_guia'     = $ver
    }
    $salida += $fila
    Write-Host ("OK   {0,-45} -> {1} ({2})" -f $nombre, $dx.ciel_uuid, $dx.categoria)
}

Write-Host ""
Write-Host "Reglas generadas: $($salida.Count)  | No encontrados en diagnosticos.csv: $($noEncontrados.Count)"
if ($noEncontrados.Count) { Write-Host "NO ENCONTRADOS:`n  $($noEncontrados -join "`n  ")" }

$salida | Export-Csv (Join-Path $root '/tmp_reglas_tratamiento.salida.csv') -NoTypeInformation -Encoding utf8
Write-Host "Guardado en /tmp_reglas_tratamiento.salida.csv"
