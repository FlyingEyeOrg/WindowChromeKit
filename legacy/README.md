# Legacy source snapshot

`SoftwareHub.DesktopAgent.Runtime/Windows` is an unmodified snapshot of the complete
window implementation from the SoftwareHub repository at commit
`6d59ba05df7174913c049217d5bb279e3c51d887`.

The snapshot is intentionally outside every project and is not compiled. Most of its
coordination, instance, system-warning, and WebView code depends on SoftwareHub
contracts, WebView2, SignalR, authentication, sessions, and runtime settings. It is
kept here so those capabilities can be reviewed and generalized incrementally without
making the reusable WPF assembly depend on the SoftwareHub product.

The reusable, dependency-free extraction lives under `src/WindowChromeKit.Wpf`.
