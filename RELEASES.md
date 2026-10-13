# Release Distribution Guide

This document explains how the pdn-liquified project distributes releases to end users.

## Published releases versus current source

The published `1.0-beta1` prerelease currently provides
`pdn-liquified-1.0-beta1.zip` only. It predates the current setup wizard, renamed
tools, and newer editing features. The package layout and workflow below describe
the current release pipeline, not assets guaranteed to exist in that older release.
Inspect [release assets](https://github.com/joelst/pdn-liquified/releases/) before
giving users a download filename. A local signed installer is not a published release.

## Release Contents

The recommended download is `pdn-liquified-setup.exe`, a per-user Inno Setup wizard
with signed Setup and Uninstall executables. The optional signed release ZIP
contains these files at its root (no versioned subfolder):

```
pdn-liquified-signed.zip
├── LiquifiedPlugin.dll          (Liquified effect - brush-based distortion)
├── GridWarpPlugin.dll           (Grid Warpified effect - mesh-based distortion)
├── DocumentRectifyPlugin.dll    (Doc Recitified effect - perspective correction)
├── install-plugins.bat          (Batch installer - double-click to run)
├── install-plugins.ps1          (PowerShell installer - advanced users)
├── INSTALL.md                   (Detailed installation instructions)
├── README.md                    (Overview and feature descriptions)
├── LICENSE                      (MIT license)
├── SHA256SUMS.txt                (Checksums of signed DLLs)
├── PACKAGE-VERSION.txt           (Explicit package/release identity)
└── BUILD.txt                    (Source commit and originating Build run)
```

The signed artifact also contains the installer and an external `SHA256SUMS.txt`
covering both the installer and ZIP.
The unsigned Build packages omit the signing checksums and `BUILD.txt`, but
include `PACKAGE-VERSION.txt`. Push/PR builds use `1.0.0-dev`. For a release,
dispatch Build on `main` with `package_version` set to the intended version,
such as `1.0.0-beta1` or `1.0.0` (without the tag's `v` prefix). Signed release
uses the downloaded package's version rather than deriving it from assembly metadata.
Installed Apps and the EXE's text product/file versions retain prerelease labels;
numeric Windows version resources use the three release components plus `.0`.
Package components must be no greater than 65535.

## Installation Instructions

### For Users (Quickstart)

1. Open [Releases](https://github.com/joelst/pdn-liquified/releases/) and select the intended version
2. Close Paint.NET
3. Download and run its signed setup wizard if included; otherwise extract its
   complete ZIP and follow [ZIP/manual installation](./INSTALL.md#option-2-zip--powershell-installation)
4. The default user install does not require administrator access
5. Done! Restart Paint.NET to see the new effects

### Full Documentation

For detailed installation steps and troubleshooting, see [INSTALL.md](./INSTALL.md)

## How to Create a Release

### Authenticode signing

The **Signed release** workflow uses the same Azure Artifact Signing (formerly Trusted
Signing) account and public-trust certificate profile as `joelst/flint`. No signing
private key or client secret is stored in this repository. GitHub OIDC authenticates
through the `release` environment, which permits only the `main` branch.

After a successful main-branch **Build**, the workflow signs the three final merged DLLs,
verifies trusted signatures and RFC3161 timestamps, and creates
`pdn-liquified-signed.zip` with signed-byte SHA256 checksums and build provenance.
It can also be dispatched from `main` with a successful Build run ID. Failed builds,
pull-request/fork artifacts, other workflows, and unsigned or untimestamped results
are rejected. The existing Build artifacts remain unsigned development artifacts.

Attach the EXE, ZIP, and external `SHA256SUMS.txt` from the **Signed release** artifact
to the GitHub release. Never rebuild, trim, merge, or modify DLLs after signing.
The workflow prepares a signed artifact; it does not publish a release automatically.
The wizard Setup and generated Uninstall are Authenticode-signed using Inno's signing
hook. The ZIP and legacy batch/PowerShell wrappers are not Authenticode-signed.

Check an extracted DLL on Windows:

```powershell
Get-AuthenticodeSignature -LiteralPath .\LiquifiedPlugin.dll |
    Select-Object Status, SignerCertificate, TimeStamperCertificate
```

Local signing of an extracted Release package is also supported with PowerShell
7.2 or later, Azure CLI login to the signing tenant, the signing account's
subscription selected, and `ArtifactSigning` version `0.1.8` installed:

```powershell
.\scripts\Sign-Release.ps1 -PackageDirectory 'C:\path\to\release-package'
```

Configure `AZURE_TRUSTED_SIGNING_ENDPOINT`, `AZURE_TRUSTED_SIGNING_ACCOUNT_NAME`, and
`AZURE_TRUSTED_SIGNING_CERTIFICATE_PROFILE_NAME` in the local environment. The script
fails when configuration or authentication is missing; it never silently skips signing.
GitHub's release environment holds these profile variables and the identity identifiers
`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID`. These are identifiers,
not client-secret credentials. Federation uses
`repo:joelst/pdn-liquified:environment:release`.

Select the subscription containing the signing account before local signing
(`az account set --subscription '<signing-subscription-id>'`), checking
`$LASTEXITCODE` after Azure CLI commands. An authenticated user with a different
active subscription can still receive HTTP 403 from signing. Check profile status,
identity, subscription/tenant context, and existing authorization before retrying;
do not broaden permissions or package unsigned output as a fallback. Restore any
previous CLI subscription selection when finished.

Signing identifies the publisher and detects binary tampering. It does not guarantee
SmartScreen reputation, Paint.NET compatibility, or correct GPU behavior.

### Prerequisites

- Changes merged to `main`, including the Build and Signed release workflows
- Successful stable and beta Build jobs for the intended source commit
- Configured `release` environment, OIDC federation, and Artifact Signing profile
- Manual Paint.NET validation of all three effects, including GPU output, keyboard
  navigation, pen input where available, and mixed-DPI interaction

### Steps to Release

1. Dispatch a main-branch Build with the intended `package_version`, and confirm
   it succeeded. Check `PACKAGE-VERSION.txt` matches your intended release/tag.
   Wait for **Signed release**, or dispatch
   it from `main` with that successful Build run ID. A PR Build cannot be signed by
   this workflow.
2. Download the `pdn-liquified-signed-<source SHA>` artifact. Keep its EXE and ZIP unchanged.
   Extract a copy for verification; confirm every DLL has a Valid Authenticode
   signature and timestamp, and test the extracted installer and effects.
3. Read `BUILD.txt` inside the ZIP and tag that exact source commit, not an unrelated
   current checkout. Replace the example version and SHA:

   ```powershell
   $sourceCommit = 'replace-with-the-40-character-source-SHA-from-BUILD.txt'
   if ($sourceCommit -notmatch '^[a-f0-9]{40}$') { throw 'Supply the source commit SHA.' }
   git tag -a v1.0.0 $sourceCommit -m 'Release version 1.0.0'
   if ($LASTEXITCODE -ne 0) { throw 'Tag creation failed.' }
   git push origin v1.0.0
   if ($LASTEXITCODE -ne 0) { throw 'Tag push failed.' }
   ```

4. Create a [GitHub release](../../releases/new) for that tag. Attach the unchanged
   `pdn-liquified-setup.exe`, `pdn-liquified-signed.zip`, and external `SHA256SUMS.txt`. If distributing individual
   DLLs as well, use the signed copies from the ZIP, not local build outputs.

For development testing, use the flat `pdn-liquified-release-stable` or
`pdn-liquified-release-beta` Build artifact. These are unsigned and must not be
presented as signed public releases.

### Release Description Template

````markdown
# Paint.NET Distortion Plugins v1.0.0

GPU-accelerated distortion effects for Paint.NET 5.x: **Liquified**, **Grid Warpified**, and **Doc Recitified**. Find all three under **Effects > Tools**.

## New in This Release

- ✨ Liquified: 10 brush modes (Forward Warp, Pucker, Bloat, Twist, etc.)
- ✨ Grid Warpified: Draggable control point mesh distortion
- ✨ Doc Recitified: Interactive perspective correction with estimated document corners

## Installation

**Recommended:** Download `pdn-liquified-setup.exe` and:
1. Close Paint.NET
2. Run the setup wizard
3. Restart Paint.NET

The signed ZIP is available for portable/manual installation.

See [INSTALL.md](INSTALL.md) for detailed instructions and troubleshooting.

## Requirements

- Paint.NET 5.1.12 or later
- Windows 10 v21H2 or later, Windows 11, or Windows Server 2022 (64-bit)
- Paint.NET provides its own runtime; end users do not need the .NET SDK

## Features

### Liquified
- 10 interactive brush modes with real-time CPU preview
- GPU rendering with RGSS multisampling for smooth results
- Shared full-resolution inverse displacement field with no stroke-count truncation
- Brush Rate and optional Windows pen pressure; mesh, backdrop, mask and reconstruction controls
- Explicit 8,388,608-pixel resource limit (4K supported), without silent downsampling
- Freeze/Unfreeze to protect areas from distortion
- Persistent brush/view preferences and Save Defaults (not per-file settings)
- Always-visible mode icons, compact top-bar settings, and collapsible advanced settings
- Ctrl+Z/Ctrl+Y undo/redo; Restore All removes deformation while retaining the mask

### Grid Warpified
- Independent grid dimensions from 2 to 50 cells (up to 2,601 control points)
- Bilinear interpolation for smooth distortion
- Real-time preview and grid overlay
- Up to 256 undo steps, with keyboard and numeric point editing
- Freeze/Unfreeze, Link/Unlink, and Move modes with Alt+1--5 shortcuts
- Contrasting linked-point connections; Esc returns constraint modes to Move
- Width, height, quality, grid-color dropdowns and a grid-visibility icon toggle in the top bar

### Doc Recitified
- Heuristic document corner estimation; inspect and adjust before applying
- Interactive 4-point perspective correction
- Multiple aspect ratio presets (A4, Letter, Square, Custom)
- GPU-accelerated rendering
- Aspect-preserving output fits inside the existing canvas with transparent margins;
  it does not resize the document
- Reset Corners is available; the dialog does not have its own undo/redo history

## Building from Source

```powershell
git clone https://github.com/joelst/pdn-liquified.git
cd pdn-liquified
dotnet build -c Release LiquifiedPlugins.slnx
```

## License

MIT - See [LICENSE](LICENSE) file

## Support

- 📖 [Installation Guide](INSTALL.md)
- 📋 [Report Issues](../../issues)
````

## Distribution Channels

### GitHub Releases (Primary)

- Hosted on GitHub under "Releases"
- Automatically discoverable by users
- Includes installer scripts and documentation
- Can attach multiple file formats

### GitHub Actions Artifacts (Development)

- CI pipeline publishes merged Release builds and flat packages as stable/beta-labelled artifacts
- Useful for testing and development
- Availability follows the repository's Actions artifact retention policy
- Located in workflow run details

## Best Practices

1. **Always include installer scripts** in releases
   - Make installation frictionless for end users
   - Reduces support burden from installation issues

2. **Include documentation in release package**
   - Users may not have internet access when installing
   - Reduces need for external references

3. **Use semantic versioning**
   - `v1.0.0` - Major.Minor.Patch
   - Tag must match GitHub Release tag

4. **Keep release notes updated**
   - List new features and improvements
   - Include installation instructions
   - Link to documentation and issue tracker

5. **Test release packages before publishing**
   - Extract and run installer script locally
   - Run `.\tests\Installer.Tests.ps1` and `dotnet run --project tests\Liquified.RegressionTests -c Release`
   - Run `.\tests\Test-PluginCompatibility.ps1 -PortableDirectory '<disposable portable host>'` against stable and beta
   - Verify plugins load correctly in Paint.NET
   - Test on both Windows 10 and Windows 11

6. **Provide multiple installation methods**
   - Signed setup wizard (recommended when published)
   - ZIP with batch/PowerShell installers (manual/portable or system deployment)
   - Manual DLL installation (not a workaround for a Windows Security block)

## Version History

| Version | Status | Notes |
| ------- | ------ | ----- |
| `1.0-beta1` | Published prerelease | Older ZIP-only release; not all current-source features |
| Next release | Unpublished | Current-source features and signed setup/ZIP pipeline described above |
