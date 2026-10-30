# Contributing to GitCompass

Thanks for helping! GitCompass is for people who are new to Git, so clear and kind changes matter as much as clever ones.

## Ways to help
- **Report a problem:** open an issue. Say what you did, what you expected and what happened. Screenshots help.
- **Suggest an idea:** open an issue describing who it helps and why.
- **Improve words:** plain-language fixes to the app or docs are very welcome.
- **Change code:** see below.

## Getting set up
Build and test instructions are in [docs/DEVELOPING.md](docs/DEVELOPING.md). In short, on Windows x64 with the pinned .NET SDK (see `global.json`) and Git for Windows:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
```

This restores packages, builds the solution and runs the test programs under `tests/`.

## Making a change
1. Fork the repository and create a branch from `main` (for example `fix/clearer-commit-message`).
2. Keep each pull request focused on one change.
3. Match the existing code style. Keep the safety rules: new projects open Read-only, and every change to a project shows a review before it runs.
4. Add or update a check in the matching test program when you change behavior.
5. Use plain words in anything a user sees. Explain Git ideas in one short sentence.
6. Run `scripts/build.ps1` and make sure it passes, then open a pull request describing what changed and how you checked it.

A CI workflow that runs the same build and tests is planned.

## Licence
By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
