# Terminal components

GitCompass redistributes @xterm/xterm 5.5.0 and @xterm/addon-fit 0.10.0 under the MIT license. Their full license texts are retained beside the vendored assets in `vendor/`. npm integrity hashes and exact versions are recorded in package-lock.json.

Terminal rendering uses only bundled JavaScript and CSS. No CDN, server, dynamic package download, Node.js runtime or remote resource is used by the installed application. Terminal fonts use Windows-installed Cascadia Mono or Consolas (no third-party font redistribution).

To reproduce the bundle during development: `npm ci --ignore-scripts --no-audit --no-fund`, followed by `npm run vendor`. Do not ship node_modules.

Microsoft.Web.WebView2 is separately restored by the .NET project at the centrally pinned package version. WebView2 Runtime is an external prerequisite; the .NET self-contained package does not include that runtime.
