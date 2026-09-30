#Requires -RunAsAdministrator
[CmdletBinding()]
param([Parameter(Mandatory)][string]$HelperPath)
$ErrorActionPreference = 'Stop'
$helper = Get-Item -LiteralPath $HelperPath
if ($helper.PSIsContainer -or $helper.Name -ne 'GlassDock.InputHelper.exe') {
    throw 'Choose the installed GlassDock.InputHelper.exe.'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$action = New-ScheduledTaskAction -Execute $helper.FullName -WorkingDirectory $helper.DirectoryName
$principal = New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
# On-demand only: the normal app starts this task after opening its pipe server.
Register-ScheduledTask -TaskName 'Doky Input Helper' -Action $action -Principal $principal -Settings $settings -Force | Out-Null
Write-Output 'Doky input helper registered. Normal Doky startup does not request elevation.'
