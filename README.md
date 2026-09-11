# GlassDock
A cleaner way to use Windows.

Seven C# projects with a standalone Glass Material Laboratory, architecture boundaries, tests and documentation.

## Development
Windows x64, .NET SDK 10.0.401 and Windows SDK 10.0.26100.0.
Use Visual Studio with WinUI development tools for the full IDE experience; VS Code is suitable for editing. No Visual Studio installation was detected on this machine; CLI builds are validated separately.
Run from the repository root:
```powershell
./scripts/Validate.ps1
dotnet run --project src/GlassDock.App
dotnet run --project src/GlassDock.Watchdog
```
App opens the Glass Material Laboratory. Compare Frosted, Clear and Refractive presets and five test scenes. Changes are session-only; close the window normally. Watchdog currently returns 0.
NuGet restore requires internet access for development dependencies; application networking is absent.
Use GlassDock.sln in Visual Studio. The current development app is x64 and unpackaged.

Read [product specification](PRODUCT_SPEC.md), [architecture](ARCHITECTURE.md), [roadmap](ROADMAP.md), and [validation](docs/PHASE_0_VALIDATION.md).
Phase 1 has native in-app glass effects. Refraction is an explicitly labelled lighting approximation. No shell behavior or commercial services are part of the laboratory.
