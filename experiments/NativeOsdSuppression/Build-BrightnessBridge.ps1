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
Write-Output (Join-Path $mods 'doky-brightness-osd-0.1.0.dll')
