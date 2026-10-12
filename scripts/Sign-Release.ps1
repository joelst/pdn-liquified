#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory
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
$directory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$names = @('LiquifiedPlugin.dll', 'GridWarpPlugin.dll', 'DocumentRectifyPlugin.dll')
$files = foreach ($name in $names) {
    $path = Join-Path $directory $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Release package is incomplete: $($name)."
    }
    $path
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
$hashes = foreach ($file in $files) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($file)
}
[IO.File]::WriteAllLines((Join-Path $directory 'SHA256SUMS.txt'), $hashes, [Text.Encoding]::ASCII)
