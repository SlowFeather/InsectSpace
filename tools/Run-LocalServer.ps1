param([int]$TcpPort = 7777, [int]$KcpPort = 7778, [switch]$ValidateOnly, [switch]$LocalEconomy, [int]$EconomyPort = 7779, [switch]$EconomyConsole, [int]$EconomyBattlePort = 7780, [switch]$LocalWorkshop)
$root = Split-Path $PSScriptRoot -Parent
$arguments = @('run','--project',"$root/server/InsectSpace.Server/InsectSpace.Server.csproj",'--','--tcp-port',"$TcpPort",'--kcp-port',"$KcpPort")
if ($ValidateOnly) { $arguments += '--validate' }
if ($LocalEconomy -or $LocalWorkshop) { $arguments += @('--local-economy', '--economy-port', "$EconomyPort", '--economy-battle-port', "$EconomyBattlePort") }
if ($LocalWorkshop) { $arguments += '--local-workshop' }
if ($EconomyConsole) { if (!$LocalEconomy -and !$LocalWorkshop) { throw 'EconomyConsole requires explicit -LocalEconomy or -LocalWorkshop.' }; $arguments += '--economy-console' }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Local server failed: $LASTEXITCODE" }
