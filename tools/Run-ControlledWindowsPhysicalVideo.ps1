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
    [string]$RecordPath,

    [ValidateRange(1, 4096)]
    [int]$MaximumDecodeOperations = 128,

    [ValidateRange(1, 4096)]
    [int]$MaximumDeliveryOperations = 128,

    [ValidateRange(1, 1048576)]
    [int]$FixturePacketBytes = 65536,

    [ValidateRange(0, 30000)]
    [int]$ObservationHoldMilliseconds = 3000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedRuntimeBuild = 'btbn.ffmpeg.20260721.win64.lgpl-shared'
$expectedFixtureSha256 = '32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15'
$expectedFixtureBytes = 66939L
$validationPrefix = 'Controlled video revalidation rejected: '

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
    $recordFull = [System.IO.Path]::GetFullPath($RecordPath)

    if (-not [System.IO.Directory]::Exists($runtimeFull)) {
        Stop-Validation 'the external runtime directory is unavailable.'
    }
    if (-not [System.IO.File]::Exists($fixtureFull)) {
        Stop-Validation 'the retained controlled fixture is unavailable.'
    }
    if (Test-IsUnderPath -Candidate $runtimeFull -Parent $repoRoot) {
        Stop-Validation 'the FFmpeg runtime must remain repository-external.'
    }
    if (Test-IsUnderPath -Candidate $fixtureFull -Parent $repoRoot) {
        Stop-Validation 'the controlled fixture must remain repository-external.'
    }
    if (Test-IsUnderPath -Candidate $recordFull -Parent $repoRoot) {
        Stop-Validation 'the physical run record must remain repository-external.'
    }
    if ([System.IO.File]::Exists($recordFull)) {
        Stop-Validation 'the requested record already exists; use a new path.'
    }

    $fixtureInfo = Get-Item -LiteralPath $fixtureFull
    if ($fixtureInfo.Length -ne $expectedFixtureBytes) {
        Stop-Validation 'the controlled fixture byte count does not match.'
    }

    $fixtureDigest = (Get-FileHash -LiteralPath $fixtureFull -Algorithm SHA256).Hash
    if ($fixtureDigest -ine $expectedFixtureSha256) {
        Stop-Validation 'the controlled fixture SHA-256 does not match.'
    }

    $recordDirectory = Split-Path -Parent $recordFull
    if ([string]::IsNullOrWhiteSpace($recordDirectory)) {
        Stop-Validation 'the record path must include an external directory.'
    }
    $null = New-Item -ItemType Directory -Path $recordDirectory -Force

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

    $temporaryRecord = Join-Path $recordDirectory (
        '.pcn-video-record-' + [System.Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        & dotnet $runnerAssembly `
            --controlled-windows-physical-run `
            --runtime-directory $runtimeFull `
            --controlled-fixture $fixtureFull `
            --fixture-sha256 $expectedFixtureSha256 `
            --output-kind video `
            --max-decode-operations $MaximumDecodeOperations `
            --max-delivery-operations $MaximumDeliveryOperations `
            --fixture-packet-bytes $FixturePacketBytes `
            --observation-hold-ms $ObservationHoldMilliseconds > $temporaryRecord
        $runExitCode = $LASTEXITCODE

        $recordLines = @(Get-Content -LiteralPath $temporaryRecord)
        [System.IO.File]::WriteAllLines(
            $recordFull,
            [string[]]$recordLines,
            [System.Text.UTF8Encoding]::new($false))
    }
    finally {
        Remove-Item -LiteralPath $temporaryRecord -Force -ErrorAction SilentlyContinue
    }

    if ($recordLines.Count -lt 3 -or
        $recordLines[0] -cne 'PEERCAST-NOVA-CONTROLLED-WINDOWS-RUN-RECORD 1' -or
        $recordLines[$recordLines.Count - 1] -cne 'END-PEERCAST-NOVA-CONTROLLED-WINDOWS-RUN-RECORD' -or
        $recordLines -cnotcontains 'Milestone: 0.0.10d Phase 5' -or
        $recordLines -cnotcontains 'Kind: Video' -or
        $recordLines -cnotcontains "Runtime-Build: $expectedRuntimeBuild" -or
        $recordLines -cnotcontains "Fixture-SHA256: $expectedFixtureSha256" -or
        $recordLines -cnotcontains 'External-Binary-Or-Payload-Published: False') {
        Stop-Validation 'the command did not produce one canonical sanitized video record.'
    }

    $preflightAccepted = $false
    if ($runExitCode -eq 0) {
        $temporaryPreflight = Join-Path $recordDirectory (
            '.pcn-video-preflight-' + [System.Guid]::NewGuid().ToString('N') + '.tmp')
        try {
            & dotnet $runnerAssembly `
                --verify-controlled-windows-run-record `
                --record $recordFull `
                --expected-kind video `
                --fixture-sha256 $expectedFixtureSha256 > $temporaryPreflight
            $preflightExitCode = $LASTEXITCODE
            $preflightLines = @(Get-Content -LiteralPath $temporaryPreflight)
        }
        finally {
            Remove-Item -LiteralPath $temporaryPreflight -Force -ErrorAction SilentlyContinue
        }

        if ($preflightExitCode -ne 0 -or
            $preflightLines -cnotcontains 'Result: VideoAccepted' -or
            $preflightLines -cnotcontains 'Expected-Kind: Video' -or
            $preflightLines -cnotcontains 'Record-Accepted: True' -or
            $preflightLines -cnotcontains 'Physical-Runs-Executed-By-This-Command: False') {
            Stop-Validation 'the canonical video record preflight was not accepted.'
        }
        $preflightAccepted = $true
    }

    $postStatus = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $postStatus.Count -ne 0) {
        Stop-Validation 'the repository changed during physical validation.'
    }

    $recordLines | ForEach-Object { Write-Output $_ }
    Write-Host "Validated commit: $head"
    Write-Host "Observation hold: $ObservationHoldMilliseconds ms"
    if ($preflightAccepted) {
        Write-Host 'Canonical video preflight: Accepted'
    }
    Write-Host 'Operator observation required: Video visible: Yes / No'
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
