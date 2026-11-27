[CmdletBinding()]
param(
    [string]$Version = "",
    [switch]$BuildInstaller,
    [string]$OutputRoot = "artifacts"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Push-Location $repoRoot
try {
    if ([string]::IsNullOrWhiteSpace($Version)) {
        [xml]$props = Get-Content (Join-Path $repoRoot "Directory.Build.props")
        $Version = ($props.Project.PropertyGroup.VersionPrefix | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
    }

    if ([string]::IsNullOrWhiteSpace($Version)) {
        throw "Version could not be resolved from parameter or Directory.Build.props."
    }

    $releaseDir = Join-Path $repoRoot (Join-Path $OutputRoot "release")
    $symbolsDir = Join-Path $repoRoot (Join-Path $OutputRoot "symbols")
    $portableDir = Join-Path $repoRoot (Join-Path $OutputRoot "publish-portable")
    $selfContainedDir = Join-Path $repoRoot (Join-Path $OutputRoot "publish-self-contained")

    New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
    New-Item -ItemType Directory -Force -Path $symbolsDir | Out-Null

    Write-Host "Publishing PeerCast Nova $Version (WindowsPortable)..."
    dotnet publish "src/Nova.App/Nova.App.csproj" -p:PublishProfile=WindowsPortable -p:Version=$Version -o $portableDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $portablePdbs = Get-ChildItem -Path $portableDir -Filter "*.pdb" -Recurse
    if ($portablePdbs.Count -eq 0) {
        throw "Portable publish did not produce PDB files."
    }

    $portableZip = Join-Path $releaseDir "PeerCast-Nova-$Version-win-x64-portable.zip"
    Compress-Archive -Path (Join-Path $portableDir "*") -DestinationPath $portableZip -Force

    Write-Host "Publishing PeerCast Nova $Version (WindowsSelfContained)..."
    dotnet publish "src/Nova.App/Nova.App.csproj" -p:PublishProfile=WindowsSelfContained -p:Version=$Version -o $selfContainedDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $selfContainedPdbs = Get-ChildItem -Path $selfContainedDir -Filter "*.pdb" -Recurse
    if ($selfContainedPdbs.Count -gt 0) {
        foreach ($pdb in $selfContainedPdbs) {
            Move-Item -Path $pdb.FullName -Destination (Join-Path $symbolsDir $pdb.Name) -Force
        }

        $symbolsZip = Join-Path $releaseDir "PeerCast-Nova-$Version-win-x64-symbols.zip"
        Compress-Archive -Path (Join-Path $symbolsDir "*") -DestinationPath $symbolsZip -Force
    }

    $selfContainedZip = Join-Path $releaseDir "PeerCast-Nova-$Version-win-x64-self-contained.zip"
    Compress-Archive -Path (Join-Path $selfContainedDir "*") -DestinationPath $selfContainedZip -Force

    if ($BuildInstaller) {
        $iscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
        if ([string]::IsNullOrWhiteSpace($iscc)) {
            $defaultIscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
            if (Test-Path $defaultIscc) {
                $iscc = $defaultIscc
            }
        }

        if ([string]::IsNullOrWhiteSpace($iscc)) {
            throw "Inno Setup 6 (ISCC.exe) was not found."
        }

        $issPath = Join-Path $repoRoot "packaging\PeerCast-Nova.iss"
        Write-Host "Building installer via ISCC: $issPath"
        & $iscc "/DAppVersion=$Version" "/DPublishDir=$selfContainedDir" "/DOutputDir=$releaseDir" $issPath
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    $shaPath = Join-Path $releaseDir "SHA256SUMS.txt"
    $hashes = Get-ChildItem -Path $releaseDir -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" } | Sort-Object Name | ForEach-Object {
        $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($_.Name)"
    }
    $hashes | Out-File -FilePath $shaPath -Encoding utf8
    Write-Host "Release artifacts written to: $releaseDir"
}
finally {
    Pop-Location
}
