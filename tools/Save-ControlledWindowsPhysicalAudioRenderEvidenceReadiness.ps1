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

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ReadinessRecordPath,

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
$expectedAudioBufferMilliseconds = 100
$maximumReadinessOutputBytes = 128 * 1024
$maximumReadinessErrorBytes = 32 * 1024
$maximumNativeOutputBytes = 32 * 1024
$maximumNativeErrorBytes = 32 * 1024
$validationPrefix = 'Controlled render readiness capture rejected: '
$validationMarker = 'PeerCastNovaControlledRenderReadinessCaptureValidation'

function Stop-CaptureValidation {
    param([Parameter(Mandatory = $true)][string]$Message)

    $exception = New-Object `
        -TypeName System.InvalidOperationException `
        -ArgumentList ($validationPrefix + $Message)
    $exception.Data[$validationMarker] = $true
    throw $exception
}

function Get-CaptureValidationFailure {
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

        $standardOutputBytes = [long](
            Get-Item -LiteralPath $standardOutputPath).Length
        $standardErrorBytes = [long](
            Get-Item -LiteralPath $standardErrorPath).Length
        $withinBounds =
            $standardOutputBytes -le $maximumNativeOutputBytes -and
            $standardErrorBytes -le $maximumNativeErrorBytes
        $outputLines = if ($CaptureOutput -and $withinBounds) {
            @(Get-Content -LiteralPath $standardOutputPath)
        }
        else {
            @()
        }
        return [pscustomobject]@{
            ExitCode = $exitCode
            OutputLines = $outputLines
            StandardOutputBytes = $standardOutputBytes
            StandardErrorBytes = $standardErrorBytes
            WithinBounds = $withinBounds
        }
    }
    finally {
        Remove-Item -LiteralPath $standardOutputPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $standardErrorPath -Force -ErrorAction SilentlyContinue
    }
}

$locationPushed = $false
$readinessStandardOutputPath = $null
$readinessStandardErrorPath = $null
$temporaryRecordPath = $null
$recordPromoted = $false
try {
    if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
        Stop-CaptureValidation 'Windows is required.'
    }

    $repoRootCommand = Invoke-SanitizedNativeCommand `
        -FilePath 'git' `
        -Arguments @('rev-parse', '--show-toplevel') `
        -CaptureOutput
    $repoRootText = @($repoRootCommand.OutputLines)
    if ($repoRootCommand.ExitCode -ne 0 -or
        -not $repoRootCommand.WithinBounds -or
        $repoRootText.Count -ne 1) {
        Stop-CaptureValidation 'run this command from the PeerCast Nova repository.'
    }

    $repoRoot = [System.IO.Path]::GetFullPath([string]$repoRootText[0])
    Push-Location $repoRoot
    $locationPushed = $true

    $readinessScript = Join-Path $repoRoot `
        'tools\Test-ControlledWindowsPhysicalAudioRenderEvidenceReadiness.ps1'
    if (-not [System.IO.File]::Exists($readinessScript)) {
        Stop-CaptureValidation 'the readiness script is unavailable.'
    }

    $runtimeFull = [System.IO.Path]::GetFullPath($RuntimeDirectory)
    $fixtureFull = [System.IO.Path]::GetFullPath($FixturePath)
    $videoRecordFull = [System.IO.Path]::GetFullPath($VideoRecordPath)
    $renderRecordFull = [System.IO.Path]::GetFullPath($RenderEvidenceRecordPath)
    $terminalRecordFull = [System.IO.Path]::GetFullPath($TerminalRenderRecordPath)
    $failureRecordFull = [System.IO.Path]::GetFullPath($FailureRecordPath)
    $readinessRecordFull = [System.IO.Path]::GetFullPath($ReadinessRecordPath)

    if ([System.IO.File]::Exists($readinessRecordFull) -or
        [System.IO.Directory]::Exists($readinessRecordFull)) {
        Stop-CaptureValidation 'the readiness record path must be fresh.'
    }

    $readinessRecordDirectory = Split-Path -Parent $readinessRecordFull
    if ([string]::IsNullOrWhiteSpace($readinessRecordDirectory) -or
        -not [System.IO.Directory]::Exists($readinessRecordDirectory)) {
        Stop-CaptureValidation 'the readiness record parent directory must exist.'
    }
    if (Test-IsUnderPath -Candidate $readinessRecordFull -Parent $repoRoot) {
        Stop-CaptureValidation 'the readiness record path must remain repository-external.'
    }
    if (Test-IsUnderPath -Candidate $readinessRecordFull -Parent $runtimeFull) {
        Stop-CaptureValidation 'the readiness record path must remain outside the runtime directory.'
    }

    foreach ($otherPath in @(
            $runtimeFull,
            $fixtureFull,
            $videoRecordFull,
            $renderRecordFull,
            $terminalRecordFull,
            $failureRecordFull)) {
        if ($readinessRecordFull.Equals(
                $otherPath,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            Stop-CaptureValidation 'the readiness record path must be distinct from every input and future output path.'
        }
    }

    $readinessStandardOutputPath = [System.IO.Path]::GetTempFileName()
    $readinessStandardErrorPath = [System.IO.Path]::GetTempFileName()
    $readinessArguments = @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        $readinessScript,
        '-ExpectedCommit',
        $ExpectedCommit,
        '-RuntimeDirectory',
        $runtimeFull,
        '-FixturePath',
        $fixtureFull,
        '-VideoRecordPath',
        $videoRecordFull,
        '-RenderEvidenceRecordPath',
        $renderRecordFull,
        '-TerminalRenderRecordPath',
        $terminalRecordFull,
        '-FailureRecordPath',
        $failureRecordFull,
        '-MaximumDecodeOperations',
        $MaximumDecodeOperations.ToString([System.Globalization.CultureInfo]::InvariantCulture),
        '-MaximumDeliveryOperations',
        $MaximumDeliveryOperations.ToString([System.Globalization.CultureInfo]::InvariantCulture),
        '-FixturePacketBytes',
        $FixturePacketBytes.ToString([System.Globalization.CultureInfo]::InvariantCulture),
        '-ObservationHoldMilliseconds',
        $ObservationHoldMilliseconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    )

    $nativeErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & 'powershell.exe' @readinessArguments `
            1> $readinessStandardOutputPath `
            2> $readinessStandardErrorPath
        $readinessExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $nativeErrorActionPreference
    }

    $readinessOutputBytes = [long](
        Get-Item -LiteralPath $readinessStandardOutputPath).Length
    $readinessErrorBytes = [long](
        Get-Item -LiteralPath $readinessStandardErrorPath).Length
    if ($readinessExitCode -ne 0) {
        Stop-CaptureValidation 'the readiness script did not succeed.'
    }
    if ($readinessOutputBytes -le 0 -or
        $readinessOutputBytes -gt $maximumReadinessOutputBytes) {
        Stop-CaptureValidation 'the readiness stdout size was invalid.'
    }
    if ($readinessErrorBytes -gt $maximumReadinessErrorBytes) {
        Stop-CaptureValidation 'the readiness stderr size exceeded its bound.'
    }
    if ($readinessErrorBytes -ne 0) {
        Stop-CaptureValidation 'the readiness stderr boundary was not empty.'
    }

    $readinessLines = @(Get-Content -LiteralPath $readinessStandardOutputPath)
    if ($readinessLines.Count -ne 27 -or
        @($readinessLines | Where-Object { [string]::IsNullOrEmpty($_) }).Count -ne 0) {
        Stop-CaptureValidation 'the readiness stdout did not have the canonical line count.'
    }
    $canonicalReadinessText = ($readinessLines -join "`n") + "`n"

    $temporaryRecordPath = Join-Path `
        $readinessRecordDirectory `
        ('.peercast-nova-readiness-' + [System.Guid]::NewGuid().ToString('N') + '.tmp')
    if ([System.IO.File]::Exists($temporaryRecordPath) -or
        [System.IO.Directory]::Exists($temporaryRecordPath)) {
        Stop-CaptureValidation 'a fresh temporary readiness record path was unavailable.'
    }

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false, $true)
    $canonicalBytes = $utf8NoBom.GetBytes($canonicalReadinessText)
    $recordStream = [System.IO.File]::Open(
        $temporaryRecordPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $recordStream.Write($canonicalBytes, 0, $canonicalBytes.Length)
        $recordStream.Flush($true)
    }
    finally {
        $recordStream.Dispose()
    }

    $runnerAssembly = Join-Path $repoRoot `
        'src\Nova.ControlledWindowsRunner\bin\Release\net10.0\Nova.ControlledWindowsRunner.dll'
    if (-not [System.IO.File]::Exists($runnerAssembly)) {
        Stop-CaptureValidation 'the controlled Windows runner assembly is unavailable after readiness.'
    }

    $preflight = Invoke-SanitizedNativeCommand `
        -FilePath 'dotnet' `
        -Arguments @(
            $runnerAssembly,
            '--verify-controlled-windows-audio-render-readiness-record',
            '--record',
            $temporaryRecordPath,
            '--expected-commit',
            $ExpectedCommit,
            '--fixture-sha256',
            $expectedFixtureSha256,
            '--max-decode-operations',
            $MaximumDecodeOperations.ToString([System.Globalization.CultureInfo]::InvariantCulture),
            '--max-delivery-operations',
            $MaximumDeliveryOperations.ToString([System.Globalization.CultureInfo]::InvariantCulture),
            '--fixture-packet-bytes',
            $FixturePacketBytes.ToString([System.Globalization.CultureInfo]::InvariantCulture),
            '--observation-hold-ms',
            $ObservationHoldMilliseconds.ToString([System.Globalization.CultureInfo]::InvariantCulture),
            '--audio-buffer-ms',
            $expectedAudioBufferMilliseconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)) `
        -CaptureOutput
    $preflightLines = @($preflight.OutputLines)
    if ($preflight.ExitCode -ne 0 -or
        -not $preflight.WithinBounds -or
        $preflight.StandardErrorBytes -ne 0 -or
        $preflightLines -cnotcontains 'Result: ReadinessReportValidated' -or
        $preflightLines -cnotcontains 'Readiness-Report-Validated: True' -or
        $preflightLines -cnotcontains 'Ready-For-Separate-Authorization: True' -or
        $preflightLines -cnotcontains 'Physical-Run-Authorization-Granted: False' -or
        $preflightLines -cnotcontains 'Physical-Runs-Executed-By-This-Command: False') {
        Stop-CaptureValidation 'the managed readiness record preflight rejected the captured report.'
    }

    if ([System.IO.File]::Exists($readinessRecordFull) -or
        [System.IO.Directory]::Exists($readinessRecordFull)) {
        Stop-CaptureValidation 'the readiness record path was occupied before promotion.'
    }
    [System.IO.File]::Move($temporaryRecordPath, $readinessRecordFull)
    $temporaryRecordPath = $null
    $recordPromoted = $true

    Write-Output 'Result: ReadinessRecordCaptured'
    Write-Output 'Readiness-Script-Executed-By-This-Command: True'
    Write-Output 'Readiness-Record-Validated: True'
    Write-Output 'Ready-For-Separate-Authorization: True'
    Write-Output 'Physical-Run-Authorization-Granted: False'
    Write-Output 'Physical-Runs-Executed-By-This-Command: False'
    Write-Output 'Audio-Acceptance-Claimed: False'
    Write-Output 'External-Binary-Or-Payload-Published: False'
    exit 0
}
catch {
    $validationFailure = Get-CaptureValidationFailure -Exception $_.Exception
    if ($null -ne $validationFailure) {
        [System.Console]::Error.WriteLine($validationFailure.Message)
    }
    else {
        [System.Console]::Error.WriteLine(
            $validationPrefix + 'unexpected capture failure.')
    }
    $retainedText = if ($recordPromoted) { 'Yes' } else { 'No' }
    [System.Console]::Error.WriteLine('Readiness record retained: ' + $retainedText)
    [System.Console]::Error.WriteLine('Physical runs executed by this command: No')
    [System.Console]::Error.WriteLine('Readiness capture does not authorize physical execution.')
    exit 1
}
finally {
    foreach ($temporaryPath in @(
            $readinessStandardOutputPath,
            $readinessStandardErrorPath,
            $temporaryRecordPath)) {
        if (-not [string]::IsNullOrWhiteSpace($temporaryPath)) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        }
    }
    if ($locationPushed) {
        Pop-Location
    }
}
