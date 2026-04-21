# Paint.NET Distortion Plugins (Liquified, Grid Warp, Document Rectify)

GPU-accelerated distortion effects for Paint.NET 5.x: interactive brush-based liquified, mesh-based grid warp, and document perspective rectification.

📖 **Documentation:**
- [Installation Guide](./INSTALL.md) — Step-by-step installation instructions and troubleshooting
- [Release Notes](./RELEASES.md) — How to create releases and distribution guide
- [Security Policy](./SECURITY.md) — Reporting security issues

## Features

### Liquified (Effects > Tools > Liquified)

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
dotnet build LiquifiedPlugins.slnx                 # Debug
dotnet build -c Release LiquifiedPlugins.slnx      # Release (IL trim + ILRepack merge + deploy)
```

**Prerequisites:** .NET 9.0 SDK, Paint.NET 5.x installed (default `C:\Program Files\paint.net`).
Override the install path with `/p:PdnRoot="<path>"`.

Release builds automatically:
1. Trim unused IL via `Microsoft.NET.ILLink`
2. Merge ComputeSharp assemblies via ILRepack
3. Copy each plugin DLL to `$(PdnRoot)\Effects\` (may require admin)
4. Copy each plugin DLL to `%USERPROFILE%\Documents\paint.net App Files\Effects\`

There are no automated tests. Verify by loading Paint.NET and running all three effects (**Effects > Tools > Liquified**, **Grid Warp**, and **Document Rectify**).

## Installation

### For End Users (from Release Builds)

**Quick Install:**
1. Download the latest release from [Releases](../../releases/latest)
2. Double-click `install-plugins.bat`
3. Done! Restart Paint.NET

For detailed instructions, troubleshooting, and manual installation, see [INSTALL.md](./INSTALL.md).

### For End Users (from Release Builds - Step by Step)
2. **Run the installer script** (recommended):
   ```powershell
   .\install-plugins.ps1
   ```
   The script will:
   - Detect your Paint.NET installation directory
   - Copy plugin DLLs to both system and user plugin folders
   - Handle administrator elevation if needed
   - Verify successful installation

3. **Manual Installation** (if the script doesn't work):
   - Locate Paint.NET installation: typically `C:\Program Files\paint.net`
   - Copy the three DLL files to: `C:\Program Files\paint.net\Effects\`
   - Also copy to: `%USERPROFILE%\Documents\paint.net App Files\Effects\`
     (Create the `Effects` folder if it doesn't exist)
   - Restart Paint.NET

4. **Verify Installation:**
   - Open Paint.NET
   - Go to **Effects > Tools**
   - You should see:
     - Liquified
     - Grid Warp
     - Document Rectify

### For Developers (from Source)

Build and deploy locally:
```powershell
cd pdn-liquified
dotnet build -c Release LiquifiedPlugins.slnx
```

The Release build automatically copies DLLs to your Paint.NET installation (see Build section above).

## CI Build Pipeline

This repository includes a GitHub Actions workflow at `.github/workflows/build.yml`.

- Triggers on pushes to `main`, pull requests targeting `main`, and manual runs.
- Installs .NET 9 SDK.
- Downloads the latest portable Paint.NET release and sets `PdnRoot` for CI builds.
- Restores and builds `LiquifiedPlugins.slnx` in Debug configuration.
- Publishes plugin DLLs as downloadable workflow artifacts.

## Architecture

| Project/File                                    | Role                                                                                              |
| ----------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `Liquified/LiquifiedEffect.cs`                  | Main `GpuImageEffect<LiquifiedConfigToken>` entry point and liquified shader pipeline.            |
| `Liquified/LiquifiedConfigForm.cs`              | Interactive brush UI with zoom/pan, freeze mask painting, undo/redo, CPU preview.                 |
| `Liquified/LiquifiedConfigToken.cs`             | Strongly typed token for liquified settings and stroke list.                                      |
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
- One namespace per plugin project: `LiquifiedPlugin`, `GridWarpPlugin`, and `DocumentRectifyPlugin`.
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
