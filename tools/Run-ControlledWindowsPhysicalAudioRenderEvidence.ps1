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

function Stop-Validation {
    param([Parameter(Mandatory = $true)][string]$Message)

    throw ($validationPrefix + $Message)
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

$locationPushed = $false
$finalExitCode = 2
try {
    if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
        Stop-Validation 'Windows is required.'
    }

    $repoRootText = @(& git rev-parse --show-toplevel)
    if ($LASTEXITCODE -ne 0 -or $repoRootText.Count -ne 1) {
        Stop-Validation 'run this command from the PeerCast Nova repository.'
    }

    $repoRoot = [System.IO.Path]::GetFullPath([string]$repoRootText[0])
    Push-Location $repoRoot
    $locationPushed = $true

    $branch = (@(& git branch --show-current) -join '').Trim()
    if ($LASTEXITCODE -ne 0 -or $branch -cne 'main') {
        Stop-Validation 'the checked-out branch must be main.'
    }

    $head = (@(& git rev-parse HEAD) -join '').Trim()
    if ($LASTEXITCODE -ne 0 -or $head -ine $ExpectedCommit) {
        Stop-Validation 'HEAD does not match the explicitly approved commit.'
    }

    $status = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) {
        Stop-Validation 'the repository working tree must be clean.'
    }

    $runtimeFull = [System.IO.Path]::GetFullPath($RuntimeDirectory)
    $fixtureFull = [System.IO.Path]::GetFullPath($FixturePath)
    $videoRecordFull = [System.IO.Path]::GetFullPath($VideoRecordPath)
    $evidenceRecordFull = [System.IO.Path]::GetFullPath($RenderEvidenceRecordPath)
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
    if ([System.IO.File]::Exists($failureRecordFull)) {
        Stop-Validation 'the requested failure record already exists; use a new path.'
    }
    if ($evidenceRecordFull.Equals(
            $failureRecordFull,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        $evidenceRecordFull.Equals(
            $videoRecordFull,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        $failureRecordFull.Equals(
            $videoRecordFull,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        Stop-Validation 'video, render evidence, and failure record paths must be distinct.'
    }

    foreach ($externalPath in @(
            $runtimeFull,
            $fixtureFull,
            $videoRecordFull,
            $evidenceRecordFull,
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
    $failureDirectory = Split-Path -Parent $failureRecordFull
    if ([string]::IsNullOrWhiteSpace($evidenceDirectory) -or
        [string]::IsNullOrWhiteSpace($failureDirectory)) {
        Stop-Validation 'render evidence and failure paths must include external directories.'
    }
    $null = New-Item -ItemType Directory -Path $evidenceDirectory -Force
    $null = New-Item -ItemType Directory -Path $failureDirectory -Force

    & dotnet restore (Join-Path $repoRoot 'PeerCast-Nova.sln')
    if ($LASTEXITCODE -ne 0) {
        Stop-Validation 'solution restore failed.'
    }

    $runnerProject = Join-Path $repoRoot `
        'src\Nova.ControlledWindowsRunner\Nova.ControlledWindowsRunner.csproj'
    & dotnet build $runnerProject --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        Stop-Validation 'controlled Windows runner build failed.'
    }

    $runnerAssembly = Join-Path $repoRoot `
        'src\Nova.ControlledWindowsRunner\bin\Release\net10.0\Nova.ControlledWindowsRunner.dll'
    if (-not [System.IO.File]::Exists($runnerAssembly)) {
        Stop-Validation 'the controlled Windows runner assembly is unavailable.'
    }

    $temporaryVideoPreflight = Join-Path $evidenceDirectory (
        '.pcn-render-evidence-video-gate-' + [System.Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        & dotnet $runnerAssembly `
            --verify-controlled-windows-run-record `
            --record $videoRecordFull `
            --expected-kind video `
            --fixture-sha256 $expectedFixtureSha256 > $temporaryVideoPreflight
        $videoPreflightExitCode = $LASTEXITCODE
        $videoPreflightLines = @(Get-Content -LiteralPath $temporaryVideoPreflight)
    }
    finally {
        Remove-Item -LiteralPath $temporaryVideoPreflight -Force -ErrorAction SilentlyContinue
    }

    if ($videoPreflightExitCode -ne 0 -or
        $videoPreflightLines -cnotcontains 'Result: VideoAccepted' -or
        $videoPreflightLines -cnotcontains 'Expected-Kind: Video' -or
        $videoPreflightLines -cnotcontains 'Record-Accepted: True' -or
        $videoPreflightLines -cnotcontains `
            'Physical-Runs-Executed-By-This-Command: False') {
        Stop-Validation 'the video record gate was not accepted.'
    }

    & dotnet $runnerAssembly `
        --capture-controlled-windows-audio-render-evidence `
        --runtime-directory $runtimeFull `
        --controlled-fixture $fixtureFull `
        --fixture-sha256 $expectedFixtureSha256 `
        --render-evidence-record-path $evidenceRecordFull `
        --failure-record-path $failureRecordFull `
        --max-decode-operations $MaximumDecodeOperations `
        --max-delivery-operations $MaximumDeliveryOperations `
        --fixture-packet-bytes $FixturePacketBytes `
        --observation-hold-ms $ObservationHoldMilliseconds
    $captureExitCode = $LASTEXITCODE

    $evidenceExists = [System.IO.File]::Exists($evidenceRecordFull)
    $failureExists = [System.IO.File]::Exists($failureRecordFull)
    if ($failureExists -and -not $evidenceExists -and $captureExitCode -eq 2) {
        Get-Content -LiteralPath $failureRecordFull | ForEach-Object { Write-Output $_ }
        Stop-Validation (
            'the child did not produce valid bounded render evidence; ' +
            'a sanitized external failure record was retained.')
    }
    if (-not $evidenceExists -or $failureExists -or $captureExitCode -ne 0) {
        Stop-Validation 'the managed render evidence capture failed integration checks.'
    }

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

    $postStatus = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $postStatus.Count -ne 0) {
        Stop-Validation 'the repository changed during physical diagnostics.'
    }

    $recordLines | ForEach-Object { Write-Output $_ }
    Write-Host "Validated commit: $head"
    Write-Host 'Video record gate: Accepted'
    Write-Host "Audio observation hold: $ObservationHoldMilliseconds ms"
    Write-Host 'Render evidence record: Produced'
    Write-Host 'Audio acceptance claimed: No'
    Write-Host 'Operator observation required: Audio audible: Yes / No'
    Write-Host 'Do not rerun this command.'
    $finalExitCode = 0
}
catch {
    $failureMessage = $_.Exception.Message
    if (-not $failureMessage.StartsWith(
            $validationPrefix,
            [System.StringComparison]::Ordinal)) {
        $failureMessage = $validationPrefix + 'unexpected helper failure.'
    }
    [System.Console]::Error.WriteLine($failureMessage)
    $finalExitCode = 2
}
finally {
    if ($locationPushed) {
        Pop-Location
    }
}

exit $finalExitCode
