$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & dotnet run --project 'tests/InsectSpace.Foundation.Tests/InsectSpace.Foundation.Tests.csproj'
    if ($LASTEXITCODE -ne 0) { throw 'Foundation regression tests failed.' }
    & dotnet run --project 'server/InsectSpace.Server/InsectSpace.Server.csproj' -- --validate
    if ($LASTEXITCODE -ne 0) { throw 'Server bootstrap validation failed.' }
} finally { Pop-Location }
