param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist'),
    [string]$PublishDirectory = (Join-Path $PSScriptRoot '..\build\publish'),
    [string]$NsisCompiler = "${env:ProgramFiles(x86)}\NSIS\makensis.exe"
)

$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..\DuoDesk.csproj')).Path
if (-not (Test-Path -LiteralPath $NsisCompiler)) {
    throw 'NSIS was not found. Install NSIS or specify -NsisCompiler with the path to makensis.exe.'
}
New-Item -ItemType Directory -Force -Path $OutputDirectory, $PublishDirectory | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path

& dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }

$setup = Join-Path $output 'HY-Duo-WhatsApp-Setup.exe'
& $NsisCompiler "-DPUBLISH_DIR=$publish" "-DSETUP_OUT=$setup" (Join-Path $PSScriptRoot 'HYDuoWhatsApp.nsi')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Write-Host "Installer created: $setup"
