# Real Git integration checks

This dependency-free executable creates a unique fixture directory below `%TEMP%/GitCompass-tests/integration/` and uses installed Git with local working copies and bare remotes. Fixtures are outside the source checkout so ordinary folders cannot inherit GitCompass's own repository during discovery. It never contacts a real hosted repository. Fixture directories are retained for inspection.

Run the Release executable through the pinned local SDK:

```powershell
& ./.tools/dotnet/dotnet.exe tests/GitCompass.Git.IntegrationTests/bin/Release/net10.0/GitCompass.Git.IntegrationTests.dll
& ./.tools/dotnet/dotnet.exe tests/GitCompass.Git.IntegrationTests/bin/Release/net10.0/GitCompass.Git.IntegrationTests.dll --focused
```

The full suite exercises candidate initialization, first-save author prerequisites and effective identity inspection, optional sharing for local-only projects, exact Unicode/leading-hyphen/wildcard staging, unborn unstaging, real commits, immutable read-only inspection, stale previews and permission revocation, scoped fetch, clone, stash preservation, fast-forward and merge/conflict recovery, branch controls, actual push URL rewriting and live publication verification, trust/identity boundaries, ignore preservation, hooks, ignored-file collisions and linked worktrees. Selected-branch comparison uses actual IDs, checks ancestry counts and stable page anchors, rejects arbitrary revision text, and fingerprints worktree/index/refs/objects/config before and after inspection.

The focused suite exercises malicious local/tag fetch mappings and scoped pruning, direct-URL push bookkeeping, a changed server ID that remains within local ancestry, failed remote-check freshness, removed upstream handling, live output before process exit, cancellation, and background helpers retaining inherited pipes. A real stash-restoration conflict proves preservation of the exact stash and its untracked contents and blocks publication even without `MERGE_HEAD`. The fixture Git helper removes inherited `GIT_*` variables to keep targets local to its dedicated fixture.

The completed current suites pass 108 full assertions and 27 focused assertions on the development Windows machine. These are automated local behavioral checks. They do not establish real credential-helper sign-in, protected branch behavior on a hosted service, supported OS coverage, human usability, or production signing/install/update acceptance. Exact command results are recorded in `docs/verification.md` by the root build process.
