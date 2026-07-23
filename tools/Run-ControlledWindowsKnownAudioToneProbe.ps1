[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$ExpectedCommit,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$RecordPath,

    [ValidateRange(0, 30000)]
    [int]$ObservationHoldMilliseconds = 1000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$validationPrefix = 'Controlled known-audio tone probe rejected: '
$recordHeader = 'PEERCAST-NOVA-CONTROLLED-WINDOWS-KNOWN-AUDIO-TONE-PROBE 1'
$recordFooter = 'END-PEERCAST-NOVA-CONTROLLED-WINDOWS-KNOWN-AUDIO-TONE-PROBE'

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
$temporaryRecord = $null
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

    $recordFull = [System.IO.Path]::GetFullPath($RecordPath)
    if (Test-IsUnderPath -Candidate $recordFull -Parent $repoRoot) {
        Stop-Validation 'the known-audio tone probe record must remain repository-external.'
    }
    if ([System.IO.File]::Exists($recordFull)) {
        Stop-Validation 'the requested record already exists; use a new path.'
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
        '.pcn-known-audio-tone-' + [System.Guid]::NewGuid().ToString('N') + '.tmp')
    & dotnet $runnerAssembly `
        --controlled-windows-known-audio-tone-probe `
        --observation-hold-ms $ObservationHoldMilliseconds > $temporaryRecord
    $runExitCode = $LASTEXITCODE

    $recordLines = @(Get-Content -LiteralPath $temporaryRecord)
    if ($recordLines.Count -ne 21 -or
        $recordLines[0] -cne $recordHeader -or
        $recordLines[$recordLines.Count - 1] -cne $recordFooter -or
        $recordLines -cnotcontains 'Milestone: 0.0.10d Phase 5' -or
        $recordLines -cnotcontains 'Sample-Format: PcmFloat32Interleaved' -or
        $recordLines -cnotcontains 'Sample-Rate: 48000' -or
        $recordLines -cnotcontains 'Channels: 2' -or
        $recordLines -cnotcontains 'Tone-Hz: 1000' -or
        $recordLines -cnotcontains 'Duration-Milliseconds: 80' -or
        $recordLines -cnotcontains 'Sample-Frames: 3840' -or
        $recordLines -cnotcontains 'Payload-Bytes: 30720' -or
        $recordLines -cnotcontains 'External-Binary-Or-Payload-Published: False') {
        Stop-Validation 'the command did not produce one complete canonical sanitized probe record.'
    }

    if ($runExitCode -eq 0 -and (
        $recordLines -cnotcontains 'Result: Completed' -or
        $recordLines -cnotcontains 'Evidence: Accepted' -or
        $recordLines -cnotcontains 'Write-Attempts: 1' -or
        $recordLines -cnotcontains 'Writes-Acknowledged: 1' -or
        $recordLines -cnotcontains 'Start-Acknowledged: True' -or
        $recordLines -cnotcontains 'Retained-WouldBlock: 0' -or
        $recordLines -cnotcontains 'Runner-State: Stopped' -or
        $recordLines -cnotcontains 'Lifecycle-State: Disposed' -or
        $recordLines -cnotcontains 'Borrowed-Window-Preserved: True' -or
        $recordLines -cnotcontains 'Borrowed-Dispatcher-Preserved: True')) {
        Stop-Validation 'the successful probe record contradicts the accepted contract.'
    }

    [System.IO.File]::WriteAllLines(
        $temporaryRecord,
        [string[]]$recordLines,
        [System.Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporaryRecord -Destination $recordFull
    $temporaryRecord = $null

    $postStatus = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $postStatus.Count -ne 0) {
        Stop-Validation 'the repository changed during the known-audio tone probe.'
    }

    $recordLines | ForEach-Object { Write-Output $_ }
    Write-Host "Validated commit: $head"
    Write-Host "Observation hold: $ObservationHoldMilliseconds ms"
    if ($runExitCode -eq 0) {
        Write-Host 'Operator observation required: Known audio tone audible: Yes / No'
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
