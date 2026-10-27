$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot ('signing-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$installer = Join-Path $root 'setup.exe'
$tool = Join-Path $root 'signtool.exe'
[IO.File]::WriteAllText($installer, 'fixture')
[IO.File]::WriteAllText($tool, 'fixture')
$before = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
$script = Join-Path $PSScriptRoot '..\duodesk\installer\sign-installer.ps1'
$validThumbprint = '0123456789ABCDEF0123456789ABCDEF01234567'
$failures = 0
function Check([bool]$result, [string]$description) {
    if ($result) { Write-Output "PASS $description" } else { Write-Output "FAIL $description"; $script:failures++ }
}
function Invoke-Validation([string]$inputPath, [string]$signTool, [string]$thumbprint, [string]$timestamp) {
    try { & $script -InstallerPath $inputPath -SignToolPath $signTool -Thumbprint $thumbprint -TimestampUrl $timestamp -ValidateOnly | Out-Null; return $true }
    catch { return $false }
}
try {
    Check (Invoke-Validation $installer $tool $validThumbprint 'https://timestamp.example.test') 'valid input passes syntax validation without certificates'
    Check (!(Invoke-Validation $installer $tool 'garbage' 'https://timestamp.example.test')) 'malformed certificate thumbprint is rejected'
    Check (!(Invoke-Validation $installer $tool $validThumbprint 'http://timestamp.example.test')) 'insecure timestamp endpoint is rejected'
    Check (!(Invoke-Validation (Join-Path $root 'missing.exe') $tool $validThumbprint 'https://timestamp.example.test')) 'missing installer is rejected'
    Check (!(Invoke-Validation $installer (Join-Path $root 'missing.exe') $validThumbprint 'https://timestamp.example.test')) 'missing SDK tool is rejected'
    Check ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -eq $before) 'validation leaves installer unchanged'
} finally {
    Remove-Item -LiteralPath $installer, $tool -Force
    Remove-Item -LiteralPath $root -Force
}
if ($failures -gt 0) { exit 1 }
