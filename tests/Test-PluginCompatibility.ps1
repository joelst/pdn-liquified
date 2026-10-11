#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PortableDirectory,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot 'PluginCompatibility\PluginCompatibility.csproj'
dotnet build $project -c $Configuration --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    throw 'Compatibility harness build failed.'
}

# Only run inside a disposable portable installation, never the user's installed Paint.NET.
$portable = (Resolve-Path -LiteralPath $PortableDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $portable 'paintdotnet.runtimeconfig.json')) -or
    $portable.StartsWith($env:ProgramFiles, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Supply an extracted disposable portable Paint.NET directory outside Program Files.'
}
$output = Join-Path $PSScriptRoot "PluginCompatibility\bin\$($Configuration)\net9.0-windows"
$ownedPaths = @()
try {
    foreach ($name in @('PluginCompatibility.exe', 'PluginCompatibility.dll')) {
        $target = Join-Path $portable $name
        if (Test-Path -LiteralPath $target) {
            throw "Compatibility harness path already exists: $($target)"
        }
        Copy-Item -LiteralPath (Join-Path $output $name) -Destination $target
        $ownedPaths += $target
    }
    $config = Join-Path $portable 'PluginCompatibility.runtimeconfig.json'
    if (Test-Path -LiteralPath $config) {
        throw "Compatibility runtime config already exists: $($config)"
    }
    Copy-Item -LiteralPath (Join-Path $portable 'paintdotnet.runtimeconfig.json') -Destination $config
    $ownedPaths += $config
    $plugins = Join-Path $portable ('compatibility-plugins-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $plugins | Out-Null
    $ownedPaths += $plugins
    foreach ($entry in @(
        @{ Project = 'Liquified'; Name = 'LiquifiedPlugin.dll' },
        @{ Project = 'GridWarp'; Name = 'GridWarpPlugin.dll' },
        @{ Project = 'DocumentRectify'; Name = 'DocumentRectifyPlugin.dll' }
    )) {
        Copy-Item -LiteralPath (Join-Path $repository "$($entry.Project)\bin\$($Configuration)\net9.0-windows\$($entry.Name)") -Destination $plugins
    }
    & (Join-Path $portable 'PluginCompatibility.exe') $plugins
    if ($LASTEXITCODE -ne 0) {
        throw "Plugin compatibility checks failed (exit $($LASTEXITCODE))."
    }
}
finally {
    foreach ($path in $ownedPaths) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}
