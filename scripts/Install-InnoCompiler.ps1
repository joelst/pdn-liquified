#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $DestinationDirectory
)
$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath($DestinationDirectory)
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$download = Join-Path ([IO.Path]::GetTempPath()) ('innosetup-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_5_4/innosetup-6.5.4.exe' -OutFile $download
    $signature = Get-AuthenticodeSignature -LiteralPath $download
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|, )O=Pyrsys B\.V\.(,|$)') {
        throw 'Inno Setup compiler download failed publisher/signature verification.'
    }
    $process = Start-Process -FilePath $download -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/NOICONS',
        ('/DIR="{0}"' -f $destination)
    ) -Wait -PassThru
    try {
        if ($process.ExitCode -ne 0) {
            throw "Inno Setup compiler installation failed (exit $($process.ExitCode))."
        }
    }
    finally {
        $process.Dispose()
    }
    $compiler = Join-Path $destination 'ISCC.exe'
    if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
        throw 'Inno Setup compiler installation did not produce ISCC.exe.'
    }
    Write-Output $compiler
}
finally {
    if (Test-Path -LiteralPath $download) {
        Remove-Item -LiteralPath $download -Force
    }
}
