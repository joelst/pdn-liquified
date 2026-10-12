#Requires -Version 5.1
<#
.SYNOPSIS
    Installs the three Paint.NET distortion plugins and verifies their contents.
.DESCRIPTION
    Defaults to the current user's actual Documents folder, including OneDrive or
    folder redirection. System installation is opt-in and may require elevation.
    Supports flat release packages and the repository's Release output folders.
.EXAMPLE
    .\install-plugins.ps1 -WhatIf
.EXAMPLE
    .\install-plugins.ps1 -InstallationScope System -PdnRoot 'D:\Apps\paint.net'
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('User', 'System')]
    [string] $InstallationScope = 'User',
    [string] $PdnRoot,
    [string] $PluginDirectory = $PSScriptRoot,
    [string] $DestinationDirectory
)

$ErrorActionPreference = 'Stop'

try {
    $plugins = @(
        @{ Name = 'LiquifiedPlugin.dll'; Project = 'Liquified' },
        @{ Name = 'GridWarpPlugin.dll'; Project = 'GridWarp' },
        @{ Name = 'DocumentRectifyPlugin.dll'; Project = 'DocumentRectify' }
    )
    foreach ($plugin in $plugins) {
        $source = Join-Path $PluginDirectory $plugin.Name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            $source = Join-Path $PluginDirectory "$($plugin.Project)\bin\Release\net9.0-windows\$($plugin.Name)"
        }
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Missing $($plugin.Name). Extract the entire release package or build all three projects in Release."
        }
        $plugin.Source = (Get-Item -LiteralPath $source).FullName
        $plugin.Hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    }

    if ($InstallationScope -eq 'System') {
        if (-not $PdnRoot) {
            $registryPath = 'HKLM:\SOFTWARE\paint.net'
            if (Test-Path -LiteralPath $registryPath) {
                $PdnRoot = (Get-ItemProperty -LiteralPath $registryPath).TARGETDIR
            }
            if (-not $PdnRoot) {
                $PdnRoot = Join-Path $env:ProgramFiles 'paint.net'
            }
        }
        if (-not (Test-Path -LiteralPath (Join-Path $PdnRoot 'paintdotnet.exe') -PathType Leaf)) {
            throw 'Paint.NET was not found. Specify -PdnRoot for a portable/custom install, or use the default User installation for Microsoft Store Paint.NET.'
        }
        if (-not $DestinationDirectory) {
            $DestinationDirectory = Join-Path $PdnRoot 'Effects'
        }

        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        try {
            $principal = [Security.Principal.WindowsPrincipal]::new($identity)
            $isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        }
        finally {
            $identity.Dispose()
        }
        if (-not $isAdmin -and -not $WhatIfPreference) {
            if (-not $PSCmdlet.ShouldProcess($DestinationDirectory, 'Elevate and install all three plugins')) {
                return
            }
            # Encode literal arguments rather than passing paths through native command-line quoting.
            $scriptLiteral = $PSCommandPath.Replace("'", "''")
            $rootLiteral = $PdnRoot.Replace("'", "''")
            $sourceLiteral = $PluginDirectory.Replace("'", "''")
            $destinationLiteral = $DestinationDirectory.Replace("'", "''")
            $invocation = "& '$($scriptLiteral)' -InstallationScope System -PdnRoot '$($rootLiteral)' -PluginDirectory '$($sourceLiteral)' -DestinationDirectory '$($destinationLiteral)'"
            $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($invocation))
            $child = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded) -Verb RunAs -Wait -PassThru
            try {
                $childExitCode = $child.ExitCode
            }
            finally {
                $child.Dispose()
            }
            exit $childExitCode
        }
    }
    elseif (-not $DestinationDirectory) {
        $documents = [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
        if (-not $documents) {
            throw 'Windows did not return a Documents folder. Specify -DestinationDirectory explicitly.'
        }
        $DestinationDirectory = Join-Path $documents 'paint.net App Files\Effects'
    }

    if (-not $PSCmdlet.ShouldProcess($DestinationDirectory, 'Install and SHA256-verify all three plugins')) {
        return
    }
    New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
    foreach ($plugin in $plugins) {
        $destination = Join-Path $DestinationDirectory $plugin.Name
        if ($plugin.Source -ne [IO.Path]::GetFullPath($destination)) {
            Copy-Item -LiteralPath $plugin.Source -Destination $destination -Force
        }
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $plugin.Hash) {
            throw "Verification failed for $($plugin.Name). Installation is incomplete; close Paint.NET and retry."
        }
        Unblock-File -LiteralPath $destination
        Write-Output "Verified: $($plugin.Name)"
    }
    Write-Output ('Installed all three plugins to {0}. Restart Paint.NET, then open Effects > Tools.' -f $DestinationDirectory)
}
catch {
    Write-Error -Message "Installation failed: $($_.Exception.Message)" -ErrorAction Continue
    exit 1
}
