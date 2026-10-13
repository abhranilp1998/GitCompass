# One-page Liquid Glass experiment

13 October 2026. This is a native WPF design preview for evaluating a more expressive glass treatment. It keeps the normal Windows 11 interface and existing Mica/Acrylic preferences.

## Open it

Choose **Settings → Try a different design → Open Liquid Glass preview**, or run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run.ps1 -LiquidGlassPreview
```

The switch selects the current development source automatically, even when an older portable ZIP is present. A project is not required. The page starts at the top; scroll to the material controls and additional sample content in smaller windows. **Back to Settings** returns to the ordinary interface.

## What to try

- Switch **Light/Dark** inside the preview. This changes its own background and colors without saving application preferences.
- Move the pointer over a lens to change its reflected highlight and secondary scene sample.
- Adjust **Glass strength** to compare the tint, blur and refracted edge.
- Use **Change scene** to compare three background palettes.
- Choose **Overview**, **Changed files**, or **Saved versions**. These change sample text only; the sample action shows an example result.

“Weekend notes,” its files and version counts are demonstration data. The preview does not open a project, initialize Git, create a commit, upload work or grant access. The ordinary operation engine is retained.

## How the material works

Each lens samples an independent, page-owned background with a `VisualBrush`, blurs that sample and applies a packaged WPF pixel shader. The shader bends sampling near rounded edges and adds a small red/blue separation. A tint, directional rim highlights, reflection and shadow form separate layers. Labels and interactive controls are placed above the optical layers, so they remain sharp and stable.

The shader is original HLSL compiled to `ps_2_0`, with 53 instruction slots and 1,200 bytes of packaged bytecode. The executable does not compile shaders at runtime or require an additional SDK. To rebuild the asset as a developer, use `powershell -ExecutionPolicy Bypass -File scripts/compile-liquid-shader.ps1`; it uses Windows `d3dcompiler_47`.

This is a custom Windows approximation inspired by [Apple's material guidance](https://developer.apple.com/design/human-interface-guidelines/materials) and [custom Liquid Glass behavior](https://developer.apple.com/documentation/swiftui/applying-liquid-glass-to-custom-views). It is not Apple's native rendering engine. The experimental page deliberately makes the optical treatment prominent for comparison; it is not a decision to apply this amount of glass throughout GitCompass. Apple's guidance favors sparing use on floating controls and navigation.

High contrast and disabled Windows transparency use solid surfaces. Unsupported rendering acceleration disables optical effects; lack of shader support keeps the blur/rim fallback. Reduced Windows animation preferences disable pointer reactions. Preference listeners are attached only while the component is loaded and removed when it leaves the page. There are no continuous rendering loops or animation timers.

## Verification and rollback

The solution builds with zero warnings and errors. The focused native suite passed **59 checks**: light/dark renders at 1200 × 860 and 640 × 560, usable content bounds, working controls, retained state during resize, unchanged persisted preferences, absence of registered projects, and listener cleanup on leaving/revisiting. Refraction, blur and pointer effects were active on this host at WPF rendering tier 2. It records active refraction separately from visual bitmap evidence. These captures include the page's own scene and effects; the desktop compositor remains outside WPF bitmap captures.

The final captures and `evidence.json` are under `.artifacts/desktop-liquid-tests/df47f820cb154a24b2b9dd9c43a5a33e/`. The existing **196 responsive checks** and **38 desktop interaction checks** also passed after integration; their fixtures are `.artifacts/desktop-responsive-tests/2905c5d6b82c499584ae81365bdeb6d9/` and `.artifacts/desktop-tests/9db74c04f39c41b69afdfe10c5fb8f3d/` respectively. No hosted requests were made.

The preview is a visual experiment. Performance on other devices, touch behavior, display scaling and representative novice usability remain acceptance work. Existing portable packages have not been regenerated.

The local checkpoint is tagged **liquid-glass-preview-checkpoint**. Earlier responsive and beginner checkpoints remain available. From a clean working tree, `git revert liquid-glass-preview-checkpoint` creates a reversal commit while retaining history. Preserve later changes first and rebuild before running the reverted source.
