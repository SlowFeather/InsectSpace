[CmdletBinding()]
param(
    [string]$Distro = 'Ubuntu-24.04',
    [string]$DatabaseName = 'insectspace',
    [string]$DatabaseUser = 'insectspace_local',
    [string]$EnvironmentPath = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($EnvironmentPath)) {
    $EnvironmentPath = Join-Path $root '.artifacts/validation/backend/wsl.env'
}
if ($DatabaseName -notmatch '^[a-zA-Z0-9_]+$' -or $DatabaseUser -notmatch '^[a-zA-Z0-9_]+$') {
    throw 'DatabaseName and DatabaseUser may contain only ASCII letters, digits, and underscores.'
}

$artifactDirectory = Split-Path $EnvironmentPath -Parent
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
$existingPassword = $null
if (Test-Path -LiteralPath $EnvironmentPath) {
    $existingPassword = (Get-Content -LiteralPath $EnvironmentPath | Where-Object { $_ -like 'INSECTSPACE_MYSQL_PASSWORD=*' } | Select-Object -First 1) -replace '^INSECTSPACE_MYSQL_PASSWORD=', ''
}
$databasePassword = if ($existingPassword) { $existingPassword } else {
    -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
}

function Invoke-WslRoot([string]$commandText) {
    $output = & wsl.exe -d $Distro --user root -- bash -lc $commandText 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "WSL command failed ($LASTEXITCODE): $($output -join [Environment]::NewLine)"
    }
    return ($output -join [Environment]::NewLine)
}

$setup = @'
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y redis-server mysql-server
service mysql start
service redis-server start
if [ -f /etc/mysql/mysql.conf.d/mysqld.cnf ]; then
  sed -i -E 's/^[#[:space:]]*bind-address[[:space:]]*=.*/bind-address = 0.0.0.0/' /etc/mysql/mysql.conf.d/mysqld.cnf
fi
if grep -q '^bind ' /etc/redis/redis.conf; then
  sed -i -E 's/^bind .*/bind 0.0.0.0 ::1/' /etc/redis/redis.conf
else
  printf '\nbind 0.0.0.0 ::1\n' >> /etc/redis/redis.conf
fi
if grep -q '^requirepass ' /etc/redis/redis.conf; then
  sed -i -E 's/^requirepass .*/requirepass __PASSWORD__/' /etc/redis/redis.conf
else
  printf '\nrequirepass __PASSWORD__\n' >> /etc/redis/redis.conf
fi
service mysql restart
service redis-server restart
mysql --protocol=socket -uroot -e "CREATE DATABASE IF NOT EXISTS __DB__; CREATE USER IF NOT EXISTS '__USER__'@'%' IDENTIFIED BY '__PASSWORD__'; ALTER USER '__USER__'@'%' IDENTIFIED BY '__PASSWORD__'; GRANT ALL PRIVILEGES ON __DB__.* TO '__USER__'@'%'; FLUSH PRIVILEGES;"
redis-cli -a '__PASSWORD__' ping
mysql --protocol=socket -uroot -Nse 'SELECT 1'
'@
$setup = $setup.Replace('__DB__', $DatabaseName).Replace('__USER__', $DatabaseUser).Replace('__PASSWORD__', $databasePassword)
Write-Host 'Starting/configuring native WSL2 MySQL and Redis...'
Invoke-WslRoot $setup | Out-Null

$wslAddress = (Invoke-WslRoot "hostname -I | cut -d' ' -f1").Trim()
if ($wslAddress -notmatch '^[0-9]+(\.[0-9]+){3}$') { throw 'Could not determine the WSL2 IPv4 address.' }
$environment = @"
INSECTSPACE_MYSQL_CONNECTION=Server=$wslAddress;Port=3306;Database=$DatabaseName;User ID=$DatabaseUser;Password=$databasePassword;SslMode=None;AllowPublicKeyRetrieval=True
INSECTSPACE_MYSQL_PASSWORD=$databasePassword
INSECTSPACE_REDIS_CONNECTION=$wslAddress`:6379`,password=$databasePassword`,abortConnect=false
INSECTSPACE_SESSION_SIGNING_KEY=$([guid]::NewGuid().ToString('N'))$([guid]::NewGuid().ToString('N'))
INSECTSPACE_PHONE_AUTH_MODE=test
INSECTSPACE_PHONE_WHITELIST=
INSECTSPACE_BIND_ADDRESS=127.0.0.1
"@
Set-Content -LiteralPath $EnvironmentPath -Value $environment -Encoding ascii
Write-Host "WSL2 backend ready at $wslAddress. Environment written to an ignored local artifact: $EnvironmentPath"
