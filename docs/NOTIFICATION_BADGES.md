# Notification badges

The badge is a non-hit-testable child of `AdaptiveAppIcon`. It inherits the existing icon/button hover and reorder transforms without changing button dimensions or input regions. AUMID counts update existing icons; discovery refreshes do not reset badge state. `99+` caps only the displayed label.

Composition keyframes use cubic-bezier (0.2,0,0.2,1): 440 ms intro, dot visible by 88 ms, growth through 273 ms, number reveal 229–343 ms, one outward pulse 273–440 ms, final settle. Increases use a 180 ms 8% pop without repeating the intro/glow. Decreases update the number. Clearing uses a 220 ms number fade, shrink to dot and fade. Reduced-motion mode settles immediately. Completion generations reject stale callbacks; unload stops animation resources. There are no badge polling/animation timers.

## Real notifications and limits

`WindowsNotificationService` reads only AUMID and count from `UserNotificationListener`, never message text. Counts represent currently retained Windows Notification Center toasts, **not** unread messages inside an app. Notifications are never dismissed by Doky. Exact AUMID matching deliberately leaves unmatched executable-only identities unbadged. Some apps do not emit Windows notifications; no count is invented for them.

The existing unpackaged Velopack identity remains `Natesystemp.GlassDock`. A separate sparse identity, `Natesystemp.Doky.NotificationIdentity`, enables the Windows listener capability. Its external location must be the exact executable directory. The ordinary unpackaged app still starts if this optional package is absent. Right-click empty dock space → **Enable notification badges** requests Windows consent only when identity is present. Previously granted access resumes on startup. Notification change events refresh counts; no periodic polling is added. Windows stores permission; denial/revocation clears counts.

## Registration (pending signing)

1. Build the app and close all running Doky instances normally.
2. Run `scripts/Build-NotificationIdentity.ps1` to build the identity package. This unsigned artifact is not installable as a production package.
3. With a trusted code-signing certificate whose subject is `CN=Natesystemp`, run the script with `-CertificateThumbprint <thumbprint> -AppDirectory <exact executable directory> -Register`.
4. Launch Doky from that directory, select **Enable notification badges**, and grant access in Windows.
5. Generate a real toast from an app with a matching AUMID, add a second notification, then dismiss notifications in Windows. Verify intro, count pop and badge removal under hover, reorder and all materials.

The script never creates/trusts certificates or changes Windows security settings. Signing and registration were explicitly left pending by the user. No installer/update hooks are changed: release deployment must register the signed package against the installed path, update that registration if the executable location changes, and unregister it on uninstall. This task does not ship a release or claim that distribution integration is complete.

Microsoft references: [notification listener](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/notification-listener), [package identity with external location](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps).
