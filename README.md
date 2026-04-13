# LiquifyPlugin for Paint.NET 5.x

Interactive brush-based liquify effect plugin with GPU-accelerated rendering for Paint.NET 5.x.

## Features

- **8 brush modes** -- Forward Warp, Pucker, Bloat, Twist CW, Twist CCW, Push Left, Reconstruct, Turbulence
- **GPU rendering** via ComputeSharp D2D1 sample-map shaders with RGSS multisampling
- **Interactive canvas** -- paint strokes directly with real-time CPU preview; GPU renders the final output
- **Zoom and pan** -- mouse-wheel zoom (10%--800%), right-click pan, double-click fit-to-window
- **Adjustable parameters** -- Brush Size (5--400 px), Strength (5--100, step 5), Density, Quality
- **Undo / Redo / Reset** with keyboard shortcuts
- **Maximizable dialog**

## Build

```powershell
dotnet build LiquifyPlugin.sln                 # Debug
dotnet build -c Release LiquifyPlugin.sln      # Release (IL trim + ILRepack merge + deploy)
```

**Prerequisites:** .NET 9.0 SDK, Paint.NET 5.x installed (default `C:\Program Files\paint.net`).
Override the install path with `/p:PdnRoot="<path>"`.

Release builds automatically:
1. Trim unused IL via `Microsoft.NET.ILLink`
2. Merge ComputeSharp assemblies via ILRepack
3. Copy the final `LiquifyPlugin.dll` to `$(PdnRoot)\Effects\` (may require admin)

There are no automated tests. Verify by loading Paint.NET and running the effect (**Effects > Tools > Liquify5**).

## Architecture

| File                    | Role                                                                                                                                                                                          |
| ----------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `LiquifyEffect.cs`      | Main `GpuImageEffect<LiquifyConfigToken>` entry point. Contains `LiquifyMode` enum, `BrushStroke` class, `LiquifySampleMapShader` (D2D1 pixel shader), and `PackedStrokePoint` packing logic. |
| `LiquifyConfigForm.cs`  | Interactive `EffectConfigForm2` dialog -- zoom/pan canvas, brush painting, undo/redo, CPU preview.                                                                                            |
| `LiquifyConfigToken.cs` | Strongly-typed `EffectConfigToken` carrying Mode, BrushSize, Strength, Density, Quality, and stroke list.                                                                                     |
| `paintdotnet_src/`      | Decompiled Paint.NET reference code. **Read-only reference, excluded from compilation.**                                                                                                      |

## Brush Modes

| Mode         | Description                                                  |
| ------------ | ------------------------------------------------------------ |
| Forward Warp | Drag pixels in the direction of the brush stroke             |
| Pucker       | Pull pixels inward toward the brush center                   |
| Bloat        | Push pixels outward from the brush center                    |
| Twist CW     | Rotate pixels clockwise around the brush center              |
| Twist CCW    | Rotate pixels counter-clockwise around the brush center      |
| Push Left    | Shift pixels perpendicular to the drag direction             |
| Reconstruct  | Restore displaced pixels back toward their original position |
| Turbulence   | Add random displacement to pixel positions within the brush  |

## Conventions

- C# latest, nullable reference types enabled, `unsafe` allowed.
- Single namespace: `LiquifyPlugin`.
- PascalCase for public members, `_camelCase` for private fields.
- Paint.NET DLL references use `Private=False` -- never bundled in output.
- DPI scaling: `AutoScaleMode.None` with manual `_dpi` factor and `S()` helper.
- All `Surface` and `IDeviceEffect` objects must be manually disposed.

## Roadmap

- **Grid Warp** -- mesh-based warp grid with draggable control points for global image deformation.
- **Smudge Brush** -- finger-paint style brush that picks up and pushes color along the stroke. Requires full custom CPU/GPU implementation (Paint.NET has no native smudge mode).
