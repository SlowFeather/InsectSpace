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
$testEnvironment = Join-Path $root '.artifacts/validation/backend/test.env'
$lines = Get-Content -LiteralPath $EnvironmentPath
$lines = $lines | ForEach-Object {
    if ($_ -like 'INSECTSPACE_PHONE_WHITELIST=*') { "INSECTSPACE_PHONE_WHITELIST=$WhitelistPhone" }
    elseif ($_ -like 'INSECTSPACE_PHONE_AUTH_MODE=*') { 'INSECTSPACE_PHONE_AUTH_MODE=test' }
    elseif ($_ -like 'INSECTSPACE_SESSION_HOURS=*') { 'INSECTSPACE_SESSION_HOURS=720' }
    else { $_ }
}
$lines | Set-Content -LiteralPath $testEnvironment -Encoding utf8NoBOM
function Assert-Rejected([string]$Uri, $Body, [int]$Status) {
    $result = Invoke-WebRequest -Method Post -Uri $Uri -ContentType 'application/json' -Body ($Body | ConvertTo-Json) -SkipHttpErrorCheck
    if ([int]$result.StatusCode -ne $Status) { throw "Expected HTTP $Status from rejection path; got $($result.StatusCode)." }
}
try {
    & (Join-Path $PSScriptRoot 'Start-BackendServices.ps1') -EnvironmentPath $testEnvironment -Build
    $ports = @(8080, 8081, 8082, 8083, 8084, 8085)
    foreach ($port in $ports) {
        $healthy = $false
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            try { $response = Invoke-RestMethod -Uri "http://127.0.0.1:$port/healthz" -TimeoutSec 2; if ($response.mySql -and $response.redis -and $response.operatingSystem -like '*Linux*') { $healthy = $true; break } } catch { Start-Sleep -Milliseconds 250 }
        }
        if (!$healthy) { throw "Backend service on port $port did not become healthy." }
    }

    $headers = @{ Authorization = '' }
    $loginBody = @{ phoneNumber = $WhitelistPhone; code = ''; register = $true } | ConvertTo-Json
    $whitelistLogin = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8081/v1/identity/phone/login' -ContentType 'application/json' -Body $loginBody
    if ([string]::IsNullOrWhiteSpace($whitelistLogin.sessionToken)) { throw 'Whitelist phone did not receive a session.' }
    $expiry = [DateTimeOffset]::Parse($whitelistLogin.expiresAt)
    if ([Math]::Abs(($expiry - [DateTimeOffset]::UtcNow).TotalHours - 720) -gt 0.1) { throw 'Default Token lifetime is not 30 days.' }
    $headers.Authorization = "Bearer $($whitelistLogin.sessionToken)"
    foreach ($variant in @(@{ phoneNumber = $WhitelistPhone }, @{ phoneNumber = $WhitelistPhone; code = $null }, @{ phoneNumber = $WhitelistPhone; code = 'ignored' })) {
        $direct = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8081/v1/identity/phone/login' -ContentType 'application/json' -Body ($variant | ConvertTo-Json)
        if ($direct.playerId -ne $whitelistLogin.playerId -or !$direct.sessionToken) { throw 'Whitelist variants did not resolve the same persistent player.' }
    }

    $world = @{ worldClusterId = 'local-cluster'; instanceId = 'local-1'; sceneId = 'home'; endpoint = '127.0.0.1:19000'; capacity = 100; epoch = 1 } | ConvertTo-Json
    Assert-Rejected 'http://127.0.0.1:8083/v1/world/register' ($world | ConvertFrom-Json) 401
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
    foreach ($variant in @(@{ phoneNumber = $nonWhitelist }, @{ phoneNumber = $nonWhitelist; code = '' }, @{ phoneNumber = $nonWhitelist; code = $null })) {
        Assert-Rejected 'http://127.0.0.1:8081/v1/identity/phone/login' $variant 400
    }
    Assert-Rejected 'http://127.0.0.1:8081/v1/identity/phone/login' @{ phoneNumber = $nonWhitelist; code = '000000' } 401
    $codeBody = @{ phoneNumber = $nonWhitelist } | ConvertTo-Json
    $code = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8081/v1/identity/phone/code' -ContentType 'application/json' -Body $codeBody
    if ([string]::IsNullOrWhiteSpace($code.developmentCode)) { throw 'Test OTP did not return a development code.' }
    $verifyBody = @{ phoneNumber = $nonWhitelist; code = $code.developmentCode; register = $true } | ConvertTo-Json
    $otpLogin = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8081/v1/identity/phone/login' -ContentType 'application/json' -Body $verifyBody
    if ([string]::IsNullOrWhiteSpace($otpLogin.sessionToken)) { throw 'Non-whitelist OTP login failed.' }
    $otpHeaders = @{ Authorization = "Bearer $($otpLogin.sessionToken)" }
    $tokenProfile = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8081/v1/identity/session/login' -Headers $otpHeaders
    if ($tokenProfile.playerId -ne $otpLogin.playerId -or $tokenProfile.expiresAt -ne $otpLogin.expiresAt) { throw 'Token-only login failed or extended fixed expiry.' }
    if ($tokenProfile.PSObject.Properties.Name -contains 'sessionToken' -or $tokenProfile.PSObject.Properties.Name -contains 'tokenHash') { throw 'Token profile exposed credential internals.' }
    Assert-Rejected 'http://127.0.0.1:8081/v1/identity/phone/login' ($verifyBody | ConvertFrom-Json) 401
    & (Join-Path $PSScriptRoot 'Stop-BackendServices.ps1')
    & (Join-Path $PSScriptRoot 'Start-BackendServices.ps1') -EnvironmentPath $testEnvironment
    $tokenAfterRestart = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8081/v1/identity/session/login' -Headers $otpHeaders
    if ($tokenAfterRestart.playerId -ne $otpLogin.playerId -or $tokenAfterRestart.expiresAt -ne $otpLogin.expiresAt) { throw 'Token-only login did not survive host restart.' }
    $restored = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8081/v1/identity/phone/login' -ContentType 'application/json' -Body $loginBody
    if ($restored.playerId -ne $whitelistLogin.playerId) { throw 'Player identity did not persist across Linux host restart.' }
    Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8081/v1/identity/session/revoke' -Headers $headers | Out-Null
    $denied = Invoke-WebRequest -Method Post -Uri 'http://127.0.0.1:8082/v1/world/join' -Headers $headers -ContentType 'application/json' -Body $routeBody -SkipHttpErrorCheck
    if ($denied.StatusCode -ne 401) { throw 'Revoked session was accepted.' }
    $deniedToken = Invoke-WebRequest -Method Post -Uri 'http://127.0.0.1:8081/v1/identity/session/login' -Headers $headers -SkipHttpErrorCheck
    if ($deniedToken.StatusCode -ne 401) { throw 'Revoked Token login was accepted.' }
    # Only expire this generated test account; keep the warm Redis entry to prove MySQL is authoritative.
    $expireSql = "UPDATE insectspace.sessions SET expires_at=UTC_TIMESTAMP(6)-INTERVAL 1 SECOND WHERE player_id=$([long]$otpLogin.playerId)"
    & wsl.exe -d Ubuntu-24.04 --user root -- mysql --protocol=socket -uroot -e $expireSql
    if ($LASTEXITCODE -ne 0) { throw 'Expiry fixture setup failed.' }
    $expired = Invoke-WebRequest -Method Post -Uri 'http://127.0.0.1:8081/v1/identity/session/login' -Headers $otpHeaders -SkipHttpErrorCheck
    if ($expired.StatusCode -ne 401) { throw 'Expired Token with warm cache was accepted.' }
    & (Join-Path $PSScriptRoot 'Stop-BackendServices.ps1')
    $lines = $lines | Where-Object { $_ -notlike 'INSECTSPACE_SESSION_HOURS=*' }
    @($lines; 'INSECTSPACE_SESSION_HOURS=2') | Set-Content -LiteralPath $testEnvironment -Encoding utf8NoBOM
    & (Join-Path $PSScriptRoot 'Start-BackendServices.ps1') -EnvironmentPath $testEnvironment
    $shortSession = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8081/v1/identity/phone/login' -ContentType 'application/json' -Body $loginBody
    if ([Math]::Abs(([DateTimeOffset]::Parse($shortSession.expiresAt)-[DateTimeOffset]::UtcNow).TotalHours-2) -gt 0.1) { throw 'Configured Token lifetime failed.' }

    $resultDirectory = Join-Path $root '.artifacts/validation/backend'
    [pscustomobject]@{ TokenOnlyLogin = $true; TokenSurvivesRestart = $true; DefaultExpiryDays = 30; ConfiguredExpiryHours = 2; ExpiredWithWarmCacheRejected = $true; FixedExpiry = $true; WhitelistLogin = $true; WhitelistVariants = 4; NonWhitelistRejected = $true; OtpLogin = $true; OtpReplayRejected = $true; Revocation = $true; RestartPersistence = $true; NativeLinuxProcesses = 6; UnauthorizedWorldRegistrationRejected = $true; WorldRoute = $route.epoch; BattleRoom = $battle.roomId; CommandIdempotent = $true; MySql = $true; Redis = $true } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $resultDirectory 'test-result.json') -Encoding ascii
    Write-Host 'BACKEND_TEST_PASSED: WSL2 MySQL/Redis, whitelist login, OTP login, routing, battle room, and command idempotency.'
}
finally {
    & (Join-Path $PSScriptRoot 'Stop-BackendServices.ps1')
    Remove-Item -LiteralPath $testEnvironment -Force -ErrorAction SilentlyContinue
}
