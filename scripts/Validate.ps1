$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet restore GlassDock.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    dotnet build GlassDock.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    foreach ($project in 'Core', 'Windows') {
        dotnet test "tests/GlassDock.$project.Tests/GlassDock.$project.Tests.csproj" -c Release --no-build --no-restore
        if ($LASTEXITCODE -ne 0) { throw "$project tests failed." }
    }
} finally { Pop-Location }
