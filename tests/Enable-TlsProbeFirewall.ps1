#Requires -RunAsAdministrator
param(
    [Parameter(Mandatory)][System.Net.IPAddress]$PcAddress,
    [Parameter(Mandatory)][System.Net.IPAddress]$HubAddress
)
$ErrorActionPreference = 'Stop'
foreach ($address in @($PcAddress, $HubAddress)) {
    if ($address.AddressFamily -ne 'InterNetwork' -or $address.GetAddressBytes()[0] -eq 0 -or $address.GetAddressBytes()[0] -ge 224) {
        throw 'Explicit unicast IPv4 addresses are required.'
    }
}
$rule = 'STWindowsMediaControl-TlsProbe'
$exe = (Resolve-Path (Join-Path $PSScriptRoot '..\artifacts\tls-probe\TlsProbe.exe')).Path
Get-NetFirewallRule -Name $rule -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -Name $rule -DisplayName 'ST Windows Media Control temporary TLS test' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8766 -LocalAddress $PcAddress.ToString() -RemoteAddress $HubAddress.ToString() -Program $exe -Profile Private | Out-Null
Write-Host 'TLS test firewall rule ready. Remove after testing: Remove-NetFirewallRule -Name STWindowsMediaControl-TlsProbe'
