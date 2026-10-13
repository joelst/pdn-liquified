# Installation Guide

This guide explains how to install the Paint.NET Distortion Plugins (Liquified, Grid Warpified, Doc Recitified).

## Choose the right package

The features and tool names in [README.md](./README.md) describe the current source.
Older downloads may have older names and fewer features.

The currently published `1.0-beta1` prerelease contains only
`pdn-liquified-1.0-beta1.zip`; it does not include the new setup wizard or all
current-source features. Open [Releases](https://github.com/joelst/pdn-liquified/releases/)
and inspect the assets for the version you intend to install. Do not assume a
setup EXE exists in an older release.

For a release that includes `pdn-liquified-setup.exe`, use the wizard below.
Otherwise use its complete ZIP package, or build from source. GitHub Actions
Build artifacts are unsigned development packages, not signed public releases.

## Requirements

- **Paint.NET 5.1.12 or later** (download from [getpaint.net](https://www.getpaint.net/)).
- **Windows 10 v21H2 or later, Windows 11, or Windows Server 2022**, with a 64-bit x64 or ARM64 CPU.
- These plugins use Paint.NET's GPU effect infrastructure. A Direct3D 11-capable
  GPU is recommended; actual rendering compatibility must be checked in Paint.NET.
- End users do not need the .NET SDK, PowerShell 7, or Inno Setup to run the setup
  EXE or use the plugins. The ZIP's PowerShell installer requires Windows
  PowerShell 5.1 or later. Source builds require the .NET 9 SDK.

Paint.NET 5.1.12 is the documented baseline. Compatibility with every later
stable/beta build is not guaranteed; check release notes and plugin loading errors
for the host version you use.

## Option 1: Setup wizard (when included in the release)

1. Close Paint.NET
2. Download `pdn-liquified-setup.exe` from the intended release's assets
3. Check the download's signature, then run the installer and accept the license
4. Keep the default Documents Effects folder, or choose the Effects folder of a portable installation
5. Finish installation, then restart Paint.NET

The Inno Setup installer runs per-user without requesting administrator access.
It supports upgrades and registers **Paint.NET Distortion Plugins** in Windows
Installed Apps. Its uninstaller removes the three plugin DLLs and this package's
documentation, not unrelated plugins. Do not choose a protected Program Files
destination; use the PowerShell system-install option below when elevation is needed.

Public release installers and their uninstallers are timestamped and Authenticode-signed.
CI's `pdn-liquified-installer-unsigned` artifact is for development testing only.

To inspect a setup EXE or DLL without running it:

```powershell
Get-AuthenticodeSignature -LiteralPath '.\pdn-liquified-setup.exe' |
    Select-Object Status, SignerCertificate, TimeStamperCertificate
```

Signed releases should report `Valid` and include a timestamp certificate. Inspect
the publisher before proceeding. Signing is not a guarantee of compatibility or
SmartScreen reputation, and older ZIP releases must not be assumed to be signed.

The default directory is the actual Windows Documents folder's
`paint.net App Files\Effects`, including folder redirection. DLLs are installed
directly there; documentation and uninstall files go in its `pdn-liquified`
subfolder. Setup checks that it can create, write, and remove a temporary file in
that support folder, but this does not guarantee every later file operation will
succeed. Review any error rather than assuming a partial installation succeeded.

## Option 2: ZIP / PowerShell installation

Extract the complete ZIP to a writable folder. If the package includes
`install-plugins.bat` and `install-plugins.ps1`, double-click the batch file to
install for the current user. This copies the three DLLs but does not register an
uninstaller or install the documentation. Do not run either script from inside
the ZIP. If an older package does not include the scripts, use manual installation.

### Using PowerShell Directly

If you prefer PowerShell:

```powershell
Set-Location 'C:\path\to\extracted\files'
.\install-plugins.ps1
```

The script locates all three DLLs before copying anything, uses the actual Windows
Documents folder (including OneDrive/folder redirection), and verifies every copied
DLL using SHA256. It accepts both a flat release package and repository Release outputs.
It checks that destination bytes match the source; this is not publisher or
Authenticode verification and does not validate a release's `SHA256SUMS.txt`.
It writes to only the selected location, but does not search for or remove older
copies elsewhere.

```powershell
.\install-plugins.ps1 -WhatIf
.\install-plugins.ps1 -InstallationScope System
.\install-plugins.ps1 -InstallationScope System -PdnRoot 'D:\Apps\paint.net'
.\install-plugins.ps1 -DestinationDirectory 'D:\Apps\paint.net\Effects'
```

System installation is opt-in and requests elevation when necessary. A cancelled
elevation, missing DLL, or failed copy returns a nonzero exit code; existing files
alone do not count as a successful installation. Close Paint.NET before updating.
Use `-DestinationDirectory` for a writable portable Effects folder without
requesting system installation or elevation.

## Option 3: Manual installation

Use this when your package contains only DLLs, or you prefer not to run the
installer. It is not a workaround for a Windows Security block: review and resolve
that block first.

### Step 1: Close Paint.NET

- Close Paint.NET completely (if open)

### Step 2: Locate Paint.NET Installation

Paint.NET is typically installed in one of these locations:

- `C:\Program Files\paint.net` (classic installer)
- A custom directory for portable Paint.NET

Microsoft Store Paint.NET uses the user Effects folder below; do not modify WindowsApps.

If Paint.NET is installed elsewhere, note the installation path.

### Step 3: Copy Plugin Files

You need to copy the three DLL files to one of these two locations:

#### Location Option 1: System-wide Effects folder

```cmd
<Paint.NET Installation>\Effects\
```

Example:

```cmd
C:\Program Files\paint.net\Effects\
```

Files to copy:

- `LiquifiedPlugin.dll`
- `GridWarpPlugin.dll`
- `DocumentRectifyPlugin.dll`

#### Location Option 2: User Effects folder

```cmd
<Your actual Documents folder>\paint.net App Files\Effects\
```

Example:

```cmd
C:\Users\YourName\OneDrive\Documents\paint.net App Files\Effects\
```

Copy the three DLL files here instead of the system folder. To find the correct
directory, open Documents in File Explorer or run:

```powershell
Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'paint.net App Files\Effects'
```

**Note:** Create the `Effects` folder if it doesn't exist.
For downloaded DLLs, inspect their signatures and the download source. If Windows
marks a trusted download as blocked, use its Properties dialog's **Unblock**
option. The PowerShell installer unblocks copied DLLs automatically.

### Step 4: Start Paint.NET

- Reopen Paint.NET

## Verification

After installation, verify that the plugins loaded:

1. Open **Paint.NET**
2. Go to **Effects** menu
3. Hover over **Tools**
4. You should see:
   - **Liquified**
   - **Grid Warpified**
   - **Doc Recitified**

Seeing all three confirms menu discovery, not correct GPU rendering. Open an image,
run each effect, make a visible edit, click OK, and inspect the committed result.
Check Paint.NET's plugin loading errors in Settings if a plugin is missing.
Older packages may use the previous tool names.

## Updating an existing installation

Close Paint.NET first. For a wizard installation, run the newer wizard and keep
the existing destination; setup reuses the previously selected directory. For
ZIP/manual installations, replace the same three DLLs in the same Effects folder.
Do not install a second copy in another Effects folder. The installers do not
automatically migrate or remove duplicate manual/system installations.

DLL filenames remain unchanged despite the new menu names. An older installed
version is not updated by changing the README or downloading source alone.

## Troubleshooting

### Setup cannot create a directory (Error 2)

The selected Effects folder or one of its parents may be unavailable. Setup checks
that it can create its support folder before installing any plugins and reports
the affected path; it does not silently select a different Documents folder.

If the path is under OneDrive, start OneDrive, confirm Documents opens in File
Explorer, and make the destination available on this device before retrying.
Also check that the path is writable and that none of its parent components is
a file. For portable Paint.NET, select its actual writable Effects directory.
For a protected classic installation, use the elevated PowerShell system-install
option instead.

### "Paint.NET installation not found"

**Solution:** For a custom/portable system installation, pass `-PdnRoot`. For Microsoft
Store Paint.NET, use the default user installation. Paint.NET must be installed to run
the plugins, but user installation does not need access to its program directory.

### "Missing ...Plugin.dll"

**Solution:** Extract the entire release package, including all three DLLs, before
running the installer. For a source checkout, build all three projects in Release.
The installer defaults to its own directory, not the shell's working directory;
use `-PluginDirectory 'C:\path\to\package'` to select another source.

### Plugins don't appear in Paint.NET

**Possible causes:**

1. Paint.NET wasn't restarted after installation
   - **Solution:** Close and reopen Paint.NET completely
2. Files were copied to the wrong location
   - **Solution:** Verify files are in one correct location mentioned in "Option 3"; remove obsolete duplicates
3. Antivirus blocked the DLLs
   - **Solution:** Inspect the security warning and confirm the download is trusted; do not disable antivirus protection

Check Paint.NET's plugin loading errors in its Settings dialog for assembly or runtime
compatibility failures. A successful file copy does not prove that the host loaded a plugin.

### Setup reports "The system cannot find the file specified" when replacing the uninstaller

Windows Defender Controlled Folder Access can produce this misleading error when
the destination is Documents, including OneDrive Documents. A writable folder and
a valid installer signature do not rule out this protection.

Open **Windows Security > Virus & threat protection > Protection history** and
check for a block involving `pdn-liquified-setup.tmp` at the time of installation.
If you trust the signed release, review the blocked app and explicitly allow it
through Controlled Folder Access using Windows Security, then retry setup.
The temporary setup path can change between runs. Do not disable antivirus or
Controlled Folder Access. The installer checks write access before replacing
files and never changes Windows Security settings itself.

### Administrator privileges required

**Solution:** The default user installation does not require admin access. Only
`-InstallationScope System` requests elevation:

Use PowerShell: `.\install-plugins.ps1 -InstallationScope System`

Running the batch file as administrator does not select system installation; its
default remains the user folder.

### Paint.NET is crashing after installation

**Solution:**

1. Close Paint.NET and preserve the error message or crash report
2. Uninstall through Windows Installed Apps for a wizard installation, or remove
   only these three DLLs for a ZIP/manual installation
3. Restart Paint.NET
4. Report the issue on [GitHub Issues](https://github.com/joelst/pdn-liquified/issues)

### Liquified rejects a large image

Liquified supports at most 8,388,608 pixels and 16,384 pixels per dimension, subject
to the GPU's bitmap limit. A 3840 x 2160 image fits; an 8K image does not. Reduce
image dimensions before using this effect. It does not silently downsample or drop
strokes. Grid Warpified and Doc Recitified do not use this Liquified-specific limit.
The limit is total pixel area, not exactly 4K dimensions; some images wider than
4K still fit. Raising the limit is not a supported user setting. Large images can
still exhaust available memory below the cap because Paint.NET and other effects
also consume resources.

## Uninstallation

For wizard installations, close Paint.NET and uninstall **Paint.NET Distortion
Plugins** from Windows Installed Apps. For ZIP/manual installations:

Close Paint.NET before deleting the DLLs. Remove only these plugin files, not the
Effects directory or other plugins.

1. Navigate to Paint.NET Effect installation locations:

- `C:\Program Files\paint.net\Effects\`
- `<Your actual Documents folder>\paint.net App Files\Effects\`

2. Delete the three DLL files:

   - `LiquifiedPlugin.dll`
   - `GridWarpPlugin.dll`
   - `DocumentRectifyPlugin.dll`

3. Restart Paint.NET

Uninstallation does not remove Liquified's shared preferences in
`%LOCALAPPDATA%\pdn-liquified`. To reset brush/view preferences, close Paint.NET and
remove only `liquified-defaults.json` and `liquified-last-used.json` there. Neither
file contains strokes, masks, or image data. Do not delete the entire Effects folder.

## Support

For issues or questions:

- Check [GitHub Issues](https://github.com/joelst/pdn-liquified/issues)
- Open a new issue with:
  - Windows version
  - Paint.NET version
  - Steps to reproduce the problem
  - Any error messages

## Building from Source

If you want to build the plugins yourself:

```powershell
git clone https://github.com/joelst/pdn-liquified.git
cd pdn-liquified
dotnet build -c Release LiquifiedPlugins.slnx /p:PdnRoot='C:\path\to\stable\paint.net'
```

Use the .NET 9 SDK and stable Paint.NET references, not development assemblies that
require a newer .NET runtime. Release builds merge dependencies but do not deploy.
Run the installer afterward, or opt in with `/p:DeployPlugins=true`; deployment uses
the actual user Documents folder unless `/p:PdnEffectsDirOverride='<directory>'` is supplied.
Local build outputs are unsigned unless explicitly signed. Building does not
create a setup EXE; see [RELEASES.md](./RELEASES.md) for packaging and signing.
