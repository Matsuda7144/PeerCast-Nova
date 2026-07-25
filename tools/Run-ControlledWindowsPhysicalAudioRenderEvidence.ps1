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
$maximumRecordCharacters = 32768
$validationPrefix = 'Controlled render evidence rejected: '
$validationMarker = 'PeerCastNovaControlledRenderEvidenceValidation'

function Stop-Validation {
    param([Parameter(Mandatory = $true)][string]$Message)

    $exception = New-Object `
        -TypeName System.InvalidOperationException `
        -ArgumentList ($validationPrefix + $Message)
    $exception.Data[$validationMarker] = $true
    throw $exception
}

function Get-ValidationFailure {
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
        $standardErrorBytes = (Get-Item -LiteralPath $standardErrorPath).Length
        return [pscustomobject]@{
            ExitCode = $exitCode
            OutputLines = $outputLines
            StandardErrorBytes = [long]$standardErrorBytes
        }
    }
    finally {
        Remove-Item -LiteralPath $standardOutputPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $standardErrorPath -Force -ErrorAction SilentlyContinue
    }
}

function Get-SanitizedFailureCategory {
    param([Parameter(Mandatory = $true)][System.Exception]$Exception)

    $current = $Exception
    while ($null -ne $current) {
        if ($current -is [System.Management.Automation.CommandNotFoundException]) {
            return 'CommandUnavailable'
        }
        if ($current -is [System.UnauthorizedAccessException] -or
            $current -is [System.Security.SecurityException]) {
            return 'AccessDenied'
        }
        if ($current -is [System.IO.IOException]) {
            return 'IoFailure'
        }
        if ($current -is [System.ArgumentException]) {
            return 'InvalidArgument'
        }
        if ($current -is [System.InvalidOperationException]) {
            return 'InvalidOperation'
        }
        $current = $current.InnerException
    }
    if ($Exception -is [System.Management.Automation.RuntimeException]) {
        return 'PowerShellRuntime'
    }
    return 'UnexpectedException'
}

function Write-SanitizedHelperFailureRecord {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Phase,
        [Parameter(Mandatory = $true)][string]$Category
    )

    $directory = Split-Path -Parent $Path
    if ([string]::IsNullOrWhiteSpace($directory)) {
        throw 'The helper failure record directory is unavailable.'
    }

    $temporary = Join-Path $directory (
        '.pcn-render-evidence-helper-failure-' +
        [System.Guid]::NewGuid().ToString('N') +
        '.tmp')
    $lines = @(
        'PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-HELPER-FAILURE 2',
        'Milestone: 0.0.10d Phase 6',
        'Kind: Audio',
        'Result: HelperFailure',
        ('Failure-Phase: ' + $Phase),
        ('Failure-Category: ' + $Category),
        'Canonical-Render-Evidence-Record-Produced: False',
        'Canonical-Terminal-Render-Record-Produced: False',
        'Sanitized-Child-Failure-Record-Produced: False',
        'Physical-Run-Completion-Established: False',
        'Raw-Exception-Published: False',
        'Raw-Native-Stderr-Published: False',
        'Audio-Acceptance-Claimed: False',
        'External-Binary-Or-Payload-Published: False',
        'END-PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-HELPER-FAILURE')
    $content = [string]::Join("`n", $lines) + "`n"
    try {
        $utf8NoBom = New-Object `
            -TypeName System.Text.UTF8Encoding `
            -ArgumentList $false
        [System.IO.File]::WriteAllText($temporary, $content, $utf8NoBom)
        [System.IO.File]::Move($temporary, $Path)
    }
    finally {
        Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    }
}

function Get-ValidatedTerminalRecordLines {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedRuntimeBuild,
        [Parameter(Mandatory = $true)][string]$ExpectedFixtureSha256,
        [Parameter(Mandatory = $true)][int]$ExpectedObservationHoldMilliseconds,
        [Parameter(Mandatory = $true)][int]$ExpectedAudioBufferMilliseconds,
        [Parameter(Mandatory = $true)][int]$MaximumCharacters
    )

    try {
        $text = [System.IO.File]::ReadAllText($Path)
        $lines = @(Get-Content -LiteralPath $Path)
        if ($text.Length -gt $MaximumCharacters -or
            $lines.Count -ne 3 -or
            $lines[0] -cne `
                'PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-TERMINAL-RENDER 1' -or
            -not $lines[1].StartsWith('Record: ', [System.StringComparison]::Ordinal) -or
            $lines[2] -cne `
                'END-PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-TERMINAL-RENDER') {
            return
        }

        $record = $lines[1].Substring('Record: '.Length) | ConvertFrom-Json
        $expectedProperties = @(
            'SchemaVersion',
            'Milestone',
            'Kind',
            'RuntimeBuildId',
            'FixtureSha256',
            'Result',
            'EvidenceResult',
            'FixtureByteCount',
            'FixturePacketCount',
            'DecodeOperationCount',
            'DeliveryAttemptCount',
            'DeliveredCount',
            'RetainedWouldBlockCount',
            'ObservationHoldMilliseconds',
            'AudioBufferMilliseconds',
            'RunnerState',
            'LifecycleState',
            'BorrowedWindowPreserved',
            'BorrowedDispatcherPreserved',
            'PreCleanupDeliveryTrace',
            'PreCleanupAudioRenderTrace',
            'PreCleanupNativeEvidence',
            'PostStopDeliveryTrace',
            'PostStopAudioRenderTrace',
            'PostStopNativeEvidence',
            'PhysicalRunStarted',
            'PhysicalRunCompleted',
            'CompletedRenderEvidenceEstablished',
            'AudioAcceptanceClaimed',
            'ExternalBinaryOrPayloadPublished')
        $actualProperties = @(
            $record.PSObject.Properties | ForEach-Object { $_.Name })
        if ($actualProperties.Count -ne $expectedProperties.Count) {
            return
        }
        for ($index = 0; $index -lt $expectedProperties.Count; $index++) {
            if ($actualProperties[$index] -cne $expectedProperties[$index]) {
                return
            }
        }

        $reviewedResults = @(
            'DecodeRejected',
            'DeliveryRejected',
            'EvidenceRejected',
            'Cancelled',
            'Faulted')
        if ([int]$record.SchemaVersion -ne 1 -or
            [string]$record.Milestone -cne '0.0.10d Phase 6' -or
            [string]$record.Kind -cne 'Audio' -or
            [string]$record.RuntimeBuildId -cne $ExpectedRuntimeBuild -or
            [string]$record.FixtureSha256 -cne $ExpectedFixtureSha256 -or
            $reviewedResults -cnotcontains [string]$record.Result -or
            [string]$record.EvidenceResult -ceq 'Accepted' -or
            [int]$record.ObservationHoldMilliseconds -ne `
                $ExpectedObservationHoldMilliseconds -or
            [int]$record.AudioBufferMilliseconds -ne $ExpectedAudioBufferMilliseconds -or
            [string]$record.RunnerState -cne 'Stopped' -or
            [string]$record.LifecycleState -cne 'Disposed' -or
            [bool]$record.PhysicalRunStarted -ne $true -or
            [bool]$record.PhysicalRunCompleted -ne $false -or
            [bool]$record.CompletedRenderEvidenceEstablished -ne $false -or
            [bool]$record.AudioAcceptanceClaimed -ne $false -or
            [bool]$record.ExternalBinaryOrPayloadPublished -ne $false -or
            $null -eq $record.PreCleanupDeliveryTrace -or
            $null -eq $record.PreCleanupAudioRenderTrace -or
            $null -eq $record.PreCleanupNativeEvidence -or
            $null -eq $record.PostStopDeliveryTrace -or
            $null -eq $record.PostStopAudioRenderTrace -or
            $null -eq $record.PostStopNativeEvidence) {
            return
        }

        $lines | ForEach-Object { Write-Output $_ }
    }
    catch {
        return
    }
}

$locationPushed = $false
$finalExitCode = 2
$failurePhase = 'Initialization'
$failureRecordAuthorized = $false
$evidenceRecordFull = $null
$terminalRecordFull = $null
$failureRecordFull = $null
try {
    $failurePhase = 'OperatingSystemGate'
    if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
        Stop-Validation 'Windows is required.'
    }

    $failurePhase = 'RepositoryDiscovery'
    $repoRootCommand = Invoke-SanitizedNativeCommand `
        -FilePath 'git' `
        -Arguments @('rev-parse', '--show-toplevel') `
        -CaptureOutput
    $repoRootText = @($repoRootCommand.OutputLines)
    if ($repoRootCommand.ExitCode -ne 0 -or $repoRootText.Count -ne 1) {
        Stop-Validation 'run this command from the PeerCast Nova repository.'
    }

    $repoRoot = [System.IO.Path]::GetFullPath([string]$repoRootText[0])
    Push-Location $repoRoot
    $locationPushed = $true

    $failurePhase = 'RepositoryIdentity'
    $branchCommand = Invoke-SanitizedNativeCommand `
        -FilePath 'git' `
        -Arguments @('branch', '--show-current') `
        -CaptureOutput
    $branch = (@($branchCommand.OutputLines) -join '').Trim()
    if ($branchCommand.ExitCode -ne 0 -or $branch -cne 'main') {
        Stop-Validation 'the checked-out branch must be main.'
    }

    $headCommand = Invoke-SanitizedNativeCommand `
        -FilePath 'git' `
        -Arguments @('rev-parse', 'HEAD') `
        -CaptureOutput
    $head = (@($headCommand.OutputLines) -join '').Trim()
    if ($headCommand.ExitCode -ne 0 -or $head -ine $ExpectedCommit) {
        Stop-Validation 'HEAD does not match the explicitly approved commit.'
    }

    $statusCommand = Invoke-SanitizedNativeCommand `
        -FilePath 'git' `
        -Arguments @('status', '--porcelain', '--untracked-files=all') `
        -CaptureOutput
    $status = @($statusCommand.OutputLines)
    if ($statusCommand.ExitCode -ne 0 -or $status.Count -ne 0) {
        Stop-Validation 'the repository working tree must be clean.'
    }

    $failurePhase = 'ExternalInputValidation'
    $runtimeFull = [System.IO.Path]::GetFullPath($RuntimeDirectory)
    $fixtureFull = [System.IO.Path]::GetFullPath($FixturePath)
    $videoRecordFull = [System.IO.Path]::GetFullPath($VideoRecordPath)
    $evidenceRecordFull = [System.IO.Path]::GetFullPath($RenderEvidenceRecordPath)
    $terminalRecordFull = [System.IO.Path]::GetFullPath($TerminalRenderRecordPath)
    $failureRecordFull = [System.IO.Path]::GetFullPath($FailureRecordPath)

    if (-not [System.IO.Directory]::Exists($runtimeFull)) {
        Stop-Validation 'the external runtime directory is unavailable.'
    }
    if (-not [System.IO.File]::Exists($fixtureFull)) {
        Stop-Validation 'the retained controlled fixture is unavailable.'
    }
    if (-not [System.IO.File]::Exists($videoRecordFull)) {
        Stop-Validation 'the accepted video record is unavailable.'
    }
    if ([System.IO.File]::Exists($evidenceRecordFull)) {
        Stop-Validation 'the requested render evidence record already exists; use a new path.'
    }
    if ([System.IO.File]::Exists($terminalRecordFull)) {
        Stop-Validation 'the requested terminal render record already exists; use a new path.'
    }
    if ([System.IO.File]::Exists($failureRecordFull)) {
        Stop-Validation 'the requested failure record already exists; use a new path.'
    }

    $recordPaths = @(
        $videoRecordFull,
        $evidenceRecordFull,
        $terminalRecordFull,
        $failureRecordFull)
    for ($left = 0; $left -lt $recordPaths.Count; $left++) {
        for ($right = $left + 1; $right -lt $recordPaths.Count; $right++) {
            if ($recordPaths[$left].Equals(
                    $recordPaths[$right],
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                Stop-Validation (
                    'video, completed evidence, terminal, and failure record paths ' +
                    'must be pairwise distinct.')
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
            Stop-Validation 'runtime, fixture, and records must remain repository-external.'
        }
    }

    $fixtureInfo = Get-Item -LiteralPath $fixtureFull
    if ($fixtureInfo.Length -ne $expectedFixtureBytes) {
        Stop-Validation 'the controlled fixture byte count does not match.'
    }
    $fixtureDigest = (Get-FileHash -LiteralPath $fixtureFull -Algorithm SHA256).Hash
    if ($fixtureDigest -ine $expectedFixtureSha256) {
        Stop-Validation 'the controlled fixture SHA-256 does not match.'
    }

    $evidenceDirectory = Split-Path -Parent $evidenceRecordFull
    $terminalDirectory = Split-Path -Parent $terminalRecordFull
    $failureDirectory = Split-Path -Parent $failureRecordFull
    if ([string]::IsNullOrWhiteSpace($evidenceDirectory) -or
        [string]::IsNullOrWhiteSpace($terminalDirectory) -or
        [string]::IsNullOrWhiteSpace($failureDirectory)) {
        Stop-Validation (
            'completed evidence, terminal, and failure paths must include ' +
            'external directories.')
    }

    $failurePhase = 'ExternalDirectoryPreparation'
    $null = New-Item -ItemType Directory -Path $evidenceDirectory -Force
    $null = New-Item -ItemType Directory -Path $terminalDirectory -Force
    $null = New-Item -ItemType Directory -Path $failureDirectory -Force
    $failureRecordAuthorized = $true

    $failurePhase = 'SolutionRestore'
    $restoreCommand = Invoke-SanitizedNativeCommand `
        -FilePath 'dotnet' `
        -Arguments @('restore', (Join-Path $repoRoot 'PeerCast-Nova.sln'))
    if ($restoreCommand.ExitCode -ne 0) {
        Stop-Validation 'solution restore failed.'
    }

    $failurePhase = 'RunnerBuild'
    $runnerProject = Join-Path $repoRoot `
        'src\Nova.ControlledWindowsRunner\Nova.ControlledWindowsRunner.csproj'
    $buildCommand = Invoke-SanitizedNativeCommand `
        -FilePath 'dotnet' `
        -Arguments @('build', $runnerProject, '--configuration', 'Release', '--no-restore')
    if ($buildCommand.ExitCode -ne 0) {
        Stop-Validation 'controlled Windows runner build failed.'
    }

    $failurePhase = 'RunnerAssemblyValidation'
    $runnerAssembly = Join-Path $repoRoot `
        'src\Nova.ControlledWindowsRunner\bin\Release\net10.0\Nova.ControlledWindowsRunner.dll'
    if (-not [System.IO.File]::Exists($runnerAssembly)) {
        Stop-Validation 'the controlled Windows runner assembly is unavailable.'
    }

    $failurePhase = 'VideoRecordGate'
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
        Stop-Validation 'the video record gate was not accepted.'
    }

    $failurePhase = 'RenderEvidenceCapture'
    $captureCommand = Invoke-SanitizedNativeCommand `
        -FilePath 'dotnet' `
        -Arguments @(
            $runnerAssembly,
            '--capture-controlled-windows-audio-render-evidence',
            '--runtime-directory',
            $runtimeFull,
            '--controlled-fixture',
            $fixtureFull,
            '--fixture-sha256',
            $expectedFixtureSha256,
            '--render-evidence-record-path',
            $evidenceRecordFull,
            '--terminal-render-record-path',
            $terminalRecordFull,
            '--failure-record-path',
            $failureRecordFull,
            '--max-decode-operations',
            $MaximumDecodeOperations.ToString([System.Globalization.CultureInfo]::InvariantCulture),
            '--max-delivery-operations',
            $MaximumDeliveryOperations.ToString([System.Globalization.CultureInfo]::InvariantCulture),
            '--fixture-packet-bytes',
            $FixturePacketBytes.ToString([System.Globalization.CultureInfo]::InvariantCulture),
            '--observation-hold-ms',
            $ObservationHoldMilliseconds.ToString([System.Globalization.CultureInfo]::InvariantCulture))
    $captureExitCode = $captureCommand.ExitCode

    $failurePhase = 'CaptureIntegration'
    $evidenceExists = [System.IO.File]::Exists($evidenceRecordFull)
    $terminalExists = [System.IO.File]::Exists($terminalRecordFull)
    $failureExists = [System.IO.File]::Exists($failureRecordFull)
    if ($failureExists -and
        -not $evidenceExists -and
        -not $terminalExists -and
        $captureExitCode -eq 2) {
        Get-Content -LiteralPath $failureRecordFull | ForEach-Object { Write-Output $_ }
        Stop-Validation (
            'the child did not produce valid bounded render evidence; ' +
            'a sanitized external failure record was retained.')
    }

    $captureOutcome = if ($evidenceExists -and
        -not $terminalExists -and
        -not $failureExists -and
        $captureExitCode -eq 0) {
        'Completed'
    }
    elseif ($terminalExists -and
        -not $evidenceExists -and
        -not $failureExists -and
        $captureExitCode -eq 1) {
        'Terminal'
    }
    else {
        $null
    }
    if ($null -eq $captureOutcome) {
        Stop-Validation 'the managed render evidence capture failed integration checks.'
    }

    $retainedRecordLines = @()
    if ($captureOutcome -ceq 'Completed') {
        $failurePhase = 'EvidenceEnvelopeValidation'
        $recordText = [System.IO.File]::ReadAllText($evidenceRecordFull)
        $recordLines = @(Get-Content -LiteralPath $evidenceRecordFull)
        $evidenceValid =
            $recordText.Length -le $maximumRecordCharacters -and
            $recordLines.Count -eq 26 -and
            $recordLines[0] -ceq `
                'PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE 1' -and
            $recordLines[1] -ceq 'Milestone: 0.0.10d Phase 5' -and
            $recordLines[2] -ceq 'Kind: Audio' -and
            $recordLines[3] -ceq ('Runtime-Build: ' + $expectedRuntimeBuild) -and
            $recordLines[4] -ceq ('Fixture-SHA256: ' + $expectedFixtureSha256) -and
            $recordLines[5] -ceq 'Result: Completed' -and
            $recordLines[6] -ceq 'Evidence: Accepted' -and
            $recordLines[10] -ceq 'Delivery-Attempts: 4' -and
            $recordLines[11] -ceq 'Delivered: 4' -and
            $recordLines[12] -ceq 'Retained-WouldBlock: 0' -and
            $recordLines[13] -ceq (
                'Observation-Hold-Milliseconds: ' + $ObservationHoldMilliseconds) -and
            $recordLines[14] -ceq (
                'Audio-Buffer-Milliseconds: ' + $expectedAudioBufferMilliseconds) -and
            $recordLines[15] -ceq 'Runner-State: Stopped' -and
            $recordLines[16] -ceq 'Lifecycle-State: Disposed' -and
            $recordLines[17] -ceq 'Borrowed-Window-Preserved: True' -and
            $recordLines[18] -ceq 'Borrowed-Dispatcher-Preserved: True' -and
            $recordLines[19] -ceq 'Physical-Runs-Executed-By-This-Command: True' -and
            $recordLines[20] -ceq 'Audio-Acceptance-Claimed: False' -and
            $recordLines[21] -ceq 'External-Binary-Or-Payload-Published: False' -and
            $recordLines[22].StartsWith(
                'Hold-Start-Checkpoint: ',
                [System.StringComparison]::Ordinal) -and
            $recordLines[23].StartsWith(
                'Pre-Cleanup-Checkpoint: ',
                [System.StringComparison]::Ordinal) -and
            $recordLines[24].StartsWith(
                'Post-Stop-Checkpoint: ',
                [System.StringComparison]::Ordinal) -and
            $recordLines[25] -ceq `
                'END-PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE'
        if (-not $evidenceValid) {
            Stop-Validation 'the retained render evidence record failed the helper envelope check.'
        }
        $retainedRecordLines = $recordLines
    }
    else {
        $failurePhase = 'TerminalEnvelopeValidation'
        $terminalLines = @(
            Get-ValidatedTerminalRecordLines `
                -Path $terminalRecordFull `
                -ExpectedRuntimeBuild $expectedRuntimeBuild `
                -ExpectedFixtureSha256 $expectedFixtureSha256 `
                -ExpectedObservationHoldMilliseconds $ObservationHoldMilliseconds `
                -ExpectedAudioBufferMilliseconds $expectedAudioBufferMilliseconds `
                -MaximumCharacters $maximumRecordCharacters)
        if ($terminalLines.Count -ne 3) {
            Stop-Validation 'the retained terminal render record failed the helper envelope check.'
        }
        $retainedRecordLines = $terminalLines
    }

    $failurePhase = 'PostRunRepositoryCheck'
    $postStatusCommand = Invoke-SanitizedNativeCommand `
        -FilePath 'git' `
        -Arguments @('status', '--porcelain', '--untracked-files=all') `
        -CaptureOutput
    $postStatus = @($postStatusCommand.OutputLines)
    if ($postStatusCommand.ExitCode -ne 0 -or $postStatus.Count -ne 0) {
        Stop-Validation 'the repository changed during physical diagnostics.'
    }

    $failurePhase = 'Completed'
    $retainedRecordLines | ForEach-Object { Write-Output $_ }
    Write-Host "Validated commit: $head"
    Write-Host 'Video record gate: Accepted'
    Write-Host "Audio observation hold: $ObservationHoldMilliseconds ms"
    if ($captureOutcome -ceq 'Completed') {
        Write-Host 'Render evidence record: Produced'
        Write-Host 'Completed render evidence: Established'
        $finalExitCode = 0
    }
    else {
        Write-Host 'Terminal render record: Produced'
        Write-Host 'Completed render evidence: Not established'
        $finalExitCode = 1
    }
    Write-Host 'Audio acceptance claimed: No'
    Write-Host 'Operator observation required: Audio audible: Yes / No'
    Write-Host 'Do not rerun this command.'
}
catch {
    $validationFailure = Get-ValidationFailure -Exception $_.Exception
    $failureCategory = if ($null -ne $validationFailure) {
        'ValidationFailure'
    }
    else {
        Get-SanitizedFailureCategory -Exception $_.Exception
    }

    $helperFailureRecordRetained = $false
    if ($failureRecordAuthorized -and
        -not [System.IO.File]::Exists($evidenceRecordFull) -and
        -not [System.IO.File]::Exists($terminalRecordFull) -and
        -not [System.IO.File]::Exists($failureRecordFull)) {
        try {
            Write-SanitizedHelperFailureRecord `
                -Path $failureRecordFull `
                -Phase $failurePhase `
                -Category $failureCategory
            $helperFailureRecordRetained = $true
        }
        catch {
            $helperFailureRecordRetained = $false
        }
    }

    if ($null -ne $validationFailure) {
        [System.Console]::Error.WriteLine($validationFailure.Message)
    }
    else {
        [System.Console]::Error.WriteLine(
            $validationPrefix + 'unexpected helper failure.')
    }
    $retainedText = if ($helperFailureRecordRetained) { 'Yes' } else { 'No' }
    [System.Console]::Error.WriteLine("Helper failure phase: $failurePhase")
    [System.Console]::Error.WriteLine("Helper failure category: $failureCategory")
    [System.Console]::Error.WriteLine(
        "Sanitized helper failure record retained: $retainedText")
    [System.Console]::Error.WriteLine('Do not rerun this command.')
    $finalExitCode = 2
}
finally {
    if ($locationPushed) {
        Pop-Location
    }
}

exit $finalExitCode
