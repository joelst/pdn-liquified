# Copilot Instructions — pdn-liquify5

Paint.NET 5.x plugin providing interactive brush-based liquify effects with GPU-accelerated rendering via ComputeSharp D2D1.

## Build

```powershell
dotnet build LiquifyPlugin.sln                 # Debug
dotnet build -c Release LiquifyPlugin.sln      # Release → IL trim + ILRepack merge + auto-deploy
```

**Prerequisites:** .NET 9.0 SDK, Paint.NET 5.x installed (default `C:\Program Files\paint.net`).
Override the install path with `/p:PdnRoot="<path>"`.

There are no automated tests — verify by loading Paint.NET and running the effect (**Effects > Tools > Liquify5**).

## Architecture

| File | Role |
|------|------|
| `LiquifyEffect.cs` | Main `GpuImageEffect<LiquifyConfigToken>` entry point. Contains `LiquifyMode` enum (8 modes), `BrushStroke` class, `LiquifySampleMapShader` (D2D1 pixel shader), `PackedStrokePoint` packing. |
| `LiquifyConfigForm.cs` | Interactive `EffectConfigForm2` dialog — zoom/pan canvas, brush painting, undo/redo, CPU preview with per-pixel displacement accumulation matching the GPU shader. |
| `LiquifyConfigToken.cs` | Strongly-typed `EffectConfigToken` — Mode, BrushSize, Strength, Density, Quality, Strokes list. |
| `paintdotnet_src/` | Decompiled Paint.NET reference code. **Excluded from compilation.** Read-only reference. |

## Key patterns

- **Single namespace:** `LiquifyPlugin` — no nested namespaces.
- **8 brush modes:** ForwardWarp, Pucker, Bloat, TwistCW, TwistCCW, PushLeft, Reconstruct, Turbulence.
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

Strokes are packed into batches of 16 (`PackedStrokePoint`: pos, radiusDensity, modeStrength) for GPU dispatch.

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
