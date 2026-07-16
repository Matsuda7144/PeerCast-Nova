param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{32}$')]
    [string] $ChannelId,

    [string] $HostName = '127.0.0.1',

    [ValidateRange(1, 65535)]
    [int] $Port = 7144,

    [ValidateRange(0, [long]::MaxValue)]
    [long] $Position = 0,

    [ValidateRange(1, 65535)]
    [int] $ListenPort = 17145,

    [ValidateRange(1000, 600000)]
    [int] $DurationMilliseconds = 30000,

    [ValidateRange(1, 1000000)]
    [int] $MinimumPackets = 4,

    [ValidateRange(1, 600000)]
    [int] $IdleTimeoutMilliseconds = 15000,

    [ValidateRange(0, 10)]
    [int] $Reconnects = 2
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$cliProject = Join-Path $repoRoot 'src/Nova.Cli/Nova.Cli.csproj'
$cliAssembly = Join-Path $repoRoot 'src/Nova.Cli/bin/Release/net10.0/PeerCast.Nova.Cli.dll'

if (-not (Test-Path -LiteralPath $cliAssembly -PathType Leaf)) {
    throw 'Release CLI not found. Run build.ps1 before the relay diagnostic.'
}

$arguments = @(
    'run',
    '--project', $cliProject,
    '--configuration', 'Release',
    '--no-build',
    '--',
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

& dotnet @arguments

if ($LASTEXITCODE -ne 0) {
    throw "Relay diagnostic failed its acceptance gate with exit code $LASTEXITCODE."
}
