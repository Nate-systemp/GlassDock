# Startup compatibility test (0.1.9)

The reported remote 25H2 crash has not been reproduced locally. Module names and
0xc000027b identify a stowed exception, not its original failing call. A remote
dump is still needed if the new startup log cannot narrow it down.

The current resolved graph has Windows App SDK 2.5.1, WinUI 2.3.9 and Win2D
1.4.0. These satisfy the published NuGet dependency constraints; there are not
two resolved WinUI packages. Keep this graph for this test instead of changing
versions and startup code simultaneously. The package is x64, self-contained,
unpackaged/app-local, published into a fresh directory.

Package with explicit architecture and the native C++ prerequisite (self-contained
.NET does not supply the unpackaged WinUI VC++ runtime):

```powershell
vpk pack --packId Natesystemp.GlassDock --packVersion 0.1.9 `
  --packDir .\publish-compat-0.1.9 --mainExe GlassDock.App.exe `
  --packTitle Doky --runtime win-x64 --framework vcredist143-x64 `
  --outputDir .\Releases-compat-0.1.9
```

See [Microsoft deployment requirements](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deployment-architecture)
and [Velopack prerequisite bootstrapping](https://docs.velopack.io/packaging/bootstrapping).
The installer checks this prerequisite; running a portable ZIP does not install it.

Previously App.OnLaunched constructed the OS dispatcher queue before creating
any window. DesktopGlassBackdrop also created the OS compositor, shared Win2D
device and mask outside its effect exception handler. Now a loaded root queues
optional initialization at low dispatcher priority. The app-lifetime queue is
created lazily. Recoverable graphics/COM initialization failures release partial
resources and leave the XAML material body visible. Disconnect invalidates queued
callbacks. No global XAML exception is marked handled.

`--basic-rendering` bypasses the OS desktop-compositor queue, Win2D mask/effect
creation and custom SystemBackdrop assignment. It uses plain rounded XAML
surfaces; desktop blur, refractive rim, custom wave silhouette and native shadows
are not available in this fallback. It does not change taskbar, keyboard or
recovery policy. WinUI itself still requires its own compositor and dispatcher.
A native fail-fast inside WinUI cannot be recovered with a managed catch block.

## Other PC test

1. Exit all old Doky instances normally. Run the 0.1.9 Setup.exe from
   Releases-compat-0.1.9 and allow its supported VC++ prerequisite installation
   if needed. Test normal launch first; if it runs, exit normally.
2. In PowerShell run:
   `& "$env:LOCALAPPDATA\Natesystemp.GlassDock\current\GlassDock.App.exe" --basic-rendering --controls`
3. Confirm the dock opens and stays running for one minute. Expand it, open and
   close a utility popup, then use Exit GlassDock. Confirm the taskbar restores.
4. Run the same command without `--basic-rendering` and repeat with advanced composition.
5. Send `%LOCALAPPDATA%\GlassDock\startup.log` and any new Event Viewer/WER entry
   if either fails. No settings need to be deleted or reset.

If basic succeeds and normal fails, use basic rendering pending investigation of
the optional compositor path. If neither reaches `Main`, investigate native
app-local runtime loading; if the last stage is `Application.Start entering`,
the failure precedes GlassDock's optional desktop composition. A log reaching
`Desktop backdrop ready` confirms that initialization completed, not that every
future rendering operation is infallible.
