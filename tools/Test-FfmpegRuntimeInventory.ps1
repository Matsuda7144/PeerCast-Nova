param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimeDirectory
)

$ErrorActionPreference = "Stop"
$expected = [ordered]@{
    "avcodec-62.dll" = @(70881792, "0C5904AE70E56FEDD9A48E3A51C085DE136AA673CDB8220CAEC834A81E24566E")
    "avformat-62.dll" = @(22070784, "0403E7473A2E2165CA80A31305B6FCC6CCDA2437482188E26EBD46858388637D")
    "avutil-60.dll" = @(2937856, "597FBF082124905EF7652247C09EDF226739C915DB51B686D45297C5D67F6D46")
    "swresample-6.dll" = @(723968, "1386CB8F61EA9C525B6D08A18A6AD71BF08311B95493963BD818E1DF888F209A")
    "swscale-9.dll" = @(12570624, "5FE2519E5CFDCDAB9815DE553BEB7A33B97D5DD4D9715AFB331F147EC2E44AD1")
}

$directory = Get-Item -LiteralPath $RuntimeDirectory
if (-not $directory.PSIsContainer) {
    throw "RuntimeDirectory must identify a directory."
}

$actualFiles = @(Get-ChildItem -LiteralPath $directory.FullName -File)
if ($actualFiles.Count -ne $expected.Count) {
    throw "Runtime inventory file count mismatch."
}

foreach ($entry in $expected.GetEnumerator()) {
    $file = Get-Item -LiteralPath (Join-Path $directory.FullName $entry.Key)
    if ($file.Length -ne [long]$entry.Value[0]) {
        throw "Runtime inventory size mismatch."
    }

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash
    if ($hash -ne $entry.Value[1]) {
        throw "Runtime inventory SHA-256 mismatch."
    }
}

Write-Host "FFmpeg runtime inventory verification passed."
