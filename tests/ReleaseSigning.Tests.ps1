BeforeAll {
    Import-Module ArtifactSigning -RequiredVersion 0.1.8
    $script:signer = Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\Sign-Release.ps1'
    $script:configurationNames = @(
        'AZURE_TRUSTED_SIGNING_ENDPOINT',
        'AZURE_TRUSTED_SIGNING_ACCOUNT_NAME',
        'AZURE_TRUSTED_SIGNING_CERTIFICATE_PROFILE_NAME'
    )
    $script:previousValues = @{}
    foreach ($name in $script:configurationNames) {
        $script:previousValues[$name] = [Environment]::GetEnvironmentVariable($name)
    }
}

AfterAll {
    foreach ($name in $script:configurationNames) {
        [Environment]::SetEnvironmentVariable($name, $script:previousValues[$name])
    }
}

Describe 'Fail-closed release signing' {
    BeforeEach {
        $script:package = Join-Path $TestDrive 'package'
        New-Item -ItemType Directory -Path $script:package -Force | Out-Null
        foreach ($name in @('LiquifiedPlugin.dll', 'GridWarpPlugin.dll', 'DocumentRectifyPlugin.dll')) {
            [IO.File]::WriteAllText((Join-Path $script:package $name), 'unsigned fixture')
        }
        $env:AZURE_TRUSTED_SIGNING_ENDPOINT = 'https://test.codesigning.azure.net'
        $env:AZURE_TRUSTED_SIGNING_ACCOUNT_NAME = 'test-account'
        $env:AZURE_TRUSTED_SIGNING_CERTIFICATE_PROFILE_NAME = 'test-profile'
        Mock Import-Module {}
        Mock Invoke-ArtifactSigning {
            [IO.File]::AppendAllText($Files, 'signed bytes')
        }
        Mock Get-AuthenticodeSignature {
            [pscustomobject]@{ Status = 'Valid'; SignerCertificate = 'test'; TimeStamperCertificate = 'test' }
        }
    }

    It 'signs all merged DLLs using SHA256 and the working RFC3161 endpoint' {
        & $script:signer -PackageDirectory $script:package
        Should -Invoke Invoke-ArtifactSigning -Times 3 -Exactly -ParameterFilter {
            $FileDigest -eq 'SHA256' -and $TimestampDigest -eq 'SHA256' -and
            $TimestampRfc3161 -eq 'http://timestamp.acs.microsoft.com'
        }
        $hashes = Get-Content -LiteralPath (Join-Path $script:package 'SHA256SUMS.txt')
        $hashes.Count | Should -Be 3
        $expected = (Get-FileHash -LiteralPath (Join-Path $script:package 'LiquifiedPlugin.dll')).Hash.ToLowerInvariant()
        $hashes[0] | Should -Be "$($expected)  LiquifiedPlugin.dll"
    }

    It 'rejects missing configuration rather than silently skipping signing' {
        [Environment]::SetEnvironmentVariable('AZURE_TRUSTED_SIGNING_ENDPOINT', $null)
        { & $script:signer -PackageDirectory $script:package } | Should -Throw '*configuration missing*'
        Should -Invoke Invoke-ArtifactSigning -Times 0 -Exactly
    }

    It 'rejects an incomplete package before signing any file' {
        Remove-Item -LiteralPath (Join-Path $script:package 'DocumentRectifyPlugin.dll')
        { & $script:signer -PackageDirectory $script:package } | Should -Throw '*incomplete*'
        Should -Invoke Invoke-ArtifactSigning -Times 0 -Exactly
    }

    It 'requires a timestamp certificate' {
        Mock Get-AuthenticodeSignature {
            [pscustomobject]@{ Status = 'Valid'; SignerCertificate = 'test'; TimeStamperCertificate = $null }
        }
        { & $script:signer -PackageDirectory $script:package } | Should -Throw '*verification failed*'
    }

    It 'rejects invalid signatures' {
        Mock Get-AuthenticodeSignature {
            [pscustomobject]@{ Status = 'HashMismatch'; SignerCertificate = 'test'; TimeStamperCertificate = 'test' }
        }
        { & $script:signer -PackageDirectory $script:package } | Should -Throw '*verification failed*'
    }

    It 'propagates signing service failures' {
        Mock Invoke-ArtifactSigning { throw 'service denied signing' }
        { & $script:signer -PackageDirectory $script:package } | Should -Throw '*service denied*'
    }

    It 'signs an installer EXE without replacing package checksums' {
        $setup = Join-Path $script:package 'setup.exe'
        [IO.File]::WriteAllText($setup, 'unsigned setup')
        $checksum = Join-Path $script:package 'SHA256SUMS.txt'
        [IO.File]::WriteAllText($checksum, 'existing package checksums')
        & $script:signer -FilePath $setup
        Should -Invoke Invoke-ArtifactSigning -Times 1 -Exactly
        [IO.File]::ReadAllText($checksum) | Should -Be 'existing package checksums'
    }

    It 'rejects non-EXE single-file signing' {
        { & $script:signer -FilePath (Join-Path $script:package 'LiquifiedPlugin.dll') } | Should -Throw '*EXE*'
        Should -Invoke Invoke-ArtifactSigning -Times 0 -Exactly
    }

    It 'accepts the temporary PE uninstaller used by Inno signing hooks' {
        $temporaryUninstaller = Join-Path $script:package 'uninst.e32.tmp'
        [IO.File]::WriteAllText($temporaryUninstaller, 'uninstaller fixture')
        & $script:signer -FilePath $temporaryUninstaller
        Should -Invoke Invoke-ArtifactSigning -Times 1 -Exactly
    }
}
