param([int]$TcpPort = 7777, [int]$KcpPort = 7778, [switch]$ValidateOnly)
$root = Split-Path $PSScriptRoot -Parent
$arguments = @('run','--project',"$root/server/InsectSpace.Server/InsectSpace.Server.csproj",'--','--tcp-port',"$TcpPort",'--kcp-port',"$KcpPort")
if ($ValidateOnly) { $arguments += '--validate' }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Local server failed: $LASTEXITCODE" }
