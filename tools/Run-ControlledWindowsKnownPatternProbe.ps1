[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$ExpectedCommit,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$RecordPath,

    [ValidateRange(0, 30000)]
    [int]$ObservationHoldMilliseconds = 3000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$validationPrefix = 'Controlled known-pattern probe rejected: '
$recordHeader = 'PEERCAST-NOVA-CONTROLLED-WINDOWS-KNOWN-PATTERN-PROBE 1'
$recordFooter = 'END-PEERCAST-NOVA-CONTROLLED-WINDOWS-KNOWN-PATTERN-PROBE'

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

    $recordFull = [System.IO.Path]::GetFullPath($RecordPath)
    if (Test-IsUnderPath -Candidate $recordFull -Parent $repoRoot) {
        Stop-Validation 'the known-pattern probe record must remain repository-external.'
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
        '.pcn-known-pattern-' + [System.Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        & dotnet $runnerAssembly `
            --controlled-windows-known-pattern-probe `
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
        $recordLines[0] -cne $recordHeader -or
        $recordLines[$recordLines.Count - 1] -cne $recordFooter -or
        $recordLines -cnotcontains 'Milestone: 0.0.10d Phase 5' -or
        $recordLines -cnotcontains 'Pattern: Bgra32QuadrantsV1' -or
        $recordLines -cnotcontains 'Width: 320' -or
        $recordLines -cnotcontains 'Height: 240' -or
        $recordLines -cnotcontains 'Stride: 1280' -or
        $recordLines -cnotcontains 'Non-Black-Pixels: 76800' -or
        $recordLines -cnotcontains 'FFmpeg-Runtime-Used: False' -or
        $recordLines -cnotcontains 'External-Media-Or-Payload-Published: False') {
        Stop-Validation 'the command did not produce one canonical sanitized probe record.'
    }

    if ($runExitCode -eq 0 -and (
        $recordLines -cnotcontains 'Result: Completed' -or
        $recordLines -cnotcontains 'Present-Attempts: 1' -or
        $recordLines -cnotcontains 'Present-Acknowledgements: 1' -or
        $recordLines -cnotcontains 'Accepted-Bytes: 307200' -or
        $recordLines -cnotcontains 'Present-WouldBlock: 0' -or
        $recordLines -cnotcontains 'Resize-Count: 0' -or
        $recordLines -cnotcontains 'Device-Lost-Count: 0' -or
        $recordLines -cnotcontains 'Runner-State: Stopped' -or
        $recordLines -cnotcontains 'Output-Cleanup-Completed: True' -or
        $recordLines -cnotcontains 'Borrowed-Window-Preserved: True' -or
        $recordLines -cnotcontains 'Borrowed-Dispatcher-Preserved: True')) {
        Stop-Validation 'the successful probe record contradicts the accepted contract.'
    }

    $postStatus = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $postStatus.Count -ne 0) {
        Stop-Validation 'the repository changed during the known-pattern probe.'
    }

    $recordLines | ForEach-Object { Write-Output $_ }
    Write-Host "Validated commit: $head"
    Write-Host "Observation hold: $ObservationHoldMilliseconds ms"
    Write-Host 'Operator observation required: Known pattern visible: Yes / No'
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
