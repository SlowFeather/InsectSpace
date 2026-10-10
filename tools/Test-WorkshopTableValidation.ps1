param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$fixturePath = Join-Path $root ('.artifacts/validation/workshop-tables/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixturePath -Force | Out-Null
# Synthetic values exercise schema validation only. Never write them into design/luban.
function Fixture {
    return @{
        items = @(@{ id=1; name='TEST ONLY'; note='synthetic' })
        offers = @(@{ id=1; gu=1001; item=0; quantity=1; yuanShi=1 })
        care = @(1001,1002,2001 | ForEach-Object { @{ id=$_; food=1; foodCount=1; fedSeconds=10; refineFee=1; success=5000; destroy=2500; note='TEST ONLY' } })
        recipes = @(@{ id=1; name='TEST ONLY'; ingredients=@(@{id=1001;count=1},@{id=1002;count=2}); output=2001; minimumRank=2; materials=@(@{id=1;count=1}); yuanShi=1; success=5000; destroy=2500; source='Synthetic test, not live gameplay' })
    }
}
function Check([string]$Name, [scriptblock]$Change, [string]$Expected) {
    $data = Fixture
    & $Change $data
    foreach ($key in $data.Keys) { ConvertTo-Json -InputObject $data[$key] -Depth 10 | Set-Content -LiteralPath (Join-Path $fixturePath "$key.json") -Encoding utf8 }
    $caught = $null
    try { & (Join-Path $PSScriptRoot 'Test-WorkshopTables.ps1') -TablesPath $fixturePath }
    catch { $caught = $_.Exception.Message }
    if (!$Expected -and $caught) { throw "$Name rejected valid fixture: $caught" }
    if ($Expected -and (!$caught -or $caught -notmatch $Expected)) { throw "$Name expected '$Expected', got '$caught'" }
    Write-Host "PASS $Name"
}
Check 'valid variable lists' { param($d) } ''
Check 'valid empty optional materials' { param($d) $d.recipes[0].materials=@() } ''
Check 'unknown field' { param($d) $d.care[0].typo=1 } 'fields must be'
Check 'overflow odds' { param($d) $d.recipes[0].success=9000 } 'probabilities exceed'
Check 'unknown Gu' { param($d) $d.care[0].id=999999 } 'unknown Gu'
Check 'duplicate amount' { param($d) $d.recipes[0].ingredients[1].id=1001 } 'duplicates id'
Check 'too many Gu' { param($d) $d.recipes[0].ingredients[1].count=6 } 'between 2 and 6'
Check 'output rank gate' { param($d) $d.recipes[0].minimumRank=1 } 'must be an integer'
Check 'fractional price' { param($d) $d.offers[0].yuanShi=0.5 } 'must be an integer'
Check 'unknown food' { param($d) $d.care[0].food=2 } 'unknown food'
Write-Host 'WORKSHOP_TABLE_VALIDATION_TESTS_PASSED: 10 cases; live tables untouched.'
