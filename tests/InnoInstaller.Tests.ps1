#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $InstallerPath,
    [Parameter(Mandatory)]
    [string] $PackageDirectory,
    [string] $TemporaryDirectory = [IO.Path]::GetTempPath(),
    [switch] $RequireSigned
)
$ErrorActionPreference = 'Stop'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{E87EFCDA-0785-41CC-95A7-237057E641A6}_is1'
if (Test-Path -LiteralPath $uninstallKey) {
    throw 'Run installer smoke checks on an account without an existing Distortion Plugins installation.'
}
$root = Join-Path $TemporaryDirectory ('pdn-inno-tests-' + [Guid]::NewGuid().ToString('N'))
$destination = Join-Path $root 'isolated Effects'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$plugins = @('LiquifiedPlugin.dll', 'GridWarpPlugin.dll', 'DocumentRectifyPlugin.dll')
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
$env:TEMP = $root
$env:TMP = $root
function Invoke-SetupProcess {
    param([string] $Path, [string[]] $Arguments)
    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -Wait -PassThru
    try {
        if ($process.ExitCode -ne 0) {
            throw "Installer process failed (exit $($process.ExitCode))."
        }
    }
    catch {
        $log = Join-Path $root 'setup.log'
        if (Test-Path -LiteralPath $log) {
            Get-Content -LiteralPath $log | Out-Host
        }
        throw
    }
    finally {
        $process.Dispose()
    }
}
try {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
        ('/DIR="{0}"' -f $destination), ('/LOG="{0}"' -f (Join-Path $root 'setup.log')))
    Invoke-SetupProcess -Path $InstallerPath -Arguments $arguments
    foreach ($name in $plugins) {
        if ((Get-FileHash -LiteralPath (Join-Path $destination $name)).Hash -ne
            (Get-FileHash -LiteralPath (Join-Path $PackageDirectory $name)).Hash) {
            throw "Installer changed or failed to copy $($name)."
        }
    }
    $uninstaller = Join-Path $destination 'pdn-liquified\unins000.exe'
    if ($RequireSigned) {
        foreach ($path in @($InstallerPath, $uninstaller)) {
            $signature = Get-AuthenticodeSignature -LiteralPath $path
            if ($signature.Status -ne 'Valid' -or $null -eq $signature.TimeStamperCertificate) {
                throw "Setup/Uninstall is not trusted and timestamped: $($path)."
            }
        }
    }
    [IO.File]::WriteAllText((Join-Path $destination 'unrelated-plugin.dll'), 'preserve me')
    [IO.File]::WriteAllText((Join-Path $destination $plugins[0]), 'stale plugin')
    Invoke-SetupProcess -Path $InstallerPath -Arguments $arguments
    if ((Get-FileHash -LiteralPath (Join-Path $destination $plugins[0])).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $PackageDirectory $plugins[0])).Hash) {
        throw 'Upgrade did not replace stale plugin bytes.'
    }
    Invoke-SetupProcess -Path $uninstaller -Arguments @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    foreach ($name in $plugins) {
        if (Test-Path -LiteralPath (Join-Path $destination $name)) {
            throw "Uninstall retained $($name)."
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $destination 'unrelated-plugin.dll'))) {
        throw 'Uninstall removed an unrelated plugin.'
    }
    Write-Output 'PASS: Inno install, byte preservation, upgrade, uninstall, and unrelated-plugin preservation.'
}
finally {
    $uninstaller = Join-Path $destination 'pdn-liquified\unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        Invoke-SetupProcess -Path $uninstaller -Arguments @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    }
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
    Remove-Item -LiteralPath $root -Recurse -Force
}
