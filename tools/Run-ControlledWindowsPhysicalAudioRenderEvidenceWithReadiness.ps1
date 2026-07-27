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
    [string]$ReadinessRecordPath,

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
    [string]$AuthorizationStatement,

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

$requiredAuthorizationStatement =
    'I explicitly authorize exactly one controlled physical audio render run.'
$expectedFixtureSha256 =
    '32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15'
$expectedAudioBufferMilliseconds = 100
$maximumNativeOutputBytes = 512 * 1024
$maximumNativeErrorBytes = 64 * 1024
$validationPrefix = 'Controlled render authorization gate rejected: '
$validationMarker = 'PeerCastNovaControlledRenderAuthorizationGateValidation'

function Stop-GateValidation {
    param([Parameter(Mandatory = $true)][string]$Message)

    $exception = New-Object `
        -TypeName System.InvalidOperationException `
        -ArgumentList ($validationPrefix + $Message)
    $exception.Data[$validationMarker] = $true
    throw $exception
}

function Get-GateValidationFailure {
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

function Invoke-BoundedNativeCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [long]$MaximumOutputBytes = $maximumNativeOutputBytes,
        [long]$MaximumErrorBytes = $maximumNativeErrorBytes,
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
            $standardOutputBytes -le $MaximumOutputBytes -and
            $standardErrorBytes -le $MaximumErrorBytes
        $outputLines = if ($CaptureOutput -and $withinBounds) {
            @(Get-Content -LiteralPath $standardOutputPath)
        }
        else {
            @()
        }
        $errorLines = if ($CaptureOutput -and $withinBounds) {
            @(Get-Content -LiteralPath $standardErrorPath)
        }
        else {
            @()
        }

        return [pscustomobject]@{
            ExitCode = $exitCode
            OutputLines = $outputLines
            ErrorLines = $errorLines
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
$helperInvoked = $false
$finalExitCode = 2
try {
    if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
        Stop-GateValidation 'Windows is required.'
    }
    if ($AuthorizationStatement -cne $requiredAuthorizationStatement) {
        Stop-GateValidation 'the exact explicit one-run authorization statement is required.'
    }

    $repoRootCommand = Invoke-BoundedNativeCommand `
        -FilePath 'git' `
        -Arguments @('rev-parse', '--show-toplevel') `
        -MaximumOutputBytes 4096 `
        -MaximumErrorBytes 4096 `
        -CaptureOutput
    $repoRootText = @($repoRootCommand.OutputLines)
    if (-not $repoRootCommand.WithinBounds -or
        $repoRootCommand.ExitCode -ne 0 -or
        $repoRootCommand.StandardErrorBytes -ne 0 -or
        $repoRootText.Count -ne 1) {
        Stop-GateValidation 'run this command from the PeerCast Nova repository.'
    }

    $repoRoot = [System.IO.Path]::GetFullPath([string]$repoRootText[0])
    Push-Location $repoRoot
    $locationPushed = $true

    $branchCommand = Invoke-BoundedNativeCommand `
        -FilePath 'git' `
        -Arguments @('branch', '--show-current') `
        -MaximumOutputBytes 4096 `
        -MaximumErrorBytes 4096 `
        -CaptureOutput
    $branch = (@($branchCommand.OutputLines) -join '').Trim()
    if (-not $branchCommand.WithinBounds -or
        $branchCommand.ExitCode -ne 0 -or
        $branchCommand.StandardErrorBytes -ne 0 -or
        $branch -cne 'main') {
        Stop-GateValidation 'the checked-out branch must be main.'
    }

    $headCommand = Invoke-BoundedNativeCommand `
        -FilePath 'git' `
        -Arguments @('rev-parse', 'HEAD') `
        -MaximumOutputBytes 4096 `
        -MaximumErrorBytes 4096 `
        -CaptureOutput
    $head = (@($headCommand.OutputLines) -join '').Trim()
    if (-not $headCommand.WithinBounds -or
        $headCommand.ExitCode -ne 0 -or
        $headCommand.StandardErrorBytes -ne 0 -or
        $head -ine $ExpectedCommit) {
        Stop-GateValidation 'HEAD does not match the explicitly approved commit.'
    }

    $statusCommand = Invoke-BoundedNativeCommand `
        -FilePath 'git' `
        -Arguments @('status', '--porcelain', '--untracked-files=all') `
        -MaximumOutputBytes 65536 `
        -MaximumErrorBytes 4096 `
        -CaptureOutput
    if (-not $statusCommand.WithinBounds -or
        $statusCommand.ExitCode -ne 0 -or
        $statusCommand.StandardErrorBytes -ne 0 -or
        @($statusCommand.OutputLines).Count -ne 0) {
        Stop-GateValidation 'the repository working tree must be clean.'
    }

    $runtimeFull = [System.IO.Path]::GetFullPath($RuntimeDirectory)
    $fixtureFull = [System.IO.Path]::GetFullPath($FixturePath)
    $videoRecordFull = [System.IO.Path]::GetFullPath($VideoRecordPath)
    $readinessRecordFull = [System.IO.Path]::GetFullPath($ReadinessRecordPath)
    $renderRecordFull = [System.IO.Path]::GetFullPath($RenderEvidenceRecordPath)
    $terminalRecordFull = [System.IO.Path]::GetFullPath($TerminalRenderRecordPath)
    $failureRecordFull = [System.IO.Path]::GetFullPath($FailureRecordPath)

    if (-not [System.IO.File]::Exists($readinessRecordFull)) {
        Stop-GateValidation 'the retained readiness record is unavailable.'
    }
    if ([System.IO.File]::Exists($renderRecordFull) -or
        [System.IO.Directory]::Exists($renderRecordFull) -or
        [System.IO.File]::Exists($terminalRecordFull) -or
        [System.IO.Directory]::Exists($terminalRecordFull) -or
        [System.IO.File]::Exists($failureRecordFull) -or
        [System.IO.Directory]::Exists($failureRecordFull)) {
        Stop-GateValidation 'every future physical outcome path must still be fresh.'
    }

    foreach ($futurePath in @(
            $renderRecordFull,
            $terminalRecordFull,
            $failureRecordFull)) {
        $futureDirectory = Split-Path -Parent $futurePath
        if ([string]::IsNullOrWhiteSpace($futureDirectory) -or
            -not [System.IO.Directory]::Exists($futureDirectory)) {
            Stop-GateValidation 'every future physical outcome parent directory must exist.'
        }
    }

    $allPaths = @(
        $runtimeFull,
        $fixtureFull,
        $videoRecordFull,
        $readinessRecordFull,
        $renderRecordFull,
        $terminalRecordFull,
        $failureRecordFull)
    for ($left = 0; $left -lt $allPaths.Count; $left++) {
        for ($right = $left + 1; $right -lt $allPaths.Count; $right++) {
            if ($allPaths[$left].Equals(
                    $allPaths[$right],
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                Stop-GateValidation 'runtime, inputs, readiness, and outcome paths must be pairwise distinct.'
            }
        }
    }
    foreach ($externalPath in $allPaths) {
        if (Test-IsUnderPath -Candidate $externalPath -Parent $repoRoot) {
            Stop-GateValidation 'runtime, inputs, readiness, and outcome paths must remain repository-external.'
        }
    }
    if (Test-IsUnderPath -Candidate $readinessRecordFull -Parent $runtimeFull) {
        Stop-GateValidation 'the readiness record must remain outside the runtime directory.'
    }

    $readinessDigestBefore = (
        Get-FileHash -LiteralPath $readinessRecordFull -Algorithm SHA256).Hash

    $restoreCommand = Invoke-BoundedNativeCommand `
        -FilePath 'dotnet' `
        -Arguments @('restore', (Join-Path $repoRoot 'PeerCast-Nova.sln'))
    if (-not $restoreCommand.WithinBounds -or $restoreCommand.ExitCode -ne 0) {
        Stop-GateValidation 'solution restore failed before readiness validation.'
    }

    $runnerProject = Join-Path $repoRoot `
        'src\Nova.ControlledWindowsRunner\Nova.ControlledWindowsRunner.csproj'
    $buildCommand = Invoke-BoundedNativeCommand `
        -FilePath 'dotnet' `
        -Arguments @('build', $runnerProject, '--configuration', 'Release', '--no-restore')
    if (-not $buildCommand.WithinBounds -or $buildCommand.ExitCode -ne 0) {
        Stop-GateValidation 'controlled Windows runner build failed before readiness validation.'
    }

    $runnerAssembly = Join-Path $repoRoot `
        'src\Nova.ControlledWindowsRunner\bin\Release\net10.0\Nova.ControlledWindowsRunner.dll'
    if (-not [System.IO.File]::Exists($runnerAssembly)) {
        Stop-GateValidation 'the controlled Windows runner assembly is unavailable.'
    }

    $readinessPreflight = Invoke-BoundedNativeCommand `
        -FilePath 'dotnet' `
        -Arguments @(
            $runnerAssembly,
            '--verify-controlled-windows-audio-render-readiness-record',
            '--record',
            $readinessRecordFull,
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
        -MaximumOutputBytes 32768 `
        -MaximumErrorBytes 32768 `
        -CaptureOutput
    $readinessLines = @($readinessPreflight.OutputLines)
    if (-not $readinessPreflight.WithinBounds -or
        $readinessPreflight.ExitCode -ne 0 -or
        $readinessPreflight.StandardErrorBytes -ne 0 -or
        $readinessLines -cnotcontains 'Result: ReadinessReportValidated' -or
        $readinessLines -cnotcontains 'Readiness-Report-Validated: True' -or
        $readinessLines -cnotcontains 'Ready-For-Separate-Authorization: True' -or
        $readinessLines -cnotcontains 'Physical-Run-Authorization-Granted: False' -or
        $readinessLines -cnotcontains 'Physical-Runs-Executed-By-This-Command: False' -or
        $readinessLines -cnotcontains 'Audio-Acceptance-Claimed: False' -or
        $readinessLines -cnotcontains 'External-Binary-Or-Payload-Published: False') {
        Stop-GateValidation 'the retained readiness record failed strict managed validation.'
    }

    $readinessDigestAfter = (
        Get-FileHash -LiteralPath $readinessRecordFull -Algorithm SHA256).Hash
    if ($readinessDigestAfter -ine $readinessDigestBefore) {
        Stop-GateValidation 'the retained readiness record changed during validation.'
    }

    $postHeadCommand = Invoke-BoundedNativeCommand `
        -FilePath 'git' `
        -Arguments @('rev-parse', 'HEAD') `
        -MaximumOutputBytes 4096 `
        -MaximumErrorBytes 4096 `
        -CaptureOutput
    $postHead = (@($postHeadCommand.OutputLines) -join '').Trim()
    $postStatusCommand = Invoke-BoundedNativeCommand `
        -FilePath 'git' `
        -Arguments @('status', '--porcelain', '--untracked-files=all') `
        -MaximumOutputBytes 65536 `
        -MaximumErrorBytes 4096 `
        -CaptureOutput
    if (-not $postHeadCommand.WithinBounds -or
        $postHeadCommand.ExitCode -ne 0 -or
        $postHeadCommand.StandardErrorBytes -ne 0 -or
        $postHead -ine $ExpectedCommit -or
        -not $postStatusCommand.WithinBounds -or
        $postStatusCommand.ExitCode -ne 0 -or
        $postStatusCommand.StandardErrorBytes -ne 0 -or
        @($postStatusCommand.OutputLines).Count -ne 0) {
        Stop-GateValidation 'the repository changed during authorization validation.'
    }

    foreach ($futurePath in @(
            $renderRecordFull,
            $terminalRecordFull,
            $failureRecordFull)) {
        if ([System.IO.File]::Exists($futurePath) -or
            [System.IO.Directory]::Exists($futurePath)) {
            Stop-GateValidation 'a future physical outcome path was occupied before helper invocation.'
        }
    }

    $helperScript = Join-Path $repoRoot `
        'tools\Run-ControlledWindowsPhysicalAudioRenderEvidence.ps1'
    if (-not [System.IO.File]::Exists($helperScript)) {
        Stop-GateValidation 'the controlled physical audio helper is unavailable.'
    }

    $helperArguments = @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        $helperScript,
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

    $helperInvoked = $true
    $helperResult = Invoke-BoundedNativeCommand `
        -FilePath 'powershell.exe' `
        -Arguments $helperArguments `
        -MaximumOutputBytes $maximumNativeOutputBytes `
        -MaximumErrorBytes $maximumNativeErrorBytes `
        -CaptureOutput
    if (-not $helperResult.WithinBounds -or
        @(0, 1, 2) -cnotcontains $helperResult.ExitCode) {
        Stop-GateValidation 'the physical helper returned an invalid bounded process result.'
    }

    $helperResult.OutputLines | ForEach-Object { Write-Output $_ }
    $helperResult.ErrorLines | ForEach-Object {
        [System.Console]::Error.WriteLine($_)
    }
    $finalExitCode = $helperResult.ExitCode
}
catch {
    $validationFailure = Get-GateValidationFailure -Exception $_.Exception
    if ($null -ne $validationFailure) {
        [System.Console]::Error.WriteLine($validationFailure.Message)
    }
    else {
        [System.Console]::Error.WriteLine(
            $validationPrefix + 'unexpected authorization gate failure.')
    }

    if ($helperInvoked) {
        [System.Console]::Error.WriteLine('Physical helper invoked: Yes')
        [System.Console]::Error.WriteLine('Physical run completion established: No')
        [System.Console]::Error.WriteLine('Do not rerun this command.')
    }
    else {
        [System.Console]::Error.WriteLine('Physical helper invoked: No')
        [System.Console]::Error.WriteLine('Physical runs executed by this command: No')
        [System.Console]::Error.WriteLine(
            'This gate failure does not authorize physical execution.')
    }
    $finalExitCode = 2
}
finally {
    if ($locationPushed) {
        Pop-Location
    }
}

exit $finalExitCode
