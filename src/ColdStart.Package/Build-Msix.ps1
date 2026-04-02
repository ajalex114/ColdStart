# Build-Msix.ps1 — Packages published ColdStart output into an MSIX
# Usage: .\Build-Msix.ps1 -Version 1.0.0 [-CertPath .\cert.pfx -CertPassword secret]
#
# Prerequisites: Windows SDK (for makeappx.exe and signtool.exe)

param(
    [Parameter(Mandatory)][string]$Version,
    [string]$PublishDir = "..\ColdStart\bin\publish",
    [string]$OutputDir = "..\..\dist",
    [string]$CertPath,
    [string]$CertPassword
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $ScriptDir

# Resolve paths
$PublishDir = Resolve-Path $PublishDir
$OutputDir = New-Item -ItemType Directory -Force -Path $OutputDir | Select-Object -ExpandProperty FullName

Write-Host "=== Building MSIX for ColdStart v$Version ===" -ForegroundColor Cyan

# Find Windows SDK tools
$sdkPaths = @(
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64",
    "${env:ProgramFiles}\Windows Kits\10\bin\*\x64"
)
$makeappx = Get-ChildItem $sdkPaths -Filter "makeappx.exe" -ErrorAction SilentlyContinue |
    Sort-Object { [version]($_.FullName -replace '.*\\(\d+\.\d+\.\d+\.\d+)\\.*','$1') } -Descending |
    Select-Object -First 1

if (-not $makeappx) {
    Write-Error "makeappx.exe not found. Install the Windows SDK."
    exit 1
}
Write-Host "Using: $($makeappx.FullName)"

$signtool = Join-Path $makeappx.DirectoryName "signtool.exe"

# Create AppxManifest with correct version
$fourPartVersion = "$Version.0"
$manifestTemplate = Get-Content "Package.appxmanifest" -Raw
$manifest = $manifestTemplate -replace 'Version="1\.0\.0\.0"', "Version=`"$fourPartVersion`""

# Create staging directory
$staging = Join-Path $env:TEMP "ColdStart-msix-staging"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null

# Copy published files
Write-Host "Copying published files..."
Copy-Item "$PublishDir\*" $staging -Recurse -Force

# Write manifest
$manifest | Set-Content "$staging\AppxManifest.xml" -Encoding UTF8

# Copy images
if (Test-Path "Images") {
    New-Item -ItemType Directory -Force -Path "$staging\Images" | Out-Null
    Copy-Item "Images\*" "$staging\Images\" -Force
}

# Create .pri stub (required for MSIX)
# For a basic WPF app without Store assets, we generate a minimal resources.pri
$priConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<PriInfo>
  <ResourceMap name="ColdStart" />
</PriInfo>
"@

# Pack MSIX
$msixPath = Join-Path $OutputDir "ColdStart-$Version.msix"
Write-Host "Packing MSIX..."
& $makeappx.FullName pack /d $staging /p $msixPath /o
if ($LASTEXITCODE -ne 0) { Write-Error "makeappx pack failed"; exit 1 }

# Sign if certificate provided
if ($CertPath -and (Test-Path $CertPath)) {
    Write-Host "Signing MSIX..."
    $signArgs = @("sign", "/fd", "SHA256", "/f", $CertPath)
    if ($CertPassword) { $signArgs += @("/p", $CertPassword) }
    $signArgs += @("/t", "http://timestamp.digicert.com", $msixPath)
    & $signtool @signArgs
    if ($LASTEXITCODE -ne 0) { Write-Warning "Signing failed — MSIX created but unsigned" }
} else {
    Write-Host "No certificate provided — MSIX is unsigned (sideloading requires certificate)" -ForegroundColor Yellow
}

# Cleanup
Remove-Item $staging -Recurse -Force

Write-Host "`n✅ MSIX created: $msixPath" -ForegroundColor Green
Pop-Location
