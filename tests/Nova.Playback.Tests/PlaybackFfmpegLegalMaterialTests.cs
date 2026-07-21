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
    public async Task Provenance_records_active_and_superseded_exact_identities()
    {
        var provenance = await File.ReadAllTextAsync(
            Path.Combine(GetLegalDirectory(), "PROVENANCE.md"));

        Assert.Contains("autobuild-2026-07-21-13-38", provenance, StringComparison.Ordinal);
        Assert.Contains(
            "8c736b2d6fe5da2a10a8896d01e53bfb0ca4f665",
            provenance,
            StringComparison.Ordinal);
        Assert.Contains(
            "703dcc25b91eacd2ab8b8b2fe888dc8d7ab4ad6d",
            provenance,
            StringComparison.Ordinal);
        Assert.Contains(
            "50764B52D38CB0BAF8AF938C0E4CA886D2C6753B520B2C319B532ED5C17E7CBF",
            provenance,
            StringComparison.Ordinal);
        Assert.Contains("70,510,808", provenance, StringComparison.Ordinal);
        Assert.Contains("223", provenance, StringComparison.Ordinal);
        Assert.Contains("autobuild-2026-07-15-14-01", provenance, StringComparison.Ordinal);
        Assert.Contains("Superseded", provenance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Candidate_transcripts_prove_required_lgpl_shared_configuration()
    {
        var legalDirectory = GetLegalDirectory();
        var buildconf = await File.ReadAllTextAsync(
            Path.Combine(legalDirectory, "ffmpeg-candidate-buildconf.txt"));
        var license = await File.ReadAllTextAsync(
            Path.Combine(legalDirectory, "ffmpeg-candidate-license.txt"));

        Assert.Contains("n8.1.2-29-g703dcc25b9-20260721", buildconf, StringComparison.Ordinal);
        Assert.Contains("--enable-shared", buildconf, StringComparison.Ordinal);
        Assert.Contains("--disable-static", buildconf, StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-gpl", buildconf, StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-nonfree", buildconf, StringComparison.Ordinal);
        Assert.Contains("GNU Lesser General Public License", license, StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-gpl", license, StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-nonfree", license, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_legal_evidence_contains_no_ffmpeg_binary_or_archive()
    {
        var legalDirectory = GetLegalDirectory();
        var forbiddenExtensions = new HashSet<string>(
            [".dll", ".exe", ".zip", ".7z", ".tar", ".xz"],
            StringComparer.OrdinalIgnoreCase);

        var forbidden = Directory.EnumerateFiles(legalDirectory, "*", SearchOption.AllDirectories)
            .Where(path => forbiddenExtensions.Contains(Path.GetExtension(path)))
            .ToArray();

        Assert.Empty(forbidden);
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
