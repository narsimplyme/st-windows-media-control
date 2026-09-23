# Run script validation with mocked firewall cmdlets; never change host rules.
$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '../windows-agent/scripts/Set-Firewall.ps1'
$source = (Get-Content -LiteralPath $scriptPath -Raw) -replace '(?m)^#Requires -RunAsAdministrator\r?\n', ''
$block = [scriptblock]::Create($source)
function Get-NetFirewallRule { throw 'Firewall cmdlet reached for invalid input' }
function New-NetFirewallRule { throw 'Firewall cmdlet reached for invalid input' }
function Remove-NetFirewallRule { throw 'Firewall cmdlet reached for invalid input' }
$configPath = Join-Path ([IO.Path]::GetTempPath()) ('st-firewall-test-' + [guid]::NewGuid() + '.json')
try {
    foreach ($case in @(
        @{id='*'; port=8765; expected='UUID'},
        @{id='?'; port=8765; expected='UUID'},
        @{id='00000000-0000-0000-0000-000000000000'; port=8765; expected='UUID'},
        @{id='12345678-1234-1234-1234-123456789abc'; port=80; expected='1024'},
        @{id='12345678-1234-1234-1234-123456789abc'; port='8765'; expected='integer'}
    )) {
        @{bindAddress='192.168.1.20';hubAddress='192.168.1.2';deviceId=$case.id;port=$case.port} | ConvertTo-Json | Set-Content -LiteralPath $configPath
        try { & $block -ConfigPath $configPath -AgentExe $scriptPath; throw 'Invalid input accepted' }
        catch { if ($_.Exception.Message -notlike ('*'+$case.expected+'*')) { throw } }
    }
    Write-Output 'PASS five invalid firewall configurations rejected before any rule lookup/mutation'
} finally { Remove-Item -LiteralPath $configPath -ErrorAction SilentlyContinue }
