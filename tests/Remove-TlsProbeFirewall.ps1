#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
Get-NetFirewallRule -Name 'STWindowsMediaControl-TlsProbe' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
