#Requires -Version 7.2
[CmdletBinding(DefaultParameterSetName = 'Package')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Package')]
    [string] $PackageDirectory,
    [Parameter(Mandatory, ParameterSetName = 'File')]
    [string] $FilePath
)
$ErrorActionPreference = 'Stop'

$required = @(
    'AZURE_TRUSTED_SIGNING_ENDPOINT',
    'AZURE_TRUSTED_SIGNING_ACCOUNT_NAME',
    'AZURE_TRUSTED_SIGNING_CERTIFICATE_PROFILE_NAME'
)
foreach ($name in $required) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "Signing configuration missing: $($name). Refusing to produce an unsigned release."
    }
}
if ($PSCmdlet.ParameterSetName -eq 'File') {
    $files = @((Resolve-Path -LiteralPath $FilePath).Path)
    if ([IO.Path]::GetExtension($files[0]) -ne '.exe' -and
        [IO.Path]::GetFileName($files[0]) -notin @('uninst.e32.tmp', 'uninst.e64.tmp')) {
        throw 'Installer signing requires an EXE or Inno temporary uninstaller file.'
    }
}
else {
    $directory = (Resolve-Path -LiteralPath $PackageDirectory).Path
    $names = @('LiquifiedPlugin.dll', 'GridWarpPlugin.dll', 'DocumentRectifyPlugin.dll')
    $files = foreach ($name in $names) {
        $path = Join-Path $directory $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Release package is incomplete: $($name)."
        }
        $path
    }
}
Import-Module ArtifactSigning -RequiredVersion 0.1.8 -ErrorAction Stop
foreach ($file in $files) {
    Invoke-ArtifactSigning `
        -Endpoint $env:AZURE_TRUSTED_SIGNING_ENDPOINT `
        -CodeSigningAccountName $env:AZURE_TRUSTED_SIGNING_ACCOUNT_NAME `
        -CertificateProfileName $env:AZURE_TRUSTED_SIGNING_CERTIFICATE_PROFILE_NAME `
        -Files $file `
        -FileDigest SHA256 `
        -TimestampRfc3161 'http://timestamp.acs.microsoft.com' `
        -TimestampDigest SHA256 `
        -Description 'Paint.NET Distortion Plugins' `
        -DescriptionUrl 'https://github.com/joelst/pdn-liquified' `
        -ExcludeManagedIdentityCredential `
        -ExcludeInteractiveBrowserCredential
    $signature = Get-AuthenticodeSignature -LiteralPath $file
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or
        $null -eq $signature.TimeStamperCertificate) {
        throw "Signing verification failed for $($file): $($signature.Status). A trusted, timestamped signature is required."
    }
    Write-Output "Verified timestamped Authenticode signature: $([IO.Path]::GetFileName($file))"
}
# Checksums must describe the signed bytes, never the pre-signing compiler output.
if ($PSCmdlet.ParameterSetName -eq 'Package') {
    $hashes = foreach ($file in $files) {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($file)
    }
    [IO.File]::WriteAllLines((Join-Path $directory 'SHA256SUMS.txt'), $hashes, [Text.Encoding]::ASCII)
}
