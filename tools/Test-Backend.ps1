[CmdletBinding()]
param(
    [string]$EnvironmentPath = '',
    [string]$WhitelistPhone = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($EnvironmentPath)) { $EnvironmentPath = Join-Path $root '.artifacts/validation/backend/wsl.env' }
function New-TestPhone {
    '+86138' + (Get-Random -Minimum 0 -Maximum 100000000).ToString('D8')
}
if ([string]::IsNullOrWhiteSpace($WhitelistPhone)) { $WhitelistPhone = New-TestPhone }
& (Join-Path $PSScriptRoot 'Initialize-WSLBackend.ps1') -EnvironmentPath $EnvironmentPath
foreach ($line in Get-Content -LiteralPath $EnvironmentPath) {
    if ($line -match '^([A-Z0-9_]+)=(.*)$') { Set-Item -Path "Env:$($Matches[1])" -Value $Matches[2] }
}
$env:INSECTSPACE_PHONE_WHITELIST = $WhitelistPhone
$env:INSECTSPACE_PHONE_AUTH_MODE = 'test'
& (Join-Path $PSScriptRoot 'Start-BackendServices.ps1') -EnvironmentPath $EnvironmentPath -Build
try {
    $ports = @(8080, 8081, 8082, 8083, 8084, 8085)
    foreach ($port in $ports) {
        $healthy = $false
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            try { $response = Invoke-RestMethod -Uri "http://127.0.0.1:$port/healthz" -TimeoutSec 2; if ($response.mySql -and $response.redis) { $healthy = $true; break } } catch { Start-Sleep -Milliseconds 250 }
        }
        if (!$healthy) { throw "Backend service on port $port did not become healthy." }
    }

    $headers = @{ Authorization = '' }
    $loginBody = @{ phoneNumber = $WhitelistPhone; code = ''; register = $true } | ConvertTo-Json
    $whitelistLogin = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8081/v1/identity/phone/login' -ContentType 'application/json' -Body $loginBody
    if ([string]::IsNullOrWhiteSpace($whitelistLogin.sessionToken)) { throw 'Whitelist phone did not receive a session.' }
    $headers.Authorization = "Bearer $($whitelistLogin.sessionToken)"

    $world = @{ worldClusterId = 'local-cluster'; instanceId = 'local-1'; sceneId = 'home'; endpoint = '127.0.0.1:19000'; capacity = 100; epoch = 1 } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8083/v1/world/register' -ContentType 'application/json' -Body $world | Out-Null
    $routeBody = @{ playerId = $whitelistLogin.playerId; homeRealmId = $whitelistLogin.homeRealmId; preferredClusterId = 'local-cluster'; preferredSceneId = 'home' } | ConvertTo-Json
    $route = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8082/v1/world/join' -Headers $headers -ContentType 'application/json' -Body $routeBody
    if ($route.epoch -lt 1) { throw 'World route epoch must be a positive value.' }
    $battleBody = @{ playerId = $whitelistLogin.playerId; homeRealmId = $whitelistLogin.homeRealmId; worldClusterId = $route.worldClusterId; instanceId = $route.instanceId; sceneId = $route.sceneId; worldEpoch = $route.epoch; simulationVersion = 'local-sim-1'; configVersion = 'local-config-1' } | ConvertTo-Json
    $battle = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8084/v1/battle/rooms' -Headers $headers -ContentType 'application/json' -Body $battleBody
    if ([string]::IsNullOrWhiteSpace($battle.roomId)) { throw 'Battle room was not created.' }
    $operationId = 'backend-test-' + [Guid]::NewGuid().ToString('N')
    $commandBody = @{ domain = 'inventory'; operationId = $operationId; playerId = $whitelistLogin.playerId; payload = @{ itemId = 'test-item'; quantity = 1 } } | ConvertTo-Json -Depth 5
    $accepted = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8085/v1/domain/commands' -Headers $headers -ContentType 'application/json' -Body $commandBody
    $acceptedAgain = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8085/v1/domain/commands' -Headers $headers -ContentType 'application/json' -Body $commandBody
    if ($accepted.requestHash -ne $acceptedAgain.requestHash) { throw 'Domain command idempotency failed.' }

    $nonWhitelist = New-TestPhone
    $codeBody = @{ phoneNumber = $nonWhitelist } | ConvertTo-Json
    $code = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8081/v1/identity/phone/code' -ContentType 'application/json' -Body $codeBody
    if ([string]::IsNullOrWhiteSpace($code.developmentCode)) { throw 'Test OTP did not return a development code.' }
    $verifyBody = @{ phoneNumber = $nonWhitelist; code = $code.developmentCode; register = $true } | ConvertTo-Json
    $otpLogin = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8081/v1/identity/phone/login' -ContentType 'application/json' -Body $verifyBody
    if ([string]::IsNullOrWhiteSpace($otpLogin.sessionToken)) { throw 'Non-whitelist OTP login failed.' }

    $resultDirectory = Join-Path $root '.artifacts/validation/backend'
    [pscustomobject]@{ WhitelistLogin = $true; OtpLogin = $true; WorldRoute = $route.epoch; BattleRoom = $battle.roomId; CommandIdempotent = $true; MySql = $true; Redis = $true } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $resultDirectory 'test-result.json') -Encoding ascii
    Write-Host 'BACKEND_TEST_PASSED: WSL2 MySQL/Redis, whitelist login, OTP login, routing, battle room, and command idempotency.'
}
finally {
    & (Join-Path $PSScriptRoot 'Stop-BackendServices.ps1')
}
