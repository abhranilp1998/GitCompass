# GitCompass — First-Run Guided Walkthrough Spec

Status: draft · Target: `GitCompass.Desktop` (WPF) · Extends the existing beginner guide in `ShellWindow.Beginner.cs`.

## 1. Goals
- A newcomer saves, branches and merges real work in a throwaway project in about 10 minutes.
- Learn by doing: every concept appears as **one line, exactly when it is needed**.
- Never surprise the user: every write shows the app's existing **preview-before-write** review (`ShellWindow.ShowPreview(OperationPreview)`), and nothing runs until they confirm.
- Experienced users can leave in one click (**Skip**), after a short, light-hearted check.

Non-goals: teaching rebase, conflicts in depth, or the terminal.

## 2. Entry points
| Trigger | Behavior |
|---|---|
| First launch (`BeginnerGuideState.WelcomeDismissed == false`) | Show the Welcome screen (W0). |
| Paused guide (`Active == true`) | Welcome shows "Continue your walkthrough" (existing `ResumeBeginnerAsync`). |
| Settings / Learn → "Start walkthrough again" | Calls `StartBeginnerAsync()`; creates a *new* practice folder (never reuses or overwrites one). |

## 3. Screen-by-screen

### W0 — Welcome
- **Title:** "Welcome to GitCompass"
- **Subtitle:** "Learn Git by doing it — in a safe practice project you can throw away."
- Body: "Git keeps a history of your work on your computer. GitHub is an optional online place to share it. We'll practice both — no account needed until the very last (optional) step."
- Buttons:
  - **Primary:** "Start the walkthrough (about 10 min)" → W1
  - **Secondary:** "Skip — I know Git" → opens the Skip quiz (Q0)
  - Link: "Open a folder instead" → behaves like Skip (also goes through Q0)

### W1 — Create practice project (one click)
- Title: "Let's make a practice project"
- Body: "We'll create a small folder with Git already set up. It isn't connected to anything online and is safe to delete later."
- Location field: default `Documents\GitCompass-Practice`, plus a "Change…" button (reuses the `OpenFolderDialog` in `CreatePracticeProjectAsync`).
- Button: **"Create practice project"** → `CreatePracticeAtAsync(parent)`
- Content: the same as `scripts/create-practice.ps1`, ported to C#:
  - `git init --initial-branch=main`
  - a practice author set locally (`GitCompass Practice` / `practice@example.invalid`)
  - files: `welcome.txt`, `.gitignore` (`scratch/`), `READ-ME-FIRST.txt`
  - one first version: "Save the first local practice version"
  - then `welcome.txt` gets an extra line and a new `ideas.txt` appears, so there is something to see straight away
  - check: the project must have no remote
- States:
  - *Busy* — "Setting up…"
  - *Success* — store `PracticeFolder` and `SavedCommitId`, open the project in **Allow changes for this session**, then go to M1
  - *Folder exists* — "A practice folder is already here. We won't touch it. Pick another place or name." (same refusal rule as the script)
  - *Git missing* — the existing `setupError` banner → Settings

### Mission bar (shown on M1–M5)
A pinned card with: "Mission N of 5", a progress dot per mission, and the buttons **Pause** (`PauseBeginnerAsync`), **Show me where** (highlights the target control) and **Exit walkthrough**.

### M1 — See a change
- Navigate("Changes").
- Goal: "Two files are different from your last saved version. Click `welcome.txt` to see what changed."
- Concept line (when the diff opens): "**Working copy** = your files right now. Git compares them with the last saved version."
- Done when: the diff for `welcome.txt` is opened, and `ideas.txt` is shown as *New*.
- Success: "Green lines were added. Git noticed — but hasn't saved anything yet."

### M2 — Stage
- Goal: "Pick `welcome.txt` to go into your next save."
- Concept line: "**Staging** = choosing which changes go into the next save. Like putting items in a box before sealing it."
- Action: Stage → preview card ("Will select: welcome.txt. Files on disk are not changed.") → Confirm.
- Done when: `OnBeginnerOperationCompletedAsync(OperationKind.Stage, Succeeded)` fires.
- Teaching moment: "`ideas.txt` is still unselected — it won't be in this save. That's on purpose."

### M3 — Commit (save a version)
- If the author is not set → the existing `BuildAuthorSetup()`. The practice repo already has a local author, so normally this is skipped.
- Goal: "Save a version with a short message."
- Message is pre-filled: "Add a line to welcome.txt" (editable).
- Concept line: "**Commit** = a saved snapshot on *your computer*. Nothing is uploaded."
- Preview → Confirm → `Commit` Succeeded → store `SavedCommitId` → show it in History graph with the new dot highlighted.
- Success: "You now have 2 versions. You can always go back to either one."

### M4 — Branch + merge
1. Goal: "Make a side branch called `try-idea`."
   - Concept line: "**Branch** = a separate line of work. Experiment without disturbing `main`."
   - `CreateBranch` + `SwitchBranch` through preview (Navigate("Branches")).
2. "Stage and save `ideas.txt` on this branch." (repeats M2/M3, with shorter copy)
3. "Switch back to `main` — notice `ideas.txt` disappears from your folder."
   - Concept line: "Files follow the branch you're on. Nothing is lost."
4. "Bring `try-idea` into `main`." → `Merge` preview → Confirm.
   - Concept line: "**Merge** = combine another branch's saved work into this one."
- Done when: `Merge` Succeeded and the History graph shows both lines joining.
- On failure or conflict: use the existing `AbortMerge` path plus the lesson link `ShowLesson("git-merge")`. The mission stays incomplete and can be retried.

### M5 — (Optional) Push to GitHub
- Card: "Want to put this online? Optional — you can finish without it."
  - **Do it:** configure a remote (`ConfigureRemote` preview) → `Push` preview → Confirm
  - **Finish without uploading**
- Concept line: "**Push** = upload your saved commits to GitHub. Commit saves locally; push shares."
- Requires the user's own empty GitHub repo URL. Git transport credentials come from Git's credential helper. Clearly label: "Your practice files will become visible to whoever can see that repo."
- Never auto-creates a repo, and never pushes without a confirmed preview.

### W-End — Done
- Title: "You did it 🎉"
- Recap of 5 checkmarks, each with its one-line concept.
- Buttons: **"Open my own project"** (`PickFolder`) · "Keep exploring the practice project" · "Delete practice folder" (preview + confirmation, and only for `PracticeFolder`)
- Set `Active = false`, `WelcomeDismissed = true`.

## 4. Skip quiz dialog (Q0)
A modal `Window`, e.g. `SkipQuizDialog.xaml` in `GitCompass.Desktop`, owned by `ShellWindow`. It uses the existing theme resources.

### Title / subtitle variants (one picked at random per opening)
| Title | Subtitle |
|---|---|
| "Do you *really* know Git?" | "Three quick questions. No pressure. (Some pressure.)" |
| "Whoa there, Git wizard 🧙" | "Prove it and the door is yours." |
| "Skipping already? Bold." | "Answer 3 and we'll get out of your way." |
| "`git gud` check" | "If you can answer these, you don't need us." |
| "Trust, but verify" | "Three questions between you and your repo." |
| "Is this your first rodeo?" | "Let's find out in about 20 seconds." |

### Questions (single choice; the order of answers is shuffled)
1. **What does staging (`git add`) do?**
   - ✅ Chooses which changes go into the next commit
   - Uploads files to GitHub
   - Saves a permanent version
   - Creates a backup copy of the folder
2. **Commit vs push?**
   - ✅ Commit saves locally; push uploads commits to a remote
   - They're the same thing
   - Push saves locally; commit uploads
   - Commit needs internet
3. **What is a branch?**
   - ✅ A separate line of work you can switch between and merge later
   - A copy of the folder on GitHub
   - A deleted commit
   - A file Git ignores

Optional pool: also ask about "merge" and "what `.gitignore` does", and pick 3 of 5 at random.

### Outcomes
- **3/3 — pass:** "Okay, you clearly know your stuff. 🫡" → **"Open folder or repo"** → close the dialog and call `PickFolder`, with "Clone a repo" as a secondary option (`ChooseCloneDestinationAsync`). Set `WelcomeDismissed = true`.
- **≤2/3 — not quite:** "Close! Git trips up everyone at first." Show the correct answer and its one-line concept for each missed question.
  - **Primary:** "Take the 10-minute walkthrough" → W1
  - **Secondary:** "Skip anyway" → same as pass (no lockout, no nagging later)
- **Close / Esc:** returns to W0. Nothing changes.
- Never block the user, and don't store or show a "score" anywhere afterwards.

## 5. Hooks into existing code
| Area | File / member | Change |
|---|---|---|
| Welcome | `ShellWindow.Beginner.cs` → `BuildBeginnerWelcome()` | Add W0 copy plus the Skip button → `ShowSkipQuiz()` |
| Start / resume / pause | `StartBeginnerAsync`, `ResumeBeginnerAsync`, `PauseBeginnerAsync` | Drive the mission index |
| Mission rendering | `BuildWalkthrough()`, `AddGuideSection()` | Render the mission bar and M1–M5 |
| Practice repo | `CreatePracticeProjectAsync`, `CreatePracticeAtAsync` | Port the `scripts/create-practice.ps1` steps through `GitWorkspace` / `Infrastructure.Git` (`GitProcess` with `ArgumentList`) |
| Progress events | `OnBeginnerOperationCompletedAsync(OperationKind, OperationState)`, `OnBeginnerProjectOpenedAsync` | Advance missions on `Stage`, `Commit`, `CreateBranch`, `SwitchBranch`, `Merge`, `ConfigureRemote`, `Push` with `Succeeded` |
| Preview | `ShellWindow.xaml.cs` → `ShowPreview(OperationPreview)` | Unchanged; missions must call it for every write |
| Navigation | `ShellWindow.xaml.cs` → `Navigate(string)`, `ShowLesson(string)` | Reuse; "Show me where" highlights a named control |
| State | `Infrastructure.Local/AppStateStore.cs` → `BeginnerGuideState` | Add `int MissionIndex`, `bool PushOffered`, `string? PracticeBranch`; bump `SchemaVersion` to 2 and migrate v1 (`ValidGuide`) |
| Concepts | `LearningCatalog.cs` / `assets/learning/manifest.json` | Read one-liners from the entries (`Purpose`) so the walkthrough and Learn don't drift apart |
| Access | Access modes (Read-only / Monitor only / Allow changes) | Only the practice folder is auto-set to Allow changes for the session; user projects keep opening Read-only |
| Tests | `tests/GitCompass.Desktop.WorkflowTests/BeginnerWorkflowChecks.cs` | Add the checks in §6 |

## 6. Acceptance criteria
1. On first launch, W0 shows both "Start the walkthrough" and "Skip — I know Git".
2. One click on "Create practice project" produces a repo with exactly 1 commit on `main`, `welcome.txt` modified, `ideas.txt` untracked and no remotes — the same result as `scripts/create-practice.ps1`.
3. Creating the practice project refuses an existing folder and leaves it byte-identical.
4. Every write in M2–M5 shows a preview first, and Cancel leaves the repo unchanged (`git status` and `git log` are the same before and after).
5. Each concept line appears only when its mission step becomes active, and is at most 1 sentence (≤ 20 words).
6. M1–M4 can be finished without a network connection or a GitHub account. M5 is clearly optional and skippable.
7. After M4, `git log --graph` shows a merge of `try-idea` into `main` containing `ideas.txt`.
8. Pause → close the app → reopen → Resume lands on the same mission (`MissionIndex` is saved).
9. Skip opens Q0 with a random title/subtitle variant. 3/3 → folder/repo picker. ≤2/3 → explanations plus "Take the walkthrough" and "Skip anyway". Both paths lead to the picker without extra prompts.
10. Esc / close on Q0 returns to W0 with no state change.
11. User projects opened after the walkthrough still default to Read-only.
12. A merge failure in M4 doesn't corrupt the practice repo. The abort path returns it to the state before the merge.
13. Keyboard-only and screen-reader use works for W0, the mission bar and Q0 (tab order, `AutomationProperties.Name` set).
14. The layout works down to the app's existing minimum of 640 × 560.
