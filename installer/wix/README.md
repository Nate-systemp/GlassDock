# GlassDock MSI (WiX 7, x64)

Run `powershell -ExecutionPolicy Bypass -File scripts/Build-Msi.ps1` from the repository.
The default release is 0.1.3; pass `-Version major.minor.patch` for later releases.
Increase one of those three MSI version fields for each release. Do not reuse a
version for different released payloads. The permanent UpgradeCode is
`5fb9248e-3141-422b-927d-5a9a57810d4c`.

The MSI version is separate from the projects' assembly/file versions. Publishing
preserves those existing values (currently 1.0.0.0); never lower binary file
versions to match the pre-1.0 MSI version. Windows Installer can skip lower-version
components during upgrade and then remove their old files. Future project file
versions must also remain nondecreasing.

The script validates the solution, cleans only the app publish and WiX outputs,
publishes the app and watchdog self-contained for win-x64, and builds the exact
`dist/GlassDock-Setup-x64.msi` path. RID-specific restore locks live under obj so
publishing does not rewrite the normal solution lock files. The JSON hash manifest
beside the MSI describes every fresh publish file for installed-byte verification.

Install is per-machine under Program Files/GlassDock, with a common Start Menu
shortcut. The shortcut uses INSTALLFOLDER as its working directory. No user data
is installed or removed under LocalAppData. Major upgrades remove the prior
product inside the installation transaction; downgrades are blocked. Exit
GlassDock normally before maintenance so its watchdog restores the taskbar and
no running app locks its files. No installer action kills Explorer or the app.

Successful full/reduced-UI installs, upgrades, and repairs (including double-clicking
the MSI again) automatically launch GlassDock after the installation commits. Accept
the app's existing Windows elevation prompt if shown. Silent/basic-UI installs (`/qn`,
`/qb`) and uninstall do not launch the dock; use the Start Menu shortcut afterward in
those cases. The shortcut is a direct executable shortcut so it remains usable without
Windows Installer advertised-shortcut resolution.

The app retains its existing elevation manifest and runtime configuration.
Self-contained publishing includes .NET and the existing app-local Windows App
SDK. ICE validation remains disabled because the regular build account receives
WIX1105 (machine policy); compiler diagnostics and actual MSI lifecycle tests are
separate checks, not a claim that ICE passed.
