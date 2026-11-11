[CmdletBinding()]
param()
$output = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\artifacts\native-osd-poc'))
New-Item -ItemType File -Path (Join-Path $output 'stop-brightness-bridge') -Force | Out-Null
$windhawk = Join-Path $output 'windhawk-portable\windhawk.exe'
$config = Join-Path $output 'windhawk-portable\AppData\Engine\Mods\doky-brightness-osd.ini'
if (Test-Path -LiteralPath $config) {
    (Get-Content -LiteralPath $config -Raw).Replace('Disabled=0','Disabled=1').Replace('enabled=1','enabled=0') | Set-Content -LiteralPath $config
}
if (Test-Path -LiteralPath $windhawk) { & $windhawk -exit -wait -timeout 15000 }
