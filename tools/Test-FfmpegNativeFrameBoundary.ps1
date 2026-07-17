param(
    [ValidateRange(1, 100)]
    [int]$Runs = 20,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\tests\Nova.Playback.Tests\Nova.Playback.Tests.csproj'
$filter = @(
    'FullyQualifiedName~PlaybackDecoderFfmpegNativeFrameBoundaryTests',
    'FullyQualifiedName~PlaybackDecoderFfmpegPacketFrameBoundaryTests',
    'FullyQualifiedName~PlaybackDecoderFfmpegCodecOpenTests',
    'FullyQualifiedName~PlaybackDecoderFfmpegContextLeaseTests'
) -join '|'

for ($run = 1; $run -le $Runs; $run++) {
    dotnet test $project `
        --configuration $Configuration `
        --no-build `
        --no-restore `
        --filter $filter `
        --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw 'FFmpeg native frame boundary failed on a bounded run.'
    }
}

Write-Host "FFmpeg native frame repetition gate passed: $Runs runs."
