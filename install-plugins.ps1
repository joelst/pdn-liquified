<#
.SYNOPSIS
    Installs Paint.NET Distortion Plugins (Liquified, Grid Warp, Document Rectify)

.DESCRIPTION
    This script automatically detects your Paint.NET installation and copies the three
    plugin DLL files to the correct locations:
    - System-wide: C:\Program Files\paint.net\Effects\
    - User-specific: %USERPROFILE%\Documents\paint.net App Files\Effects\

.EXAMPLE
    .\install-plugins.ps1

.NOTES
    Requires Paint.NET 5.x to be installed.
    May request administrator elevation for system-wide installation.
#>

#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

Write-Host 'Paint.NET Distortion Plugins Installer' -ForegroundColor Cyan
Write-Host "======================================`n" -ForegroundColor Cyan

# ═══════════════════════════════════════════════════════════════════
#  Detect Paint.NET Installation
# ═══════════════════════════════════════════════════════════════════

Write-Host 'Detecting Paint.NET installation...' -ForegroundColor Yellow

$PdnInstallPath = $null
$PdnProgramFiles = 'C:\Program Files\paint.net'
$PdnProgramFilesX86 = 'C:\Program Files (x86)\paint.net'

if (Test-Path $PdnProgramFiles) {
  $PdnInstallPath = $PdnProgramFiles
  Write-Host "Found Paint.NET at: $PdnInstallPath" -ForegroundColor Green
}
elseif (Test-Path $PdnProgramFilesX86) {
  $PdnInstallPath = $PdnProgramFilesX86
  Write-Host "Found Paint.NET at: $PdnInstallPath" -ForegroundColor Green
}
else {
  Write-Host 'ERROR: Paint.NET installation not found!' -ForegroundColor Red
  Write-Host "`nPlease ensure Paint.NET 5.x is installed in one of these locations:"
  Write-Host "  - $PdnProgramFiles"
  Write-Host "  - $PdnProgramFilesX86"
  Write-Host "`nIf Paint.NET is installed elsewhere, manually copy the DLLs to:"
  Write-Host '  - <PaintNetDir>\Effects\'
  Write-Host '  - %USERPROFILE%\Documents\paint.net App Files\Effects\'
  exit 1
}

# ═══════════════════════════════════════════════════════════════════
#  Locate Plugin DLL Files
# ═══════════════════════════════════════════════════════════════════

Write-Host "`nLocating plugin DLL files..." -ForegroundColor Yellow

$pluginDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$dllFiles = @(
  'Liquified\bin\Release\net9.0-windows\LiquifiedPlugin.dll',
  'GridWarp\bin\Release\net9.0-windows\GridWarpPlugin.dll',
  'DocumentRectify\bin\Release\net9.0-windows\DocumentRectifyPlugin.dll'
)

$foundPlugins = @()
foreach ($dll in $dllFiles) {
  $dllPath = Join-Path $pluginDir $dll
  if (Test-Path $dllPath) {
    $foundPlugins += @{ Name = (Split-Path -Leaf $dll); Path = $dllPath }
    Write-Host "  ✓ Found: $(Split-Path -Leaf $dll)" -ForegroundColor Green
  }
  else {
    Write-Host "  ✗ Missing: $dll" -ForegroundColor Red
  }
}

if ($foundPlugins.Count -eq 0) {
  Write-Host "`nERROR: No plugin DLLs found!" -ForegroundColor Red
  Write-Host "`nPlease build the project first:"
  Write-Host '  dotnet build -c Release LiquifiedPlugins.slnx'
  exit 1
}

if ($foundPlugins.Count -lt 3) {
  Write-Host "`nWARNING: Only found $($foundPlugins.Count) of 3 plugins." -ForegroundColor Yellow
}

# ═══════════════════════════════════════════════════════════════════
#  Installation Paths
# ═══════════════════════════════════════════════════════════════════

$systemEffectsPath = Join-Path $PdnInstallPath 'Effects'
$userAppFiles = Join-Path $env:USERPROFILE 'Documents\paint.net App Files\Effects'

Write-Host "`nInstallation targets:" -ForegroundColor Yellow
Write-Host "  System: $systemEffectsPath" -ForegroundColor Cyan
Write-Host "  User:   $userAppFiles" -ForegroundColor Cyan

# ═══════════════════════════════════════════════════════════════════
#  Check Administrator Privileges
# ═══════════════════════════════════════════════════════════════════

$isAdmin = [Security.Principal.WindowsIdentity]::GetCurrent().Groups `
| Where-Object { $_.Value -eq 'S-1-5-32-544' }

if (-not $isAdmin) {
  Write-Host "`nRequesting administrator privileges for system-wide installation..." -ForegroundColor Yellow

  $scriptPath = $MyInvocation.MyCommand.Definition
  $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`""

  try {
    Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs -Wait
    exit 0
  }
  catch {
    Write-Host 'ERROR: Failed to elevate privileges. Attempting user-only installation...' -ForegroundColor Yellow
  }
}

# ═══════════════════════════════════════════════════════════════════
#  System-Wide Installation
# ═══════════════════════════════════════════════════════════════════

if ($isAdmin) {
  Write-Host "`nInstalling to system Effects folder..." -ForegroundColor Yellow

  if (-not (Test-Path $systemEffectsPath)) {
    try {
      New-Item -ItemType Directory -Path $systemEffectsPath -Force | Out-Null
      Write-Host "Created directory: $systemEffectsPath" -ForegroundColor Green
    }
    catch {
      Write-Host "ERROR: Could not create system Effects directory: $_" -ForegroundColor Red
    }
  }

  foreach ($plugin in $foundPlugins) {
    try {
      $destPath = Join-Path $systemEffectsPath $plugin.Name
      Copy-Item -Path $plugin.Path -Destination $destPath -Force
      Write-Host "  ✓ Installed: $($plugin.Name)" -ForegroundColor Green
    }
    catch {
      Write-Host "  ✗ Failed to copy $($plugin.Name): $_" -ForegroundColor Red
    }
  }
}

# ═══════════════════════════════════════════════════════════════════
#  User-Specific Installation
# ═══════════════════════════════════════════════════════════════════

Write-Host "`nInstalling to user Effects folder..." -ForegroundColor Yellow

if (-not (Test-Path $userAppFiles)) {
  try {
    New-Item -ItemType Directory -Path $userAppFiles -Force | Out-Null
    Write-Host "Created directory: $userAppFiles" -ForegroundColor Green
  }
  catch {
    Write-Host "ERROR: Could not create user Effects directory: $_" -ForegroundColor Red
  }
}

foreach ($plugin in $foundPlugins) {
  try {
    $destPath = Join-Path $userAppFiles $plugin.Name
    Copy-Item -Path $plugin.Path -Destination $destPath -Force
    Write-Host "  ✓ Installed: $($plugin.Name)" -ForegroundColor Green
  }
  catch {
    Write-Host "  ✗ Failed to copy $($plugin.Name): $_" -ForegroundColor Red
  }
}

# ═══════════════════════════════════════════════════════════════════
#  Verification
# ═══════════════════════════════════════════════════════════════════

Write-Host "`nVerifying installation..." -ForegroundColor Yellow

$installedCount = 0

if ($isAdmin) {
  foreach ($plugin in $foundPlugins) {
    $destPath = Join-Path $systemEffectsPath $plugin.Name
    if (Test-Path $destPath) {
      Write-Host "  ✓ Verified (system): $($plugin.Name)" -ForegroundColor Green
      $installedCount++
    }
  }
}

foreach ($plugin in $foundPlugins) {
  $destPath = Join-Path $userAppFiles $plugin.Name
  if (Test-Path $destPath) {
    Write-Host "  ✓ Verified (user): $($plugin.Name)" -ForegroundColor Green
    $installedCount++
  }
}

# ═══════════════════════════════════════════════════════════════════
#  Summary
# ═══════════════════════════════════════════════════════════════════

Write-Host "`n" -ForegroundColor Cyan
if ($installedCount -gt 0) {
  Write-Host '✓ Installation completed successfully!' -ForegroundColor Green
  Write-Host "`nNext steps:" -ForegroundColor Cyan
  Write-Host "  1. Close Paint.NET if it's running"
  Write-Host '  2. Reopen Paint.NET'
  Write-Host '  3. Go to Effects > Tools'
  Write-Host '  4. You should see: Liquified, Grid Warp, Document Rectify'
  Write-Host "`nFor more information, visit:" -ForegroundColor Cyan
  Write-Host '  https://github.com/joelst/pdn-liquified' -ForegroundColor Cyan
}
else {
  Write-Host '✗ Installation failed!' -ForegroundColor Red
  Write-Host 'Please check the errors above and try again.' -ForegroundColor Yellow
  exit 1
}

Write-Host "`nPress any key to exit..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
