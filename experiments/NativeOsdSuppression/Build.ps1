[CmdletBinding()]
param([switch]$RunFixtures)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$output = Join-Path $repo 'artifacts\native-osd-poc'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = if (Test-Path -LiteralPath $vswhere) { & $vswhere -prerelease -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath }
if (-not $vs) {
    $compiler = rg --files (Join-Path $env:ProgramFiles 'Microsoft Visual Studio') | Where-Object { $_ -match '\\VC\\Tools\\MSVC\\[^\\]+\\bin\\Hostx64\\x64\\cl\.exe$' } | Sort-Object -Descending | Select-Object -First 1
} else {
    $compiler = Get-ChildItem -LiteralPath (Join-Path $vs 'VC\Tools\MSVC') -Directory | Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'bin\Hostx64\x64\cl.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $compiler) { throw 'An installed MSVC x64 toolset is required; this script installs nothing.' }
$toolset = [IO.Path]::GetFullPath((Join-Path (Split-Path $compiler) '..\..\..'))
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdk = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory | Sort-Object Name -Descending | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'um\Windows.h') } | Select-Object -First 1
if (-not $sdk) { throw 'Installed Windows SDK headers are required.' }
$oldPath = $env:PATH; $oldInclude = $env:INCLUDE; $oldLib = $env:LIB
try {
    $env:PATH = (Split-Path $compiler) + ';' + $env:PATH
    $env:INCLUDE = (@((Join-Path $toolset 'include')) + @('ucrt','shared','um','winrt' | ForEach-Object { Join-Path $sdk.FullName $_ })) -join ';'
    $env:LIB = (@((Join-Path $toolset 'lib\x64')) + @('ucrt\x64','um\x64' | ForEach-Object { Join-Path (Join-Path $sdkRoot ('Lib\' + $sdk.Name)) $_ })) -join ';'
    & $compiler /nologo /std:c++20 /EHsc /W4 /WX /permissive- /MD /Od /DUNICODE /D_UNICODE (Join-Path $PSScriptRoot 'NativeOsdLab.cpp') "/Fo$output\NativeOsdLab.obj" "/Fe$output\NativeOsdLab.exe" /link /INCREMENTAL:NO user32.lib gdi32.lib
    if ($LASTEXITCODE -ne 0) { throw 'Native lab compilation failed.' }
    Write-Output ('BUILT: ' + (Join-Path $output 'NativeOsdLab.exe'))
    & $compiler /nologo /std:c++20 /EHsc /W4 /WX /permissive- /MD /Od /DUNICODE /D_UNICODE (Join-Path $PSScriptRoot 'ObservationTests.cpp') "/Fo$output\ObservationTests.obj" "/Fe$output\ObservationTests.exe" /link /INCREMENTAL:NO user32.lib version.lib
    if ($LASTEXITCODE -ne 0) { throw 'Read-only observation fixture compilation failed.' }
    & $compiler /nologo /c /std:c++20 /EHsc /W4 /WX /permissive- /MD /DUNICODE /D_UNICODE (Join-Path $PSScriptRoot 'AdapterCompileCheck.cpp') "/Fo$output\AdapterCompileCheck.obj"
    if ($LASTEXITCODE -ne 0) { throw 'Adapter syntax check failed (this is not a Windhawk SDK build).' }
    $policy = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'PolicyHooks.hpp') -Raw).Replace('#pragma once', '')
    $identity = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'NativeShellIdentity.hpp') -Raw).Replace('#pragma once', '').Replace('#include "PolicyHooks.hpp"', '')
    $adapter = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'explorer-prototype.wh.cpp') -Raw).Replace('#include "PolicyHooks.hpp"', $policy).Replace('#include "NativeShellIdentity.hpp"', $identity)
    $observation = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ObservationHooks.hpp') -Raw).Replace('#pragma once', '').Replace('#include "NativeShellIdentity.hpp"', '')
    $adapter = $adapter.Replace('#include "ObservationHooks.hpp"', $observation)
    $adapter | Set-Content -LiteralPath (Join-Path $output 'doky-native-osd-prototype.wh.cpp') -Encoding utf8
    Write-Output 'EXPORTED SOURCE ONLY: adapter syntax-checked with a signature shim, not built with Windhawk or executed in Explorer.'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $output 'THIRD_PARTY_NOTICES.md') -Force

    if ($RunFixtures) {
        & (Join-Path $output 'ObservationTests.exe') | Tee-Object -FilePath (Join-Path $output 'observation-test.log')
        if ($LASTEXITCODE -ne 0) { throw 'Read-only observation fixtures failed.' }
        foreach ($mode in '--external-test','--policy-test') {
            & (Join-Path $output 'NativeOsdLab.exe') $mode | Tee-Object -FilePath (Join-Path $output ($mode.TrimStart('-') + '.log'))
            if ($LASTEXITCODE -ne 0) { throw "Owned fixture mode failed: $mode" }
        }
    }
} finally { $env:PATH = $oldPath; $env:INCLUDE = $oldInclude; $env:LIB = $oldLib }
