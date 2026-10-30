# Beginner audit (0.2.6)

Audit date: 31 Oct 2026. Persona: someone who has never used Git, GitHub or a terminal. Scope: XAML, code-built screens (`ShellWindow*.cs`), dialogs, error messages, tooltips, Settings, README and release notes. Open GitHub issues at audit time: **none**.

Wording rule now used: everyday words first, Git word in brackets, e.g. **Save a version (commit)**. All everyday names and definitions come from one place, `src/GitCompass.Domain/GitGlossary.cs`. The app uses it for hover tooltips and `GitGlossary.Label`. The README "Words you'll see" table is generated from it, and a domain test fails if the two drift apart.

Severity: **High** = blocks or scares a beginner; **Medium** = confusing; **Low** = polish.

| # | Screen | Issue | Severity | Fixed? |
|---|---|---|---|---|
| 1 | Practice missions | Pause / Exit buttons shown twice (walkthrough page + missions section) | High (bug) | Yes, 0.2.5 |
| 2 | Release packaging | Manual looked up by app version instead of `-ManualVersion`; every 0.2.x release run failed | High (bug) | Yes, 0.2.5 |
| 3 | Problems panel | Errors without a known pattern showed only the raw Git message | High | Yes: calm fallback "What happened / What to do" with one next step |
| 4 | Problems panel | No plain explanation for "not a git repository", `index.lock` (Git busy), "nothing to commit", offline / host not found, missing author | High | Yes, added to `GitErrorExplainer` |
| 5 | Problems panel | Explanations used "Git Credential Manager", "SSH key", "force-push", "remote" | Medium | Yes, reworded |
| 6 | Problems panel | Generic title "Action could not complete"; buttons "Review operation output" / "Prepare reviewed support information" | Medium | Yes: "That step didn't work", "See full details", "Get help: make a summary to share" |
| 7 | Projects | Empty state "Your recently opened projects will appear here." had no next step | Medium | Yes, points to Select project folder and Learn |
| 8 | Changes | Heading talked about "saved file contents" and "the next version"; "entire reviewed index", "staged paths", "editor buffers" | High | Yes, plain wording + count of chosen files, and a hint when nothing is chosen |
| 9 | Changes | Empty file lists said "No contents in this group." | Medium | Yes, tells the user to edit a file and "Check again for changes" |
| 10 | Changes | Diff box didn't explain + / - lines | Medium | Yes |
| 11 | Changes | Buttons "Preserve work with a stash", "Review ignore suggestions", "Write commit message", "Refresh local status" | Medium | Yes, everyday labels with Git word in brackets |
| 12 | Changes | Commit message box label was "Commit message — …" | Low | Yes: "Message: what did you change, and why? (commit message)" |
| 13 | Overview | Footer "Check remote (Fetch) … Update applies reviewed commits … Push uploads committed versions" | Medium | Yes, rewritten as a quick guide |
| 14 | Overview | "No commit history is available yet." with no next step | Medium | Yes |
| 15 | History | Heading "Recorded commits and their actual parents…"; "Previous/Next history page" ambiguous | Medium | Yes: "History", "Newer versions" / "Older versions" |
| 16 | Non-Git folder | "This folder is not using Git yet … has not created a Git repository" | Medium | Yes: "This folder doesn't keep saved versions yet" + "Start saving versions here" |
| 17 | Project setup | "Commit author name", "Remote name", "Authorized repository URL", "Tracking branch" | High | Yes, plain field labels |
| 18 | Header | Branch label "Detached HEAD" / "No commits yet" / "Not using Git" | Medium | Yes, plain text with Git word in brackets |
| 19 | Header | Access modes (Read-only / Monitor only / Allow changes) had no explanation; "Check team updates" had no hint | Medium | Yes, tooltips + screen-reader help text |
| 20 | Terminal | "Allow changes is required for an interactive terminal." / "owned activity is closing" | Low | Yes |
| 21 | Review card | "tied to the observed files, index, refs, and access grant" | Medium | Yes |
| 22 | Pull requests | "Publish its reviewed HEAD…", "explicit destination … this PR" | Medium | Yes |
| 23 | Merge review | "Preview merge of fetched upstream" | Medium | Yes |
| 24 | Conflict recovery | "Ordinary synchronization pauses … stage each resolved path" | High | Yes: step-by-step marker instructions |
| 25 | Settings | "Preserve damaged settings and use reviewed defaults", "Git executable path", "Remote check interval", GitHub client-ID wording | Medium | Yes |
| 26 | README | SmartScreen warning mentioned without saying how to continue | Medium | Yes: "More info → Run anyway" |
| 27 | README | No glossary; "branches", "pull requests" unexplained | Medium | Yes, generated table |
| 28 | Operation buttons | Review buttons used Git jargon ("Run reviewed operation", "Review local commit") | Low | Yes (0.2.7): plain labels such as "Confirm and run this step", "Preview saving this version (commit)"; UI tests, user guide and manual updated together |
| 29 | Branches / Pull requests pages | Headings and help said "pull request", "recorded branch tips", "hosted collaboration request" | Low | Yes (0.2.7): "lines of work (branches)", "review request (pull request)", plain sign-in text |
| 30 | Learn lessons (LearningCatalog) | Lesson text used "index", "refs", "upstream", "operation-specific" | Medium | Yes (0.2.7): concept lessons and the shared command-lesson text rewritten in both `build-catalog.ps1` and `manifest.json`; lessons show the GitGlossary everyday word and an official doc link; a domain test checks the two files agree and bans that jargon |
| 31 | docs/user-guide.md, functional-documentation.md | Quoted old labels | Low | Yes (0.2.7) for the markdown. The PDF manual was not regenerated: that needs Windows (PowerShell + the packaging script) |
| 32 | Accessibility | Glossary tooltips only on hover | Low | Yes (0.2.7): F1 anywhere or the "? What do these words mean (F1)" button opens a keyboard and screen-reader friendly dialog with definitions, lesson buttons and official doc links |
| 33 | Accessibility | Contrast not measured | Low | Partly (0.2.7): every theme text/background pair, including Liquid Glass (translucent buttons composited over page and scene), is computed in `ThemeContrast` and tested to be at least 4.5:1 (lowest 4.67). The Windows accent colour is now adjusted to stay readable. Real acrylic/blur on Windows still needs checking by eye |
| 34 | Release | App is unsigned and there is no real GitHub App client ID | High | No: needs the owner (signing certificate; registering the GitHub App). |
| 35 | Learn generator | `build-catalog.ps1` lacked the "basics" lesson that `manifest.json` has | Low | Yes (0.2.7): added; a test compares them. The generator itself still needs PowerShell to run |
| 36 | Glossary | No links to official Git docs | Low | Yes (0.2.7): every term has a verified git-scm.com or docs.github.com link (tooltips, "What's this?", lessons, error cards, README table) |

Safety behaviour is unchanged. Only wording changed. Every change still goes through the same preview, access check and confirmation.
