param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{32}$')]
    [string] $ChannelId,

    [string] $HostName = '127.0.0.1',

    [ValidateRange(1, 65535)]
    [int] $Port = 7144,

    [ValidateRange(0, [long]::MaxValue)]
    [long] $Position = 0,

    [ValidateRange(1, 1000000)]
    [int] $PacketLimit = 4,

    [ValidateRange(1, 600000)]
    [int] $TimeoutMilliseconds = 10000,

    [ValidateRange(1, 600000)]
    [int] $IdleTimeoutMilliseconds = 5000,

    [ValidateRange(0, 10)]
    [int] $Reconnects = 0
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$cliProject = Join-Path $repoRoot 'src/Nova.Cli/Nova.Cli.csproj'
$cliAssembly = Join-Path $repoRoot 'src/Nova.Cli/bin/Release/net10.0/PeerCast.Nova.Cli.dll'

if (-not (Test-Path -LiteralPath $cliAssembly -PathType Leaf)) {
    throw 'Release CLI not found. Run build.ps1 before the live session probe.'
}

$arguments = @(
    'run',
    '--project', $cliProject,
    '--configuration', 'Release',
    '--no-build',
    '--',
    '--pcp-channel-session-probe',
    '--host', $HostName,
    '--port', $Port,
    '--channel', $ChannelId,
    '--position', $Position,
    '--session-packets', $PacketLimit,
    '--session-timeout-ms', $TimeoutMilliseconds,
    '--session-idle-timeout-ms', $IdleTimeoutMilliseconds,
    '--session-reconnects', $Reconnects
)

& dotnet @arguments

if ($LASTEXITCODE -ne 0) {
    throw "Channel Session Probe failed with exit code $LASTEXITCODE."
}
