BeforeAll {
    $workflow = Join-Path (Split-Path -Parent $PSScriptRoot) '.github/workflows/signed-release.yml'
    $text = Get-Content -LiteralPath $workflow -Raw
    $body = [regex]::Match($text, '(?ms)        run: \|\r?\n(?<body>.*?)\r?\n      - name: Checkout trusted signing scripts').Groups['body'].Value
    if (-not $body) { throw 'Build provenance step not found.' }
    $script:validate = [scriptblock]::Create($body)
    $script:previousValues = @{}
    foreach ($name in @('REQUESTED_RUN', 'REPOSITORY', 'GITHUB_OUTPUT')) {
        $script:previousValues[$name] = [Environment]::GetEnvironmentVariable($name)
    }
    function gh { throw 'Unexpected live GitHub API call.' }
}

AfterAll {
    foreach ($name in $script:previousValues.Keys) {
        [Environment]::SetEnvironmentVariable($name, $script:previousValues[$name])
    }
}

Describe 'Trusted signing build selection' {
    BeforeEach {
        $env:REPOSITORY = 'joelst/pdn-liquified'
        $env:REQUESTED_RUN = '123456789'
        $env:GITHUB_OUTPUT = Join-Path $TestDrive 'outputs.txt'
        Remove-Item -LiteralPath $env:GITHUB_OUTPUT -ErrorAction SilentlyContinue
        $script:run = @{
            id = 123456789
            workflow_id = 42
            conclusion = 'success'
            status = 'completed'
            head_branch = 'main'
            head_repository = @{ full_name = 'joelst/pdn-liquified' }
            event = 'push'
            head_sha = 'a' * 40
        }
        $script:workflowPath = '.github/workflows/build.yml'
        $script:pages = @(@{ workflow_runs = @($script:run) })
        Mock gh {
            $global:LASTEXITCODE = 0
            switch ($args[1]) {
                'repos/joelst/pdn-liquified/actions/workflows/build.yml/runs?branch=main&status=success&per_page=100' {
                    ConvertTo-Json -InputObject $script:pages -Depth 5 -Compress
                }
                'repos/joelst/pdn-liquified/actions/runs/123456789' {
                    ConvertTo-Json -InputObject $script:run -Depth 5 -Compress
                }
                'repos/joelst/pdn-liquified/actions/workflows/42' {
                    @{ path = $script:workflowPath } | ConvertTo-Json -Compress
                }
                default { throw "Unexpected API endpoint: $($args[1])" }
            }
        }
    }

    It 'preserves explicit and workflow_run build IDs without discovery' {
        & $script:validate
        Get-Content -LiteralPath $env:GITHUB_OUTPUT | Should -Be @('run_id=123456789', "sha=$('a' * 40)")
        Should -Invoke gh -Times 2 -Exactly
    }

    It 'discovers a successful main Build when the input is blank' {
        $env:REQUESTED_RUN = ''
        & $script:validate
        Get-Content -LiteralPath $env:GITHUB_OUTPUT | Should -Contain 'run_id=123456789'
        Should -Invoke gh -Times 1 -Exactly -ParameterFilter {
            $args[1] -like '*?branch=main&status=success&per_page=100' -and
            $args[2] -eq '--paginate' -and $args[3] -eq '--slurp'
        }
    }

    It 'skips pull-request runs and searches subsequent pages' {
        $env:REQUESTED_RUN = ''
        $script:pages = @(
            @{ workflow_runs = @(@{ id = 987654321; event = 'pull_request' }) },
            @{ workflow_runs = @($script:run) }
        )
        $script:run.event = 'workflow_dispatch'
        & $script:validate
        Get-Content -LiteralPath $env:GITHUB_OUTPUT | Should -Contain 'run_id=123456789'
    }

    It 'selects the newest eligible run rather than an older one' {
        $env:REQUESTED_RUN = ''
        $script:pages = @(@{ workflow_runs = @($script:run, @{ id = 111111111; event = 'push' }) })
        & $script:validate
        Get-Content -LiteralPath $env:GITHUB_OUTPUT | Should -Contain 'run_id=123456789'
    }

    It 'rejects package versions with actionable guidance before API access' {
        $env:REQUESTED_RUN = '1.0.261013'
        { & $script:validate } | Should -Throw '*Set package_version in Build*'
        Should -Invoke gh -Times 0 -Exactly
        Test-Path -LiteralPath $env:GITHUB_OUTPUT | Should -BeFalse
    }

    It 'fails closed when no eligible build exists' {
        $env:REQUESTED_RUN = ''
        $script:pages = @(@{ workflow_runs = @(@{ id = 987654321; event = 'pull_request' }) })
        { & $script:validate } | Should -Throw '*No successful main-branch Build found*'
        Test-Path -LiteralPath $env:GITHUB_OUTPUT | Should -BeFalse
    }

    It 'fails closed on a discovery API error' {
        $env:REQUESTED_RUN = ''
        Mock gh { $global:LASTEXITCODE = 1; '{"workflow_runs":[]}' }
        { & $script:validate } | Should -Throw '*Build run discovery failed*'
        Test-Path -LiteralPath $env:GITHUB_OUTPUT | Should -BeFalse
    }

    It 'fails closed on a run lookup API error' {
        Mock gh { $global:LASTEXITCODE = 1; '{}' }
        { & $script:validate } | Should -Throw '*Build run lookup failed*'
        Test-Path -LiteralPath $env:GITHUB_OUTPUT | Should -BeFalse
    }

    It 'rejects untrusted <Field> for both explicit and discovered builds' -ForEach @(
        @{ Field = 'conclusion'; Value = 'failure' }
        @{ Field = 'status'; Value = 'in_progress' }
        @{ Field = 'head_branch'; Value = 'feature' }
        @{ Field = 'head_repository'; Value = @{ full_name = 'fork/pdn-liquified' } }
        @{ Field = 'event'; Value = 'pull_request' }
        @{ Field = 'head_sha'; Value = 'invalid' }
    ) {
        $script:run[$Field] = $Value
        { & $script:validate } | Should -Throw '*Only successful trusted main-branch Build runs*'
        $env:REQUESTED_RUN = ''
        if ($Field -eq 'event') {
            { & $script:validate } | Should -Throw '*No successful main-branch Build found*'
        } else {
            { & $script:validate } | Should -Throw '*Only successful trusted main-branch Build runs*'
        }
        Test-Path -LiteralPath $env:GITHUB_OUTPUT | Should -BeFalse
    }

    It 'rejects other workflows after discovery' {
        $env:REQUESTED_RUN = ''
        $script:workflowPath = '.github/workflows/other.yml'
        { & $script:validate } | Should -Throw '*Only successful trusted main-branch Build runs*'
        Test-Path -LiteralPath $env:GITHUB_OUTPUT | Should -BeFalse
    }
}
