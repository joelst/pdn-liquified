# Release Distribution Guide

This document explains how the pdn-liquified project distributes releases to end users.

## Release Contents

Each release includes:

```
pdn-liquified-v1.0.0/
├── LiquifiedPlugin.dll          (Liquified effect - brush-based distortion)
├── GridWarpPlugin.dll           (Grid Warp effect - mesh-based distortion)
├── DocumentRectifyPlugin.dll    (Document Rectify effect - perspective correction)
├── install-plugins.bat          (Batch installer - double-click to run)
├── install-plugins.ps1          (PowerShell installer - advanced users)
├── INSTALL.md                   (Detailed installation instructions)
└── README.md                    (Overview and feature descriptions)
```

## Installation Instructions

### For Users (Quickstart)

1. Download the release ZIP file from [Releases](../../releases)
2. Extract to any folder
3. **Double-click `install-plugins.bat`**
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

Attach the ZIP and its external `SHA256SUMS.txt` from the **Signed release** artifact
to the GitHub release. Never rebuild, trim, merge, or modify DLLs after signing.
The workflow prepares a signed artifact; it does not publish a release automatically.
The ZIP and installer wrappers are not themselves Authenticode-signed.

Check an extracted DLL on Windows:

```powershell
Get-AuthenticodeSignature -LiteralPath .\LiquifiedPlugin.dll |
    Select-Object Status, SignerCertificate, TimeStamperCertificate
```

Local signing of an extracted Release package is also supported after Azure CLI login
to the signing tenant and installing `ArtifactSigning` version `0.1.8`:

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

Signing identifies the publisher and detects binary tampering. It does not guarantee
SmartScreen reputation, Paint.NET compatibility, or correct GPU behavior.

### Prerequisites

- Local repository with a clean `main` branch
- Built Release binaries (run `dotnet build -c Release LiquifiedPlugins.slnx`)

### Steps to Release

1. **Verify build artifacts exist:**
   ```powershell
   dir Liquified\bin\Release\net9.0-windows\LiquifiedPlugin.dll
   dir GridWarp\bin\Release\net9.0-windows\GridWarpPlugin.dll
   dir DocumentRectify\bin\Release\net9.0-windows\DocumentRectifyPlugin.dll
   ```

2. **Create a release tag:**
   ```powershell
   git tag -a v1.0.0 -m "Release version 1.0.0"
   git push origin v1.0.0
   ```

3. **Package release files:**
   ```powershell
   $version = "1.0.0"
   mkdir "pdn-liquified-v$version"

   # Copy DLLs
   copy "Liquified\bin\Release\net9.0-windows\LiquifiedPlugin.dll" "pdn-liquified-v$version\"
   copy "GridWarp\bin\Release\net9.0-windows\GridWarpPlugin.dll" "pdn-liquified-v$version\"
   copy "DocumentRectify\bin\Release\net9.0-windows\DocumentRectifyPlugin.dll" "pdn-liquified-v$version\"

   # Copy installer scripts
   copy "install-plugins.bat" "pdn-liquified-v$version\"
   copy "install-plugins.ps1" "pdn-liquified-v$version\"
   copy "INSTALL.md" "pdn-liquified-v$version\"
   copy "README.md" "pdn-liquified-v$version\"

   # Create ZIP
   Compress-Archive -Path "pdn-liquified-v$version" -DestinationPath "pdn-liquified-v$version.zip"
   ```

4. **Create GitHub Release:**
   - Go to [Releases](../../releases/new)
   - **Tag:** `v1.0.0` (must match git tag)
   - **Title:** `Paint.NET Distortion Plugins v1.0.0`
   - **Description:** (use template below)
   - **Attach files:**
     - `pdn-liquified-v1.0.0.zip`
     - Individual DLL files (optional, for advanced users)

### Release Description Template

```markdown
# Paint.NET Distortion Plugins v1.0.0

GPU-accelerated distortion effects for Paint.NET 5.x.

## New in This Release

- ✨ Liquified: 10 brush modes (Forward Warp, Pucker, Bloat, Twist, etc.)
- ✨ Grid Warp: Draggable control point mesh distortion
- ✨ Document Rectify: Automatic perspective correction

## Installation

**Recommended:** Download `pdn-liquified-v1.0.0.zip` and:
1. Extract the ZIP file
2. Double-click `install-plugins.bat`
3. Restart Paint.NET

See [INSTALL.md](INSTALL.md) for detailed instructions and troubleshooting.

## Requirements

- Paint.NET 5.x or later
- Windows 10 v21H2 or later, Windows 11, or Windows Server 2022 (64-bit)
- .NET 9.0 runtime (included with Paint.NET)

## Features

### Liquified
- 10 interactive brush modes with real-time CPU preview
- GPU rendering with RGSS multisampling for smooth results
- Shared full-resolution inverse displacement field with no stroke-count truncation
- Brush Rate and optional Windows pen pressure; mesh, backdrop, mask and reconstruction controls
- Explicit 8,388,608-pixel resource limit (4K supported), without silent downsampling
- Freeze/Unfreeze to protect areas from distortion
- Undo/Redo/Reset with keyboard shortcuts

### Grid Warp
- Draggable control point grid (2×2 to 6×6)
- Bilinear interpolation for smooth distortion
- Real-time preview and grid overlay
- Full undo/redo support

### Document Rectify
- Automatic document corner detection
- Interactive 4-point perspective correction
- Multiple aspect ratio presets (A4, Letter, Square, Custom)
- GPU-accelerated rendering

## Building from Source

```powershell
git clone https://github.com/joelst/pdn-liquified.git
cd pdn-liquified
dotnet build -c Release LiquifiedPlugins.slnx
```

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for full version history.

## License

[License Type] - See [LICENSE](LICENSE) file

## Support

- 📖 [Installation Guide](INSTALL.md)
- 📋 [Report Issues](../../issues)
- 💬 [Discussions](../../discussions)
```

## Distribution Channels

### GitHub Releases (Primary)

- Hosted on GitHub under "Releases"
- Automatically discoverable by users
- Includes installer scripts and documentation
- Can attach multiple file formats

### GitHub Actions Artifacts (Development)

- CI pipeline publishes merged Release builds and flat packages as stable/beta-labelled artifacts
- Useful for testing and development
- Available for 90 days by default
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
   - Batch file (easiest for users)
   - PowerShell script (more flexible)
   - Manual instructions (fallback)

## Version History

| Version | Release Date | Notes                                     |
| ------- | ------------ | ----------------------------------------- |
| 1.0.0   | TBD          | Initial release with 3 distortion effects |
