# Copilot Instructions — pdn-liquify5

Paint.NET 5.x plugin providing GPU-accelerated distortion effects via ComputeSharp D2D1: interactive brush-based liquify and mesh-based grid warp.

## Build

```powershell
dotnet build LiquifyPlugins.slnx                 # Debug
dotnet build -c Release LiquifyPlugins.slnx      # Release → IL trim + ILRepack merge + auto-deploy
```

**Prerequisites:** .NET 9.0 SDK, Paint.NET 5.x installed (default `C:\Program Files\paint.net`).
Override the install path with `/p:PdnRoot="<path>"`.

There are no automated tests — verify by loading Paint.NET and running the effects (**Effects > Tools > Liquify5**, **Grid Warp**, and **Document Rectify**).

## Architecture

| File | Role |
| --- | --- |
| `Liquify/LiquifyEffect.cs` | Main `GpuImageEffect<LiquifyConfigToken>` entry point. Contains `LiquifyMode` enum (10 modes), `BrushStroke` class, `LiquifySampleMapShader`, and packed stroke dispatch. |
| `Liquify/LiquifyConfigForm.cs` | Interactive `EffectConfigForm2` dialog — zoom/pan canvas, brush painting, freeze mask, undo/redo, CPU preview. |
| `Liquify/LiquifyConfigToken.cs` | Strongly-typed `EffectConfigToken` — mode, brush settings, quality, and stroke list. |
| `GridWarp/GridWarpEffect.cs` | `GpuImageEffect<GridWarpConfigToken>` with `GridWarpSampleMapShader` — bilinear displacement from grid control points. |
| `GridWarp/GridWarpConfigForm.cs` | Interactive `EffectConfigForm2` dialog — draggable grid points, zoom/pan, grid overlay, CPU preview. |
| `GridWarp/GridWarpConfigToken.cs` | `EffectConfigToken` — grid dimensions, quality, flattened displacement array. |
| `DocumentRectify/DocumentRectifyEffect.cs` | `GpuImageEffect<DocumentRectifyConfigToken>` with `DocumentRectifySampleMapShader` — homography-based perspective rectification. |
| `DocumentRectify/DocumentRectifyConfigForm.cs` | Interactive `EffectConfigForm2` dialog — draggable corners, auto-detect, aspect controls, CPU preview. |
| `DocumentRectify/DocumentRectifyConfigToken.cs` | `EffectConfigToken` — corner coordinates, quality, and aspect settings. |
| `paintdotnet_src/` | Decompiled Paint.NET reference code. **Excluded from compilation.** Read-only reference. |

## Key patterns

- **Namespace per project:** `LiquifyPlugin`, `GridWarpPlugin`, and `DocumentRectifyPlugin`.
- **10 liquify brush modes:** ForwardWarp, Pucker, Bloat, TwistCW, TwistCCW, PushLeft, Reconstruct, Turbulence, Freeze, Unfreeze.
- **GPU rendering:** `SampleMapRenderer` with chained batches of `LiquifySampleMapShader` (16 stroke points per batch). RGSS multisampling for quality levels.
- **CPU preview:** Per-pixel displacement accumulation in `RegeneratePreview()`, matching GPU shader constants exactly.
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

Liquify strokes are packed into batches of 16 (`PackedStrokePoint`: pos, radiusDensity, modeStrength) for GPU dispatch.
Grid warp displacements are uploaded via an RGB128Float displacement texture sampled with linear filtering.

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
