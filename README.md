# GitCompass

**GitCompass is a Windows app that helps you learn Git by showing what changed in your project, where your work is saved, and what to do next.**

![Overview screen](docs/images/functional-overview.png)

---

## Download

Get the latest version from the **[Releases page](https://github.com/abhranilp1998/GitCompass/releases)**.

> **Release pending:** no release has been published on that page yet. Until then, build from source (see [docs/DEVELOPING.md](docs/DEVELOPING.md)).
>
> The current build is an early test version. It is not digitally signed yet, so Windows may show a warning the first time you open it.

## Quick start (3 steps)

1. **Download and unzip.** Unzip the **whole** ZIP file into a folder, then open `GitCompass.exe` from inside that folder.
2. **Try the Learn page.** It explains the basics in plain language.
3. **Open a project.** Choose a folder. GitCompass opens it in **Read-only** mode first, so nothing in your files changes until you choose to allow it.

Want to practise first? Use a practice project (source builds can create one with `scripts/create-practice.ps1`). It is a small throwaway folder with one saved version, one changed file and one new file, and it isn't connected to anything online.

## What you need

- Windows 11 (64-bit). Other Windows versions have not been tested yet.
- [Git for Windows](https://git-scm.com/downloads/win). GitCompass tells you if it can't find it.
- [Microsoft WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/), only for the built-in terminal. Most Windows 11 PCs already have it.
- You do **not** need to install .NET, Visual Studio or a GitHub account.

## What it does

![Changes screen](docs/images/functional-changes.png)

- **Shows what changed:** see which files you edited since your last saved version.
- **Helps you save versions:** pick the changes you want, then save them on your computer.
- **Shows your history** as a simple picture of saved versions and branches.

![History screen](docs/images/functional-history.png)

- **Suggests the next step**, and shows you what an action will do before it happens.
- **Keeps you safe:** projects start in Read-only, and you decide when to allow changes.
- **Has a built-in Learn guide** that works offline.
- **Works with GitHub (optional):** upload your saved work and view pull requests. Some GitHub features need extra setup for now.
- **Looks good:** light, dark or system theme, and it fits small windows.

## FAQ

**Do I need a GitHub account?**
No. Git works on your computer by itself. GitHub is only needed if you want to share your work online.

**What's the difference between Git and GitHub?**
Git keeps the history of your project on your computer. GitHub is a website where you can upload that history to share it or back it up.

**Can GitCompass break my project?**
New projects open in Read-only, so GitCompass only looks. When you allow changes, it shows what will happen before it does anything.

**Does it stop other apps from changing my files?**
No. Its safety settings only apply inside GitCompass.

**Windows says the app is unrecognized. Is that normal?**
Yes, for now. This early version isn't digitally signed yet.

**Where can I read more?**
See the [user guide](docs/user-guide.md), or the [full manual](docs/functional-documentation.md) for every screen in detail.

## For developers

Building from source, tests and project structure are covered in **[docs/DEVELOPING.md](docs/DEVELOPING.md)**.
