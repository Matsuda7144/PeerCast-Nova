param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{32}$')]
    [string] $ChannelId,

    [string] $HostName = '127.0.0.1',

    [ValidateRange(1, 65535)]
    [int] $Port = 7144,

    [ValidateRange(0, [long]::MaxValue)]
    [long] $Position = 0,

    [switch] $TraceAtoms,

    [switch] $ObservePostHandshake,

    [ValidateRange(1, 64)]
    [int] $ObserveAtoms = 4,

    [ValidateRange(1, 60000)]
    [int] $ObserveTimeoutMilliseconds = 2000,

    [ValidateRange(8, 16777216)]
    [int] $ObserveMaxAtomBytes = 262144
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$cliProject = Join-Path $repoRoot 'src/Nova.Cli/Nova.Cli.csproj'
$cliAssembly = Join-Path $repoRoot 'src/Nova.Cli/bin/Release/net10.0/PeerCast.Nova.Cli.dll'

if (-not (Test-Path -LiteralPath $cliAssembly -PathType Leaf)) {
    throw 'Release CLI not found. Run build.ps1 before the live probe.'
}

$arguments = @(
    'run',
    '--project', $cliProject,
    '--configuration', 'Release',
    '--no-build',
    '--',
    '--pcp-compatibility-probe',
    '--host', $HostName,
    '--port', $Port,
    '--channel', $ChannelId,
    '--position', $Position
)

if ($TraceAtoms) {
    $arguments += '--trace-atoms'
}

if ($ObservePostHandshake) {
    if (-not $TraceAtoms) {
        $arguments += '--trace-atoms'
    }

    $arguments += @(
        '--observe-post-handshake',
        '--observe-atoms', $ObserveAtoms,
        '--observe-timeout-ms', $ObserveTimeoutMilliseconds,
        '--observe-max-atom-bytes', $ObserveMaxAtomBytes
    )
}

& dotnet @arguments

if ($LASTEXITCODE -ne 0) {
    throw "Compatibility Probe failed with exit code $LASTEXITCODE."
}
