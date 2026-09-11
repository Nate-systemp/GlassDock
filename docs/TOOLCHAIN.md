# Toolchain decisions
Verified 2026-09-11:
- Installed .NET SDK 10.0.401 matches the stable .NET 10 SDK: https://dotnet.microsoft.com/en-us/download
- Windows App SDK 2.4.0 is stable: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads
- Windows SDK 10.0.26100.0 is installed locally.
Direct packages are pinned and transitive graphs are committed in packages.lock.json. Update deliberately and validate before committing lock changes.
No workloads or IDE were installed by this task. NuGet uses its standard per-user cache.
The first dotnet template invocation reported creating an ASP.NET Core HTTPS development certificate automatically. It is unrelated to GlassDock, was not trusted by this task, and is never included in the repository. Subsequent validation disables CLI telemetry and first-run certificate generation through process environment variables.
