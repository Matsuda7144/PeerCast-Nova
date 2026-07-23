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
    [string]$AudioRecordPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$FailureRecordPath,

    [ValidateRange(1, 4096)]
    [int]$MaximumDecodeOperations = 128,

    [ValidateRange(1, 4096)]
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
$validationPrefix = 'Controlled audio validation rejected: '

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
    $audioRecordFull = [System.IO.Path]::GetFullPath($AudioRecordPath)
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
    if ([System.IO.File]::Exists($audioRecordFull)) {
        Stop-Validation 'the requested audio record already exists; use a new path.'
    }
    if ([System.IO.File]::Exists($failureRecordFull)) {
        Stop-Validation 'the requested failure record already exists; use a new path.'
    }
    if ($videoRecordFull.Equals(
            $audioRecordFull,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        Stop-Validation 'video and audio record paths must be different.'
    }
    if ($failureRecordFull.Equals(
            $audioRecordFull,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        $failureRecordFull.Equals(
            $videoRecordFull,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        Stop-Validation 'failure, video, and audio record paths must be different.'
    }
    if (Test-IsUnderPath -Candidate $runtimeFull -Parent $repoRoot) {
        Stop-Validation 'the FFmpeg runtime must remain repository-external.'
    }
    if (Test-IsUnderPath -Candidate $fixtureFull -Parent $repoRoot) {
        Stop-Validation 'the controlled fixture must remain repository-external.'
    }
    if (Test-IsUnderPath -Candidate $videoRecordFull -Parent $repoRoot) {
        Stop-Validation 'the video record must remain repository-external.'
    }
    if (Test-IsUnderPath -Candidate $audioRecordFull -Parent $repoRoot) {
        Stop-Validation 'the audio record must remain repository-external.'
    }
    if (Test-IsUnderPath -Candidate $failureRecordFull -Parent $repoRoot) {
        Stop-Validation 'the failure record must remain repository-external.'
    }

    $fixtureInfo = Get-Item -LiteralPath $fixtureFull
    if ($fixtureInfo.Length -ne $expectedFixtureBytes) {
        Stop-Validation 'the controlled fixture byte count does not match.'
    }

    $fixtureDigest = (Get-FileHash -LiteralPath $fixtureFull -Algorithm SHA256).Hash
    if ($fixtureDigest -ine $expectedFixtureSha256) {
        Stop-Validation 'the controlled fixture SHA-256 does not match.'
    }

    $audioRecordDirectory = Split-Path -Parent $audioRecordFull
    if ([string]::IsNullOrWhiteSpace($audioRecordDirectory)) {
        Stop-Validation 'the audio record path must include an external directory.'
    }
    $null = New-Item -ItemType Directory -Path $audioRecordDirectory -Force
    $failureRecordDirectory = Split-Path -Parent $failureRecordFull
    if ([string]::IsNullOrWhiteSpace($failureRecordDirectory)) {
        Stop-Validation 'the failure record path must include an external directory.'
    }
    $null = New-Item -ItemType Directory -Path $failureRecordDirectory -Force

    & dotnet restore (Join-Path $repoRoot 'PeerCast-Nova.sln')
    if ($LASTEXITCODE -ne 0) {
        Stop-Validation 'solution restore failed.'
    }

    $runnerProject = Join-Path $repoRoot 'src\Nova.ControlledWindowsRunner\Nova.ControlledWindowsRunner.csproj'
    & dotnet build $runnerProject --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        Stop-Validation 'controlled Windows runner build failed.'
    }

    $runnerAssembly = Join-Path $repoRoot 'src\Nova.ControlledWindowsRunner\bin\Release\net10.0\Nova.ControlledWindowsRunner.dll'
    if (-not [System.IO.File]::Exists($runnerAssembly)) {
        Stop-Validation 'the controlled Windows runner assembly is unavailable.'
    }
    $temporaryVideoPreflight = Join-Path $audioRecordDirectory (
        '.pcn-video-gate-' + [System.Guid]::NewGuid().ToString('N') + '.tmp')
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
        $videoPreflightLines -cnotcontains 'Physical-Runs-Executed-By-This-Command: False') {
        Stop-Validation 'the video record gate was not accepted.'
    }

    & dotnet $runnerAssembly `
        --capture-controlled-windows-audio-child `
        --runtime-directory $runtimeFull `
        --controlled-fixture $fixtureFull `
        --fixture-sha256 $expectedFixtureSha256 `
        --audio-record-path $audioRecordFull `
        --failure-record-path $failureRecordFull `
        --max-decode-operations $MaximumDecodeOperations `
        --max-delivery-operations $MaximumDeliveryOperations `
        --fixture-packet-bytes $FixturePacketBytes `
        --observation-hold-ms $ObservationHoldMilliseconds
    $captureExitCode = $LASTEXITCODE
    $audioRecordExists = [System.IO.File]::Exists($audioRecordFull)
    $failureRecordExists = [System.IO.File]::Exists($failureRecordFull)

    if ($failureRecordExists -and -not $audioRecordExists -and $captureExitCode -eq 2) {
        Get-Content -LiteralPath $failureRecordFull | ForEach-Object { Write-Output $_ }
        Stop-Validation (
            'the child process terminated before producing a canonical audio record; ' +
            'a sanitized external failure record was retained.')
    }
    if (-not $audioRecordExists -or
        $failureRecordExists -or
        $captureExitCode -notin @(0, 1)) {
        Stop-Validation 'the managed audio child capture command failed integration checks.'
    }

    $runExitCode = $captureExitCode
    $recordLines = @(Get-Content -LiteralPath $audioRecordFull)

    $audioPreflightAccepted = $false
    if ($runExitCode -eq 0) {
        $temporaryAudioPreflight = Join-Path $audioRecordDirectory (
            '.pcn-audio-preflight-' + [System.Guid]::NewGuid().ToString('N') + '.tmp')
        try {
            & dotnet $runnerAssembly `
                --verify-controlled-windows-run-record `
                --record $audioRecordFull `
                --expected-kind audio `
                --fixture-sha256 $expectedFixtureSha256 > $temporaryAudioPreflight
            $audioPreflightExitCode = $LASTEXITCODE
            $audioPreflightLines = @(Get-Content -LiteralPath $temporaryAudioPreflight)
        }
        finally {
            Remove-Item -LiteralPath $temporaryAudioPreflight -Force -ErrorAction SilentlyContinue
        }

        if ($audioPreflightExitCode -ne 0 -or
            $audioPreflightLines -cnotcontains 'Result: AudioAccepted' -or
            $audioPreflightLines -cnotcontains 'Expected-Kind: Audio' -or
            $audioPreflightLines -cnotcontains 'Record-Accepted: True' -or
            $audioPreflightLines -cnotcontains 'Physical-Runs-Executed-By-This-Command: False') {
            Stop-Validation 'the canonical audio record preflight was not accepted.'
        }
        $audioPreflightAccepted = $true
    }

    $postStatus = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $postStatus.Count -ne 0) {
        Stop-Validation 'the repository changed during physical validation.'
    }

    $recordLines | ForEach-Object { Write-Output $_ }
    Write-Host "Validated commit: $head"
    Write-Host 'Video record gate: Accepted'
    Write-Host "Audio observation hold: $ObservationHoldMilliseconds ms"
    if ($audioPreflightAccepted) {
        Write-Host 'Canonical audio preflight: Accepted'
    }
    Write-Host 'Operator observation required: Audio audible: Yes / No'
    $finalExitCode = $runExitCode
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
