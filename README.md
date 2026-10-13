# Paint.NET Distortion Plugins (Liquified, Grid Warpified, Doc Recitified)

GPU-accelerated and high-DPI compatible distortion effects for Paint.NET 5.x: **Liquified** for interactive brush-based distortion, **Grid Warpified** for mesh-based distortion, and **Doc Recitified** for document perspective correction.

**Version scope:** This README describes the current source, not every published
download. The published `1.0-beta1` prerelease has an older ZIP-only package;
the setup wizard and newer features described here are not included in that
release. See [installation options](./INSTALL.md#choose-the-right-package).

Effect menu names and dialog titles use the names above. DLL filenames remain
`LiquifiedPlugin.dll`, `GridWarpPlugin.dll`, and `DocumentRectifyPlugin.dll` so
upgrades replace existing plugins rather than creating duplicate installations.
Hovering each effect in Paint.NET's menu shows its plugin name, author, copyright,
build version, repository link, and DLL location. `EffectSupportInfo.cs` registers
Paint.NET support metadata for all three effects using their assembly metadata;
Paint.NET supplies the installed DLL location.

**Documentation:**

- [Installation Guide](./INSTALL.md) — Step-by-step installation instructions and troubleshooting
- [Release Distribution Guide](./RELEASES.md) — Signing, packaging, and publishing
- [Security Policy](./SECURITY.md) — Reporting security issues

## Features

### Liquified (Effects > Tools > Liquified)

- **10 brush modes** -- Forward Warp, Pucker, Bloat, Twist CW, Twist CCW, Push Left, Reconstruct, Turbulence, Freeze, Unfreeze
- **GPU rendering** via ComputeSharp D2D1 sample-map shaders with RGSS multisampling
- **Interactive canvas** -- paint strokes directly with real-time CPU preview; GPU renders the final output
- **Zoom and pan** -- mouse-wheel zoom with 10%--800% limits, right-click pan,
  double-click fit-to-window (automatic fit can fall outside the wheel limits)
- **Adjustable parameters** -- Brush Size, Pressure/Strength, Density, stationary Rate, Quality, optional Windows pen pressure
- **Reconstruction and masks** -- global reconstruction amount, local Reconstruct brush, Remove Deformation, Mask None/All/Invert, and independent mask visibility
- **View options** -- deformation mesh with adjustable spacing, document-composition backdrop, selection-aware preview, and processing status
- **Undo / Redo** with `Ctrl+Z` / `Ctrl+Y`; Remove Deformation removes deformation
  while preserving the mask
- **Common-settings bar** -- brush size, pressure, density, rate, quality, and mesh
  size stay above the canvas, alongside Show Mesh, Show Mask, Show Backdrop, and
  Use Pen Pressure icon toggles.
  Filled icons indicate enabled options; hover for current-action guidance.
  Remove Deformation is in the left settings bar. A collapse/expand
  chevron toggles the advanced left panel without hiding these controls, Undo,
  Redo, OK, or Cancel. The global reconstruction amount remains in advanced settings.
- **Persistent mode rail** -- the ten larger brush icons remain on the left when
  settings are hidden. `Alt+1` through `Alt+9` select Forward Warp through Freeze;
  `Alt+0` selects Unfreeze. Tooltips and accessible descriptions list each hotkey.
- **Theme-aware controls** -- follows Paint.NET light/dark theme changes and Windows
  high-contrast colors. Mask None/All/Invert, Remove Deformation, Undo, and
  Redo use drawn icons with descriptive tooltips. Footer buttons share a common
  height, with processing status on a separate row.
- **Gentler starting brush** -- pressure and density default to 25%.
- **Save Defaults** -- explicitly save brush and view preferences for new sessions
  to `%LOCALAPPDATA%\pdn-liquified\liquified-defaults.json`. Strokes, masks, and
  reconstruction are never included. Existing effect tokens retain their own
  brush/view settings; saved defaults do not override an existing token.

Brush Size is the brush **diameter**, from 5 to 1,200 image pixels. The UI pressure
range is 5%--100%, density is 0%--100%, and Rate is 1--120 applications per second.
Stationary Rate does not apply Forward Warp because that mode needs movement.
Quality ranges from 1 to 4, generating 1 to 16 GPU samples per output pixel;
it does not increase CPU preview sampling.

Global Reconstruct Amount defaults to **0%**, preserving the deformation you paint.
Increasing it blends the whole deformation back toward the original; 100% shows
the original. It is not the strength of the local Reconstruct brush. Reconstruction
is excluded from saved defaults and last-used preferences.

Liquified automatically saves last-used brush/display preferences when you click
OK to `%LOCALAPPDATA%\pdn-liquified\liquified-last-used.json`. Cancel does not change
them. These shared preferences take precedence over saved defaults when the host
creates a new token; existing host-managed tokens retain their own preferences.
No manual file linking is required. Strokes, masks, and reconstruction are excluded.
If saving last-used preferences fails, a warning is shown without blocking the
image edit. Explicit Save Defaults failures are also reported.

Automatic **per-file** persistence is not available: Paint.NET's public effect API
does not expose a filename or stable document identifier. Registry storage would
not supply that missing identity, and the effect cannot safely write settings into
the document's read-only metadata. The plugin does not use image-content hashes or
private host APIs to guess which file is open.

All three dialogs share theme-aware info icons and collapse/expand chevrons.
Liquified and Grid Warpified use the same closed-lock/open-lock Freeze/Unfreeze
glyphs, blue frozen-state color, and selected-mode palette. High-contrast mode
uses Windows system colors instead.
Hover an info icon for a description or click it to open a readable explanation.
Settings can be collapsed without losing access to OK/Cancel, Undo/Redo, or brush modes.
Liquified's top-bar Mesh, Mask, and
Backdrop icons explain their preview-only behavior through tooltips and accessible
descriptions; hiding the mask does not remove freeze protection.
Forward Warp is a **pull/drag** deformation, not a color-blending smudge brush.

Liquified keyboard controls: `[`/`]` or keypad minus/plus changes brush size; arrows
move and apply the keyboard brush (Shift uses a larger step); Ctrl+arrows pans.
`Ctrl+0` resets zoom/pan, and `Ctrl+Z`/`Ctrl+Y` undo/redo. Focused sliders keep their
native arrow-key behavior. Pen pressure uses a separate Windows `WM_POINTER` /
`GetPointerPenInfo` input path on the plugin's own canvas, not Paint.NET's internal
pen system (which is not exposed through the public plugin API). A compatible
Windows pen driver must report pressure; mouse input and unsupported drivers use
full intensity. The top-bar pen icon enables pressure scaling, not hardware
detection: the status shows a live **Pen: percentage** during a pressure-reporting
stroke, or the full-intensity fallback. This path has not been verified with a
physical pen; enabling the option alone does not prove device compatibility.

CPU preview uses bilinear image sampling; final Quality controls GPU RGSS multisampling.
Both use the same full-resolution inverse displacement map, with no stroke-count cap
and no repeated resampling of already-distorted image data. Full-resolution maps and
GPU uploads consume memory proportional to document area; the status does not imply
that a low-resolution preview reduces final rendering requirements.

Liquified currently accepts at most **8,388,608 pixels** and **16,384 pixels per
dimension**, additionally bounded by the active device's bitmap limit. This includes
3840 x 2160 (4K). It estimates a conservative 128 bytes/pixel within a 1 GiB pipeline
budget and rejects larger documents explicitly before image-sized allocations; it
does not silently downsample or omit strokes. Reduce document dimensions to use Liquified
on larger images. This resource limit does not apply to Grid Warpified or Doc Recitified.
It is a total-pixel budget, not a strict 4K width/height limit: wider images can fit
if their total area remains within the budget and each dimension meets the limit.
An 8K UHD image (7680 x 4320) exceeds the current budget; merely raising the cap
would increase the estimated working buffers to roughly 4 GiB before host overhead.

Mask-wide actions and Remove Deformation participate in undo/redo; Remove Deformation removes
deformation while retaining protection. Liquified retains **256 undo steps**, using shared
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

### Grid Warpified (Effects > Tools > Grid Warpified)

- **Draggable control point grid** -- independent width/height from 2 to 50 cells (up to 2,601 control points)
- **Bilinear displacement interpolation** -- smooth distortion across the image
- **GPU rendering** via sample-map shader with RGSS multisampling
- **Interactive canvas** -- drag points with a bounded, cancellable background preview; GPU renders full-resolution output
- **Grid overlay** -- toggleable grid lines and control point visualization
- **Undo / Redo** with `Ctrl+Z` / `Ctrl+Y`, plus a Reset button for unfrozen points
- **Accessible point editing** -- select points with Ctrl+Page Up/Down, nudge with arrows (Shift for 10 pixels), or edit X/Y numerically; Tab/Shift+Tab leaves the canvas
- **Persistent top-bar settings** -- Grid Width, Grid Height, Quality, and named Grid
  Color dropdowns, plus a grid-icon visibility toggle, remain available when the sidebar is collapsed.
  Info icons explain dimension resets and quality/overlay behavior.
- **Point constraints** -- Ctrl+click adds/removes points from a selection;
  Ctrl+Shift+Page Up/Down extends it from the keyboard. Freeze/Unfreeze modes
  fix or release clicked points. Link mode joins points and merges their existing
  groups; dragging, arrow nudging, or numeric editing in Move mode moves unfrozen
  group members by the same amount. Unlink mode detaches only the clicked point.
  Frozen points remain fixed, including during Reset. Large blue padlocks with
  black/white outlines mark frozen points in every mode, even with Show Grid off;
  squares mark linked handles, and dashed rings mark selected points. Linked
  groups also have thick, dashed magenta connecting lines with black/white outlines,
  independent of the chosen grid color; nonadjacent points are connected too.
  Constraints are retained in effect tokens and participate in undo/redo.
- **Safe point-editing modes** -- persistent Move, Freeze (closed lock), Unfreeze
  (open lock), Link (chain), and Unlink (broken chain) icons select a mode, with
  `Alt+1` through `Alt+5` shortcuts. The active mode has a thick border and visible
  instructions. The selected icon and active-mode banner have contrasting filled
  backgrounds in both themes and use system highlight colors in high contrast.
  The banner reminds you that **Esc returns to Move**, and a **Move (Esc)** button
  exits the mode without closing the dialog. Freeze/Unfreeze/Unlink modes apply on point clicks without dragging;
  Link uses the first clicked point as an anchor and joins subsequently clicked
  points to its group. Navigate with Ctrl+Page Up/Down and press Space to apply a
  mode from the keyboard. Numeric editing and arrow movement are disabled outside
  Move mode. `Esc` returns to Move without closing the dialog. Click Link again to
  choose a new anchor and start another group. There are no duplicate sidebar
  constraint actions: the top-bar modes are the single editing workflow.

Multi-selection alone does not move points together or batch-apply a constraint.
Each constraint-mode click acts on that point; Link uses its anchor as described
above. To move points together, link them first. The selection cannot be empty.
The grid icon has a filled selected background while the grid is visible and a
Show/Hide Grid tooltip. Toggling it off hides grid lines and ordinary handles, but keeps frozen-point
padlocks and the selected-point focus visible, without removing deformation,
links, or frozen flags. Entering a constraint mode turns Show Grid back on.
Grid color, visibility, editing mode, and sidebar visibility are dialog-only;
they are not stored in the effect token or saved defaults.

Quality ranges from 1 to 7 (1 to 49 GPU samples per output pixel). The CPU preview
is capped at 1,400 pixels on its longest side; final rendering remains full-resolution.

In Move mode with an unfrozen point selected and the canvas focused, `F6` focuses
the X editor; Tab then moves to Y. Arrow-key repeats are separate undo steps. Changing grid dimensions resets the
displacements, freeze/link constraints, and history; choose the grid size before shaping the image.

### Doc Recitified (Effects > Tools > Doc Recitified)

- **Perspective correction** -- map a user-selected quadrilateral to a rectified rectangle
- **Interactive corners** -- drag TL/TR/BR/BL handles with live preview
- **Keyboard and numeric corners** -- Ctrl+Page Up/Down selects a corner, arrows nudge it, and F6 focuses the X coordinate editor; Tab/Shift+Tab leaves the canvas
- **Aspect options** -- Fill Canvas, Preserve Detected, Source, Square, Letter, Legal, A4, Custom
- **Auto Detect** -- estimate document corners from content against background
- **GPU rendering** via sample-map shader with RGSS multisampling
- **Reset Corners** -- restore the inset starting quadrilateral; this dialog does
  not have its own undo/redo history
- **View controls** -- right-drag pan, wheel zoom, double-click fit, and collapsible settings

Auto Detect estimates corners; inspect and adjust them before accepting the effect.
It is a content/background heuristic, not OCR or calibrated camera correction.
Aspect-preserving modes center the output rectangle inside the existing canvas;
they do not resize or crop the document. Areas outside that rectangle are transparent.
Quality ranges from 1 to 4 (1 to 16 GPU samples per output pixel). Custom aspect is
a width-to-height ratio, not a requested output size.
Undefined aspect-mode values in imported tokens fall back to Preserve Detected in
both the dialog and renderer.

Grid Warpified and Doc Recitified canvases show keyboard focus. `Ctrl+0` resets the view
to 100%; Grid Warpified supports `Ctrl+Z`/`Ctrl+Y` for keyboard and numeric edits. Both
dialogs rescale when moved between monitors with different DPI.

Grid Warpified retains at most 256 undo steps. At the maximum 50×50-cell grid, each
snapshot (displacements, freeze flags, and link IDs) is about 33 KiB; 256 snapshots
use about 8.3 MiB of array storage, plus object overhead. Older history is discarded
as new edits are recorded.

## Build

```powershell
dotnet build LiquifiedPlugins.slnx                 # Debug
dotnet build -c Release LiquifiedPlugins.slnx      # Release (IL trim + ILRepack merge; no deployment)
```

**Prerequisites:** .NET 9.0 SDK, stable Paint.NET 5.1.12 reference assemblies (default `C:\Program Files\paint.net`).
Override the reference path with `/p:PdnRoot="<path>"`, including a disposable portable installation.
Do not use development-main assemblies requiring .NET 11 to build these .NET 9 plugins.
Paint.NET 5.1.12 is the documented baseline, not a guarantee that every future
stable/beta host works. Check the intended host versions before publishing.

Release builds automatically:

1. Trim unused ComputeSharp dependency IL via `Microsoft.NET.ILLink`
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
Invoke-Pester -Path .\tests\ReleaseSigning.Tests.ps1
```

The compatibility check loads the merged Release plugins using the portable host's own
runtime, resolves types, JIT-checks effect/dialog/token methods, clones tokens, and
constructs/disposes effects and dialogs. It also checks keyboard handlers and guidance,
bounded Grid Warpified history, malformed aspect modes, and complete/partial effect-array
cleanup. These are programmatic checks, not end-to-end UI interaction or GPU rendering.
Signing tests require Pester 5.7.1; see the signed-release workflow for setup.
Also load Paint.NET and exercise all three effects (**Effects > Tools >
Liquified**, **Grid Warpified**, and **Doc Recitified**), including mixed-DPI and pen input.

## Installation

### For End Users

Open [Releases](https://github.com/joelst/pdn-liquified/releases/) and inspect the
intended version's assets. If it contains `pdn-liquified-setup.exe`, close Paint.NET,
check the EXE's signature, run it, and restart Paint.NET. If only a ZIP is available,
extract the entire package and use its included installer scripts or install the
three DLLs manually. The existing `1.0-beta1` prerelease is ZIP-only.

The Inno Setup installer supports upgrades and uninstall through Windows Installed
Apps. Signed releases include signed Setup and Uninstall executables. The optional
ZIP and batch/PowerShell installer remain available for manual or system deployment.

For detailed instructions, troubleshooting, and manual installation, see [INSTALL.md](./INSTALL.md).

### Installation Details

1. **Run the installer script** when included in the extracted ZIP:

   ```powershell
   .\install-plugins.ps1
   ```

   The script will:
   - Locate all three plugin DLLs in a flat package or repository Release outputs
   - Copy to the actual user Documents Effects directory, including redirected folders
   - Support optional system installation with `-InstallationScope System`
   - Verify copied bytes against source SHA256 hashes and report failures with a
     nonzero exit code (this is not signature or release-manifest verification)

2. **Manual Installation** (if the script doesn't work):
   - Locate Paint.NET installation: typically `C:\Program Files\paint.net`
   - Copy the three DLL files to: `C:\Program Files\paint.net\Effects\`
   - Or use your actual Documents folder's `paint.net App Files\Effects\`
     (Create the `Effects` folder if it doesn't exist; do not install duplicate copies)
   - Restart Paint.NET

3. **Verify Installation:**
   - Open Paint.NET
   - Go to **Effects > Tools**
   - You should see:
     - Liquified
     - Grid Warpified
     - Doc Recitified

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
- Compiles and smoke-tests an unsigned Inno Setup installer on the stable job,
  including upgrades, uninstall, and preservation of unrelated plugins.
- A separate **Signed release** workflow signs successful main-build DLLs using Azure
  Artifact Signing and OIDC, then produces a verified signed ZIP and signed
  Setup/Uninstall executables. See [release signing](./RELEASES.md#authenticode-signing).

### Build the installer

After creating a flat Release package with all three DLLs, `README.md`, `INSTALL.md`,
and `LICENSE`, install Inno Setup 6.5.4 and run:

```powershell
.\scripts\Build-Installer.ps1 -PackageDirectory 'F:\git\release-package' -PackageVersion '1.0.0'
```

The compiler can be selected with `-CompilerPath`; output defaults to
`artifacts\installer\pdn-liquified-setup.exe`. Add `-Signed` only with already
signed/timestamped DLLs and configured Azure signing credentials/profile variables.
Inno's signing hook signs both the generated uninstaller and Setup during compilation.
`-PackageVersion` is required: use the release version without a `v` prefix,
for example `1.0.0-beta1` or `1.0.0`. Installed Apps and installer product metadata
use this identity, not the plugins' `5.1.*` compatibility assembly version.
CI push builds use `1.0.0-dev`; dispatch Build with `package_version` for a release.
The signed workflow preserves that build's `PACKAGE-VERSION.txt`.
The helper requires PowerShell 7.2 or later; installing the resulting EXE does not.

`tests\InnoInstaller.Tests.ps1` exercises an installer in an isolated directory.
Run it only on an account without an existing wizard installation; it refuses to
overwrite that installation's uninstall registration. Use `-TemporaryDirectory`
to choose the smoke-test workspace.

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
| `DialogUi.cs`                                  | Shared themed icons, compact information controls, collapsible settings, and dialog actions.       |
| `EffectSupportInfo.cs`                         | Menu-hover support metadata derived from each plugin's assembly metadata.                         |
| `Liquified/LiquifiedPreferences.cs`            | Shared last-used/default brush and view preferences; excludes image edits and file identity.       |
| `paintdotnet_src/`                              | Decompiled Paint.NET reference code. Read-only reference, excluded from compilation.              |

## Brush Modes

| Mode         | Description                                                  |
| ------------ | ------------------------------------------------------------ |
| Forward Warp | Drag pixels in the direction of the brush stroke             |
| Pucker       | Pull pixels inward toward the brush center                   |
| Bloat        | Push pixels outward from the brush center                    |
| Twist CW     | Rotate pixels clockwise around the brush center              |
| Twist CCW    | Rotate pixels counter-clockwise around the brush center      |
| Push Left    | Apply tangential displacement around the brush center       |
| Reconstruct  | Restore displaced pixels back toward their original position |
| Turbulence   | Add deterministic noise displacement within the brush        |
| Freeze       | Protect pixels from subsequent non-freeze Liquified strokes  |
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
- Image data, strokes, masks, and deformation stay in memory and host-managed
  effect tokens. Liquified additionally writes shared brush/view preferences
  to JSON files in `%LOCALAPPDATA%\pdn-liquified`; Grid Warpified and Doc Recitified
  do not have disk-backed preference files. This is not automatic per-file persistence.
- Project metadata avoids embedding personal contact information.
- Before publishing changes, run a quick secret/PII scan and review generated logs/artifacts before commit.
