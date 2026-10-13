# GitCompass internal pilot task sheet

Use the unsigned internal build with a disposable project and a local test remote first. Record the package hash, app/Git/WebView2 versions, Windows edition/build/architecture, display scaling and assistive technology. This sheet records human and deployment acceptance; automated fixture results are in `verification.md`.

## Beginner task observation

Observe without giving the next click. For each task record **completed independently / completed with help / blocked**, minutes, mistaken actions, wording the person misunderstood and their own explanation. Do not ask a participant to experiment destructively on a real company repository.

| Task | Observable success | Comprehension prompt |
| --- | --- | --- |
| Open a supplied disposable project | Correct root/branch visible; starts Read-only; original content unchanged. | What can the app do in this mode? |
| Inspect one new and one modified file | Selects intended file, reads diff, distinguishes staged and unstaged contents. | Which contents would the next commit include? |
| Save one selected file locally | Reviews exact selection, enters message, commits selected contents, sees real history entry; second file stays uncommitted. | Where is the saved version now? Has anyone else received it? |
| Check team history | Chooses permitted check mode, runs fetch, recognizes freshness and incoming count. | Have your working files changed just because you checked? |
| Receive clean incoming work | Reviews incoming versions, enables changes deliberately, fast-forwards to reviewed target. | How does Update differ from Check remote? |
| Upload one local commit | Reviews destination and versions; sees verified publication. | Which of your edits were uploaded? Which stayed local? |
| Handle a rejected upload or text conflict | Keeps local work, follows recorded reason/recovery, avoids forced overwrite; understands uncertainty. | What remains intact, and what needs checking? |
| Prepare support evidence | Reviews redacted Markdown and JSON, removes a deliberately planted private field, saves locally. | Has the app sent this report to anyone? |

Before accepting beginner usability, require the participant to explain **changed file → staged contents → local commit → published commit**, and distinguish **fetch / update / push** without a prompt. Record any assistance instead of counting it as independent success. Repeat failed tasks after specific wording/layout repairs.

## Manual Windows checks

- Open/cancel the native folder picker; inspect a nested folder, linked worktree, no-repository folder and invalid folder.
- Complete the main workflow with keyboard only; check focus order, visible focus, selector/graph/dock/terminal access and cancel behavior.
- Use a screen reader to read project/mode, separate counts, plan changes, file selection, commit list and actionable errors.
- Check high contrast, text scaling and 100%, 150% and 200% DPI, including a small laptop window and the resizable dock.
- Run two real terminal tabs: type, scroll/select/copy, review multiline paste, resize, interrupt, switch selected project and close active sessions. Confirm their starting-project labels remain truthful.
- Downgrade during owned work and shell activity; verify the pending mode explanation, cancellation/closure choices and final mode.
- Start the extracted package as a standard user, with no development SDK required; test missing Git/WebView2 guidance and offline local help/terminal assets after prerequisites are installed.
- Fully exit and verify no GitCompass-owned monitoring or shell activity remains. Try notifications/quiet settings and review the affected project's plan.

## External acceptance

Live GitHub tests require an administrator-provided App registration/permissions and explicitly designated sandbox: authorize, refresh/revoke access, read PRs, verify source publication, submit draft/ready requests and reconcile an uncertain response. Record actual URLs privately where company policy allows; no production PR or push is an automatic test.

Production acceptance additionally needs the supported company OS matrix, realistic project performance measurements, publisher copyright/end-user distribution terms, a distribution/signing decision and fresh install/update checks proving project data is preserved. A trial on the development machine cannot close those gates.
