# Copilot Instructions — pdn-liquified

Paint.NET 5.x plugins providing GPU-accelerated distortion effects via ComputeSharp D2D1: brush-based Liquified, mesh-based Grid Warpified, and Doc Recitified. Display branding differs from the unchanged GridWarp/DocumentRectify project, namespace, and DLL identities.

## Build

```powershell
dotnet build LiquifiedPlugins.slnx                 # Debug
dotnet build -c Release LiquifiedPlugins.slnx      # Release: IL trim + ILRepack merge; no deployment
```

**Prerequisites:** .NET 9.0 SDK and stable Paint.NET 5.1.12 reference assemblies (default `C:\Program Files\paint.net`).
Override the reference path with `/p:PdnRoot="<path>"`. Do not build these .NET 9 plugins against development assemblies requiring .NET 11.
Deployment is opt-in with `/p:DeployPlugins=true`; it uses the actual Documents Effects folder.

Run `dotnet run --project tests\Liquified.RegressionTests -c Release`,
`.\tests\Installer.Tests.ps1`, and
`.\tests\Test-PluginCompatibility.ps1 -PortableDirectory '<disposable portable host>'`.
Signing regressions use Pester 5.7.1 in `tests\ReleaseSigning.Tests.ps1`.
The Inno Setup installer is defined in `installer\DistortionPlugins.iss`.
`scripts\Build-Installer.ps1` compiles it from a flat package using Inno Setup 6.5.4;
`-PackageVersion` is mandatory and independent of `5.1.*` assembly compatibility versions.
Build dispatch accepts `package_version`; signed packaging preserves `PACKAGE-VERSION.txt`.
`-Signed` requires signed DLLs and signs Setup/Uninstall with the existing Azure profile.
`tests\InnoInstaller.Tests.ps1` installs/upgrades/uninstalls in an isolated destination;
it refuses to run if a real wizard installation is already registered.
Also verify in Paint.NET (**Effects > Tools > Liquified**, **Grid Warpified**, and **Doc Recitified**):
automated checks do not validate live GPU rendering, pen hardware, or mixed-DPI interaction.

## Architecture

| File | Role |
| --- | --- |
| `Liquified/LiquifiedEffect.cs` | `GpuImageEffect<LiquifiedConfigToken>` entry point; uploads the inverse deformation field and renders via `LiquifiedSampleMapShader`. |
| `Liquified/LiquifiedDeformation.cs` | Shared CPU/GPU sample-map evaluation, chronological masks, reconstruction, and resource validation. |
| `Liquified/LiquifiedTypes.cs` | Brush modes, strokes, and chronological history actions. |
| `Liquified/LiquifiedConfigForm.cs` | Interactive `EffectConfigForm2` dialog — zoom/pan canvas, brush painting, freeze mask, undo/redo, CPU preview. |
| `Liquified/LiquifiedConfigToken.cs` | Strongly-typed `EffectConfigToken` — mode, brush settings, quality, and stroke list. |
| `GridWarp/GridWarpEffect.cs` | `GpuImageEffect<GridWarpConfigToken>` with `GridWarpSampleMapShader` — bilinear displacement from grid control points. |
| `GridWarp/GridWarpConfigForm.cs` | Interactive `EffectConfigForm2` dialog — draggable grid points, zoom/pan, grid overlay, CPU preview. |
| `GridWarp/GridWarpConfigToken.cs` | `EffectConfigToken` — grid dimensions, quality, flattened displacement array. |
| `DocumentRectify/DocumentRectifyEffect.cs` | `GpuImageEffect<DocumentRectifyConfigToken>` with `DocumentRectifySampleMapShader` — homography-based perspective rectification. |
| `DocumentRectify/DocumentRectifyConfigForm.cs` | Interactive `EffectConfigForm2` dialog — draggable corners, auto-detect, aspect controls, CPU preview. |
| `DocumentRectify/DocumentRectifyConfigToken.cs` | `EffectConfigToken` — corner coordinates, quality, and aspect settings. |
| `DialogUi.cs` | Shared theme-aware icons, information controls, collapse controls, and dialog actions. |
| `EffectSupportInfo.cs` | Shared Paint.NET menu-hover support metadata derived from assembly attributes. |
| `Liquified/LiquifiedPreferences.cs` | Local JSON last-used/default brush and display preferences, excluding image edits. |
| `paintdotnet_src/` | Decompiled Paint.NET reference code. **Excluded from compilation.** Read-only reference. |

## Key patterns

- **Namespace per project:** `LiquifiedPlugin`, `GridWarpPlugin`, and `DocumentRectifyPlugin`.
- **10 liquified brush modes:** ForwardWarp, Pucker, Bloat, TwistCW, TwistCCW, PushLeft, Reconstruct, Turbulence, Freeze, Unfreeze.
- **GPU rendering:** `SampleMapRenderer` samples the original image using an uploaded full-resolution inverse map; no stroke batches or point cap. Quality controls RGSS multisampling.
- **CPU preview:** Background incremental replay uses the same deformation evaluator; cached map and mask share a history boundary. Bilinear preview sampling differs intentionally from GPU RGSS.
- **Resource limits:** Liquified rejects documents above 8,388,608 pixels or device/dimension limits; no silent downsampling. Both Liquified and Grid Warpified retain at most 256 undo steps.
- **Grid Warpified:** Independent dimensions from 2 to 50 cells (up to 2,601 points). Dimension changes reset displacements, freeze/link constraints, and history. Linked unfrozen points move together; multi-selection alone does not imply group movement.
- **Settings persistence:** Liquified stores shared brush/view preferences in `%LOCALAPPDATA%\pdn-liquified`, not per-file preferences. Grid Warpified constraints persist in tokens; its overlay settings and editing mode are dialog-only. Doc Recitified has no local undo/redo history.
- **Keyboard editing:** Grid Warpified/Doc Recitified use Ctrl+Page Up/Down for point selection, arrows for nudging, F6 for the X editor, and normal Tab focus traversal.
- **Smoothstep falloff:** `s*s*(3-2*s)` with density modulation. No blend cap — cumulative displacement.
- **DPI scaling:** `AutoScaleMode.None`; manual `_dpi = DeviceDpi / 96f` with `S(int v)` helper.
- **Obsolescence warning:** `#pragma warning disable CS0612, CS0618` for `EffectConfigForm2`.

## GPU shader conventions (ComputeSharp D2D1)

```csharp
[D2DInputCount(1)]
[D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
[D2DGeneratedPixelShaderDescriptor]
[AutoConstructor]
internal readonly partial struct MyShader : ID2D1PixelShader { ... }
```

Liquified and Grid Warpified upload displacement textures sampled with linear filtering.
Dispose retained device-effect arrays before graph replacement/invalidation, including
partially initialized arrays after creation failures.

## Conventions

- C# latest, nullable reference types enabled, `unsafe` allowed.
- PascalCase for public members, `_camelCase` for private fields.
- Paint.NET DLL references use `Private=False` — never bundled in output.
- Canvas uses region-based partial invalidation during mouse moves; full invalidate on pan/zoom.
- All `Surface` and `IDeviceEffect` objects must be manually disposed.

## What NOT to do

- Do not add code to `paintdotnet_src/` — it is excluded from compilation.
- Do not set `Private=True` on Paint.NET assembly references.
- Do not remove `#pragma warning disable` — the suppressed warnings are intentional.
