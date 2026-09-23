#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ConfigPath,
    [Parameter(Mandatory)][string]$AgentExe
)
$ErrorActionPreference = 'Stop'
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
$exe = (Resolve-Path -LiteralPath $AgentExe).Path
$pc = [System.Net.IPAddress]::Parse($config.bindAddress)
$hub = [System.Net.IPAddress]::Parse($config.hubAddress)
if ($pc.Equals([System.Net.IPAddress]::Any) -or $hub.Equals([System.Net.IPAddress]::Any)) { throw 'Explicit addresses required.' }
$ruleId = if ($config.firewallRuleId) { $config.firewallRuleId } else { $config.deviceId }
$ruleName = 'STMediaBridge-' + $ruleId
$sources = @($hub.ToString())
Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -Name $ruleName -DisplayName 'ST Windows Media Control (hub control)' `
    -Direction Inbound -Action Allow -Protocol TCP -LocalPort $config.port -LocalAddress $pc.ToString() `
    -RemoteAddress $sources -Program $exe -Profile Private | Out-Null
Write-Host 'Created a Private-profile rule restricted to the configured hub.'
