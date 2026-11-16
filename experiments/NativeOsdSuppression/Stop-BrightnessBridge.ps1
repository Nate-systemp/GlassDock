[CmdletBinding()]
param()
foreach ($app in Get-Process GlassDock.App -ErrorAction SilentlyContinue) {
    $mapping = $null
    try {
        $name = "Local\Doky.NativeOsd.v2.$($app.SessionId).$($app.Id).$($app.StartTime.ToUniversalTime().ToFileTimeUtc())"
        $mapping = [IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting($name, [IO.MemoryMappedFiles.MemoryMappedFileRights]::ReadWrite)
        $view = $mapping.CreateViewAccessor(0, 128, [IO.MemoryMappedFiles.MemoryMappedFileAccess]::ReadWrite)
        try { $view.Write(104, [int]1) } finally { $view.Dispose() }
        return
    } catch [IO.FileNotFoundException] { }
    finally { if ($mapping) { $mapping.Dispose() }; $app.Dispose() }
}
$output = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\artifacts\native-osd-poc'))
New-Item -ItemType File -Path (Join-Path $output 'stop-brightness-bridge') -Force | Out-Null
$windhawk = Join-Path $output 'windhawk-portable\windhawk.exe'
$config = Join-Path $output 'windhawk-portable\AppData\Engine\Mods\doky-brightness-osd.ini'
if (Test-Path -LiteralPath $config) {
    (Get-Content -LiteralPath $config -Raw).Replace('Disabled=0','Disabled=1').Replace('enabled=1','enabled=0') | Set-Content -LiteralPath $config
}
if (Test-Path -LiteralPath $windhawk) { & $windhawk -exit -wait -timeout 15000 }
