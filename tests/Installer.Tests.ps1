#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

$installer = Join-Path (Split-Path -Parent $PSScriptRoot) 'install-plugins.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('pdn-installer-tests-' + [Guid]::NewGuid().ToString('N'))
$flat = Join-Path $testRoot 'flat package'
$destination = Join-Path $testRoot 'redirected Documents\paint.net App Files\Effects'
$plugins = @('LiquifiedPlugin.dll', 'GridWarpPlugin.dll', 'DocumentRectifyPlugin.dll')

function Invoke-InstallerTest {
    param(
        [string] $Source,
        [string] $Target,
        [int] $ExpectedExitCode,
        [switch] $DryRun
    )
    $arguments = @('-NoProfile', '-File', $installer, '-PluginDirectory', $Source, '-DestinationDirectory', $Target)
    if ($DryRun) {
        $arguments += '-WhatIf'
    }
    & powershell.exe @arguments
    if ($LASTEXITCODE -ne $ExpectedExitCode) {
        throw "Expected exit $($ExpectedExitCode), received $($LASTEXITCODE)."
    }
}

try {
    New-Item -ItemType Directory -Path $flat -Force | Out-Null
    foreach ($name in $plugins) {
        [IO.File]::WriteAllText((Join-Path $flat $name), "fixture $($name)")
    }
    Invoke-InstallerTest -Source $flat -Target $destination -ExpectedExitCode 0 -DryRun
    if (Test-Path -LiteralPath $destination) {
        throw 'WhatIf changed the filesystem.'
    }
    Invoke-InstallerTest -Source $flat -Target $destination -ExpectedExitCode 0
    foreach ($name in $plugins) {
        $sourceHash = (Get-FileHash -LiteralPath (Join-Path $flat $name)).Hash
        $destinationHash = (Get-FileHash -LiteralPath (Join-Path $destination $name)).Hash
        if ($sourceHash -ne $destinationHash) {
            throw "Flat package verification failed for $($name)."
        }
    }
    [IO.File]::WriteAllText((Join-Path $destination $plugins[0]), 'stale DLL')
    Invoke-InstallerTest -Source $flat -Target $destination -ExpectedExitCode 0
    if ((Get-FileHash -LiteralPath (Join-Path $flat $plugins[0])).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $destination $plugins[0])).Hash) {
        throw 'A stale plugin was not replaced.'
    }

    $repository = Join-Path $testRoot 'repository'
    $projects = @('Liquified', 'GridWarp', 'DocumentRectify')
    for ($i = 0; $i -lt $plugins.Count; $i++) {
        $output = Join-Path $repository "$($projects[$i])\bin\Release\net9.0-windows"
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $flat $plugins[$i]) -Destination $output
    }
    Invoke-InstallerTest -Source $repository -Target (Join-Path $testRoot 'repository destination') -ExpectedExitCode 0

    $dryRunOutput = & powershell.exe -NoProfile -File $installer -PluginDirectory $repository -WhatIf
    if ($LASTEXITCODE -ne 0) {
        throw 'Default user-folder dry run failed.'
    }
    $actualEffects = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'paint.net App Files\Effects'
    if (-not (($dryRunOutput -join [Environment]::NewLine).Contains($actualEffects))) {
        throw 'Default installation does not use the actual Windows Documents folder.'
    }
    $fakeHost = Join-Path $testRoot 'portable host'
    New-Item -ItemType Directory -Path $fakeHost | Out-Null
    [IO.File]::WriteAllText((Join-Path $fakeHost 'paintdotnet.exe'), 'host fixture')
    & powershell.exe -NoProfile -File $installer -PluginDirectory $repository -InstallationScope System -PdnRoot $fakeHost -WhatIf
    if ($LASTEXITCODE -ne 0 -or (Test-Path -LiteralPath (Join-Path $fakeHost 'Effects'))) {
        throw 'System WhatIf requested elevation or changed the destination.'
    }

    Remove-Item -LiteralPath (Join-Path $flat $plugins[2])
    $missingDestination = Join-Path $testRoot 'missing destination'
    Invoke-InstallerTest -Source $flat -Target $missingDestination -ExpectedExitCode 1
    if (Test-Path -LiteralPath $missingDestination) {
        throw 'Missing source DLLs caused a partial installation.'
    }
    $blocked = Join-Path $testRoot 'destination is a file'
    [IO.File]::WriteAllText($blocked, 'not a directory')
    Invoke-InstallerTest -Source $repository -Target $blocked -ExpectedExitCode 1
    Write-Output 'PASS: flat/repository packages, WhatIf, redirected destination, stale replacement, missing files and copy failure.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
