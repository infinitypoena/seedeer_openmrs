<#
.SYNOPSIS
  Respaldo de la base de datos OpenMRS (MariaDB en Docker) con retención.

.DESCRIPTION
  Hace mariadb-dump dentro del contenedor de la BD, comprime a .sql.gz con fecha
  y borra las copias más antiguas que excedan la retención. Pensado para correrse
  a mano o programado (Task Scheduler / cron).

.EJEMPLOS
  pwsh scripts/backup_openmrs.ps1
  pwsh scripts/backup_openmrs.ps1 -Destino D:\respaldos -Retencion 14

.RESTAURAR
  # ⚠️ Restaurar SOBRESCRIBE la base actual. Detener el backend primero:
  #   docker stop openmrs-distro-referenceapplication-360-backend-1
  #   gzip -dc backups/openmrs_YYYYMMDD_HHmm.sql.gz | docker exec -i <contenedor-db> mariadb -uroot -p<pass> openmrs
  #   docker start openmrs-distro-referenceapplication-360-backend-1

.NOTAS
  La contraseña de root se pasa por el parámetro -RootPassword o por la variable de
  entorno OPENMRS_DB_ROOT_PASSWORD. Nunca se embebe en el script (va a git).
#>
param(
    [string]$Contenedor = 'openmrs-distro-referenceapplication-360-db-1',
    [string]$BaseDatos  = 'openmrs',
    [string]$Destino    = (Join-Path $PSScriptRoot '..\backups'),
    [int]$Retencion     = 7,   # nº de copias a conservar
    [string]$RootPassword = ''
)

$ErrorActionPreference = 'Stop'

$rootPass = if ($RootPassword) { $RootPassword } else { $env:OPENMRS_DB_ROOT_PASSWORD }
if (-not $rootPass) {
    throw "Falta la contraseña de root de MariaDB: pásala con -RootPassword o define OPENMRS_DB_ROOT_PASSWORD. (Está en el .env / MYSQL_ROOT_PASSWORD del compose de la distro.)"
}

# Verificar que el contenedor está corriendo
$estado = docker inspect -f '{{.State.Running}}' $Contenedor 2>$null
if ($estado -ne 'true') { throw "El contenedor '$Contenedor' no está corriendo." }

New-Item -ItemType Directory -Force $Destino | Out-Null
$Destino = (Resolve-Path $Destino).Path
$archivo = Join-Path $Destino ("openmrs_{0}.sql.gz" -f (Get-Date -Format 'yyyyMMdd_HHmm'))

Write-Host "Respaldando '$BaseDatos' desde $Contenedor..."
# --single-transaction: dump consistente sin bloquear la instancia en uso (InnoDB)
docker exec $Contenedor sh -c "mariadb-dump -uroot -p'$rootPass' --single-transaction --routines --triggers $BaseDatos | gzip -c" > $archivo
if ($LASTEXITCODE -ne 0 -or (Get-Item $archivo).Length -lt 1MB) {
    Remove-Item $archivo -ErrorAction SilentlyContinue
    throw "El dump falló o quedó sospechosamente pequeño — no se guardó."
}

$tamano = [math]::Round((Get-Item $archivo).Length / 1MB, 1)
Write-Host "OK: $archivo ($tamano MB)"

# Retención: conservar las N más recientes
$viejas = Get-ChildItem $Destino -Filter 'openmrs_*.sql.gz' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -Skip $Retencion
foreach ($v in $viejas) {
    Remove-Item $v.FullName
    Write-Host "Retención: eliminado $($v.Name)"
}

Write-Host "Copias actuales: $((Get-ChildItem $Destino -Filter 'openmrs_*.sql.gz').Count) (retención: $Retencion)"
