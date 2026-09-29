[CmdletBinding()]
param([switch]$DeleteSelf, [switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$title = 'ST Windows Media Control'
$ko = [Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName -eq 'ko'
function Text($korean, $english) { if ($ko) { $korean } else { $english } }
# Never accept a caller-supplied recursive deletion target.
$local = [Environment]::GetFolderPath('LocalApplicationData')
$root = [IO.Path]::GetFullPath((Join-Path $local 'STMediaBridge'))
$cache = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) '.net\STMediaBridge.Agent'))
function Assert-NoLinks([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    $item = Get-Item -LiteralPath $path -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing linked path: $path" }
    if ($item.PSIsContainer) {
        foreach ($child in Get-ChildItem -LiteralPath $path -Force) { Assert-NoLinks $child.FullName }
    }
}
function Check-Targets {
    if ([IO.Path]::GetDirectoryName($root) -ne [IO.Path]::GetFullPath($local)) { throw 'Invalid installation path.' }
    # Also reject redirected parent directories, including the extraction parent.
    foreach ($target in @($root, $cache)) {
        $parent = [IO.Directory]::GetParent($target)
        while ($null -ne $parent) {
            if ((Test-Path -LiteralPath $parent.FullName) -and
                ((Get-Item -LiteralPath $parent.FullName -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "Refusing linked parent: $($parent.FullName)"
            }
            $parent = $parent.Parent
        }
        Assert-NoLinks $target
    }
}
try {
    Check-Targets
    if ($ValidateOnly) { Write-Output 'Uninstall targets validated; nothing changed.'; return }
    $answer = [Windows.Forms.MessageBox]::Show((Text `
        "프로그램과 설정, 페어링 정보, 인증서, 로그, 자동 실행 및 방화벽 규칙을 제거합니다.`nSmartThings 기기와 다운로드한 원본 EXE는 직접 삭제해 주세요.`n`n제거할까요?" `
        "Remove the program, settings, pairing information, certificates, logs, startup entries and firewall rule?`nRemove the SmartThings device and original downloaded EXE separately."),
        $title, 'YesNo', 'Warning', 'Button2')
    if ($answer -ne 'Yes') { return }
    $configFile = Join-Path $root 'agent.json'
    if (Test-Path -LiteralPath $configFile) {
        $config = Get-Content -LiteralPath $configFile -Raw | ConvertFrom-Json
        $rule = $config.firewallRuleId
        if (-not $rule) { $rule = $config.deviceId }
        $guid = [Guid]::Empty
        if (-not [Guid]::TryParseExact([string]$rule, 'D', [ref]$guid) -or $guid -eq [Guid]::Empty) { throw 'Invalid firewall rule ID.' }
        # Only a validated public identifier crosses UAC; another administrator
        # never needs to read the user's private configuration or certificates.
        $code = '$ErrorActionPreference="Stop"; try { $policy=New-Object -ComObject HNetCfg.FwPolicy2; $policy.Rules.Remove("STMediaBridge-' + $guid.ToString('D') + '"); exit 0 } catch { if ($_.Exception.HResult -eq -2147024894 -or $_.Exception.InnerException.HResult -eq -2147024894) { exit 0 }; exit 1 }'
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code))
        $ps = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
        $helper = Start-Process -FilePath $ps -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile','-EncodedCommand',$encoded) -PassThru -Wait
        if ($helper.ExitCode -ne 0) { throw 'Could not remove the firewall rule. No application files were deleted.' }
    }
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $service = New-Object -ComObject Schedule.Service
    $service.Connect()
    $folder = $service.GetFolder('\')
    try { $folder.DeleteTask(('ST MediaBridge-' + $sid), 0) }
    catch { if ($_.Exception.HResult -notin @(-2147024894, -2147024893) -and $_.Exception.InnerException.HResult -notin @(-2147024894, -2147024893)) { throw } }
    foreach ($key in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Run', 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run')) {
        if (Test-Path -LiteralPath $key) {
            $opened = Get-Item -LiteralPath $key
            $opened.Close()
            Remove-ItemProperty -LiteralPath $key -Name $title -ErrorAction SilentlyContinue
            if (Get-ItemProperty -LiteralPath $key -Name $title -ErrorAction SilentlyContinue) { throw 'Could not remove startup entry.' }
        }
    }
    # Match the exact installed executable; never terminate by process name alone.
    $exe = Join-Path $root 'bin\STMediaBridge.Agent.exe'
    foreach ($process in Get-Process -Name 'STMediaBridge.Agent' -ErrorAction SilentlyContinue) {
        if ($process.Path -eq $exe) { $process.Kill(); if (-not $process.WaitForExit(10000)) { throw "Installed process did not exit." } }
    }
    foreach ($target in @($root, $cache)) {
        for ($attempt = 0; $attempt -lt 10; $attempt++) {
            try {
                Check-Targets
                if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
                break
            } catch { if ($attempt -eq 9) { throw }; Start-Sleep -Milliseconds 500 }
        }
    }
    [Windows.Forms.MessageBox]::Show((Text '제거가 완료되었습니다.' 'Uninstall complete.'), $title) | Out-Null
} catch {
    [Windows.Forms.MessageBox]::Show((Text '제거를 완료하지 못했습니다. 남은 파일을 보존했습니다. 다시 실행해 주세요.' 'Uninstall could not finish. Remaining files were preserved. Please retry.') + "`n" + $_.Exception.Message, $title, 'OK', 'Error') | Out-Null
    exit 1
} finally {
    if ($DeleteSelf -and [IO.Path]::GetFileName($PSCommandPath) -match '^STWMC-uninstall-[0-9a-f]{32}\.ps1$' -and
        [IO.Path]::GetDirectoryName($PSCommandPath) -eq [IO.Path]::GetTempPath().TrimEnd('\')) {
        Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
    }
}
