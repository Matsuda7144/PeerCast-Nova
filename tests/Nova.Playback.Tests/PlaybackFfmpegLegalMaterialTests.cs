using System.IO.Compression;
using System.Security.Cryptography;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFfmpegLegalMaterialTests
{
    private const string GplSha256 =
        "3972DC9744F6499F0F9B2DBF76696F2AE7AD8AF9B23DDE66D6AF86C9DFB36986";
    private const string LgplSha256 =
        "E3A994D82E644B03A792A930F574002658412F62407F5FEE083F2555C5F23118";

    [Fact]
    public async Task Included_GNU_license_material_has_fixed_verbatim_text_hashes()
    {
        var legalDirectory = GetLegalDirectory();
        var gplPath = Path.Combine(legalDirectory, "COPYING.GPLv3");
        var gplArchivePath = Path.Combine(legalDirectory, "COPYING.GPLv3.gz");
        var lgplPath = Path.Combine(legalDirectory, "COPYING.LGPLv3");

        Assert.True(File.Exists(gplPath));
        Assert.True(File.Exists(gplArchivePath));
        Assert.True(File.Exists(lgplPath));

        var gplBytes = await File.ReadAllBytesAsync(gplPath);
        Assert.Equal(35_149, gplBytes.Length);
        Assert.Equal(GplSha256, Convert.ToHexString(SHA256.HashData(gplBytes)));
        var gplText = System.Text.Encoding.UTF8.GetString(gplBytes);
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", gplText, StringComparison.Ordinal);
        Assert.Contains("END OF TERMS AND CONDITIONS", gplText, StringComparison.Ordinal);

        await using var archive = File.OpenRead(gplArchivePath);
        await using var gzip = new GZipStream(archive, CompressionMode.Decompress);
        await using var decoded = new MemoryStream();
        await gzip.CopyToAsync(decoded);
        Assert.Equal(gplBytes, decoded.ToArray());

        var lgplBytes = await File.ReadAllBytesAsync(lgplPath);
        Assert.Equal(7_652, lgplBytes.Length);
        Assert.Equal(LgplSha256, Convert.ToHexString(SHA256.HashData(lgplBytes)));
        Assert.Contains(
            "GNU LESSER GENERAL PUBLIC LICENSE",
            System.Text.Encoding.UTF8.GetString(lgplBytes),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Notice_and_replacement_instructions_separate_review_from_host_attestation()
    {
        var legalDirectory = GetLegalDirectory();
        var notice = await File.ReadAllTextAsync(Path.Combine(legalDirectory, "NOTICE.md"));
        var replacement = await File.ReadAllTextAsync(
            Path.Combine(legalDirectory, "RUNTIME-REPLACEMENT.md"));
        var sums = await File.ReadAllTextAsync(Path.Combine(legalDirectory, "SHA256SUMS"));

        Assert.Contains("FFmpeg External Runtime Notice", notice, StringComparison.Ordinal);
        Assert.Contains("Host-supplied", notice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("host declaration and host attestation", notice, StringComparison.Ordinal);
        Assert.Contains("Unreviewed", notice, StringComparison.Ordinal);
        Assert.Contains("does not bundle", notice, StringComparison.Ordinal);
        Assert.Contains("reverse engineering", notice, StringComparison.Ordinal);
        Assert.Contains("trademark of Fabrice Bellard", notice, StringComparison.Ordinal);
        Assert.Contains("COPYING.GPLv3", notice, StringComparison.Ordinal);

        Assert.Contains(
            "PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied",
            replacement,
            StringComparison.Ordinal);
        Assert.Contains(
            "RequiredDeclaredLicenseIdentifier",
            replacement,
            StringComparison.Ordinal);
        Assert.Contains("AllowHostAttestedRuntime = true", replacement, StringComparison.Ordinal);
        Assert.Contains("avcodec-62.dll", replacement, StringComparison.Ordinal);
        Assert.Contains("avformat-62.dll", replacement, StringComparison.Ordinal);
        Assert.Contains("avutil-60.dll", replacement, StringComparison.Ordinal);
        Assert.Contains("swresample-6.dll", replacement, StringComparison.Ordinal);
        Assert.Contains("swscale-9.dll", replacement, StringComparison.Ordinal);
        Assert.Contains("AllowBundled = false", replacement, StringComparison.Ordinal);

        Assert.Contains($"{GplSha256}  COPYING.GPLv3", sums, StringComparison.Ordinal);
        Assert.Contains(LgplSha256, sums, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provenance_records_exact_release_and_missing_transcript_gate()
    {
        var provenance = await File.ReadAllTextAsync(
            Path.Combine(GetLegalDirectory(), "PROVENANCE.md"));

        Assert.Contains("autobuild-2026-07-15-14-01", provenance, StringComparison.Ordinal);
        Assert.Contains(
            "1f74efed63f467dbf0d1e5dd8548bf2188f4ad21",
            provenance,
            StringComparison.Ordinal);
        Assert.Contains(
            "94138f6973dd1ac6208ace92148ac0d172455d65",
            provenance,
            StringComparison.Ordinal);
        Assert.Contains(
            "F711E0478C03FD48624C59A0487E0C5D904779A3C67280A0A7B49B0002F12B43",
            provenance,
            StringComparison.Ordinal);
        Assert.Contains("ffmpeg.exe -buildconf", provenance, StringComparison.Ordinal);
        Assert.Contains("ffmpeg.exe -L", provenance, StringComparison.Ordinal);
        Assert.Contains("did not retain verbatim transcripts", provenance, StringComparison.Ordinal);
        Assert.Contains("remains `Pending`", provenance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Distribution_guard_rejects_conflicting_reverse_engineering_terms()
    {
        var guard = await File.ReadAllTextAsync(
            Path.Combine(GetLegalDirectory(), "NOVA-DISTRIBUTION-GUARD.md"));

        Assert.Contains("must not be published", guard, StringComparison.Ordinal);
        Assert.Contains("reverse engineering", guard, StringComparison.Ordinal);
        Assert.Contains("decompile", guard, StringComparison.Ordinal);
        Assert.Contains("disassemble", guard, StringComparison.Ordinal);
        Assert.Contains("translated", guard, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_build_and_publish_copy_the_complete_legal_directory()
    {
        var root = FindRepositoryRoot();
        var project = await File.ReadAllTextAsync(
            Path.Combine(root, "src", "Nova.Cli", "Nova.Cli.csproj"));

        Assert.Contains(
            "THIRD-PARTY-LICENSES\\FFmpeg\\**\\*",
            project,
            StringComparison.Ordinal);
        Assert.Contains(
            "CopyToOutputDirectory=\"PreserveNewest\"",
            project,
            StringComparison.Ordinal);
        Assert.Contains(
            "CopyToPublishDirectory=\"PreserveNewest\"",
            project,
            StringComparison.Ordinal);
    }

    private static string GetLegalDirectory() => Path.Combine(
        FindRepositoryRoot(),
        "THIRD-PARTY-LICENSES",
        "FFmpeg");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PeerCast-Nova.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The repository root could not be located.");
    }
}
