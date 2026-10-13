# Terminal hosting, support matrix and deployment

The integrated terminal uses Windows ConPTY, owned process jobs, and locally packaged xterm.js in WebView2. It is a real PowerShell session with independent input/output and resize, interrupt and disposal. Each tab retains its starting project. General shell commands can change directory and Git state; the app therefore requires Allow changes, keeps token material out of the child environment, coordinates owned metadata activity and rechecks facts before managed operations.

The WebView accepts only its local origin and fixed schemas for session operations. Output is untrusted terminal data, not HTML. Navigation, automatic links and clipboard writes are restricted. Assets and licenses are pinned locally; neither an online renderer nor a process-output text box is a terminal substitute.

The first delivery is an unsigned self-contained `win-x64` portable directory and ZIP. All files must be extracted together. .NET is bundled; Git for Windows and WebView2 remain separately installed prerequisites. Package launches, terminal asset presence, notices and repository invariance need actual package checks. A build configuration named Release does not imply completed product acceptance.

Windows 11 x64 is the initial target; tested development build 26300 is recorded in verification. The .NET API baseline, ConPTY minimum and WebView2 availability intersect with vendor-supported OS versions. Windows 10 editions/builds and ARM64 are distinct unverified gates. Keep those claims narrower than theoretical framework support.

Production packaging remains a company decision between signed MSIX or an approved per-user installer, with signing identity, installation/update behavior and a runtime-update process. Portable trial evidence does not establish those gates. See [`research.md`](../research.md) for official deployment sources and [`verification.md`](../verification.md) for measured results.
