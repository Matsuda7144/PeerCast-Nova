$ErrorActionPreference = "Stop"

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string[]] $Arguments
    )

    & dotnet @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Remove-InvalidLibrarySearchPaths {
    param(
        [AllowNull()]
        [string] $Value
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

        Write-Warning "Ignoring missing LIB search path for this build: $path"
    }

    return $validPaths -join ';'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK was not found on PATH. Install the .NET 10 SDK and open a new PowerShell session."
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$originalLib = $env:LIB
Push-Location $repoRoot

try {
    # CS1668 is caused by stale machine/user LIB entries, not by Nova source.
    # Keep valid entries, remove only missing directories for this process,
    # and restore the original environment in the finally block.
    $env:LIB = Remove-InvalidLibrarySearchPaths $originalLib

    Invoke-DotNet @("restore", "PeerCast-Nova.sln")
    Invoke-DotNet @(
        "build",
        "PeerCast-Nova.sln",
        "--configuration", "Release",
        "--no-restore"
    )
    Invoke-DotNet @(
        "test",
        "PeerCast-Nova.sln",
        "--configuration", "Release",
        "--no-build",
        "--logger", "console;verbosity=normal"
    )
    Invoke-DotNet @(
        "run",
        "--project", "src/Nova.Cli/Nova.Cli.csproj",
        "--configuration", "Release",
        "--no-build",
        "--",
        "--transport-self-test",
        "--pcp-framing-self-test"
    )
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
