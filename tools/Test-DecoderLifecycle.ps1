[CmdletBinding()]
param(
    [ValidateRange(1, 100)]
    [int]$Runs = 20,

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

        Write-Warning "Ignoring missing LIB search path for this test process: $path"
    }

    return $validPaths -join ';'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK was not found on PATH.'
}
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tests/Nova.Playback.Tests/Nova.Playback.Tests.csproj'
$filter = 'FullyQualifiedName~PlaybackDecoderRunnerTests'
$originalLib = $env:LIB

Push-Location $root
try {
    $env:LIB = Remove-InvalidLibrarySearchPaths $originalLib
    for ($run = 1; $run -le $Runs; $run++) {
        Write-Host "Decoder lifecycle test run $run / $Runs"
        & dotnet test $project `
            --configuration $Configuration `
            --no-restore `
            --filter $filter

        if ($LASTEXITCODE -ne 0) {
            throw "Decoder lifecycle tests failed on run $run."
        }
    }

    Write-Host "Decoder lifecycle repetition gate passed: $Runs runs."
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
