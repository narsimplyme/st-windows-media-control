[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$taskName = 'ST MediaBridge-' + $identity.User.Value
if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $taskName
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ST Windows Media Control' -ErrorAction SilentlyContinue
Write-Host 'Startup registration removed. Files and configuration retained in %LOCALAPPDATA%\STMediaBridge.'
Write-Host 'Remove the STMediaBridge-<firewallRuleId (or deviceId for older configurations)> firewall rule in elevated PowerShell if it was added.'
