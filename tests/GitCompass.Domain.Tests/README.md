# Domain and permission checks

The executable verifies ordered beginner guidance for mixed local/remote state, divergence, interrupted operations, explicit task intent, unknown/unborn/detached/missing-upstream state, limited history and user goals. It also checks every registered capability against Read-only/Monitor only, grant revocation, pending terminal shutdown, session-only unrestricted access, and shared-worktree terminal/managed-operation exclusion.

Run after the Release build:

```powershell
& ./.tools/dotnet/dotnet.exe tests/GitCompass.Domain.Tests/bin/Release/net10.0/GitCompass.Domain.Tests.dll
```

The current suite contains 73 assertions, including local-only guidance and first-save author prerequisites. It has no Git process, network or UI dependency; real repository effects are covered separately by the Git integration executable.
