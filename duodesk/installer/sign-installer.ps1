[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [Parameter(Mandatory)][string]$SignToolPath,
    [Parameter(Mandatory)][string]$Thumbprint,
    [Parameter(Mandatory)][string]$TimestampUrl,
    [switch]$MachineStore,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$installer = (Resolve-Path -LiteralPath $InstallerPath -ErrorAction Stop).Path
$signTool = (Resolve-Path -LiteralPath $SignToolPath -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $installer -PathType Leaf) -or [IO.Path]::GetExtension($installer) -ine '.exe') {
    throw 'Installer harus berupa file .exe yang ada.'
}
if (-not (Test-Path -LiteralPath $signTool -PathType Leaf) -or [IO.Path]::GetFileName($signTool) -ine 'signtool.exe') {
    throw 'Alat harus berupa signtool.exe dari Windows SDK.'
}
$normalized = $Thumbprint -replace '\s', ''
if ($normalized -notmatch '^[0-9A-Fa-f]{40}$') { throw 'Thumbprint sertifikat harus terdiri dari 40 digit heksadesimal.' }
$normalized = $normalized.ToUpperInvariant()
$timestamp = $null
if (-not [Uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$timestamp) -or
    $timestamp.Scheme -ne 'https' -or -not [string]::IsNullOrEmpty($timestamp.UserInfo) -or
    -not [string]::IsNullOrEmpty($timestamp.Fragment)) {
    throw 'Gunakan URL timestamp HTTPS yang valid tanpa kredensial.'
}
if ($ValidateOnly) {
    Write-Output 'Format parameter dan lokasi file valid. Sertifikat tidak diakses dan file tidak ditandatangani.'
    return
}

# ValidateOnly deliberately stops before accessing credentials or executing any tool.
$toolSignature = Get-AuthenticodeSignature -LiteralPath $signTool
if ($toolSignature.Status -ne 'Valid' -or
    $null -eq $toolSignature.SignerCertificate -or
    $toolSignature.SignerCertificate.Subject -notmatch '(?i)\bMicrosoft\b') {
    throw 'signtool.exe harus memiliki tanda tangan Microsoft yang valid.'
}
$store = if ($MachineStore) { 'Cert:\LocalMachine\My' } else { 'Cert:\CurrentUser\My' }
$cert = Get-Item -LiteralPath (Join-Path $store $normalized) -ErrorAction Stop
if (-not $cert.HasPrivateKey) { throw 'Sertifikat tidak memiliki akses ke private key.' }
if ($cert.NotBefore -gt (Get-Date) -or $cert.NotAfter -le (Get-Date)) { throw 'Sertifikat belum berlaku atau sudah kedaluwarsa.' }
if (-not ($cert.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' })) {
    throw 'Sertifikat tidak memiliki kegunaan Code Signing.'
}

$signArgs = @('sign', '/sha1', $normalized, '/fd', 'SHA256', '/tr', $timestamp.AbsoluteUri, '/td', 'SHA256', '/v')
if ($MachineStore) { $signArgs += '/sm' }
$signArgs += $installer
& $signTool @signArgs
if ($LASTEXITCODE -ne 0) { throw "Penandatanganan gagal (signtool exit $LASTEXITCODE)." }
& $signTool verify /pa /all /tw /v $installer
if ($LASTEXITCODE -ne 0) { throw "Verifikasi tanda tangan gagal (signtool exit $LASTEXITCODE)." }
$signature = Get-AuthenticodeSignature -LiteralPath $installer
if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or
    $signature.SignerCertificate.Thumbprint -ine $normalized -or $null -eq $signature.TimeStamperCertificate) {
    throw 'Hasil tidak memiliki tanda tangan valid, sertifikat yang dipilih, dan timestamp.'
}
Write-Output 'Installer ditandatangani dan diverifikasi dengan timestamp.'
