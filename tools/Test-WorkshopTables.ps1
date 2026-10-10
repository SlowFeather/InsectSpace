param(
    [string]$TablesPath = (Join-Path $PSScriptRoot '../design/luban/GuWorkshop/Tables'),
    [string]$GuPath = (Join-Path $PSScriptRoot '../design/luban/GuPaths/Tables/gu.json'),
    [switch]$AllowUnconfigured
)
$ErrorActionPreference = 'Stop'
function Require([bool]$Condition, [string]$Message) { if (!$Condition) { throw "Workshop config: $Message" } }
function Fields($Row, [string[]]$Names, [string]$Context) {
    $actual = @($Row.PSObject.Properties.Name)
    Require ($actual.Count -eq $Names.Count -and @($Names | Where-Object { $_ -notin $actual }).Count -eq 0) "$Context fields must be: $($Names -join ', ')"
}
function Integer($Value, [long]$Min, [long]$Max, [string]$Context) {
    Require (($Value -is [int] -or $Value -is [long]) -and $Value -ge $Min -and $Value -le $Max) "$Context must be an integer in [$Min,$Max]."
}
function Odds($Row, [string]$Context) {
    Integer $Row.success 0 10000 "$Context.success"; Integer $Row.destroy 0 10000 "$Context.destroy"
    Require (($Row.success + $Row.destroy) -le 10000) "$Context probabilities exceed 10000 basis points."
}
$data = @{}; $maps = @{}
foreach ($name in @('items','offers','care','recipes')) {
    $raw = Get-Content -LiteralPath (Join-Path $TablesPath "$name.json") -Raw
    Require ($raw.TrimStart().StartsWith('[')) "$name.json must contain an array."
    $rows = @($raw | ConvertFrom-Json); $data[$name] = $rows; $map = @{}
    foreach ($row in $rows) {
        Integer $row.id 1 ([int]::MaxValue) "$name.id"
        Require (!$map.ContainsKey($row.id)) "$name contains duplicate id $($row.id)."
        $map[$row.id] = $row
    }
    $maps[$name] = $map
}
$total = ($data.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
if ($total -eq 0 -and $AllowUnconfigured) { Write-Host 'WORKSHOP_CONFIG_UNCONFIGURED: schema only; runtime startup remains blocked.'; return }
foreach ($name in $data.Keys) { Require ($data[$name].Count -gt 0) "$name.json is empty; confirm game rules before building playable tables." }
Require ($data.items.Count -le 32) 'At most 32 material definitions are supported by protocol v1.'
$gu = @{}
foreach ($row in @(Get-Content -LiteralPath $GuPath -Raw | ConvertFrom-Json)) { $gu[$row.id] = $row }
foreach ($row in $data.items) {
    Fields $row @('id','name','note') "item $($row.id)"
    Require (![string]::IsNullOrWhiteSpace($row.name)) "item $($row.id) has no name."
}
foreach ($row in $data.care) {
    $ctx = "care $($row.id)"; Fields $row @('id','food','foodCount','fedSeconds','refineFee','success','destroy','note') $ctx
    Require ($gu.ContainsKey($row.id)) "$ctx refers to an unknown Gu."
    Integer $row.food 1 ([int]::MaxValue) "$ctx.food"; Require ($maps.items.ContainsKey($row.food)) "$ctx refers to unknown food."
    Integer $row.foodCount 1 9999 "$ctx.foodCount"; Integer $row.fedSeconds 1 604800 "$ctx.fedSeconds"
    Integer $row.refineFee 1 ([int]::MaxValue) "$ctx.refineFee"; Odds $row $ctx
}
foreach ($row in $data.offers) {
    $ctx = "offer $($row.id)"; Fields $row @('id','gu','item','quantity','yuanShi') $ctx
    Integer $row.gu 0 ([int]::MaxValue) "$ctx.gu"; Integer $row.item 0 ([int]::MaxValue) "$ctx.item"
    Integer $row.quantity 1 9999 "$ctx.quantity"; Integer $row.yuanShi 1 ([int]::MaxValue) "$ctx.yuanShi"
    Require (($row.gu -gt 0 -and $row.item -eq 0 -and $row.quantity -eq 1 -and $maps.care.ContainsKey($row.gu)) -or
        ($row.gu -eq 0 -and $maps.items.ContainsKey($row.item))) "$ctx must sell one configured Gu or a known material stack."
}
foreach ($row in $data.recipes) {
    $ctx = "recipe $($row.id)"
    Fields $row @('id','name','ingredients','output','minimumRank','materials','yuanShi','success','destroy','source') $ctx
    Require (![string]::IsNullOrWhiteSpace($row.name) -and ![string]::IsNullOrWhiteSpace($row.source)) "$ctx needs a name and source/design note."
    Integer $row.output 1 ([int]::MaxValue) "$ctx.output"; Require ($maps.care.ContainsKey($row.output)) "$ctx output has no care definition."
    Integer $row.minimumRank $gu[$row.output].rank 9 "$ctx.minimumRank"; Integer $row.yuanShi 1 ([int]::MaxValue) "$ctx.yuanShi"; Odds $row $ctx
    foreach ($field in @('ingredients','materials')) {
        Require ($row.$field -is [array]) "$ctx.$field must be an array."
        $known = if ($field -eq 'ingredients') { $maps.care } else { $maps.items }
        $limit = if ($field -eq 'ingredients') { 6 } else { 9999 }
        $seen = @{}; [long]$sum = 0
        foreach ($amount in $row.$field) {
            Fields $amount @('id','count') "$ctx.$field"
            Integer $amount.id 1 ([int]::MaxValue) "$ctx.$field.id"; Integer $amount.count 1 $limit "$ctx.$field.count"
            Require ($known.ContainsKey($amount.id)) "$ctx.$field references unknown id $($amount.id)."
            Require (!$seen.ContainsKey($amount.id)) "$ctx.$field duplicates id $($amount.id); use its count instead."
            $seen[$amount.id] = $true; $sum += $amount.count
        }
        if ($field -eq 'ingredients') { Require ($sum -ge 2 -and $sum -le 6) "$ctx requires between 2 and 6 Gu instances." }
        else { Require ($row.materials.Count -le 32) "$ctx has too many material types." }
    }
}
Write-Host "WORKSHOP_CONFIG_PASSED: care=$($data.care.Count), offers=$($data.offers.Count), recipes=$($data.recipes.Count)."
