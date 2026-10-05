[CmdletBinding()]
param(
    [string]$Distro = 'Ubuntu-24.04',
    [string]$DatabaseName = 'insectspace',
    [string]$DatabaseUser = 'insectspace_local',
    [string]$EnvironmentPath = '',
    [switch]$RotateLocalSecrets
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
$existing = @{}
if (Test-Path -LiteralPath $EnvironmentPath) {
    foreach ($line in Get-Content -LiteralPath $EnvironmentPath) {
        if ($line -match '^([A-Z0-9_]+)=(.*)$') { $existing[$Matches[1]] = $Matches[2] }
    }
    $existingPassword = (Get-Content -LiteralPath $EnvironmentPath | Where-Object { $_ -like 'INSECTSPACE_MYSQL_PASSWORD=*' } | Select-Object -First 1) -replace '^INSECTSPACE_MYSQL_PASSWORD=', ''
}
if ($RotateLocalSecrets) { $existingPassword = $null; $existing.Remove('INSECTSPACE_SESSION_SIGNING_KEY'); $existing.Remove('INSECTSPACE_INTERNAL_SERVICE_KEY') }
$databasePassword = if ($existingPassword) { $existingPassword } else {
    -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
}

function Invoke-WslRoot([string]$commandText) {
    $output = & wsl.exe -d $Distro --user root -- bash -lc $commandText 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "WSL infrastructure command failed ($LASTEXITCODE); inspect the local infrastructure services."
    }
    return ($output -join [Environment]::NewLine)
}

$setup = @'
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
if ! dpkg -s redis-server mysql-server python3 libicu74 >/dev/null 2>&1; then
  apt-get update
  apt-get install -y redis-server mysql-server python3 libicu74
fi
service mysql start
service redis-server start
if [ -f /etc/mysql/mysql.conf.d/mysqld.cnf ]; then
  sed -i -E 's/^[#[:space:]]*bind-address[[:space:]]*=.*/bind-address = 127.0.0.1/' /etc/mysql/mysql.conf.d/mysqld.cnf
fi
if grep -q '^bind ' /etc/redis/redis.conf; then
  sed -i -E 's/^bind .*/bind 127.0.0.1 ::1/' /etc/redis/redis.conf
else
  printf '\nbind 127.0.0.1 ::1\n' >> /etc/redis/redis.conf
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
$setupPath = Join-Path $artifactDirectory 'initialize-local.sh'
try {
    [IO.File]::WriteAllText($setupPath, $setup.Replace("`r", ''), [Text.UTF8Encoding]::new($false))
    $linuxSetup = (& wsl.exe -d $Distro -- wslpath -a ([IO.Path]::GetFullPath($setupPath).Replace('\','/'))).Trim()
    & wsl.exe -d $Distro --user root -- bash $linuxSetup *> $null
    if ($LASTEXITCODE -ne 0) { throw 'WSL infrastructure setup failed.' }
} finally { Remove-Item -LiteralPath $setupPath -Force -ErrorAction SilentlyContinue }

$wslAddress = (Invoke-WslRoot "hostname -I | cut -d' ' -f1").Trim()
if ($wslAddress -notmatch '^[0-9]+(\.[0-9]+){3}$') { throw 'Could not determine the WSL2 IPv4 address.' }
$sessionKey = if ($existing['INSECTSPACE_SESSION_SIGNING_KEY']) { $existing['INSECTSPACE_SESSION_SIGNING_KEY'] } else { [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) }
$serviceKey = if ($existing['INSECTSPACE_INTERNAL_SERVICE_KEY']) { $existing['INSECTSPACE_INTERNAL_SERVICE_KEY'] } else { [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) }
$environment = @"
INSECTSPACE_MYSQL_CONNECTION=Server=127.0.0.1;Port=3306;Database=$DatabaseName;User ID=$DatabaseUser;Password=$databasePassword;SslMode=None;AllowPublicKeyRetrieval=True
INSECTSPACE_MYSQL_PASSWORD=$databasePassword
INSECTSPACE_REDIS_CONNECTION=127.0.0.1:6379,password=$databasePassword,abortConnect=false
INSECTSPACE_SESSION_SIGNING_KEY=$sessionKey
INSECTSPACE_INTERNAL_SERVICE_KEY=$serviceKey
INSECTSPACE_PHONE_AUTH_MODE=test
INSECTSPACE_SESSION_HOURS=$(if ($existing['INSECTSPACE_SESSION_HOURS']) { $existing['INSECTSPACE_SESSION_HOURS'] } else { '720' })
INSECTSPACE_PHONE_WHITELIST=$($existing['INSECTSPACE_PHONE_WHITELIST'])
INSECTSPACE_LOCAL_BACKEND=true
INSECTSPACE_BIND_ADDRESS=127.0.0.1
"@
Set-Content -LiteralPath $EnvironmentPath -Value $environment -Encoding ascii
Write-Host "WSL2 backend ready at $wslAddress. Environment written to an ignored local artifact: $EnvironmentPath"
