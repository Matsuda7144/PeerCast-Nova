param(
    [ValidateRange(1, 100)]
    [int]$Runs = 20,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "..\tests\Nova.Playback.Tests\Nova.Playback.Tests.csproj"
$filter = "FullyQualifiedName~PlaybackDecoderBackendBoundaryTests"

$originalLib = $env:LIB
try {
    if ($env:LIB) {
        $env:LIB = (($env:LIB -split ";") | Where-Object {
            $_ -and (Test-Path -LiteralPath $_)
        }) -join ";"
    }

    for ($run = 1; $run -le $Runs; $run++) {
        Write-Host "Decoder backend boundary test run $run / $Runs"
        dotnet test $project `
            --configuration $Configuration `
            --filter $filter `
            --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw "Decoder backend boundary test run $run failed."
        }
    }
}
finally {
    $env:LIB = $originalLib
}

Write-Host "Decoder backend boundary repetition gate passed: $Runs runs."
