# Access enforcement and typed Git execution

New projects start Read-only. Monitor only permits narrowly scoped fetch and hosted reads. Allow changes permits reviewed writes and interactive terminals for this session. Access is checked centrally at preview/lease/start and again in hosted/terminal paths; disabled buttons are explanatory UI, not the policy boundary. Revocation invalidates queued work immediately and reports a pending stricter mode until owned activity closes. Common Git-directory identity coordinates linked worktrees and mutually excludes managed effects and interactive shells sharing metadata.

An immutable snapshot and preview carry project identity, generation, exact file/index evidence and ref targets. Execution refreshes relevant facts and rejects changed evidence. Candidate initialization/clone grants cannot silently retarget a newly discovered parent repository. Trust in hooks, filters and transport helpers is a separate deliberate choice.

Git runs through an absolute executable and typed argument lists without a shell. Inspection disables optional locks, executable monitoring and external diff/text-conversion helpers. The runner independently drains bounded streams, classifies operation-specific outcomes and owns only its process job. Cancellation or lingering descendants can produce Outcome unknown; arbitrary lock deletion and generic reset are excluded from recovery.

Inspection parallelizes independent fixed read queries and hashes file contents with a bounded four-read worker limit. Ordered aggregation preserves deterministic content evidence. It continues to read and hash every selected file byte; performance work does not replace that with a metadata-only cache. This distinction matters when contents change but status codes stay the same.

Fetch constrains destinations to approved remote-tracking refs, overriding inherited local-branch/tag mappings. Guided updates use reviewed object IDs and protect ignored-file collisions. Push resolves its actual URL independently of the fetched upstream and supports one explicit normal destination; configured mirror or multiple-destination behavior is blocked. These choices preserve standard Git transport/hooks after trust while reducing unintended application-managed effects.

See [`engine-api.md`](../engine-api.md) for implemented contract names and [`acceptance.md`](../acceptance.md) for evidence limits.
