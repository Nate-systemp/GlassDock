$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "Building Doky..." -ForegroundColor Cyan
dotnet build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`nRunning tests..." -ForegroundColor Cyan
dotnet test
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`nBuild and tests passed. Runtime validation is still required." -ForegroundColor Green
