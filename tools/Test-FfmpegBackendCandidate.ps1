param(
    [Parameter(Mandatory = $true)]
    [string]$ArchivePath
)

$ErrorActionPreference = "Stop"
$expectedName = "ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip"
$expectedHash = "F711E0478C03FD48624C59A0487E0C5D904779A3C67280A0A7B49B0002F12B43"
$expectedVersion = "n8.1.2-22-g94138f6973-20260715"

$archive = Get-Item -LiteralPath $ArchivePath
if ($archive.Name -ne $expectedName) {
    throw "Unexpected candidate archive name."
}

$actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive.FullName).Hash
if ($actualHash -ne $expectedHash) {
    throw "Candidate archive SHA-256 mismatch."
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    Expand-Archive -LiteralPath $archive.FullName -DestinationPath $temporaryRoot
    $ffmpeg = Get-ChildItem $temporaryRoot -Recurse -File -Filter ffmpeg.exe |
        Select-Object -First 1
    if (-not $ffmpeg) {
        throw "Candidate archive does not contain ffmpeg.exe."
    }

    $versionOutput = & $ffmpeg.FullName -version 2>&1
    if ($LASTEXITCODE -ne 0 -or $versionOutput[0] -notmatch [regex]::Escape($expectedVersion)) {
        throw "Candidate FFmpeg version mismatch."
    }

    $configuration = ($versionOutput | Where-Object { $_ -like "configuration:*" }) -join " "
    foreach ($required in @("--enable-shared", "--disable-static", "--enable-version3")) {
        if ($configuration -notmatch [regex]::Escape($required)) {
            throw "Candidate FFmpeg configuration is missing a required flag."
        }
    }

    foreach ($forbidden in @("--enable-gpl", "--enable-nonfree")) {
        if ($configuration -match [regex]::Escape($forbidden)) {
            throw "Candidate FFmpeg configuration contains a forbidden flag."
        }
    }

    $demuxers = (& $ffmpeg.FullName -hide_banner -demuxers 2>&1) -join "`n"
    if ($demuxers -notmatch "(?m)^\s+D\s+flv\s" -or
        $demuxers -notmatch "(?m)^\s+D\s+live_flv\s") {
        throw "Candidate FFmpeg build lacks required FLV demuxers."
    }

    $license = Get-ChildItem $temporaryRoot -Recurse -File -Filter LICENSE.txt |
        Select-Object -First 1
    if (-not $license -or ((Get-Content $license.FullName -First 3) -join " ") -notmatch
        "GNU LESSER GENERAL PUBLIC LICENSE") {
        throw "Candidate license evidence is missing."
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolvedTemporaryRoot = (Resolve-Path -LiteralPath $temporaryRoot).Path
        $resolvedSystemTemp = [System.IO.Path]::GetFullPath(
            [System.IO.Path]::GetTempPath()).TrimEnd('\')
        if ([System.IO.Path]::GetDirectoryName($resolvedTemporaryRoot) -ne
            $resolvedSystemTemp) {
            throw "Refusing to remove a temporary path outside the system temp directory."
        }

        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}

Write-Host "FFmpeg backend candidate verification passed."
