param(
    [ValidateRange(1, 100)]
    [int]$Runs = 20,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\tests\Nova.Playback.Tests\Nova.Playback.Tests.csproj'
$filter = @(
    'FullyQualifiedName~PlaybackDecoderFfmpegParameterTransferTests',
    'FullyQualifiedName~PlaybackDecoderFfmpegCodecParametersTests',
    'FullyQualifiedName~PlaybackDecoderFfmpegContextLeaseTests',
    'FullyQualifiedName~PlaybackDecoderFfmpegAbiSymbolGateTests'
) -join '|'

$originalLib = $env:LIB
try {
    $env:LIB = (($env:LIB -split ';') | Where-Object {
        $_ -and (Test-Path -LiteralPath $_)
    }) -join ';'

    for ($run = 1; $run -le $Runs; $run++) {
        dotnet test $project `
            --configuration $Configuration `
            --no-build `
            --no-restore `
            --filter $filter `
            --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            throw "FFmpeg native parameter transfer failed on a bounded run."
        }
    }
}
finally {
    $env:LIB = $originalLib
}

Write-Host "FFmpeg native parameter transfer repetition gate passed: $Runs runs."
