# Installation Guide

This guide explains how to install the Paint.NET Distortion Plugins (Liquified, Grid Warp, Document Rectify).

## Requirements

- **Paint.NET 5.1.12 or later** (download from [getpaint.net](https://www.getpaint.net/)).
- **Windows 10 v21H2 or later, Windows 11, or Windows Server 2022**, with a 64-bit x64 or ARM64 CPU.
- A Direct3D 11-capable GPU is recommended. These plugins use Paint.NET's GPU effect infrastructure.

## Option 1: Automated Installation (Recommended)

### Using the Batch File (Easiest)

1. Close Paint.NET
2. Download the plugin DLLs and installer files from [Releases](https://github.com/joelst/pdn-liquified/releases/)
3. Extract to a folder (e.g., `C:\Users\YourName\Downloads\pdn-liquified`)
4. **Double-click** `install-plugins.bat`
5. Wait for the verification message; the default user installation does not request administrator access
6. Restart Paint.NET

### Using PowerShell Directly

If you prefer PowerShell:

```powershell
cd "C:\path\to\extracted\files"
.\install-plugins.ps1
```

The script locates all three DLLs before copying anything, uses the actual Windows
Documents folder (including OneDrive/folder redirection), and verifies every copied
DLL using SHA256. It accepts both a flat release package and repository Release outputs.
It does not install duplicate copies to both locations.

```powershell
.\install-plugins.ps1 -WhatIf
.\install-plugins.ps1 -InstallationScope System
.\install-plugins.ps1 -InstallationScope System -PdnRoot 'D:\Apps\paint.net'
```

System installation is opt-in and requests elevation when necessary. A cancelled
elevation, missing DLL, or failed copy returns a nonzero exit code; existing files
alone do not count as a successful installation. Close Paint.NET before updating.

## Option 2: Manual Installation

If the automated installer doesn't work for any reason:

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

### Step 4: Start Paint.NET

- Reopen Paint.NET

## Verification

After installation, verify that the plugins loaded:

1. Open **Paint.NET**
2. Go to **Effects** menu
3. Hover over **Tools**
4. You should see:
   - **Liquified**
   - **Grid Warp**
   - **Document Rectify**

If you see all three effects, installation was successful!

## Troubleshooting

### "Paint.NET installation not found"

**Solution:** For a custom/portable system installation, pass `-PdnRoot`. For Microsoft
Store Paint.NET, use the default user installation. Paint.NET must be installed to run
the plugins, but user installation does not need access to its program directory.

### "No plugin DLLs found"

**Solution:** You're running the installer from the wrong folder. Extract the downloaded files to a folder and run the installer from that folder.

### Plugins don't appear in Paint.NET

**Possible causes:**

1. Paint.NET wasn't restarted after installation
   - **Solution:** Close and reopen Paint.NET completely
2. Files were copied to the wrong location
   - **Solution:** Verify files are in one correct location mentioned in "Option 2"; remove obsolete duplicates
3. Antivirus blocked the DLLs
   - **Solution:** Inspect the security warning and confirm the download is trusted; do not disable antivirus protection

Check Paint.NET's plugin loading errors in its Settings dialog for assembly or runtime
compatibility failures. A successful file copy does not prove that the host loaded a plugin.

### Administrator privileges required

**Solution:** The default user installation does not require admin access. Only
`-InstallationScope System` requests elevation:

- Right-click `install-plugins.bat` → **Run as administrator**
- Or use PowerShell: `.\install-plugins.ps1 -InstallationScope System`

### Paint.NET is crashing after installation

**Solution:**

1. This is unlikely but can happen if there's a compatibility issue
2. Uninstall the plugins by deleting the three DLL files from the Effects folders
3. Restart Paint.NET
4. Report the issue on [GitHub Issues](https://github.com/joelst/pdn-liquified/issues)

## Uninstallation

To remove the plugins:

1. Navigate to Paint.NET Effect installation locations:

- `C:\Program Files\paint.net\Effects\`
- `<Your actual Documents folder>\paint.net App Files\Effects\`

2. Delete the three DLL files:

   - `LiquifiedPlugin.dll`
   - `GridWarpPlugin.dll`
   - `DocumentRectifyPlugin.dll`

3. Restart Paint.NET

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
