[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9A-Fa-f]{32}$')]
    [string]$ChannelId,

    [ValidateNotNullOrEmpty()]
    [string]$HostName = '127.0.0.1',

    [ValidateRange(1, 65535)]
    [int]$Port = 7144,

    [ValidateRange(1, 1000000)]
    [int]$InputLimit = 4,

    [ValidateRange(1000, 120000)]
    [int]$DurationMilliseconds = 15000,

    [ValidateRange(1000, 120000)]
    [int]$IdleTimeoutMilliseconds = 10000,

    [ValidateRange(10, 30000)]
    [int]$DecoderStopTimeoutMilliseconds = 2000,

    [ValidateRange(0, 10)]
    [int]$Reconnects = 0,

    [ValidateRange(16, 1048576)]
    [int]$MaxProbeBytes = 65536,

    [ValidateRange(0, 64)]
    [int]$MaxDiscontinuities = 4,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

function Remove-InvalidLibrarySearchPaths {
    param(
        [AllowNull()]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $Value
    }

    $validPaths = [System.Collections.Generic.List[string]]::new()

    foreach ($entry in $Value -split ';') {
        $path = $entry.Trim()

        if ([string]::IsNullOrWhiteSpace($path)) {
            continue
        }

        if (Test-Path -LiteralPath $path -PathType Container) {
            $validPaths.Add($path)
            continue
        }

        Write-Warning "Ignoring missing LIB search path for this probe process: $path"
    }

    return $validPaths -join ';'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK was not found on PATH.'
}

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/Nova.Cli/Nova.Cli.csproj'
$originalLib = $env:LIB
$arguments = @(
    'run',
    '--project', $project,
    '--configuration', $Configuration,
    '--no-restore',
    '--',
    '--playback-decoder-lifecycle-probe',
    '--host', $HostName,
    '--port', $Port.ToString([Globalization.CultureInfo]::InvariantCulture),
    '--channel', $ChannelId,
    '--playback-lifecycle-inputs', $InputLimit.ToString([Globalization.CultureInfo]::InvariantCulture),
    '--playback-lifecycle-duration-ms', $DurationMilliseconds.ToString([Globalization.CultureInfo]::InvariantCulture),
    '--playback-idle-timeout-ms', $IdleTimeoutMilliseconds.ToString([Globalization.CultureInfo]::InvariantCulture),
    '--playback-decoder-stop-timeout-ms', $DecoderStopTimeoutMilliseconds.ToString([Globalization.CultureInfo]::InvariantCulture),
    '--playback-reconnects', $Reconnects.ToString([Globalization.CultureInfo]::InvariantCulture),
    '--playback-probe-max-bytes', $MaxProbeBytes.ToString([Globalization.CultureInfo]::InvariantCulture),
    '--playback-probe-max-discontinuities', $MaxDiscontinuities.ToString([Globalization.CultureInfo]::InvariantCulture)
)

Push-Location $root
try {
    $env:LIB = Remove-InvalidLibrarySearchPaths $originalLib

    & dotnet @arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Decoder lifecycle probe failed with exit code $LASTEXITCODE."
    }
}
finally {
    if ($null -eq $originalLib) {
        Remove-Item Env:LIB -ErrorAction SilentlyContinue
    }
    else {
        $env:LIB = $originalLib
    }

    Pop-Location
}
