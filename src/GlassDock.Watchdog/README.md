# GlassDock.Watchdog

Independent development recovery executable. --status inspects taskbar visibility; --restore requests immediate restoration without App. App owns the --watch handshake and supplies heartbeats. Tests expire after 60 seconds, after five seconds without heartbeat, or when the parent exits. No restart loop or Explorer termination exists. See ../../docs/DESKTOP_RECOVERY_DESIGN.md.
