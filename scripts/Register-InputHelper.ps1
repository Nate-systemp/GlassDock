# Register only the protected helper. NEVER register an elevated on-demand task
# against user-writable source, Debug, publish or Velopack current folders.
#Requires -RunAsAdministrator
[CmdletBinding()]
param([Parameter(Mandatory)][string]$HelperPath)

$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess) { throw 'Run this from 64-bit Administrator PowerShell.' }
$expected = [IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'Doky\InputHelper\GlassDock.InputHelper.exe'))
$actual = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($HelperPath))
if (-not [string]::Equals($actual, $expected, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to elevate a user-writable helper. Run Install-InputHelper.ps1 to install into $expected first."
}
$helper = Get-Item -LiteralPath $actual -ErrorAction Stop
if ($helper.PSIsContainer -or $helper.Name -cne 'GlassDock.InputHelper.exe') {
    throw 'Protected GlassDock.InputHelper.exe is missing.'
}
if (-not (Test-Path -LiteralPath (Join-Path $helper.DirectoryName 'GlassDock.Windows.dll') -PathType Leaf)) {
    throw 'Protected helper is incomplete. Re-run Install-InputHelper.ps1.'
}

$taskName = 'Doky Input Helper'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$old = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($old -and $old.State -eq 'Running') {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction Stop
}
$action = New-ScheduledTaskAction -Execute $helper.FullName -WorkingDirectory $helper.DirectoryName
$principal = New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
# On-demand only. The normal Doky application runs the task after opening its pipe.
Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null
$check = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
if (-not [string]::Equals($check.Actions[0].Execute.Trim('"'), $helper.FullName,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Task registration returned an unexpected executable path.'
}
Write-Host "REGISTERED: $taskName" -ForegroundColor Green
Write-Host "EXECUTABLE: $($check.Actions[0].Execute)"
Write-Host "RUN LEVEL: $($check.Principal.RunLevel)"
Write-Host 'Restart Doky normally. No UAC is needed on normal launches.'
