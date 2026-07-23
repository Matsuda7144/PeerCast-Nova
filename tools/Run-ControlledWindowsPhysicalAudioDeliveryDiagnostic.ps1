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
    [string]$DiagnosticRecordPath,

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
$validationPrefix = 'Controlled delivery diagnostic rejected: '

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
    $diagnosticRecordFull = [System.IO.Path]::GetFullPath($DiagnosticRecordPath)
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
    if ([System.IO.File]::Exists($diagnosticRecordFull)) {
        Stop-Validation 'the requested diagnostic record already exists; use a new path.'
    }
    if ([System.IO.File]::Exists($failureRecordFull)) {
        Stop-Validation 'the requested failure record already exists; use a new path.'
    }
    if ($diagnosticRecordFull.Equals(
            $failureRecordFull,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        $diagnosticRecordFull.Equals(
            $videoRecordFull,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        $failureRecordFull.Equals(
            $videoRecordFull,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        Stop-Validation 'video, diagnostic, and failure record paths must be distinct.'
    }

    foreach ($externalPath in @(
            $runtimeFull,
            $fixtureFull,
            $videoRecordFull,
            $diagnosticRecordFull,
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

    $diagnosticDirectory = Split-Path -Parent $diagnosticRecordFull
    $failureDirectory = Split-Path -Parent $failureRecordFull
    if ([string]::IsNullOrWhiteSpace($diagnosticDirectory) -or
        [string]::IsNullOrWhiteSpace($failureDirectory)) {
        Stop-Validation 'diagnostic and failure paths must include external directories.'
    }
    $null = New-Item -ItemType Directory -Path $diagnosticDirectory -Force
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

    $temporaryVideoPreflight = Join-Path $diagnosticDirectory (
        '.pcn-delivery-video-gate-' + [System.Guid]::NewGuid().ToString('N') + '.tmp')
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
        --capture-controlled-windows-delivery-diagnostic `
        --runtime-directory $runtimeFull `
        --controlled-fixture $fixtureFull `
        --fixture-sha256 $expectedFixtureSha256 `
        --diagnostic-record-path $diagnosticRecordFull `
        --failure-record-path $failureRecordFull `
        --max-decode-operations $MaximumDecodeOperations `
        --max-delivery-operations $MaximumDeliveryOperations `
        --fixture-packet-bytes $FixturePacketBytes `
        --observation-hold-ms $ObservationHoldMilliseconds
    $captureExitCode = $LASTEXITCODE

    $diagnosticExists = [System.IO.File]::Exists($diagnosticRecordFull)
    $failureExists = [System.IO.File]::Exists($failureRecordFull)
    if ($failureExists -and -not $diagnosticExists -and $captureExitCode -eq 2) {
        Get-Content -LiteralPath $failureRecordFull | ForEach-Object { Write-Output $_ }
        Stop-Validation (
            'the child did not produce a valid bounded delivery diagnostic; ' +
            'a sanitized external failure record was retained.')
    }
    if (-not $diagnosticExists -or
        $failureExists -or
        $captureExitCode -notin @(0, 1)) {
        Stop-Validation 'the managed delivery diagnostic capture failed integration checks.'
    }

    $recordLines = @(Get-Content -LiteralPath $diagnosticRecordFull)
    $diagnosticValid =
        $recordLines.Count -eq 27 -and
        $recordLines[0] -ceq `
            'PEERCAST-NOVA-CONTROLLED-WINDOWS-DELIVERY-DIAGNOSTIC 1' -and
        $recordLines[1] -ceq 'Milestone: 0.0.10d Phase 5' -and
        $recordLines[2] -ceq 'Kind: Audio' -and
        $recordLines[3] -ceq ('Runtime-Build: ' + $expectedRuntimeBuild) -and
        $recordLines[4] -ceq ('Fixture-SHA256: ' + $expectedFixtureSha256) -and
        $recordLines[18] -ceq 'Delivery-Result-Overflowed: False' -and
        $recordLines[23] -ceq 'External-Binary-Or-Payload-Published: False' -and
        $recordLines[24] -ceq 'Physical-Runs-Executed-By-This-Command: True' -and
        $recordLines[25] -ceq 'Audio-Acceptance-Claimed: False' -and
        $recordLines[26] -ceq `
            'END-PEERCAST-NOVA-CONTROLLED-WINDOWS-DELIVERY-DIAGNOSTIC'
    if (-not $diagnosticValid) {
        Stop-Validation 'the retained diagnostic record failed the helper envelope check.'
    }

    $postStatus = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $postStatus.Count -ne 0) {
        Stop-Validation 'the repository changed during physical diagnostics.'
    }

    $recordLines | ForEach-Object { Write-Output $_ }
    Write-Host "Validated commit: $head"
    Write-Host 'Video record gate: Accepted'
    Write-Host "Audio observation hold: $ObservationHoldMilliseconds ms"
    Write-Host 'Diagnostic record: Produced'
    Write-Host 'Audio acceptance claimed: No'
    Write-Host 'Operator observation required: Audio audible: Yes / No'
    Write-Host 'Do not rerun this command.'
    $finalExitCode = $captureExitCode
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
