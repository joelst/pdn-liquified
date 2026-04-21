# Paint.NET Distortion Plugins (Liquify, Grid Warp, Document Rectify)

GPU-accelerated distortion effects for Paint.NET 5.x: interactive brush-based liquify, mesh-based grid warp, and document perspective rectification.

## Features

### Liquify (Effects > Tools > Liquify5)

- **10 brush modes** -- Forward Warp, Pucker, Bloat, Twist CW, Twist CCW, Push Left, Reconstruct, Turbulence, Freeze, Unfreeze
- **GPU rendering** via ComputeSharp D2D1 sample-map shaders with RGSS multisampling
- **Interactive canvas** -- paint strokes directly with real-time CPU preview; GPU renders the final output
- **Zoom and pan** -- mouse-wheel zoom (10%--800%), right-click pan, double-click fit-to-window
- **Adjustable parameters** -- Brush Size, Strength, Density, Quality
- **Undo / Redo / Reset** with keyboard shortcuts

### Grid Warp (Effects > Tools > Grid Warp)

- **Draggable control point grid** -- 2×2 to 6×6 cells (up to 49 control points)
- **Bilinear displacement interpolation** -- smooth distortion across the image
- **GPU rendering** via sample-map shader with RGSS multisampling
- **Interactive canvas** -- drag points with real-time CPU preview; GPU renders the final output
- **Grid overlay** -- toggleable grid lines and control point visualization
- **Undo / Redo / Reset** with keyboard shortcuts

### Document Rectify (Effects > Tools > Document Rectify)

- **Perspective correction** -- map a user-selected quadrilateral to a rectified rectangle
- **Interactive corners** -- drag TL/TR/BR/BL handles with live preview
- **Aspect options** -- Fill Canvas, Preserve Detected, Source, Square, Letter, Legal, A4, Custom
- **Auto Detect** -- estimate document corners from content against background
- **GPU rendering** via sample-map shader with RGSS multisampling

## Build

```powershell
dotnet build LiquifyPlugins.slnx                 # Debug
dotnet build -c Release LiquifyPlugins.slnx      # Release (IL trim + ILRepack merge + deploy)
```

**Prerequisites:** .NET 9.0 SDK, Paint.NET 5.x installed (default `C:\Program Files\paint.net`).
Override the install path with `/p:PdnRoot="<path>"`.

Release builds automatically:
1. Trim unused IL via `Microsoft.NET.ILLink`
2. Merge ComputeSharp assemblies via ILRepack
3. Copy each plugin DLL to `$(PdnRoot)\Effects\` (may require admin)
4. Copy each plugin DLL to `%USERPROFILE%\Documents\paint.net App Files\Effects\`

There are no automated tests. Verify by loading Paint.NET and running all three effects (**Effects > Tools > Liquify5**, **Grid Warp**, and **Document Rectify**).

## Architecture

| Project/File                                    | Role                                                                                              |
| ----------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `Liquify/LiquifyEffect.cs`                      | Main `GpuImageEffect<LiquifyConfigToken>` entry point and liquify shader pipeline.                |
| `Liquify/LiquifyConfigForm.cs`                  | Interactive brush UI with zoom/pan, freeze mask painting, undo/redo, CPU preview.                 |
| `Liquify/LiquifyConfigToken.cs`                 | Strongly typed token for liquify settings and stroke list.                                        |
| `GridWarp/GridWarpEffect.cs`                    | Main `GpuImageEffect<GridWarpConfigToken>` with grid displacement shader pipeline.                |
| `GridWarp/GridWarpConfigForm.cs`                | Interactive grid point editing UI with zoom/pan and undo/redo.                                    |
| `GridWarp/GridWarpConfigToken.cs`               | Token for grid dimensions, quality, and flattened displacements.                                  |
| `DocumentRectify/DocumentRectifyEffect.cs`      | Main `GpuImageEffect<DocumentRectifyConfigToken>` with perspective rectification shader pipeline. |
| `DocumentRectify/DocumentRectifyConfigForm.cs`  | Interactive corner handle UI with auto-detect and aspect controls.                                |
| `DocumentRectify/DocumentRectifyConfigToken.cs` | Token for corners, quality, and output aspect settings.                                           |
| `paintdotnet_src/`                              | Decompiled Paint.NET reference code. Read-only reference, excluded from compilation.              |

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
| Freeze       | Protect pixels from subsequent non-freeze liquify strokes    |
| Unfreeze     | Remove freeze protection from previously frozen pixels       |

## Conventions

- C# latest, nullable reference types enabled, `unsafe` allowed.
- One namespace per plugin project: `LiquifyPlugin`, `GridWarpPlugin`, and `DocumentRectifyPlugin`.
- PascalCase for public members, `_camelCase` for private fields.
- Paint.NET DLL references use `Private=False` -- never bundled in output.
- DPI scaling: `AutoScaleMode.None` with manual `_dpi` factor and `S()` helper.
- All `Surface` and `IDeviceEffect` objects must be manually disposed.

## Security and Privacy

- No network calls, telemetry, analytics, or external process execution in plugin runtime code.
- No credentials, API keys, tokens, or personal data are required by these effects.
- Runtime state is limited to in-memory effect tokens and Paint.NET-provided image surfaces.
- Project metadata avoids embedding personal contact information.
- Before publishing changes, run a quick secret/PII scan and review generated logs/artifacts before commit.

## Roadmap

- **Smudge Brush** -- finger-paint style brush that picks up and pushes color along the stroke. Requires full custom CPU/GPU implementation (Paint.NET has no native smudge mode).
