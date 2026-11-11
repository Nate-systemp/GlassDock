[CmdletBinding()]
param(
    [switch]$Enable,
    [switch]$EnableDnd,
    [switch]$EnableEnergySaver,
    [string]$DokyPath = (Join-Path $PSScriptRoot '..\..\artifacts\brightness-suppression-build\bin\GlassDock.App\debug_win-x64\GlassDock.App.exe'),
    [string]$WindhawkPath = (Join-Path $PSScriptRoot '..\..\artifacts\native-osd-poc\windhawk-portable')
)
$ErrorActionPreference = 'Stop'
if (!$Enable) { throw 'Explicit opt-in required: -Enable.' }
$DokyPath = [IO.Path]::GetFullPath($DokyPath)
$WindhawkPath = [IO.Path]::GetFullPath($WindhawkPath)
$output = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\artifacts\native-osd-poc'))
$stopFile = Join-Path $output 'stop-brightness-bridge'
$statusFile = Join-Path $output 'brightness-active.json'
$modConfig = Join-Path $WindhawkPath 'AppData\Engine\Mods\doky-brightness-osd.ini'
$engineConfig = Join-Path $WindhawkPath 'AppData\Engine\settings.ini'
if (!(Test-Path -LiteralPath $DokyPath)) { throw 'Build Doky first.' }
if (!(Test-Path -LiteralPath (Join-Path $WindhawkPath 'AppData\Engine\Mods\64\doky-brightness-osd-0.1.0.dll'))) { throw 'Build-BrightnessBridge.ps1 is required first.' }
if (Get-Process GlassDock.App -ErrorAction SilentlyContinue) { throw 'Exit the existing Doky instance before this opt-in launch.' }
if (Get-Process windhawk -ErrorAction SilentlyContinue) { throw 'Exit the existing Windhawk instance before this isolated opt-in launch.' }
foreach ($other in Get-ChildItem (Join-Path $WindhawkPath 'AppData\Engine\Mods') -Filter '*.ini') {
    if ($other.FullName -ne $modConfig -and (Get-Content -LiteralPath $other.FullName -Raw) -match '(?m)^Disabled=0\s*$') {
        throw "Another mod is enabled: $($other.Name). Disable it before this isolated test."
    }
}
if (Test-Path -LiteralPath $stopFile) { Remove-Item -LiteralPath $stopFile }
$originalEngineConfig = Get-Content -LiteralPath $engineConfig -Raw
function Wait-BrightnessModule([int]$ShellPid) {
    $deadline = [Environment]::TickCount64 + 15000
    do {
        $shell = Get-Process -Id $ShellPid
        if (@($shell.Modules | Where-Object ModuleName -eq 'doky-brightness-osd-0.1.0.dll').Count -eq 1) { return $shell }
        Start-Sleep -Milliseconds 250
    } while ([Environment]::TickCount64 -lt $deadline)
    throw 'Brightness module did not load within 15 seconds. Check pinned binaries and live-process gates.'
}
$bridgeCreated = (Get-Process -Id $PID).StartTime.ToUniversalTime().ToFileTimeUtc()
$app = $null; $view = $null; $map = $null; $engineStarted = $false
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DokyBrightnessShellIdentity {
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    public static uint Pid() { uint pid; GetWindowThreadProcessId(GetShellWindow(), out pid); return pid; }
}
'@
try {
    $info = [Diagnostics.ProcessStartInfo]::new($DokyPath)
    $info.UseShellExecute = $false
    $info.WorkingDirectory = Split-Path $DokyPath
    $info.Environment['DOKY_ENABLE_EXPERIMENTAL_BRIGHTNESS_OSD'] = '1'
    if ($EnableDnd) { $info.Environment['DOKY_ENABLE_EXPERIMENTAL_DND'] = '1' }
    if ($EnableEnergySaver) { $info.Environment['DOKY_ENABLE_EXPERIMENTAL_ENERGY_SAVER'] = '1' }
    $info.Environment['DOKY_BRIGHTNESS_OSD_BRIDGE_PID'] = "$PID"
    $info.Environment['DOKY_BRIGHTNESS_OSD_BRIDGE_CREATED'] = "$bridgeCreated"
    $app = [Diagnostics.Process]::Start($info)
    $until = [Environment]::TickCount64 + 30000
    do {
        if ($app.HasExited) { throw 'Doky exited before brightness became available.' }
        try {
            if (!$map) {
                $map = [IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting("Local\Doky.BrightnessOsd.v1.$($app.SessionId)", [IO.MemoryMappedFiles.MemoryMappedFileRights]::Read)
                $view = $map.CreateViewAccessor(0, 128, [IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
            }
            $ready = $view.ReadInt32(12) -eq 1 -and $view.ReadInt32(16) -eq $app.Id -and $view.ReadInt64(32) -gt [Environment]::TickCount64
        } catch [IO.FileNotFoundException] { $ready = $false }
        if (!$ready) { Start-Sleep -Milliseconds 200 }
    } until ($ready -or [Environment]::TickCount64 -ge $until)
    if (!$ready) { throw 'No live WMI brightness/HUD lease became available within 30 seconds. Windows retains its popup.' }
    @"
[Settings]
LoggingVerbosity=1
Include=$env:windir\explorer.exe
Exclude=*
InjectIntoCriticalProcesses=0
InjectIntoIncompatiblePrograms=0
InjectIntoGames=0
"@ | Set-Content -LiteralPath $engineConfig
    @"
[Mod]
LibraryFileName=doky-brightness-osd-0.1.0.dll
Disabled=0
LoggingEnabled=1
DebugLoggingEnabled=0
Include=$env:windir\explorer.exe
Architecture=x86-64
Version=0.1.0
[Settings]
enabled=1
"@ | Set-Content -LiteralPath $modConfig
    $engineProcess = Start-Process (Join-Path $WindhawkPath 'windhawk.exe') -ArgumentList '-tray-only' -WindowStyle Hidden -PassThru
    $engineStarted = $true
    $shellProcess = Wait-BrightnessModule ([DokyBrightnessShellIdentity]::Pid())
    $loaded = $true
    while (!$app.HasExited -and !(Test-Path -LiteralPath $stopFile)) {
        if ($engineProcess.HasExited) { throw 'The Windhawk bridge exited. Native brightness handling is restored.' }
        $currentShell = [DokyBrightnessShellIdentity]::Pid()
        if ($currentShell -ne 0 -and $currentShell -ne $shellProcess.Id) {
            # Reload only the owned bridge after Windows independently recreates its Shell.
            & (Join-Path $WindhawkPath 'windhawk.exe') -exit -wait -timeout 15000
            $engineProcess = Start-Process (Join-Path $WindhawkPath 'windhawk.exe') -ArgumentList '-tray-only' -WindowStyle Hidden -PassThru
            $shellProcess = Wait-BrightnessModule $currentShell
            $loaded = $true
        }
        @{ DokyPid = $app.Id; BridgePid = $PID; ExplorerPid = $shellProcess.Id; Loaded = $loaded; SuppressedRequests = $view.ReadInt64(40); Updated = [DateTime]::UtcNow.ToString('o') } |
            ConvertTo-Json | Set-Content -LiteralPath $statusFile
        Start-Sleep -Seconds 1
    }
} finally {
    if (Test-Path -LiteralPath $modConfig) {
        (Get-Content -LiteralPath $modConfig -Raw).Replace('Disabled=0','Disabled=1').Replace('enabled=1','enabled=0') | Set-Content -LiteralPath $modConfig
    }
    if ($engineStarted) { & (Join-Path $WindhawkPath 'windhawk.exe') -exit -wait -timeout 15000 }
    $originalEngineConfig | Set-Content -LiteralPath $engineConfig
    if ($view) { $view.Dispose() }
    if ($map) { $map.Dispose() }
    @{ Stopped = [DateTime]::UtcNow.ToString('o'); SuppressionEnabled = $false } | ConvertTo-Json | Set-Content -LiteralPath $statusFile
}
