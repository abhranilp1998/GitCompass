# Build Git Project Assistant from the specification

Use Git_Project_Assistant_Build_Spec.md as the authoritative product and technical brief. It is self-contained. The four companion PNGs show approved visual direction; missing images do not prevent work because the specification describes the layout and behavior.

## Task for Codex

Build one Windows desktop application called Git Project Assistant in the chosen workspace, starting from an empty repository or preserving any existing user work. Use C#, .NET 10 LTS, WPF with Fluent styling, the installed Git CLI, a deterministic repository-state advice engine, a separate GitHub REST adapter, and ConPTY plus locally packaged xterm.js in WebView2 for the real terminal.

Read the complete specification before implementation. Work through milestones M0 to M7 in order, keeping each slice runnable and recording verification. Complete the P0 pilot and then the P1 full-release scope. Do not stop after proposing a plan or rendering a static interface. Do not label missing functionality as complete.

The single shared shell must provide a prominent native project-folder picker, current status, explained next steps, a real embedded history graph, Changes, Branches, Pull requests, offline Learn content, Settings, and a persistent resizable dock for separate Git Output, Problems and real terminal sessions. Normalize the visual concepts into consistent navigation and light/dark themes.

Open new projects in strict Read-only. Provide explicit Monitor only and session-scoped Allow changes levels. Enforce capabilities in the operation coordinator, scheduler, retries and terminal bridge. Read-only must not modify the selected repository or start a shell; Monitor only permits narrowly scoped fetch metadata and hosted reads; writes and interactive terminal require Allow changes. Revocation must invalidate queued work, and a stricter mode is not reported as active until owned operations/sessions are handled.

Implement actual repository inspection and correct advice for mixed states, including local edits, staged changes, ahead/behind, divergence, detached or unborn HEAD, missing tracking, stale remote information and interrupted operations. Never infer a wrong branch without configured task intent. Fetch, update and push need distinct explanations and results. Revalidate every operation preview and refresh the plan afterward.

Keep each process and terminal session identified by project. Capture stdout/stderr without deadlocks, classify results by operation, keep the UI responsive, and provide reviewed redacted diagnostics. Core status, history, explanations and terminal assets must work offline after prerequisites are installed.

Use local temporary repositories, a bare test remote and two clones for automated synchronization tests. Do not use a colleague's real repository as a destructive fixture. Do not push to a real remote, create a real PR or transmit diagnostics as part of automated development unless the user explicitly designates and authorizes the sandbox. Keep GitHub API tests behind a controlled HTTP adapter until registration is supplied.

Ask for missing company inputs only when the relevant milestone depends on them. Continue independent local implementation when GitHub registration, OS inventory or production signing is unavailable. Report the exact unverified gate; do not invent credentials, URLs, client IDs, signing identities or test results.

At each milestone update docs/verification.md with requirement IDs, acceptance evidence, current limitations and the next milestone. Keep README setup/build/run instructions and architecture decisions current. Deliver reproducible source, the tested internal package, and a clear final summary of implemented features, actual checks and remaining external gates.

## Required implementation records

- Solution and dependency structure from specification section 10.
- A requirements checklist covering FR01 through FR20.
- Verification scenarios AT01 through AT37 plus AT08a.
- Architecture decisions for terminal hosting, support matrix, permission enforcement, Git runner and deployment.
- User guide and offline learning manifest.
- Third-party notices and pinned dependency versions.

## First concrete slice

Prove the Windows UI and terminal integration, then build a native folder picker that opens a real temporary repository in Read-only and displays a truthful snapshot, real graph and operation output. Assert repository fingerprints stay unchanged. Add subsequent actual workflows through the coordinator rather than bypassing the permission model.
