param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{32}$')]
    [string] $ChannelId,

    [ValidatePattern('^[\x21-\x7E]{1,255}$')]
    [string] $HostName = '127.0.0.1',

    [ValidateRange(1, 65535)]
    [int] $Port = 7144,

    [ValidateRange(0, [long]::MaxValue)]
    [long] $Position = 0,

    [ValidateRange(1, 65535)]
    [int] $ListenPort = 17145,

    [ValidateRange(1000, 600000)]
    [int] $DurationMilliseconds = 60000,

    [ValidateRange(1, 1000000)]
    [int] $MinimumPackets = 4,

    [ValidateRange(1, 600000)]
    [int] $IdleTimeoutMilliseconds = 15000,

    [ValidateRange(0, 10)]
    [int] $Reconnects = 2,

    [ValidateRange(1000, 60000)]
    [int] $StartTimeoutMilliseconds = 10000,

    [ValidateRange(1000, 600000)]
    [int] $DownstreamTimeoutMilliseconds = 30000
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$cliAssembly = Join-Path $repoRoot 'src/Nova.Cli/bin/Release/net10.0/PeerCast.Nova.Cli.dll'

if (-not (Test-Path -LiteralPath $cliAssembly -PathType Leaf)) {
    throw 'Release CLI not found. Run build.ps1 before the relay diagnostic.'
}

$relayJob = $null
$gatePassed = $false

function Receive-RelayJobOutput {
    param(
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.Job] $Job,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]] $Lines
    )

    $exitCode = $null

    foreach ($item in @(Receive-Job -Job $Job -ErrorAction SilentlyContinue)) {
        if ($null -eq $item) {
            continue
        }

        $exitProperty = $item.PSObject.Properties['NovaRelayExitCode']

        if ($null -ne $exitProperty) {
            $exitCode = [int] $exitProperty.Value
            continue
        }

        $Lines.Add($item.ToString())
    }

    return $exitCode
}

try {
    $relayJob = Start-Job -ScriptBlock {
        param(
            [string] $CliAssembly,
            [string] $HostName,
            [int] $Port,
            [string] $ChannelId,
            [long] $Position,
            [int] $ListenPort,
            [int] $DurationMilliseconds,
            [int] $MinimumPackets,
            [int] $IdleTimeoutMilliseconds,
            [int] $Reconnects
        )

        $arguments = @(
            '--pcp-relay-diagnostics',
            '--host', $HostName,
            '--port', $Port,
            '--channel', $ChannelId,
            '--position', $Position,
            '--relay-listen-port', $ListenPort,
            '--relay-duration-ms', $DurationMilliseconds,
            '--relay-min-packets', $MinimumPackets,
            '--relay-idle-timeout-ms', $IdleTimeoutMilliseconds,
            '--relay-reconnects', $Reconnects
        )

        & dotnet $CliAssembly @arguments 2>&1 |
            ForEach-Object { $_.ToString() }
        $exitCode = $LASTEXITCODE

        return [PSCustomObject] @{
            NovaRelayExitCode = $exitCode
        }
    } -ArgumentList @(
        $cliAssembly,
        $HostName,
        $Port,
        $ChannelId,
        $Position,
        $ListenPort,
        $DurationMilliseconds,
        $MinimumPackets,
        $IdleTimeoutMilliseconds,
        $Reconnects)

    $relayLines = [System.Collections.Generic.List[string]]::new()
    $relayExitCode = $null
    $readinessObserved = $false
    $readinessStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    while ($readinessStopwatch.ElapsedMilliseconds -lt $StartTimeoutMilliseconds) {
        $observedExitCode = Receive-RelayJobOutput `
            -Job $relayJob `
            -Lines $relayLines

        if ($null -ne $observedExitCode) {
            $relayExitCode = $observedExitCode
        }

        if ($relayLines.Contains('Relay State: Ready')) {
            $readinessObserved = $true
            break
        }

        if ($relayJob.State -in @('Completed', 'Failed', 'Stopped')) {
            break
        }

        Start-Sleep -Milliseconds 100
    }

    $downstreamExitCode = $null
    $downstreamText = 'Downstream Start: ReadinessNotObserved'
    $relayCompletionTimedOut = $false

    if ($readinessObserved) {
        $downstreamArguments = @(
            '--pcp-channel-session-probe',
            '--host', '127.0.0.1',
            '--port', $ListenPort,
            '--channel', $ChannelId,
            '--position', 0,
            '--session-packets', $MinimumPackets,
            '--session-timeout-ms', $DownstreamTimeoutMilliseconds,
            '--session-idle-timeout-ms', $IdleTimeoutMilliseconds,
            '--session-reconnects', 0
        )

        $downstreamOutput = @(
            & dotnet $cliAssembly @downstreamArguments 2>&1)
        $downstreamExitCode = $LASTEXITCODE
        $downstreamText = [string]::Join(
            [Environment]::NewLine,
            [string[]] $downstreamOutput)

        $relayCompletionSeconds = [int][Math]::Ceiling(
            ($DurationMilliseconds + 15000) / 1000.0)
        $completedJob = Wait-Job `
            -Job $relayJob `
            -Timeout $relayCompletionSeconds

        if ($null -eq $completedJob) {
            $relayCompletionTimedOut = $true
        }
    }
    else {
        $completedJob = Wait-Job -Job $relayJob -Timeout 1

        if ($null -eq $completedJob -and
            $relayJob.State -notin @('Completed', 'Failed', 'Stopped')) {
            $downstreamText = 'Downstream Start: ReadinessTimeout'
        }
    }

    $observedExitCode = Receive-RelayJobOutput `
        -Job $relayJob `
        -Lines $relayLines

    if ($null -ne $observedExitCode) {
        $relayExitCode = $observedExitCode
    }

    $relayText = if ($relayLines.Count -eq 0) {
        'Relay Process: NoOutput'
    }
    else {
        [string]::Join([Environment]::NewLine, $relayLines)
    }

    if ($relayCompletionTimedOut) {
        $relayText = [string]::Join(
            [Environment]::NewLine,
            @($relayText, 'Relay Process: CompletionTimeout'))
    }

    Write-Output '=== Nova Relay ==='
    Write-Output $relayText
    Write-Output '=== Nova Downstream Session ==='
    Write-Output $downstreamText

    $relayPassed =
        $relayExitCode -eq 0 -and
        -not $relayCompletionTimedOut -and
        $relayText -match '(?m)^Acceptance Gate: Passed\r?$'
    $downstreamPassed =
        $downstreamExitCode -eq 0 -and
        $downstreamText -match '(?m)^Stop Trigger: PacketLimit\r?$'

    if (-not ($relayPassed -and $downstreamPassed)) {
        Write-Output 'End-to-End Gate: Failed'
    }
    else {
        $gatePassed = $true
        Write-Output 'End-to-End Gate: Passed'
    }
}
finally {
    if ($null -ne $relayJob) {
        if ($relayJob.State -notin @('Completed', 'Failed', 'Stopped')) {
            Stop-Job -Job $relayJob -ErrorAction SilentlyContinue
        }

        Remove-Job -Job $relayJob -Force -ErrorAction SilentlyContinue
    }
}

if (-not $gatePassed) {
    exit 1
}
