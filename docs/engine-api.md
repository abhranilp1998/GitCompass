# Engine API

Instantiate `GitCompass.Infrastructure.Git.GitWorkspaceFactory.Create(absoluteGitExecutablePath)` to obtain `GitCompass.Application.GitWorkspace`. All models use the `GitCompass.Domain` namespace. The public contracts live in `Models.cs` and `GitWorkspace.cs`.

- `Task<RepositorySnapshot> OpenAsync(string folder, CancellationToken = default)` registers and inspects; new access is ReadOnly.
- `Task<RepositorySnapshot> RefreshAsync(Guid projectId, CancellationToken = default)` only inspects.
- `RecommendationPlan Plan(Guid projectId, UserGoal goal = Understand)` gives ordered deterministic guidance.
- `Task<HistoryPage> HistoryAsync(Guid projectId, int skip = 0, int take = 200, CancellationToken = default)` returns actual parent edges.
- `Task<HistoryPage> HistoryAsync(Guid projectId, HistoryRequest request, CancellationToken = default)` compares selected branches. Select exact `BranchInfo.Id` values from the current branch inventory; arbitrary revision expressions and stale branch IDs are rejected. Null left/right selections default to the inspected HEAD/upstream. `HistoryRequest` holds left/right IDs, skip and take; page size is bounded to 500. `HistoryPage.Comparison.LeftOnly/RightOnly` reports actual ancestry counts, while `IsShallow` qualifies limited history. Typed comparison pages use a combined `leftId:rightId` anchor; retain both selected IDs when paging and refresh selections when refs move. Inspection writes no objects, refs, index or working files.
- `Task<DiffResult> DiffAsync(Guid projectId, DiffRequest request, CancellationToken = default)` returns bounded text with external drivers disabled.
- `Task<ImmutableArray<IgnoreSuggestion>> IgnoreSuggestionsAsync(Guid projectId, CancellationToken = default)` reads suggestions without writing.
- `Task<OperationPreview> PreviewAsync(OperationRequest request, CancellationToken = default)` checks access and binds exact evidence.
- `Task<OperationResult> ExecuteAsync(OperationPreview preview, CancellationToken = default)` consumes the issued preview once, serializes by common Git directory, checks grants and refreshed evidence, executes and refreshes.
- `SetTrusted(Guid,bool)`, `SetMonitoring(Guid,bool)`, `SetIntendedBranch(Guid,string?)` configure explicit in-session choices.
- `Access.Get(Guid)` reads the session grant; `Access.SetLevel(Guid, AccessLevel)` changes it. `PendingLevel` remains visible until active leases end.
- `Access.Authorize(Guid, OperationKind, long? expectedGeneration = null)` gates actions. `Access.Acquire(...)` returns an `AccessLease` whose `Revoked` token requests cancellation on downgrade. Terminals must retain a lease until owned processes are closed and authorize before input. `AccessLease.Dispose()` completes pending downgrade when no owned activities remain.
- `Activity` emits typed `OperationEvent`; `SnapshotChanged` emits refreshed facts. The desktop must marshal events to its dispatcher.

`OperationRequest.Paths` and `IgnorePatterns` are immutable arrays (`ImmutableArray.Create(...)`). Request fields are selected according to OperationKind: Message for Commit; Remote/RemoteUrl for ConfigureRemote; Branch for Initialize/CreateBranch/SwitchBranch; TargetId for Merge/CreateBranch/ApplyStash; IncludeUntracked for Stash; SetUpstream for initial Push. Preview `CommandDisplay` is teaching text, never shell input. Confirm the displayed preview before executing it.

Push resolves and validates one actual URL, reviews its live destination ID, and executes against that URL directly with one branch refspec. This avoids inherited named-remote fetch mappings changing local branches/tags as push bookkeeping. Multi-step URL rewrites are rejected rather than silently changing the reviewed destination. A server destination change invalidates the preview, including a change that remains within local ancestry. Publication success is separately verified at the actual URL. `SetUpstream` then writes only the reviewed branch's local tracking keys. Cached tracking refs and ahead/behind may still describe the earlier fetch; another permitted remote check updates those comparisons.

Commit verification compares the resulting tree with the reviewed index, the current branch with the reviewed branch, and the immediate parent with the reviewed HEAD. Hook-altered contents or branch/ancestry produce `OutcomeUnknown` with refreshed facts. Cancellation and descendant/pipe cleanup also retain uncertainty instead of deleting locks or inventing rollback success.

Unmerged paths pause ordinary guided operations even when Git has no recorded merge state, as happens after a conflicting stash apply. Resolution can be reviewed and staged; the exact preserved stash is retained. The app never substitutes generic reset, lock deletion or automatic stash drop for recovery.

Freshness fingerprints include complete file-content SHA-256 evidence rather than relying on modification times. A bounded four-worker pass uses pooled read buffers and ordered aggregation; independent read-only metadata queries overlap. This keeps large-repository refresh responsive while retaining content-change invalidation.

AllowChanges does not itself establish trust in repository executable configuration. Explicitly call SetTrusted after the repository trust explanation before guided writes/network actions. No arbitrary process command is exposed.
