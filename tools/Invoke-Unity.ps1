param(
    [ValidateSet('Prepare','Artifacts','EditMode','PlayMode','Native','NativePatch','MiniGame','MiniGameNative','WeChatPrepare','WeChatNative')][string]$Action = 'Prepare',
    [string]$Editor = 'D:\APP\UnityEditor\2022.3.2t2\2022.3.62t16\Editor\Tuanjie.exe',
    [int]$TimeoutSeconds = 600
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sourceProject = Join-Path $root 'client/unity/InsectSpaceClient'
$project = Join-Path $root '.artifacts/unity/ValidationClient'
$output = Join-Path $root '.artifacts/validation'
New-Item -ItemType Directory -Force $output | Out-Null
if (!(Test-Path -LiteralPath $Editor)) { throw "Tuanjie editor not found: $Editor. Supply -Editor." }
$lock = Join-Path $project 'Temp/TuanjieLockfile'
if (Test-Path $lock) {
    try { $handle = [IO.File]::Open($lock, 'Open', 'ReadWrite', 'None'); $handle.Dispose() }
    catch { throw 'The isolated validation copy is already running.' }
}
# Same directory depth preserves the vendored UPM relative paths. Never move or
# close the user's live project, and keep build/test work out of its Library.
New-Item -ItemType Directory -Force $project | Out-Null
foreach ($folder in @('Assets','Packages','ProjectSettings')) {
    New-Item -ItemType Directory -Force (Join-Path $project $folder) | Out-Null
    Copy-Item -Path "$sourceProject/$folder/*" -Destination "$project/$folder" -Recurse -Force
}
$manifestFile = Join-Path $project 'Packages/manifest.json'
$manifest = Get-Content -Raw $manifestFile | ConvertFrom-Json -AsHashtable
# Optional editor assistants are machine-local and unrelated to framework validation.
$manifest.dependencies.Remove('cn.tuanjie.ai.generators')
$manifest.dependencies.Remove('cn.tuanjie.codely.bridge')
if ($Action -eq 'MiniGameNative') {
    $manifest.dependencies.Remove('com.qq.weixin.minigame')
}
$manifest | ConvertTo-Json -Depth 20 | Set-Content $manifestFile -Encoding utf8NoBOM
$log = Join-Path $output "unity-$Action.log"
$arguments = @('-batchmode','-nographics','-projectPath',"`"$project`"",'-logFile',"`"$log`"")
switch ($Action) {
    'Prepare' { $arguments += @('-executeMethod','InsectSpace.Editor.FoundationSetup.Prepare','-quit') }
    'Artifacts' { $arguments += @('-executeMethod','InsectSpace.Editor.HotUpdateBuild.ValidateBuildArtifacts','-quit') }
    'Native' { $arguments += @('-buildTarget','Win64','-executeMethod','InsectSpace.Editor.NativeValidationBuild.Build','-quit') }
    'NativePatch' { $arguments += @('-buildTarget','Win64','-executeMethod','InsectSpace.Editor.NativeValidationBuild.BuildPatch','-quit') }
    'MiniGame' { $arguments += @('-executeMethod','InsectSpace.Editor.MiniGameBuild.Preflight','-quit') }
    'MiniGameNative' { $arguments += @('-buildTarget','MiniGame','-executeMethod','InsectSpace.Editor.MiniGameBuild.ExportNative','-quit') }
    'WeChatPrepare' { $arguments += @('-buildTarget','MiniGame','-executeMethod','InsectSpace.Editor.MiniGameBuild.SelectWeChatPlatform','-quit') }
    'WeChatNative' { $arguments += @('-buildTarget','MiniGame','-executeMethod','InsectSpace.Editor.WeChatBuild.ExportNative','-quit') }
    default {
        $arguments += @('-runTests','-testPlatform',$Action,'-testResults',"`"$output/$Action.xml`"")
        $resultPath = Join-Path $output "$Action.xml"
        if (Test-Path $resultPath) { Remove-Item -LiteralPath $resultPath }
    }
}
$previousOutput = $env:INSECTSPACE_BUILD_OUTPUT_ROOT
$previousOverlay = $env:INSECTSPACE_NATIVE_OVERLAY
$motherHash = $null
if ($Action -eq 'NativePatch') {
    $motherHash = (Get-FileHash -LiteralPath (Join-Path $project 'HybridCLRData/ValidationPlayer/GameAssembly.dll') -Algorithm SHA256).Hash
}
try {
    if ($Action -in @('Artifacts','Native','NativePatch','MiniGameNative','WeChatNative')) {
        # Keep published versions immutable while allowing repeated validation runs.
        $env:INSECTSPACE_BUILD_OUTPUT_ROOT = Join-Path $root ('.artifacts/yoo/validation-' + [Guid]::NewGuid().ToString('N'))
    }
    if ($Action -in @('Native','MiniGameNative','WeChatNative')) {
        & "$PSScriptRoot/Install-NativeToolchain.ps1"
        $env:INSECTSPACE_NATIVE_OVERLAY = Join-Path $root '.artifacts/toolchains/tuanjie-8.5.0/libil2cpp'
    }
    $process = Start-Process -FilePath $Editor -ArgumentList $arguments -PassThru -WindowStyle Hidden
} finally {
    $env:INSECTSPACE_BUILD_OUTPUT_ROOT = $previousOutput
    $env:INSECTSPACE_NATIVE_OVERLAY = $previousOverlay
}
if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    throw "Unity validation timed out. See $log"
}
if ($process.ExitCode -ne 0) {
    Select-String -Path $log -Pattern 'error CS|^Exception|^Error:|compiler errors|executeMethod|Aborting batchmode' |
        Select-Object -Last 15 | ForEach-Object { Write-Host $_.Line }
    throw "Unity validation failed: exit $($process.ExitCode). See $log"
}
if ($Action -in @('EditMode','PlayMode')) {
    if (!(Test-Path $resultPath)) { throw 'Unity did not produce a test result.' }
    [xml]$result = Get-Content -Raw $resultPath
    if ($result.'test-run'.result -ne 'Passed' -or [int]$result.'test-run'.passed -lt 1) {
        throw "Unity tests did not pass. See $resultPath"
    }
    Write-Host "$Action passed: $($result.'test-run'.passed) tests."
} else {
    $marker = if ($Action -eq 'WeChatPrepare') { 'WECHAT_SUBPLATFORM_SELECTED WeChat' } else { 'FOUNDATION_PREPARED' }
    if (!(Select-String -Path $log -Pattern $marker -Quiet)) {
        throw "Missing preparation completion marker. See $log"
    }
    if ($Action -eq 'Artifacts' -and !(Select-String -Path $log -Pattern 'CORE_PACKAGE_BUILT' -Quiet)) {
        throw "Missing resource-build completion marker. See $log"
    }
    if ($Action -eq 'Native') {
        if (!(Select-String -Path $log -Pattern 'NATIVE_PLAYER_BUILT' -Quiet)) {
            throw "Missing native-build completion marker. See $log"
        }
        $playerLog = Join-Path $output 'native-player.log'
        $player = Join-Path $project 'HybridCLRData/ValidationPlayer/InsectSpace.Validation.exe'
        $run = Start-Process -FilePath $player -ArgumentList @('-batchmode','-nographics',
            '-insectspace-validate','-logFile',"`"$playerLog`"") -PassThru -WindowStyle Hidden
        if (!$run.WaitForExit(180000)) {
            Stop-Process -Id $run.Id -ErrorAction SilentlyContinue
            throw "Native Player timed out. See $playerLog"
        }
        if ($run.ExitCode -ne 0 -or
            !(Select-String -Path $playerLog -Pattern 'NATIVE_CODE_LOADED' -Quiet) -or
            !(Select-String -Path $playerLog -Pattern 'NATIVE_VALIDATION_PASSED' -Quiet) -or
            (Select-String -Path $playerLog -Pattern 'BOOT_FAILED|NATIVE_VALIDATION_FAILED|Exception:' -Quiet)) {
            throw "Native Player validation failed. See $playerLog"
        }
        Write-Host "Native IL2CPP Player verified. Log: $playerLog"
    }
    if ($Action -eq 'NativePatch') {
        if (!(Select-String -Path $log -Pattern 'NATIVE_PATCH_BUILT' -Quiet)) {
            throw "Missing native patch completion marker. See $log"
        }
        & "$PSScriptRoot/Test-NativePatch.ps1" -Project $project -ExpectedMotherHash $motherHash
    }
    if ($Action -eq 'MiniGame' -and !(Select-String -Path $log -Pattern 'MINIGAME_MANAGED_COMPILED' -Quiet)) {
        throw "Missing MiniGame compilation marker. See $log"
    }
    if ($Action -eq 'MiniGameNative') {
        if (!(Select-String -Path $log -Pattern 'MINIGAME_NATIVE_EXPORTED' -Quiet)) {
            throw "Missing MiniGame native export marker. See $log"
        }
        & "$PSScriptRoot/Test-MiniGameExport.ps1"
    }
    if ($Action -eq 'WeChatNative') {
        if (!(Select-String -Path $log -Pattern 'WECHAT_NATIVE_EXPORTED' -Quiet)) {
            throw "Missing WeChat conversion completion marker. See $log"
        }
        & "$PSScriptRoot/Test-WeChatExport.ps1"
    }
}
Write-Host "Unity $Action verified. Log: $log"
