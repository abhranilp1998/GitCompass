# GitCompass architecture

Application name: GitCompass. The original build pack is retained unchanged in `archive/reference/`.

The desktop app follows the supplied specification: .NET 10, C#, WPF with Fluent styling, an installed Git CLI, deterministic advice, a separate GitHub REST adapter, and ConPTY with local xterm.js in WebView2. Domain facts and advice have no UI or process dependencies. Application coordinates reviewed operations. Git and hosted adapters perform typed work; the desktop composition root binds them together.

The workspace-local SDK is pinned to 10.0.401. Development dependency downloads are not application behavior. Runtime never silently installs Git or WebView2. New folders open in Read-only; Allow changes is session-scoped. The engine, scheduler, terminal bridge and hosted writes must enforce access independently of disabled controls.

Prototype and verify native UI and ConPTY early. A process-output text box cannot replace the terminal. Real ancestry supplies graph edges. Unknown remote facts remain unknown, and failed checks retain previous successful observation timestamps.

Build internal x64 self-contained packages first. Windows 11 is the initial target; the TFM baseline does not imply generic Windows 10 support. ARM64, production signing, colleague OS inventory and a live GitHub App sandbox are distinct validation gates.
