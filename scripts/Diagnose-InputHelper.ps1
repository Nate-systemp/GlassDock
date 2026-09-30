# No administrator rights or changes required. Reports why normal Doky may
# still be using its fallback hook instead of the elevated input helper.
[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads\Doky-InputHelper-Diagnostics.txt')
)
$ErrorActionPreference = 'Continue'
$report = New-Object System.Collections.Generic.List[string]
function Add-Line([string]$line) { $script:report.Add($line); Write-Host $line }

Add-Line '===== DOKY INPUT HELPER DIAGNOSTICS ====='
Add-Line "Time: $(Get-Date -Format o)"
Add-Line "User: $([Security.Principal.WindowsIdentity]::GetCurrent().Name)"
Add-Line ''
Add-Line '===== REGISTERED TASK ====='
$task = Get-ScheduledTask -TaskName 'Doky Input Helper' -ErrorAction SilentlyContinue
if ($null -eq $task) {
    Add-Line 'MISSING: Doky Input Helper scheduled task is not registered.'
} else {
    Add-Line "Task state: $($task.State)"
    Add-Line "Run level: $($task.Principal.RunLevel)"
    Add-Line "Principal: $($task.Principal.UserId)"
    foreach ($action in $task.Actions) {
        Add-Line "Execute: $($action.Execute)"
        Add-Line "Arguments: $($action.Arguments)"
        Add-Line "Working directory: $($action.WorkingDirectory)"
        $exe = [Environment]::ExpandEnvironmentVariables($action.Execute.Trim('"'))
        Add-Line "Registered helper exists: $(Test-Path -LiteralPath $exe -PathType Leaf)"
        $protected = Join-Path $env:ProgramFiles 'Doky\InputHelper\GlassDock.InputHelper.exe'
        Add-Line "Points to protected helper: $([string]::Equals([IO.Path]::GetFullPath($exe), [IO.Path]::GetFullPath($protected), [StringComparison]::OrdinalIgnoreCase))"
    }
    $status = Get-ScheduledTaskInfo -TaskName 'Doky Input Helper' -ErrorAction SilentlyContinue
    if ($status) {
        Add-Line "Last task result: $($status.LastTaskResult)"
        Add-Line "Last run time: $($status.LastRunTime)"
    }
}
Add-Line ''
Add-Line '===== RUNNING PROCESSES ====='
try {
    $processes = Get-CimInstance Win32_Process -Filter "Name='GlassDock.App.exe' OR Name='GlassDock.InputHelper.exe'" -ErrorAction Stop
    if (-not $processes) { Add-Line 'No Doky or InputHelper process is running.' }
    foreach ($p in $processes) {
        Add-Line "$($p.Name) PID=$($p.ProcessId) PATH=$($p.ExecutablePath)"
        if ($p.Name -eq 'GlassDock.App.exe' -and $p.ExecutablePath) {
            $expected = Join-Path (Split-Path $p.ExecutablePath -Parent) 'GlassDock.InputHelper.exe'
            Add-Line "Bundled helper next to running app (NOT an elevated task target): $expected"
            Add-Line "Bundled helper exists: $(Test-Path -LiteralPath $expected -PathType Leaf)"
        }
    }
} catch { Add-Line "Cannot read running process paths: $($_.Exception.Message)" }
Add-Line ''
Add-Line '===== PROTECTED HELPER PATH ====='
$installed = Join-Path $env:ProgramFiles 'Doky\InputHelper\GlassDock.InputHelper.exe'
Add-Line "Expected protected helper: $installed"
Add-Line "Exists: $(Test-Path -LiteralPath $installed -PathType Leaf)"
Add-Line ''
Add-Line '===== STARTUP LOG ====='
$log = Join-Path $env:LOCALAPPDATA 'Doky\input-helper.log'
if (Test-Path -LiteralPath $log) {
    Get-Content -LiteralPath $log -Tail 12 | ForEach-Object { Add-Line $_ }
} else { Add-Line 'No diagnostic log yet (older app build or helper never attempted).'}
Add-Line ''
Add-Line 'No automatic task repair was performed.'
$dir = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$report | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Host "`nREPORT SAVED: $OutputPath" -ForegroundColor Green
