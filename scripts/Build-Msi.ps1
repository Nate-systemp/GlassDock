param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.3'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Root = Split-Path -Parent $PSScriptRoot
$PublishDir = Join-Path $Root 'publish\GlassDock'
$DistDir = Join-Path $Root 'dist'
$WixProject = Join-Path $Root 'installer\wix\GlassDock.Installer.wixproj'
$FinalMsi = Join-Path $DistDir 'GlassDock-Setup-x64.msi'
$packageVersion = [version]$Version
if ($packageVersion.Major -gt 255 -or $packageVersion.Minor -gt 255 -or $packageVersion.Build -gt 65535) {
    throw 'MSI version must be major.minor.patch within 255.255.65535.'
}
function Run-Step([string]$Name, [scriptblock]$Command) {
    Write-Host $Name
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE." }
}
function Clear-OutputDirectory([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside the repository: $full"
    }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
    New-Item -ItemType Directory -Path $full -Force | Out-Null
}
if (Get-Process GlassDock.App,GlassDock.Watchdog -ErrorAction SilentlyContinue) {
    throw 'Exit GlassDock normally before building the installer.'
}
# Remove only this build's outputs, not unrelated distribution artifacts.
Clear-OutputDirectory $PublishDir
Clear-OutputDirectory (Join-Path $Root 'installer\wix\obj')
Clear-OutputDirectory (Join-Path $Root 'installer\wix\bin')
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null
if (Test-Path -LiteralPath $FinalMsi) { Remove-Item -LiteralPath $FinalMsi -Force }
Run-Step 'Validate solution (locked restore, Release build, both test projects)' {
    powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root 'scripts\Validate.ps1')
}
Run-Step 'Publish self-contained x64 application' {
    dotnet publish (Join-Path $Root 'src\GlassDock.App\GlassDock.App.csproj') -c Release -r win-x64 --self-contained true -p:NuGetLockFilePath=obj/packages.publish.lock.json -o $PublishDir
}
# RID-specific restore has its own obj lock file; never rewrite solution lock files.
# Publish the recovery executable itself: a build output is not a deployment payload.
Clear-OutputDirectory (Join-Path $PublishDir 'Recovery')
Run-Step 'Publish self-contained x64 watchdog' {
    dotnet publish (Join-Path $Root 'src\GlassDock.Watchdog\GlassDock.Watchdog.csproj') -c Release -r win-x64 --self-contained true -p:NuGetLockFilePath=obj/packages.publish.lock.json -o (Join-Path $PublishDir 'Recovery')
}
foreach ($file in @('GlassDock.App.exe','GlassDock.App.dll','GlassDock.App.pri','coreclr.dll','hostfxr.dll','Microsoft.ui.xaml.dll','Recovery\GlassDock.Watchdog.exe','Recovery\GlassDock.Watchdog.dll','Recovery\coreclr.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PublishDir $file))) { throw "Missing publish dependency: $file" }
}
Run-Step 'Restore WiX 7' { dotnet restore $WixProject --locked-mode }
Run-Step 'Build fresh x64 MSI' {
    dotnet build $WixProject -c Release --no-restore -p:InstallerVersion=$Version
}
$builtMsi = Join-Path $Root 'installer\wix\bin\x64\Release\GlassDock-Setup-x64.msi'
if (-not (Test-Path -LiteralPath $builtMsi)) { throw "Missing expected MSI: $builtMsi" }
Copy-Item -LiteralPath $builtMsi -Destination $FinalMsi -Force
# This manifest lets deployment verification compare EVERY installed byte to this publish.
$manifest = @(Get-ChildItem -LiteralPath $PublishDir -File -Recurse | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{ Path=$_.FullName.Substring($PublishDir.Length + 1); SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $DistDir 'GlassDock-payload.sha256.json') -Encoding UTF8
Write-Host "Created $FinalMsi ($($manifest.Count) published files), version $Version"

