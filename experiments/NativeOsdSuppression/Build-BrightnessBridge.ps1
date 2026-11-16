[CmdletBinding()]
param([string]$WindhawkPath = (Join-Path $PSScriptRoot '..\..\artifacts\native-osd-poc\windhawk-portable'))
$ErrorActionPreference = 'Stop'
$WindhawkPath = [IO.Path]::GetFullPath($WindhawkPath)
$compiler = Join-Path $WindhawkPath 'Compiler\bin\clang++.exe'
$engine = Join-Path $WindhawkPath 'Engine\1.7.3\64\windhawk.lib'
if (!(Test-Path -LiteralPath $compiler) -or !(Test-Path -LiteralPath $engine)) { throw 'The reviewed portable Windhawk 1.7.3 SDK is required.' }
$mods = Join-Path $WindhawkPath 'AppData\Engine\Mods\64'
New-Item -ItemType Directory -Path $mods -Force | Out-Null
$options = @(
    '-std=c++20', '-O2', '-shared', '-DUNICODE', '-D_UNICODE', '-DWINVER=0x0A00', '-D_WIN32_WINNT=0x0A00',
    '-D_WIN32_IE=0x0A00', '-DNTDDI_VERSION=0x0A000008', '-D__USE_MINGW_ANSI_STDIO=0', '-DWH_MOD',
    '-DWH_MOD_ID=L"doky-brightness-osd"', '-DWH_MOD_VERSION=L"0.1.0"', $engine,
    (Join-Path $PSScriptRoot 'brightness-suppression.wh.cpp'), '-include', 'windhawk_api.h',
    '-I', (Join-Path $WindhawkPath 'Compiler\include'), '-target', 'x86_64-w64-mingw32',
    '-Wl,--export-all-symbols', '-luser32', '-lversion', '-lbcrypt', '-Wall', '-Wextra', '-Werror',
    '-o', (Join-Path $mods 'doky-brightness-osd-0.1.0.dll')
)
& $compiler @options
if ($LASTEXITCODE -ne 0) { throw 'Brightness bridge compilation failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $mods 'doky-brightness-THIRD_PARTY_NOTICES.md') -Force
$runtime = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\artifacts\native-osd-runtime'))
$runtimeEngine = Join-Path $runtime 'Engine\1.7.3'
$runtimeMods = Join-Path $runtime 'AppData\Engine\Mods\64'
New-Item -ItemType Directory -Path $runtimeEngine,$runtimeMods -Force | Out-Null
foreach ($name in @('windhawk.exe','windhawk-x64-helper.exe','windhawk.ini')) {
    Copy-Item -LiteralPath (Join-Path $WindhawkPath $name) -Destination $runtime -Force
}
foreach ($architecture in @('32','64')) {
    Copy-Item -LiteralPath (Join-Path $WindhawkPath "Engine\1.7.3\$architecture") -Destination $runtimeEngine -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $WindhawkPath 'Engine\1.7.3\engine.ini') -Destination $runtimeEngine -Force
foreach ($name in @('doky-brightness-osd-0.1.0.dll','libc++.whl','libunwind.whl','windhawk-mod-shim.dll','doky-brightness-THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $mods $name) -Destination $runtimeMods -Force
}
"[Settings]`nLoggingVerbosity=1`nExclude=*`nInjectIntoCriticalProcesses=0`nInjectIntoIncompatiblePrograms=0`nInjectIntoGames=0" |
    Set-Content -LiteralPath (Join-Path $runtime 'AppData\Engine\settings.ini')
"[Mod]`nLibraryFileName=doky-brightness-osd-0.1.0.dll`nDisabled=1`n[Settings]`nenabled=0" |
    Set-Content -LiteralPath (Join-Path $runtime 'AppData\Engine\Mods\doky-brightness-osd.ini')
"[Settings]`nLanguage=en`nLoggingVerbosity=0`nHideTrayIcon=0`nSafeMode=0`nDisableUpdateCheck=1" |
    Set-Content -LiteralPath (Join-Path $runtime 'AppData\settings.ini')
Write-Output (Join-Path $mods 'doky-brightness-osd-0.1.0.dll')
