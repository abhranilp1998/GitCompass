# Responsive layout and Windows glass update

13 October 2026. This source update follows the [beginner Fluent update](beginner-fluent-update.md) and the user's screenshot of a crowded smaller Settings window. The existing 0.1.1 portable ZIP and exported manuals have not been regenerated.

## Try the current source

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -ExecutionPolicy Bypass -File scripts/run.ps1 -Development
```

Open Settings and resize the window. Choose **Window background → Glass (Acrylic)** to try the native frosted effect, or keep **Mica (Windows default)**. Appearance still offers System, Light, and Dark. Appearance and background material save immediately and independently.

## Layout changes

The screenshot showed a fixed sidebar consuming almost a third of the small window, project actions sharing a crowded header, and an activity toolbar wrapping across multiple rows. The new shell adapts the existing controls without rebuilding the page on resize, so drafts, selections and operation reviews survive.

| Area | Behavior |
| --- | --- |
| Navigation | Below 1000 logical client-width units, a menu button opens a drawer with the same page names. Choose an item to close it; keyboard selection can move between items before Enter or Space activates one. Escape, the close button, and the outside surface dismiss it. Wider windows restore the sidebar. |
| Header | Project actions stack below the summary when the available content width is below 680. Long paths, branches and breadcrumbs use ellipsis and retain full tooltips. With no project, empty project controls no longer reserve header space. |
| Settings | Controls move below their descriptions when the card's available width is below 600. The material choice, quiet-hour inputs and ordinary preference controls use the same layout helpers. |
| Shared content | Two-column groups stack below 700; three-column groups stack below 640. Long action labels and walkthrough headings wrap. History limits its graph column to retain readable commit labels. |
| Activity and Terminal | Closed tools occupy zero space. The open dock is capped using actual client height and the measured header/banner. Compact Activity keeps filters and puts secondary commands under More; Terminal keeps New PowerShell session plus More. The existing command and permission handlers are retained. |
| Notices | Long notices have a bounded, scrollable area. At the minimum window size with tools open, the layout reserves a page viewport and visible toolbar/session area rather than allowing the notice to push tools beyond the client. |

The supported minimum outer window size is now **640 × 560 logical units**. WPF controls and breakpoints use device-independent units, so physical pixels depend on Windows display scaling. Resizing, opening navigation and changing the background never initialize Git or grant write access.

## What glass means here

The implementation uses documented Windows DWM materials: **Mica** for a subtle wallpaper tint, **Acrylic** for a frosted glass background, and **Solid** for an opaque surface. This is Windows frosted glass; custom refractive Liquid Glass shaders and fluid lens animations are not included. Mica remains the default main-window material, consistent with Microsoft's guidance. Content cards retain opaque backgrounds for readable text.

The native background API requires **Windows 11 22H2 / build 22621 or later**. The app retains the ordinary Windows frame, caption buttons, resizing and Snap behavior; it does not create a transparent, borderless layered window. It uses a solid fallback when Windows contrast mode or disabled transparency requires one, during a remote desktop session, on unsupported systems, or when the native API/preferences cannot be used. Acrylic also falls back during energy saver. Settings explains the effective material or fallback and retains the user's choice for later.

The backdrop controller listens for appearance, transparency and power changes and removes its event hooks when the window closes. The additive material preference defaults older settings files to Mica while preserving the saved theme and other preferences. Unsupported material values are rejected without replacing the saved record.

Primary references: [DWM system backdrop types and supported Windows build](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type), [Microsoft Acrylic guidance and fallbacks](https://learn.microsoft.com/en-us/windows/apps/design/style/acrylic).

## Verification

The solution builds with zero warnings and errors. The new native suite passed **196 checks** across Light/Dark Settings at 1200 × 860, 900 × 620, 760 × 640, 640 × 600, and 640 × 560, plus compact Home, Learn, Projects, an ordinary folder, open Activity, open Terminal and a long notice. It verifies real bounds, no horizontal control clipping, drawer activation, no page rebuild on resize/current-page activation, preserved unsaved interval drafts and access selection, restored wide navigation, zero closed-dock space, overflow actions and material persistence.

On this host, actual native window handles reported successful DWM attribute 38 values **1 (Solid), 2 (Mica), and 3 (Acrylic)**. A fresh shell restored the saved Acrylic preference and reported value 3 again. All other preferences remained equal. The adapter suite passed **34 checks**, including old-settings compatibility, material persistence, rejection without overwrite, and the existing 400 concurrent readers during 40 atomic saves.

Regression evidence also passed **14 page/theme renders**, **38 desktop interactions**, and **52 beginner workflow checks**, including a real reviewed local commit, untouched unselected files, pause/resume, fresh-session Read-only, and automatic appearance persistence. These use disposable fixtures and no live hosted submissions. A render test was updated to deliberately open Activity before checking visible per-operation controls, because closed tools are now genuinely collapsed.

The final responsive captures are in `.artifacts/desktop-responsive-tests/9007b57e5c664dd0a294de479ad1a4ea/`; the saved results are `.artifacts/responsive-glass-20261013/responsive-results.log`. Main-page renders are in `.artifacts/desktop-tests/fc2766733d7f41afbbdc9bb703c3386a/`; the final 38 interaction checks use `.artifacts/desktop-tests/e2cb0008a70e4c30bc7da28d261f41e0/`. Beginner captures are in `.artifacts/native-beginner/7b2bd2fb0bd348f6ba99b83489c7b67c/`. The supplied screenshot is preserved separately as `.artifacts/responsive-glass-20261013/00-user-small-window.png`.

These are actual WPF layout renders. A WPF bitmap cannot include DWM's desktop compositor, so captures explicitly use an opaque theme underlay and the exact client viewbox. The native attribute checks verify the requested Windows material was accepted; screenshots do not establish desktop blur appearance, animation smoothness, GPU/power cost or behavior on every Windows installation. Those visual checks, high-contrast/transparency changes, multiple display scales and representative novice testing remain device acceptance work.

## Code and rollback

Shared reflow helpers are in `AdaptiveLayout.cs`; shell resizing/navigation/tool overflow are in `ShellWindow.Responsive.cs`; native material lifecycle is isolated in `WindowsBackdrop.cs`. The original operation engine and access model are retained. Material preferences remain outside project folders.

This update is saved as a local checkpoint tagged **responsive-glass-checkpoint**. Earlier **beginner-fluent-checkpoint** and **baseline-before-fluent-redesign** checkpoints are retained. From a clean working tree, `git revert responsive-glass-checkpoint` creates a reversal commit while preserving history; rebuild before running the reverted source. Preserve any later changes before reverting. No remote publication is part of this update.
