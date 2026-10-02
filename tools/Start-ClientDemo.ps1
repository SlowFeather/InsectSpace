param([switch]$OpenOnly, [switch]$KeepGameView, [ValidateRange(10, 180)][int]$TimeoutSeconds = 60)
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
$demoSetup = Join-Path $PSScriptRoot 'unity-demo/CreateDemoScene.cs'
Assert-DemoEditor
$created = Invoke-DemoCommand @('command', 'eval_file', $demoSetup)
Write-Host $created.result
if (!$KeepGameView) {
    $view = Invoke-DemoCommand @('command', 'eval_file', (Join-Path $PSScriptRoot 'unity-demo/ConfigureDemoView.cs'))
    Write-Host $view.result
}
if (!$OpenOnly) {
    $null = Invoke-DemoCommand @('command', 'editor_play')
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 500
        $status = Invoke-DemoRead @('command', 'eval', 'var h = UnityEngine.Object.FindObjectOfType<InsectSpace.Gameplay.Demo.ClientDemoHub>(); return new { present = h != null, ready = h != null && h.Ready, error = h == null ? null : h.Error, lessons = InsectSpace.Gameplay.Demo.ClientDemoHub.LessonCount, scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path };')
        $status.result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $demoEvidence 'launch.json') -Encoding utf8
        if ($status.result.error) { throw "Demo startup failed: $($status.result.error)" }
        if ($status.result.ready) {
            Write-Host "CLIENT_DEMO_READY lessons=$($status.result.lessons). Select the Game tab. See docs/Client-Demos.md."
            return
        }
    } while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    throw "Demo did not become Ready within $TimeoutSeconds seconds. Inspect the Game view and Console; no fallback was used."
}
