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
$parsedRuleId = [guid]::Empty
if (-not [guid]::TryParseExact([string]$ruleId, 'D', [ref]$parsedRuleId) -or $parsedRuleId -eq [guid]::Empty) {
    throw 'Firewall rule ID must be a nonzero UUID; wildcard rule names are forbidden.'
}
if ($config.port -isnot [long] -and $config.port -isnot [int]) { throw 'Port must be an integer.' }
if ($config.port -lt 1024 -or $config.port -gt 65535) { throw 'Port must be 1024-65535.' }
foreach ($address in @($pc, $hub)) {
    if ($address.AddressFamily -ne 'InterNetwork' -or $address.GetAddressBytes()[0] -eq 0 -or $address.GetAddressBytes()[0] -ge 224) {
        throw 'A unicast IPv4 address is required.'
    }
}
$ruleName = 'STMediaBridge-' + $ruleId
$sources = @($hub.ToString())
Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -Name $ruleName -DisplayName 'ST Windows Media Control (hub control)' `
    -Direction Inbound -Action Allow -Protocol TCP -LocalPort $config.port -LocalAddress $pc.ToString() `
    -RemoteAddress $sources -Program $exe -Profile Private | Out-Null
Write-Host 'Created a Private-profile rule restricted to the configured hub.'
