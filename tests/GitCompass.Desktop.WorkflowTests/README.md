# Native local workflow verification

This test creates its own disposable repository under `.artifacts/desktop-workflow/<unique-id>`. It launches the actual WPF shell, selects native controls, reviews and executes selected staging and a local commit, and verifies Git's recorded contents. No remote is created, no push occurs, and the test never touches a colleague repository.

The fixture's known trust is injected into the private workspace using reflection to avoid a human trust modal. Access selection, file selection, review command, execution, commit message and resulting page refresh are driven through actual rendered WPF controls. This proves their integration; it does not prove comprehension of the human trust explanation or native folder-dialog usability.

12 October 2026, Windows x64 build26300/.NET10.0.401/Git2.54.0.windows.1: **10 assertions passed**, exit0. Repository index remained byte-identical after opening and rendering in Read-only. Stage stayed disabled before permission selection. The reviewed file contents became the new local commit and two real history rows appeared. The native selector locked back to Read-only afterward.

```powershell
& .tools/dotnet/dotnet.exe run --project tests/GitCompass.Desktop.WorkflowTests --no-restore
```

Remaining human gates include native folder-picker interaction, permission/trust explanation comprehension, keyboard/screen-reader/high-contrast/DPI usability, diagnostic review, and a representative beginner pilot.
