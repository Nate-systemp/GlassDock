# Doky Home dashboard

Home remains a retained window owned by the primary dock. Secondary docks forward Home commands to that owner. It keeps installed-app and Windows Settings search, pinned and running app strips, keyboard selection, and launch through the existing application services.

The current dashboard has three visible cards: search, pinned apps, and running apps. A fourth card shows results while a query is active. The layout fits the selected monitor's work area and DPI without a scroll viewer. App strips use the same canonical dock snapshots, including stack members.

One desktop backdrop and retained popup palette serve the cards. Settings and system-theme changes update the existing brushes and surfaces; hiding Home retains the window, while closing unsubscribes from the shared application and search sources. No system controls, power actions, recent-items placeholder, or media panel are present.

Automated checks cover layout bounds, app membership, appearance wiring, retained subscriptions, search, and monitor placement. Desktop visual behavior and repeated theme switching still require a live UI check.
