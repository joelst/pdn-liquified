# Paint.NET Distortion Plugins (Liquified, Grid Warp, Document Rectify)

GPU-accelerated and high-DPI compatible distortion effects for Paint.NET 5.x: interactive brush-based liquified, mesh-based grid warp, and document perspective rectification.

**Documentation:**

- [Installation Guide](./INSTALL.md) — Step-by-step installation instructions and troubleshooting
- [Release Notes](./RELEASES.md) — How to create releases and distribution guide
- [Security Policy](./SECURITY.md) — Reporting security issues

## Features

### Liquified (Effects > Tools > Liquified)

- **10 brush modes** -- Forward Warp, Pucker, Bloat, Twist CW, Twist CCW, Push Left, Reconstruct, Turbulence, Freeze, Unfreeze
- **GPU rendering** via ComputeSharp D2D1 sample-map shaders with RGSS multisampling
- **Interactive canvas** -- paint strokes directly with real-time CPU preview; GPU renders the final output
- **Zoom and pan** -- mouse-wheel zoom (10%--800%), right-click pan, double-click fit-to-window
- **Adjustable parameters** -- Brush Size, Pressure/Strength, Density, stationary Rate, Quality, optional Windows pen pressure
- **Reconstruction and masks** -- global reconstruction amount, local Reconstruct brush, Restore All, Mask None/All/Invert, and independent mask visibility
- **View options** -- deformation mesh with adjustable spacing, document-composition backdrop, selection-aware preview, and processing status
- **Undo / Redo / Reset** with keyboard shortcuts

Liquify keyboard controls: `[`/`]` or keypad minus/plus changes brush size; arrows
move and apply the keyboard brush (Shift uses a larger step); Ctrl+arrows pans.
`Ctrl+0` resets zoom/pan, and `Ctrl+Z`/`Ctrl+Y` undo/redo. Focused sliders keep their
native arrow-key behavior. Pen pressure requires a driver reporting Windows pointer
pressure; otherwise brush intensity uses full pressure.

CPU preview uses bilinear image sampling; final Quality controls GPU RGSS multisampling.
Both use the same full-resolution inverse displacement map, with no stroke-count cap
and no repeated resampling of already-distorted image data. Full-resolution maps and
GPU uploads consume memory proportional to document area; the status does not imply
that a low-resolution preview reduces final rendering requirements.

Liquify currently accepts at most **8,388,608 pixels** and **16,384 pixels per
dimension**, additionally bounded by the active device's bitmap limit. This includes
3840 x 2160 (4K). It reserves a conservative 128 bytes/pixel within a 1 GiB pipeline
budget and rejects larger documents explicitly before image-sized allocations; it
does not silently downsample or omit strokes. Reduce document dimensions to use Liquify
on larger images. This resource limit does not apply to Grid Warp or Document Rectify.

Mask-wide actions and Restore All participate in undo/redo; Restore All removes
deformation while retaining protection. Liquify retains **256 undo steps**, using shared
stroke history rather than full-image snapshots. The mesh solves the inverse sampling
field for forward vertex locations so it follows visible content. Segments that cannot
be resolved because of folds or unreachable coordinates are omitted and reported in
status. Reconstruction is relative to the original coordinate system regardless of
history length.

Before release, check a high-contrast test image in the host: paint more than 256
applications, cross a freeze boundary, reconstruct earlier deformation, and compare
the dialog with the committed output. Check continuous stationary Rate, undo/redo of
mask actions, deformed mesh, backdrop with a partially transparent layer and selection,
keyboard-only editing, and a mixed-DPI monitor move. Verify pen pressure on actual
Windows Ink hardware; type-loading checks do not exercise GPU rendering or pen input.

### Grid Warp (Effects > Tools > Grid Warp)

- **Draggable control point grid** -- 2×2 to 6×6 cells (up to 49 control points)
- **Bilinear displacement interpolation** -- smooth distortion across the image
- **GPU rendering** via sample-map shader with RGSS multisampling
- **Interactive canvas** -- drag points with a bounded, cancellable background preview; GPU renders full-resolution output
- **Grid overlay** -- toggleable grid lines and control point visualization
- **Undo / Redo / Reset** with keyboard shortcuts
- **Accessible point editing** -- select points with Ctrl+Page Up/Down, nudge with arrows (Shift for 10 pixels), or edit X/Y numerically; Tab/Shift+Tab leaves the canvas

### Document Rectify (Effects > Tools > Document Rectify)

- **Perspective correction** -- map a user-selected quadrilateral to a rectified rectangle
- **Interactive corners** -- drag TL/TR/BR/BL handles with live preview
- **Keyboard and numeric corners** -- Ctrl+Page Up/Down selects a corner, arrows nudge it, and F6 focuses the X coordinate editor; Tab/Shift+Tab leaves the canvas
- **Aspect options** -- Fill Canvas, Preserve Detected, Source, Square, Letter, Legal, A4, Custom
- **Auto Detect** -- estimate document corners from content against background
- **GPU rendering** via sample-map shader with RGSS multisampling

Grid Warp and Document Rectify canvases show keyboard focus. `Ctrl+0` resets the view
to 100%; Grid Warp supports `Ctrl+Z`/`Ctrl+Y` for keyboard and numeric edits. Both
dialogs rescale when moved between monitors with different DPI.

## Build

```powershell
dotnet build LiquifiedPlugins.slnx                 # Debug
dotnet build -c Release LiquifiedPlugins.slnx      # Release (IL trim + ILRepack merge; no deployment)
```

**Prerequisites:** .NET 9.0 SDK, stable Paint.NET 5.1.12 reference assemblies (default `C:\Program Files\paint.net`).
Override the reference path with `/p:PdnRoot="<path>"`, including a disposable portable installation.
Do not use development-main assemblies requiring .NET 11 to build these .NET 9 plugins.

Release builds automatically:

1. Trim unused IL via `Microsoft.NET.ILLink`
2. Merge ComputeSharp assemblies via ILRepack
Deployment is opt-in: `/p:DeployPlugins=true` copies to the actual Windows Documents
Effects directory. `/p:PdnEffectsDirOverride="<directory>"` overrides that target.
Copy failures fail the build. Ordinary builds and CI never modify installed plugins.

Automated regression and compatibility checks:

```powershell
dotnet run --project tests\Liquified.RegressionTests -c Release
dotnet run --project tests\Liquified.RegressionTests -c Release -- --benchmark
.\tests\Installer.Tests.ps1
.\tests\Test-PluginCompatibility.ps1 -PortableDirectory 'C:\path\to\disposable\portable\paint.net'
```

The compatibility check loads the merged Release plugins using the portable host's own
runtime, resolves types, JIT-checks effect/dialog/token methods, clones tokens, and
constructs/disposes effects and dialogs. It is not a GPU
render or a UI test. Also load Paint.NET and exercise all three effects (**Effects > Tools >
Liquified**, **Grid Warp**, and **Document Rectify**), including mixed-DPI and pen input.

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
   - Find all DLLs in a release package or repository Release outputs
   - Copy to the actual user Documents Effects directory, including redirected folders
   - Support optional system installation with `-InstallationScope System`
   - Verify every DLL's SHA256 and report failures with a nonzero exit code

3. **Manual Installation** (if the script doesn't work):
   - Locate Paint.NET installation: typically `C:\Program Files\paint.net`
   - Copy the three DLL files to: `C:\Program Files\paint.net\Effects\`
   - Or use your actual Documents folder's `paint.net App Files\Effects\`
     (Create the `Effects` folder if it doesn't exist; do not install duplicate copies)
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

Run `.\install-plugins.ps1` after the Release build, or opt into build deployment (see Build above).

## CI Build Pipeline

This repository includes a GitHub Actions workflow at `.github/workflows/build.yml`.

- Triggers on pushes to `main`, pull requests targeting `main`, and manual runs.
- Installs .NET 9 SDK.
- Builds against stable portable Paint.NET references in Release, without deployment.
- Runs deformation regressions and installer checks.
- Loads the same merged plugins in stable and beta portable host runtimes.
- Publishes channel-labelled DLL and flat release-package artifacts.
- A separate **Signed release** workflow signs successful main-build DLLs using Azure
  Artifact Signing and OIDC, then produces a verified signed ZIP. See [release signing](./RELEASES.md#authenticode-signing).

## Architecture

| Project/File                                    | Role                                                                                              |
| ----------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `Liquified/LiquifiedEffect.cs`                  | Main `GpuImageEffect<LiquifiedConfigToken>` entry point and liquified shader pipeline.            |
| `Liquified/LiquifiedConfigForm.cs`              | Interactive brush UI with zoom/pan, freeze mask painting, undo/redo, CPU preview.                 |
| `Liquified/LiquifiedConfigToken.cs`             | Strongly typed token for liquified settings and stroke list.                                      |
| `Liquified/LiquifiedDeformation.cs`             | Shared inverse displacement-field evaluation, mask chronology, reconstruction, and validation.    |
| `Liquified/LiquifiedTypes.cs`                   | Brush modes, strokes, and mask commands shared with pure regression tests.                        |
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
