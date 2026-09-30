# One-time admin operation (also repeat after a release changes helper code).
# Publish the standalone helper into the HelperPayload subfolder first.
# WARNING: An elevated scheduled task must not run EXEs from per-user writable
# LocalAppData or a development directory. Copy the standalone runtime into
# protected Program Files, then register only THAT executable.
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$SourceDirectory = (Join-Path $PSScriptRoot 'HelperPayload')
)
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess) { throw 'Run this from 64-bit Administrator PowerShell.' }
$source = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($SourceDirectory))
$required = @(
    'GlassDock.InputHelper.exe',
    'GlassDock.InputHelper.dll',
    'GlassDock.InputHelper.deps.json',
    'GlassDock.InputHelper.runtimeconfig.json',
    'GlassDock.Windows.dll',
    'GlassDock.Core.dll'
)
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) {
        throw "Standalone helper publish is incomplete: $source\$name"
    }
}
$parent = Join-Path $env:ProgramFiles 'Doky'
$destination = Join-Path $parent 'InputHelper'
if ([string]::Equals($source.TrimEnd('\'), $destination.TrimEnd('\'),
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Source and destination must be different.'
}
$backup = Join-Path $parent ('InputHelper-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$task = Get-ScheduledTask -TaskName 'Doky Input Helper' -ErrorAction SilentlyContinue
if ($task -and $task.State -eq 'Running') {
    Stop-ScheduledTask -TaskName 'Doky Input Helper' -ErrorAction Stop
}
# A previous release may have registered this HIGH-PRIVILEGE task against a
# user-writable EXE. Remove that unsafe task even if later installation fails.
$protectedExe = Join-Path $destination 'GlassDock.InputHelper.exe'
if ($task -and -not [string]::Equals($task.Actions[0].Execute.Trim('"'), $protectedExe,
        [StringComparison]::OrdinalIgnoreCase)) {
    Unregister-ScheduledTask -TaskName 'Doky Input Helper' -Confirm:$false -ErrorAction Stop
}
$deadline = (Get-Date).AddSeconds(10)
while ((Get-Process 'GlassDock.InputHelper' -ErrorAction SilentlyContinue) -and
        (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
if (Get-Process 'GlassDock.InputHelper' -ErrorAction SilentlyContinue) {
    throw 'Close every Doky instance and stop the old helper before installing.'
}

# We never overwrite an active protected payload in place. Rename it so a
# failed copy/registration can restore the earlier installed helper.
New-Item -Path $parent -ItemType Directory -Force | Out-Null
if (Test-Path -LiteralPath $destination) {
    Move-Item -LiteralPath $destination -Destination $backup -ErrorAction Stop
}
try {
    New-Item -Path $destination -ItemType Directory -Force | Out-Null
    # Well-known SIDs: SYSTEM/Administrators full, Users read+execute only.
    # This also removes any unsafe inheritable write access on the new folder.
    & icacls.exe $destination /inheritance:r /grant:r `
        '*S-1-5-18:(OI)(CI)F' `
        '*S-1-5-32-544:(OI)(CI)F' `
        '*S-1-5-32-545:(OI)(CI)RX' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not secure the helper installation directory.' }

    Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force -ErrorAction Stop
    }
    # Verify the running binary really is what the admin selected before
    # granting a persistent elevated task. Directory ACLs block later edits.
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $source 'GlassDock.InputHelper.exe') -Algorithm SHA256).Hash
    $installedHash = (Get-FileHash -LiteralPath (Join-Path $destination 'GlassDock.InputHelper.exe') -Algorithm SHA256).Hash
    if ($sourceHash -ne $installedHash) { throw 'Helper copy verification failed.' }

    $register = Join-Path $PSScriptRoot 'Register-InputHelper.ps1'
    if (-not (Test-Path -LiteralPath $register -PathType Leaf)) {
        throw 'Register-InputHelper.ps1 is missing next to this installer script.'
    }
    & $register -HelperPath (Join-Path $destination 'GlassDock.InputHelper.exe')
    if ($null -ne $backup -and (Test-Path -LiteralPath $backup)) {
        Remove-Item -LiteralPath $backup -Recurse -Force
    }
    Write-Host 'PROTECTED INPUT HELPER INSTALLED.' -ForegroundColor Green
}
catch {
    $failed = $_
    if (Test-Path -LiteralPath $destination) {
        Remove-Item -LiteralPath $destination -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $backup) {
        Move-Item -LiteralPath $backup -Destination $destination -ErrorAction SilentlyContinue
        $oldHelper = Join-Path $destination 'GlassDock.InputHelper.exe'
        if (Test-Path -LiteralPath $oldHelper) {
            try { & (Join-Path $PSScriptRoot 'Register-InputHelper.ps1') -HelperPath $oldHelper }
            catch { Write-Warning 'Rollback copied the old helper; its task may need manual repair.' }
        }
    }
    throw $failed
}
