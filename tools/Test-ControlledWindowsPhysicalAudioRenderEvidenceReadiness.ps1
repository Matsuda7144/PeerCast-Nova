[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$ExpectedCommit,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$RuntimeDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$FixturePath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$VideoRecordPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$RenderEvidenceRecordPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$TerminalRenderRecordPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$FailureRecordPath,

    [ValidateRange(4, 4096)]
    [int]$MaximumDecodeOperations = 128,

    [ValidateRange(4, 4096)]
    [int]$MaximumDeliveryOperations = 128,

    [ValidateRange(1, 1048576)]
    [int]$FixturePacketBytes = 65536,

    [ValidateRange(0, 30000)]
    [int]$ObservationHoldMilliseconds = 1000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedFixtureSha256 = '32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15'
$expectedFixtureBytes = 66939L
$expectedRuntimeBuild = 'btbn.ffmpeg.20260721.win64.lgpl-shared'
$expectedAudioBufferMilliseconds = 100
$validationPrefix = 'Controlled render readiness rejected: '
$validationMarker = 'PeerCastNovaControlledRenderReadinessValidation'

function Stop-ReadinessValidation {
    param([Parameter(Mandatory = $true)][string]$Message)

    $exception = New-Object `
        -TypeName System.InvalidOperationException `
        -ArgumentList ($validationPrefix + $Message)
    $exception.Data[$validationMarker] = $true
    throw $exception
}

function Get-ReadinessValidationFailure {
    param([Parameter(Mandatory = $true)][System.Exception]$Exception)

    $current = $Exception
    while ($null -ne $current) {
        if ($current.Data.Contains($validationMarker) -and
            $current.Data[$validationMarker] -eq $true) {
            return $current
        }
        $current = $current.InnerException
    }
    return $null
}

function Test-IsUnderPath {
    param(
        [Parameter(Mandatory = $true)][string]$Candidate,
        [Parameter(Mandatory = $true)][string]$Parent
    )

    $candidateFull = [System.IO.Path]::GetFullPath($Candidate)
    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd(
        [char[]]@('\', '/'))
    return $candidateFull.Equals(
        $parentFull,
        [System.StringComparison]::OrdinalIgnoreCase) -or
        $candidateFull.StartsWith(
            $parentFull + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)
}

function Invoke-SanitizedNativeCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$CaptureOutput
    )

    $standardOutputPath = [System.IO.Path]::GetTempFileName()
    $standardErrorPath = [System.IO.Path]::GetTempFileName()
    try {
        $nativeErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & $FilePath @Arguments 1> $standardOutputPath 2> $standardErrorPath
            $exitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $nativeErrorActionPreference
        }

        $outputLines = if ($CaptureOutput) {
            @(Get-Content -LiteralPath $standardOutputPath)
        }
        else {
            @()
        }
        $standardOutputBytes = (Get-Item -LiteralPath $standardOutputPath).Length
        $standardErrorBytes = (Get-Item -LiteralPath $standardErrorPath).Length
        return [pscustomobject]@{
            ExitCode = $exitCode
            OutputLines = $outputLines
            StandardOutputBytes = [long]$standardOutputBytes
            StandardErrorBytes = [long]$standardErrorBytes
        }
    }
    finally {
        Remove-Item -LiteralPath $standardOutputPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $standardErrorPath -Force -ErrorAction SilentlyContinue
    }
}

$locationPushed = $false
try {
    if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
        Stop-ReadinessValidation 'Windows is required.'
    }

    $repoRootCommand = Invoke-SanitizedNativeCommand `
        -FilePath 'git' `
        -Arguments @('rev-parse', '--show-toplevel') `
        -CaptureOutput
    $repoRootText = @($repoRootCommand.OutputLines)
    if ($repoRootCommand.ExitCode -ne 0 -or $repoRootText.Count -ne 1) {
        Stop-ReadinessValidation 'run this command from the PeerCast Nova repository.'
    }

    $repoRoot = [System.IO.Path]::GetFullPath([string]$repoRootText[0])
    Push-Location $repoRoot
    $locationPushed = $true

    $branchCommand = Invoke-SanitizedNativeCommand `
        -FilePath 'git' `
        -Arguments @('branch', '--show-current') `
        -CaptureOutput
    $branch = (@($branchCommand.OutputLines) -join '').Trim()
    if ($branchCommand.ExitCode -ne 0 -or $branch -cne 'main') {
        Stop-ReadinessValidation 'the checked-out branch must be main.'
    }

    $headCommand = Invoke-SanitizedNativeCommand `
        -FilePath 'git' `
        -Arguments @('rev-parse', 'HEAD') `
        -CaptureOutput
    $head = (@($headCommand.OutputLines) -join '').Trim()
    if ($headCommand.ExitCode -ne 0 -or $head -ine $ExpectedCommit) {
        Stop-ReadinessValidation 'HEAD does not match the reviewed commit.'
    }

    $statusCommand = Invoke-SanitizedNativeCommand `
        -FilePath 'git' `
        -Arguments @('status', '--porcelain', '--untracked-files=all') `
        -CaptureOutput
    if ($statusCommand.ExitCode -ne 0 -or @($statusCommand.OutputLines).Count -ne 0) {
        Stop-ReadinessValidation 'the repository working tree must be clean.'
    }

    $runtimeFull = [System.IO.Path]::GetFullPath($RuntimeDirectory)
    $fixtureFull = [System.IO.Path]::GetFullPath($FixturePath)
    $videoRecordFull = [System.IO.Path]::GetFullPath($VideoRecordPath)
    $evidenceRecordFull = [System.IO.Path]::GetFullPath($RenderEvidenceRecordPath)
    $terminalRecordFull = [System.IO.Path]::GetFullPath($TerminalRenderRecordPath)
    $failureRecordFull = [System.IO.Path]::GetFullPath($FailureRecordPath)

    if (-not [System.IO.Directory]::Exists($runtimeFull)) {
        Stop-ReadinessValidation 'the external runtime directory is unavailable.'
    }
    if (-not [System.IO.File]::Exists($fixtureFull)) {
        Stop-ReadinessValidation 'the retained controlled fixture is unavailable.'
    }
    if (-not [System.IO.File]::Exists($videoRecordFull)) {
        Stop-ReadinessValidation 'the accepted video record is unavailable.'
    }

    foreach ($recordPath in @(
            $evidenceRecordFull,
            $terminalRecordFull,
            $failureRecordFull)) {
        if ([System.IO.File]::Exists($recordPath) -or
            [System.IO.Directory]::Exists($recordPath)) {
            Stop-ReadinessValidation 'every future output record path must be fresh.'
        }
        $recordDirectory = Split-Path -Parent $recordPath
        if ([string]::IsNullOrWhiteSpace($recordDirectory) -or
            -not [System.IO.Directory]::Exists($recordDirectory)) {
            Stop-ReadinessValidation 'every future output record parent directory must exist.'
        }
    }

    $recordPaths = @(
        $evidenceRecordFull,
        $terminalRecordFull,
        $failureRecordFull)
    for ($left = 0; $left -lt $recordPaths.Count; $left++) {
        for ($right = $left + 1; $right -lt $recordPaths.Count; $right++) {
            if ($recordPaths[$left].Equals(
                    $recordPaths[$right],
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                Stop-ReadinessValidation 'future output record paths must be pairwise distinct.'
            }
        }
    }

    foreach ($externalPath in @(
            $runtimeFull,
            $fixtureFull,
            $videoRecordFull,
            $evidenceRecordFull,
            $terminalRecordFull,
            $failureRecordFull)) {
        if (Test-IsUnderPath -Candidate $externalPath -Parent $repoRoot) {
            Stop-ReadinessValidation 'runtime, fixture, video record, and future outputs must remain repository-external.'
        }
    }

    $fixtureInfo = Get-Item -LiteralPath $fixtureFull
    if ($fixtureInfo.Length -ne $expectedFixtureBytes) {
        Stop-ReadinessValidation 'the controlled fixture byte count does not match.'
    }
    $fixtureDigest = (Get-FileHash -LiteralPath $fixtureFull -Algorithm SHA256).Hash
    if ($fixtureDigest -ine $expectedFixtureSha256) {
        Stop-ReadinessValidation 'the controlled fixture SHA-256 does not match.'
    }

    $restoreCommand = Invoke-SanitizedNativeCommand `
        -FilePath 'dotnet' `
        -Arguments @('restore', (Join-Path $repoRoot 'PeerCast-Nova.sln'))
    if ($restoreCommand.ExitCode -ne 0) {
        Stop-ReadinessValidation 'solution restore failed.'
    }

    $runnerProject = Join-Path $repoRoot `
        'src\Nova.ControlledWindowsRunner\Nova.ControlledWindowsRunner.csproj'
    $buildCommand = Invoke-SanitizedNativeCommand `
        -FilePath 'dotnet' `
        -Arguments @('build', $runnerProject, '--configuration', 'Release', '--no-restore')
    if ($buildCommand.ExitCode -ne 0) {
        Stop-ReadinessValidation 'controlled Windows runner build failed.'
    }

    $runnerAssembly = Join-Path $repoRoot `
        'src\Nova.ControlledWindowsRunner\bin\Release\net10.0\Nova.ControlledWindowsRunner.dll'
    if (-not [System.IO.File]::Exists($runnerAssembly)) {
        Stop-ReadinessValidation 'the controlled Windows runner assembly is unavailable.'
    }

    $videoPreflightCommand = Invoke-SanitizedNativeCommand `
        -FilePath 'dotnet' `
        -Arguments @(
            $runnerAssembly,
            '--verify-controlled-windows-run-record',
            '--record',
            $videoRecordFull,
            '--expected-kind',
            'video',
            '--fixture-sha256',
            $expectedFixtureSha256) `
        -CaptureOutput
    $videoPreflightLines = @($videoPreflightCommand.OutputLines)
    if ($videoPreflightCommand.ExitCode -ne 0 -or
        $videoPreflightLines -cnotcontains 'Result: VideoAccepted' -or
        $videoPreflightLines -cnotcontains 'Expected-Kind: Video' -or
        $videoPreflightLines -cnotcontains 'Record-Accepted: True' -or
        $videoPreflightLines -cnotcontains `
            'Physical-Runs-Executed-By-This-Command: False') {
        Stop-ReadinessValidation 'the video record gate was not accepted.'
    }

    $postStatusCommand = Invoke-SanitizedNativeCommand `
        -FilePath 'git' `
        -Arguments @('status', '--porcelain', '--untracked-files=all') `
        -CaptureOutput
    if ($postStatusCommand.ExitCode -ne 0 -or
        @($postStatusCommand.OutputLines).Count -ne 0) {
        Stop-ReadinessValidation 'the repository changed during readiness validation.'
    }

    @(
        'PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-READINESS 1',
        'Milestone: 0.0.10d Phase 7',
        'Kind: Audio',
        'Result: ReadyForSeparateAuthorization',
        ('Validated-Commit: ' + $head.ToLowerInvariant()),
        ('Expected-Runtime-Build: ' + $expectedRuntimeBuild),
        'Runtime-Directory-Present: True',
        ('Fixture-SHA256: ' + $expectedFixtureSha256),
        ('Fixture-Bytes: ' + $expectedFixtureBytes.ToString([System.Globalization.CultureInfo]::InvariantCulture)),
        'Video-Record-Gate: Accepted',
        ('Maximum-Decode-Operations: ' + $MaximumDecodeOperations.ToString([System.Globalization.CultureInfo]::InvariantCulture)),
        ('Maximum-Delivery-Operations: ' + $MaximumDeliveryOperations.ToString([System.Globalization.CultureInfo]::InvariantCulture)),
        ('Fixture-Packet-Bytes: ' + $FixturePacketBytes.ToString([System.Globalization.CultureInfo]::InvariantCulture)),
        ('Observation-Hold-Milliseconds: ' + $ObservationHoldMilliseconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)),
        ('Audio-Buffer-Milliseconds: ' + $expectedAudioBufferMilliseconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)),
        'Completed-Record-Path-Fresh: True',
        'Terminal-Record-Path-Fresh: True',
        'Failure-Record-Path-Fresh: True',
        'Record-Paths-Repository-External: True',
        'Record-Paths-Pairwise-Distinct: True',
        'Runner-Build: Accepted',
        'Readiness-Only: True',
        'Physical-Run-Authorization-Granted: False',
        'Physical-Runs-Executed-By-This-Command: False',
        'Audio-Acceptance-Claimed: False',
        'External-Binary-Or-Payload-Published: False',
        'END-PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-READINESS') |
        ForEach-Object { Write-Output $_ }
    exit 0
}
catch {
    $validationFailure = Get-ReadinessValidationFailure -Exception $_.Exception
    if ($null -ne $validationFailure) {
        [System.Console]::Error.WriteLine($validationFailure.Message)
    }
    else {
        [System.Console]::Error.WriteLine(
            $validationPrefix + 'unexpected readiness failure.')
    }
    [System.Console]::Error.WriteLine('Physical runs executed by this command: No')
    [System.Console]::Error.WriteLine('Readiness does not authorize physical execution.')
    exit 1
}
finally {
    if ($locationPushed) {
        Pop-Location
    }
}
