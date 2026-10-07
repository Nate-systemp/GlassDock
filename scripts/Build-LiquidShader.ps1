$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$sdk = 'C:\Program Files (x86)\Windows Kits\10'
$version = Get-ChildItem -LiteralPath (Join-Path $sdk 'bin') -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'x64\fxc.exe') } |
    Sort-Object Name -Descending | Select-Object -First 1
if (!$version) { throw 'Install the Windows SDK HLSL compiler (fxc.exe).' }
$shader = Join-Path $repo 'src\GlassDock.App\Rendering\Shaders\LiquidGlass'
& (Join-Path $version.FullName 'x64\fxc.exe') /nologo /T ps_4_0 /E main /D D2D_FULL_SHADER /D D2D_ENTRY=main /I (Join-Path $sdk "Include\$($version.Name)\um") /Fo "$shader.bin" "$shader.hlsl"
if ($LASTEXITCODE -ne 0) { throw 'Liquid Glass shader compilation failed.' }
