$ErrorActionPreference = 'Stop'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Run-BrightnessBridge.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Bridge runner failed parsing.' }
$writer = $ast.Find({ param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Write-BridgeStatus'
}, $true)
if (-not $writer) { throw 'Bridge status writer unavailable.' }
Invoke-Expression $writer.Extent.Text
$statusFile = Join-Path ([IO.Path]::GetTempPath()) ('doky-status-test-' + [guid]::NewGuid().ToString('N') + '.json')
try {
    foreach ($record in @(@{ Loaded = $true }, @{ SuppressionEnabled = $false })) {
        $locked = [IO.File]::Open($statusFile, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        try { Write-BridgeStatus $record } finally { $locked.Dispose() }
        Write-BridgeStatus $record
        $actual = Get-Content -LiteralPath $statusFile -Raw | ConvertFrom-Json -AsHashtable
        if ($actual.Count -ne 1 -or $actual[$record.Keys[0]] -ne $record[$record.Keys[0]]) { throw 'Status writes did not recover after contention.' }
    }
    & (Join-Path $PSScriptRoot 'Run-BrightnessBridge.ps1') -Enable -DokyPath (Join-Path $env:SystemRoot 'System32\cmd.exe') -DokyArguments @('/d','/c','exit 0')
    'PASS status-file contention recovers and the default launcher returns without persistent supervision.'
} finally { Remove-Item -LiteralPath $statusFile -ErrorAction SilentlyContinue }
