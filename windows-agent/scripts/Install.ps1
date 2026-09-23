[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][System.Net.IPAddress]$PcAddress,
    [Parameter(Mandatory)][System.Net.IPAddress]$HubAddress,
    [ValidateRange(1024,65535)][int]$Port = 8765
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $PublishDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $source 'STMediaBridge.Agent.exe'))) {
    throw 'PublishDirectory must contain a published STMediaBridge.Agent.exe.'
}
if ($PcAddress.AddressFamily -ne 'InterNetwork' -or $HubAddress.AddressFamily -ne 'InterNetwork' -or
    $PcAddress.Equals([System.Net.IPAddress]::Any) -or $HubAddress.Equals([System.Net.IPAddress]::Any)) {
    throw 'Use explicit IPv4 addresses for the PC and hub.'
}
$installRoot = Join-Path $env:LOCALAPPDATA 'STMediaBridge'
$bin = Join-Path $installRoot 'bin'
$configPath = Join-Path $installRoot 'agent.json'
$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$taskName = 'ST MediaBridge-' + $identity.User.Value
New-Item -ItemType Directory -Path $bin -Force | Out-Null
# Preserve identity and valid current tokens. Stop only this user's named task.
$installedExe = Join-Path $bin 'STMediaBridge.Agent.exe'
# Task Scheduler can report Ready before the process has released its DLLs.
# Capture process handles before stopping so we can wait for actual termination.
$agentProcesses = @(Get-Process -Name 'STMediaBridge.Agent' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $installedExe })
$existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($existingTask) {
    Stop-ScheduledTask -TaskName $taskName
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-ScheduledTask -TaskName $taskName).State -eq 'Running' -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }
    if ((Get-ScheduledTask -TaskName $taskName).State -eq 'Running') { throw 'Existing agent task did not stop; no files were replaced.' }
}
foreach ($agentProcess in $agentProcesses) {
    try {
        if (-not $agentProcess.WaitForExit(10000)) {
            throw 'The installed agent is still running. Exit it from the tray before updating; no files were replaced.'
        }
    } finally { $agentProcess.Dispose() }
}
if ($source -ne $bin) {
    # Image/DLL cleanup can briefly outlast process termination. Retry only
    # Windows sharing/lock violations; permission and other failures stay visible.
    $copyDeadline = [DateTime]::UtcNow.AddSeconds(10)
    while ($true) {
        try {
            Copy-Item -Path (Join-Path $source '*') -Destination $bin -Recurse -Force -ErrorAction Stop
            break
        } catch {
            $nativeError = $_.Exception.HResult -band 0xffff
            if ($nativeError -notin @(32, 33) -or [DateTime]::UtcNow -ge $copyDeadline) { throw }
            Start-Sleep -Milliseconds 250
        }
    }
}
if (Test-Path -LiteralPath $configPath) {
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
} else {
    $config = [pscustomobject]@{deviceId=[guid]::NewGuid().ToString(); token=''}
}
if ($config.token -cmatch '^[0-9a-fA-F]{64}$') {
    Write-Host 'Migrating legacy token: a new 32-character secret will be generated. Update the SmartThings pairing token after installation.'
    $config.token = ''
}
if ([string]::IsNullOrEmpty($config.token)) {
    $secret = New-Object byte[] 16
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        do {
            $rng.GetBytes($secret)
            $config.token = ([BitConverter]::ToString($secret)).Replace('-','')
        } while ($config.token -eq ('0' * 32))
    } finally { $rng.Dispose() }
}
if ($config.token -cnotmatch '^[0-9a-fA-F]{32}$' -or $config.token -eq ('0' * 32)) { throw 'Invalid pairing token; use --rotate-token before reinstalling.' }
$parsedId = [guid]::Empty
if (-not [guid]::TryParseExact($config.deviceId, 'D', [ref]$parsedId) -or $parsedId -eq [guid]::Empty) { throw 'Configuration needs a nonzero device UUID.' }
$config | Add-Member -NotePropertyName bindAddress -NotePropertyValue $PcAddress.ToString() -Force
$config | Add-Member -NotePropertyName hubAddress -NotePropertyValue $HubAddress.ToString() -Force
$config | Add-Member -NotePropertyName port -NotePropertyValue $Port -Force
# The old phone-IP allowlist is no longer used. Missing artworkEnabled means true.
$config.PSObject.Properties.Remove('artworkClients')
$config | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding UTF8
# Remove inherited access from the secret file; retain user and LocalSystem.
$acl = New-Object System.Security.AccessControl.FileSecurity
$acl.SetAccessRuleProtection($true, $false)
$acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($identity.User, 'FullControl', 'Allow')))
$acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule([System.Security.Principal.SecurityIdentifier]'S-1-5-18', 'FullControl', 'Allow')))
Set-Acl -LiteralPath $configPath -AclObject $acl
$exe = Join-Path $bin 'STMediaBridge.Agent.exe'
$action = New-ScheduledTaskAction -Execute $exe -Argument ('--config "' + $configPath + '"') -WorkingDirectory $bin
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
$principal = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $taskName -Description 'ST Windows Media Control — Windows media and volume control from SmartThings' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName $taskName
Write-Host "Installed for $($identity.Name). Configuration: $configPath"
Write-Host 'Next: run Set-Firewall.ps1 from an elevated PowerShell, then pair in SmartThings.'
