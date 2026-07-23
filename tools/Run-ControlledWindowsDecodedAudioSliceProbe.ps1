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

    [ValidateRange(1, 66939)]
    [int]$FixturePacketBytes = 65536,

    [ValidateRange(0, 30000)]
    [int]$ObservationHoldMilliseconds = 1000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedFixtureSha256 =
    '32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15'
$expectedFixtureBytes = 66939L
$recordHeader =
    'PEERCAST-NOVA-CONTROLLED-WINDOWS-DECODED-AUDIO-SLICE-PROBE 1'
$recordFooter =
    'END-PEERCAST-NOVA-CONTROLLED-WINDOWS-DECODED-AUDIO-SLICE-PROBE'
$validationPrefix = 'Controlled decoded-audio slice probe rejected: '

$runtimeFiles = @(
    @('avcodec-62.dll', 70875136L,
        '4936D6DD5C1615AC4166017A9843F4D8DC415EF6D3156A4138B84C2A4419818C'),
    @('avformat-62.dll', 22070784L,
        '198B582EE199BA16C973A438FA125A2448772755BC2E95CC01B28AB434270290'),
    @('avutil-60.dll', 2937856L,
        '419513335F9209499243C3C7F2EA46C0B3F477281D80DE90E696E2E1062D7452'),
    @('swresample-6.dll', 723968L,
        '344CA8558DCED8431D54E692A171F5167510E940E860292F33CD77460DB0E83B'),
    @('swscale-9.dll', 12570624L,
        '7EBE9B9B4B8B23DA6E86631573E85AE484392CB7B3D0ECF506F621BDC1222229')
)

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
$temporaryRecord = $null
$finalExitCode = 2
try {
    if ([System.Environment]::OSVersion.Platform -ne
        [System.PlatformID]::Win32NT) {
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
    $originMain = (@(& git rev-parse origin/main) -join '').Trim()
    if ($LASTEXITCODE -ne 0 -or
        $head -ine $ExpectedCommit -or
        $originMain -ine $ExpectedCommit) {
        Stop-Validation 'HEAD and origin/main must match the approved commit.'
    }

    $status = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) {
        Stop-Validation 'the repository working tree must be clean.'
    }

    $runtimeFull = [System.IO.Path]::GetFullPath($RuntimeDirectory)
    $fixtureFull = [System.IO.Path]::GetFullPath($FixturePath)
    $recordFull = [System.IO.Path]::GetFullPath($RecordPath)
    if (-not [System.IO.Directory]::Exists($runtimeFull)) {
        Stop-Validation 'the approved external runtime directory is unavailable.'
    }
    if (-not [System.IO.File]::Exists($fixtureFull)) {
        Stop-Validation 'the retained controlled fixture is unavailable.'
    }
    if ([System.IO.File]::Exists($recordFull)) {
        Stop-Validation 'the requested record already exists; use a new path.'
    }
    if (Test-IsUnderPath -Candidate $runtimeFull -Parent $repoRoot) {
        Stop-Validation 'the FFmpeg runtime must remain repository-external.'
    }
    if (Test-IsUnderPath -Candidate $fixtureFull -Parent $repoRoot) {
        Stop-Validation 'the controlled fixture must remain repository-external.'
    }
    if (Test-IsUnderPath -Candidate $recordFull -Parent $repoRoot) {
        Stop-Validation 'the probe record must remain repository-external.'
    }

    foreach ($runtimeFile in $runtimeFiles) {
        $candidate = Join-Path $runtimeFull ([string]$runtimeFile[0])
        if (-not [System.IO.File]::Exists($candidate)) {
            Stop-Validation 'an approved runtime load-role DLL is missing.'
        }
        $candidateInfo = Get-Item -LiteralPath $candidate
        if ($candidateInfo.Length -ne [long]$runtimeFile[1]) {
            Stop-Validation 'an approved runtime load-role DLL size does not match.'
        }
        $candidateHash =
            (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
        if ($candidateHash -ine [string]$runtimeFile[2]) {
            Stop-Validation 'an approved runtime load-role DLL SHA-256 does not match.'
        }
    }

    $fixtureInfo = Get-Item -LiteralPath $fixtureFull
    if ($fixtureInfo.Length -ne $expectedFixtureBytes) {
        Stop-Validation 'the controlled fixture byte count does not match.'
    }
    $fixtureDigest =
        (Get-FileHash -LiteralPath $fixtureFull -Algorithm SHA256).Hash
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

    $runnerProject = Join-Path $repoRoot (
        'src\Nova.ControlledWindowsRunner\Nova.ControlledWindowsRunner.csproj')
    & dotnet build $runnerProject --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        Stop-Validation 'controlled Windows runner build failed.'
    }

    $runnerAssembly = Join-Path $repoRoot (
        'src\Nova.ControlledWindowsRunner\bin\Release\net10.0\' +
        'Nova.ControlledWindowsRunner.dll')
    if (-not [System.IO.File]::Exists($runnerAssembly)) {
        Stop-Validation 'the controlled Windows runner assembly is unavailable.'
    }

    $temporaryRecord = Join-Path $recordDirectory (
        '.pcn-decoded-audio-slice-' +
        [System.Guid]::NewGuid().ToString('N') + '.tmp')
    & dotnet $runnerAssembly `
        --controlled-windows-decoded-audio-slice-probe `
        --runtime-directory $runtimeFull `
        --controlled-fixture $fixtureFull `
        --max-decode-operations $MaximumDecodeOperations `
        --fixture-packet-bytes $FixturePacketBytes `
        --observation-hold-ms $ObservationHoldMilliseconds > $temporaryRecord
    $runExitCode = $LASTEXITCODE

    $recordLines = @(Get-Content -LiteralPath $temporaryRecord)
    if ($recordLines.Count -ne 26 -or
        $recordLines[0] -cne $recordHeader -or
        $recordLines[$recordLines.Count - 1] -cne $recordFooter -or
        $recordLines -cnotcontains
            'Runtime-Build: btbn.ffmpeg.20260721.win64.lgpl-shared' -or
        $recordLines -cnotcontains
            ('Fixture-SHA256: ' + $expectedFixtureSha256) -or
        $recordLines -cnotcontains 'Sample-Format: PcmFloat32Interleaved' -or
        $recordLines -cnotcontains 'Sample-Rate: 48000' -or
        $recordLines -cnotcontains 'Channels: 2' -or
        $recordLines -cnotcontains 'Duration-Milliseconds: 80' -or
        $recordLines -cnotcontains 'Output-Sample-Frames: 3840' -or
        $recordLines -cnotcontains 'Payload-Bytes: 30720' -or
        $recordLines -cnotcontains
            'External-Binary-Or-Payload-Published: False') {
        Stop-Validation (
            'the command did not produce one complete canonical sanitized probe record.')
    }

    if ($runExitCode -eq 0 -and (
        $recordLines -cnotcontains 'Result: Completed' -or
        $recordLines -cnotcontains 'Evidence: Accepted' -or
        $recordLines -cnotcontains 'Decoded-Frames-Consumed: 4' -or
        $recordLines -cnotcontains 'Full-Decoded-Frames-Copied: 3' -or
        $recordLines -cnotcontains
            'Final-Frame-Sample-Frames-Copied: 768' -or
        $recordLines -cnotcontains 'Write-Attempts: 1' -or
        $recordLines -cnotcontains 'Writes-Acknowledged: 1' -or
        $recordLines -cnotcontains 'Start-Acknowledged: True' -or
        $recordLines -cnotcontains 'Retained-WouldBlock: 0' -or
        $recordLines -cnotcontains 'Video-Present-Attempts: 0' -or
        $recordLines -cnotcontains 'Runner-State: Stopped' -or
        $recordLines -cnotcontains 'Lifecycle-State: Disposed' -or
        $recordLines -cnotcontains 'Borrowed-Window-Preserved: True' -or
        $recordLines -cnotcontains 'Borrowed-Dispatcher-Preserved: True')) {
        Stop-Validation 'the successful probe record contradicts the accepted contract.'
    }

    $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllLines(
        $temporaryRecord,
        [string[]]$recordLines,
        $utf8WithoutBom)
    Move-Item -LiteralPath $temporaryRecord -Destination $recordFull
    $temporaryRecord = $null

    $postStatus = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $postStatus.Count -ne 0) {
        Stop-Validation 'the repository changed during the decoded-audio slice probe.'
    }

    $recordLines | ForEach-Object { Write-Output $_ }
    Write-Host "Validated commit: $head"
    Write-Host "Observation hold: $ObservationHoldMilliseconds ms"
    if ($runExitCode -eq 0) {
        Write-Host (
            'Operator observation required: ' +
            'Decoded fixture audio recognizable: Yes / No')
    }
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
    if ($temporaryRecord -and [System.IO.File]::Exists($temporaryRecord)) {
        Remove-Item -LiteralPath $temporaryRecord -Force -ErrorAction SilentlyContinue
    }
    if ($locationPushed) {
        Pop-Location
    }
}

exit $finalExitCode
