# Real Git integration checks

This dependency-free executable creates a unique fixture directory below `.artifacts/tests/` and uses installed Git with local working copies and bare remotes. It never contacts a real hosted repository. Fixture directories are retained for inspection.

Run the Release executable through the pinned local SDK:

```powershell
& ./.tools/dotnet/dotnet.exe tests/GitCompass.Git.IntegrationTests/bin/Release/net10.0/GitCompass.Git.IntegrationTests.dll
& ./.tools/dotnet/dotnet.exe tests/GitCompass.Git.IntegrationTests/bin/Release/net10.0/GitCompass.Git.IntegrationTests.dll --focused
```

The full suite exercises candidate initialization, exact Unicode/leading-hyphen/wildcard staging, unborn unstaging, real commits, immutable read-only inspection, stale previews and permission revocation, scoped fetch, clone, stash preservation, fast-forward and merge/conflict recovery, branch controls, actual push URL rewriting and live publication verification, trust/identity boundaries, ignore preservation, hooks, ignored-file collisions and linked worktrees. Selected-branch comparison uses actual IDs, checks ancestry counts and stable page anchors, rejects arbitrary revision text, and fingerprints worktree/index/refs/objects/config before and after inspection.

The focused suite exercises malicious local/tag fetch mappings and scoped pruning, direct-URL push bookkeeping, a changed server ID that remains within local ancestry, failed remote-check freshness, removed upstream handling, live output before process exit, cancellation, and background helpers retaining inherited pipes. A real stash-restoration conflict proves preservation of the exact stash and its untracked contents and blocks publication even without `MERGE_HEAD`. The fixture Git helper removes inherited `GIT_*` variables to keep targets local to its dedicated fixture.

The completed current suites pass 100 full assertions and 27 focused assertions on the development Windows machine. These are automated local behavioral checks. They do not establish real credential-helper sign-in, protected branch behavior on a hosted service, supported OS coverage, human usability, or production signing/install/update acceptance. Exact command results are recorded in `docs/verification.md` by the root build process.
