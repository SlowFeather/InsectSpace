# Shared CLI plumbing for the local teaching workflow. No project/package settings are changed.
$ErrorActionPreference = 'Stop'
$demoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$demoProject = Join-Path $demoRoot 'client/unity/InsectSpaceClient'
$demoEvidence = Join-Path $demoRoot '.artifacts/validation/client-demo'
if (!(Get-Command unity -ErrorAction SilentlyContinue)) { throw 'Unity CLI is required. See docs/Client-Demos.md.' }

function Invoke-DemoCommand([string[]]$Arguments) {
    $raw = & unity @Arguments --project-path $demoProject --format json --no-pager
    if ($LASTEXITCODE -ne 0) { throw ($raw -join "`n") }
    $response = ($raw -join "`n") | ConvertFrom-Json
    if (!$response.success -or $response.data.result.success -eq $false) { throw ($raw -join "`n") }
    return $response.data.result
}

# Read-only queries may briefly fail while entering/leaving Play reloads the Pipeline server.
# Never retry scene creation, Play, tests or any other mutation this way.
function Invoke-DemoRead([string[]]$Arguments) {
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try { return Invoke-DemoCommand $Arguments }
        catch {
            if ($attempt -eq 29 -or $_.Exception.Message -notmatch '400 Bad Request|HTTP 400|ECONNREFUSED|ECONNRESET|fetch failed|No.*Editor|domain.reload|unavailable|socket hang up|Network error|Connection reset by server') { throw }
            Start-Sleep -Milliseconds 500
        }
    }
}

function Assert-DemoEditor {
    $state = Invoke-DemoCommand @('command', 'editor_status')
    if ($state.unityVersion -notlike '6000.*') { throw 'This launcher requires the Unity 6 project.' }
    if ($state.playMode -ne 'stopped') { throw 'Stop the current Play session before running this command.' }
    $raw = & unity recompile --project-path $demoProject --format json --timeout 120
    if ($LASTEXITCODE -ne 0) { throw ($raw -join "`n") }
    # Compilation completes just before the Editor's domain reload. Confirm the main thread
    # can evaluate code again before sending a scene-changing command.
    Start-Sleep -Seconds 1
    $null = Invoke-DemoRead @('command', 'eval', 'return UnityEngine.Application.unityVersion;')
    New-Item -ItemType Directory -Path $demoEvidence -Force | Out-Null
    $raw | Set-Content -LiteralPath (Join-Path $demoEvidence 'compile.json') -Encoding utf8
}
