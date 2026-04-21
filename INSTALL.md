# Installation Guide

This guide explains how to install the Paint.NET Distortion Plugins (Liquified, Grid Warp, Document Rectify).

## Requirements

- **Paint.NET 5.x or later** (download from [getpaint.net](https://www.getpaint.net/))
- **Windows 7 or later** (due to Paint.NET requirements)

## Option 1: Automated Installation (Recommended)

### Using the Batch File (Easiest)

1. Close Paint.NET
2. Download the plugin DLLs and installer files from [Releases](https://github.com/joelst/pdn-liquified/releases/)
3. Extract to a folder (e.g., `C:\Users\YourName\Downloads\pdn-liquified`)
4. **Double-click** `install-plugins.bat`
5. Grant administrator access if prompted
6. Wait for the completion message

### Using PowerShell Directly

If you prefer PowerShell:

```powershell
cd "C:\path\to\extracted\files"
.\install-plugins.ps1
```

The script will:
- ✓ Auto-detect your Paint.NET installation
- ✓ Create the Effects folder if needed
- ✓ Copy all three plugin DLLs to the correct locations
- ✓ Handle administrator elevation automatically
- ✓ Verify successful installation

## Option 2: Manual Installation

If the automated installer doesn't work for any reason:

### Step 1: Close Paint.NET

- Close Paint.NET completely (if open)

### Step 2: Locate Paint.NET Installation

Paint.NET is typically installed in one of these locations:
- `C:\Program Files\paint.net` (64-bit)
- `C:\Program Files (x86)\paint.net` (32-bit)

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
%USERPROFILE%\Documents\paint.net App Files\Effects\
```

Example:

```cmd
C:\Users\YourName\Documents\paint.net App Files\Effects\
```

Copy the same three DLL files here.

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

**Solution:** Paint.NET must be installed. Download and install from [getpaint.net](https://www.getpaint.net/), then run the installer again.

### "No plugin DLLs found"

**Solution:** You're running the installer from the wrong folder. Extract the downloaded files to a folder and run the installer from that folder.

### Plugins don't appear in Paint.NET

**Possible causes:**

1. Paint.NET wasn't restarted after installation
   - **Solution:** Close and reopen Paint.NET completely
2. Files were copied to the wrong location
   - **Solution:** Verify files are in both locations mentioned in "Option 2"
3. Antivirus blocked the DLLs
   - **Solution:** Check your antivirus/security software and whitelist the Effects folder

### Administrator privileges required

**Solution:** The system Effects folder requires admin access. The installer will request elevation automatically. If you see an issue:

- Right-click `install-plugins.bat` → **Run as administrator**
- Or use PowerShell: Right-click PowerShell → **Run as administrator**, then run `.\install-plugins.ps1`

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
- `%USERPROFILE%\Documents\paint.net App Files\Effects\`
- `C:\Users\YourName\Documents\paint.net App Files\Effects\`

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
dotnet build -c Release LiquifiedPlugins.slnx
```
