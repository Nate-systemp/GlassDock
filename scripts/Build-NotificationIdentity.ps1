param(
    [string]$CertificateThumbprint,
    [string]$AppDirectory,
    [switch]$Register,
    [switch]$DevelopmentLocation
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$sdk = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Directory |
    Sort-Object Name -Descending | Where-Object { Test-Path (Join-Path $_.FullName 'x64\makeappx.exe') } | Select-Object -First 1
if (!$sdk) { throw 'Windows SDK MakeAppx is required.' }
$output = Join-Path $repo 'artifacts\notification-identity'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$package = Join-Path $output 'Doky.NotificationIdentity.msix'
& (Join-Path $sdk.FullName 'x64\makeappx.exe') pack /o /nv /d (Join-Path $repo 'packaging\NotificationIdentity') /p $package
if ($LASTEXITCODE -ne 0) { throw 'Identity package build failed.' }
if ($CertificateThumbprint) {
    $cert = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
    if ($cert.Subject -ne 'CN=Natesystemp' -or !$cert.HasPrivateKey) { throw 'A signing certificate with subject CN=Natesystemp and private key is required.' }
    & (Join-Path $sdk.FullName 'x64\signtool.exe') sign /fd SHA256 /sha1 $CertificateThumbprint $package
    if ($LASTEXITCODE -ne 0) { throw 'Identity package signing failed.' }
}
if ($Register) {
    if (!$CertificateThumbprint) { throw 'Registration requires a signed package trusted by Windows. No trust or security settings are modified by this script.' }
    $installedApp = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Natesystemp.GlassDock\current'
    if (!$AppDirectory) { $AppDirectory = $installedApp }
    $resolvedApp = (Resolve-Path -LiteralPath $AppDirectory).Path
    if (!$DevelopmentLocation -and $resolvedApp.TrimEnd('\') -ne $installedApp.TrimEnd('\')) {
        throw 'Production notification identity must point to the existing Velopack current directory. Use -DevelopmentLocation only for an explicit development registration.'
    }
    if (!(Test-Path -LiteralPath (Join-Path $resolvedApp 'GlassDock.App.exe'))) { throw 'AppDirectory does not contain GlassDock.App.exe.' }
    Add-AppxPackage -Path $package -ExternalLocation $resolvedApp
    $registered = Get-AppxPackage -Name 'Natesystemp.Doky.NotificationIdentity'
    $registration = Get-ItemProperty -LiteralPath "HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages\$($registered.PackageFullName)"
    if ($registration.PackageRootFolder.TrimEnd('\') -ne $resolvedApp.TrimEnd('\')) {
        throw 'Windows did not register the requested external location; do not launch the package entry.'
    }
    Write-Output "Verified external executable: $(Join-Path $resolvedApp 'GlassDock.App.exe')"
}
Write-Output "Identity package: $package"
if (!$CertificateThumbprint) { Write-Warning 'Unsigned build artifact only; sign with a trusted CN=Natesystemp certificate before registering.' }
