#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')]
    [string] $PackageVersion,
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\installer'),
    [string] $CompilerPath,
    [switch] $Signed
)
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
foreach ($name in @('LiquifiedPlugin.dll', 'GridWarpPlugin.dll', 'DocumentRectifyPlugin.dll', 'LICENSE', 'README.md', 'INSTALL.md')) {
    $path = Join-Path $package $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Installer package is incomplete: $($name)."
    }
    if ($Signed -and $name.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($signature.Status -ne 'Valid' -or $null -eq $signature.TimeStamperCertificate) {
            throw "Installer input must be trusted and timestamped: $($name)."
        }
    }
}
if (-not $CompilerPath) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compiler) {
        $CompilerPath = $compiler.Source
    }
    else {
        $CompilerPath = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    }
}
if (-not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Inno Setup compiler not found. Install Inno Setup 6.5.4 or supply -CompilerPath.'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$numericVersion = ($PackageVersion -split '-')[0]
foreach ($part in $numericVersion.Split('.')) {
    if ([long]$part -gt 65535) {
        throw 'Package version components must not exceed 65535 for Windows version resources.'
    }
}
$arguments = @("/DPackageDirectory=$($package)", "/DOutputDirectory=$($output)",
    "/DAppVersion=$($PackageVersion)", "/DNumericVersion=$($numericVersion).0")
if ($Signed) {
    $signer = Join-Path $PSScriptRoot 'Sign-Release.ps1'
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $arguments += '/DSignedBuild'
    $arguments += ('/Sartifactsigning=$q{0}$q -NoProfile -File $q{1}$q -FilePath $f' -f $pwsh, $signer)
}
$arguments += Join-Path $PSScriptRoot '..\installer\DistortionPlugins.iss'
& $CompilerPath @arguments | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed (exit $($LASTEXITCODE))."
}
$setup = Join-Path $output 'pdn-liquified-setup.exe'
if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) {
    throw 'Inno Setup did not produce the expected installer.'
}
$versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($setup)
if ($versionInfo.ProductVersion.Trim() -ne $PackageVersion -or
    $versionInfo.FileVersion.Trim() -ne $PackageVersion) {
    throw 'Installer version resources do not match the requested package version.'
}
if ($Signed) {
    $signature = Get-AuthenticodeSignature -LiteralPath $setup
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.TimeStamperCertificate) {
        throw 'Installer signature verification failed.'
    }
}
Write-Output $setup
