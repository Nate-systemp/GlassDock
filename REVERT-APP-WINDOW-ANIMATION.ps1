$ErrorActionPreference = "Stop"

$repo = "C:\Dev\GlassDock"

Remove-Item "$repo\src\GlassDock.App\Desktop\ApplicationWindowTransition.cs" `
    -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Application-window transition file removed." -ForegroundColor Green
Write-Host "The original files from this ZIP should already have replaced the patched versions."
Write-Host ""
Write-Host "Now run:"
Write-Host "  cd C:\Dev\GlassDock"
Write-Host "  dotnet build"
Write-Host "  dotnet test"
