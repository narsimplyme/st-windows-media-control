$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$source = Join-Path $PSScriptRoot '../windows-agent/scripts/Uninstall.ps1'
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
# Load only the pure path guards. Never execute the actual uninstaller in tests.
foreach ($name in @('Assert-NoLinks', 'Check-Targets')) {
    $function = $ast.Find({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true)
    . ([scriptblock]::Create($function.Extent.Text))
}
$sandbox = Join-Path ([IO.Path]::GetTempPath()) ('STWMC-removal-test-' + [Guid]::NewGuid().ToString('N'))
$local = $sandbox
$root = Join-Path $local 'STMediaBridge'
$cache = Join-Path $sandbox 'cache'
$outside = Join-Path $sandbox 'unrelated'
$link = Join-Path $root 'redirect'
try {
    New-Item -ItemType Directory -Path $root, $cache, $outside | Out-Null
    Set-Content -LiteralPath (Join-Path $outside 'sentinel.txt') -Value 'preserve'
    Check-Targets
    Write-Output 'PASS ordinary installation paths'
    $saved = $root; $root = $sandbox
    $rejected = $false
    try { Check-Targets } catch { $rejected = $true }
    if (-not $rejected) { throw 'Parent deletion was accepted' }
    $root = $saved
    Write-Output 'PASS parent deletion rejected'
    New-Item -ItemType Junction -Path $link -Target $outside | Out-Null
    $rejected = $false
    try { Check-Targets } catch { $rejected = $true }
    if (-not $rejected) { throw 'Junction was accepted' }
    if ((Get-Content -LiteralPath (Join-Path $outside 'sentinel.txt')) -ne 'preserve') { throw 'Outside data changed' }
    Write-Output 'PASS junction rejected and outside data preserved'
    $service = New-Object -ComObject Schedule.Service
    $service.Connect()
    try { $service.GetFolder('\').DeleteTask(('STWMC-Missing-Test-' + [Guid]::NewGuid()), 0) }
    catch { if ($_.Exception.HResult -notin @(-2147024894, -2147024893) -and $_.Exception.InnerException.HResult -notin @(-2147024894, -2147024893)) { throw } }
    Write-Output 'PASS missing legacy task is harmless'
} finally {
    # Unlink without traversal, then delete only the exact test sandbox.
    if (Test-Path -LiteralPath $link) { [IO.Directory]::Delete($link) }
    if ([IO.Path]::GetDirectoryName($sandbox) -ne [IO.Path]::GetTempPath().TrimEnd('\')) { throw 'Unsafe test cleanup path' }
    if (Test-Path -LiteralPath $sandbox) { Remove-Item -LiteralPath $sandbox -Recurse -Force }
}
