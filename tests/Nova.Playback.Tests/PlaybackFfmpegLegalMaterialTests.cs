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
        var lgplPath = Path.Combine(legalDirectory, "COPYING.LGPLv3");
        var gplArchivePath = Path.Combine(legalDirectory, "COPYING.GPLv3.gz");

        Assert.True(File.Exists(lgplPath));
        Assert.True(File.Exists(gplArchivePath));

        var lgplBytes = await File.ReadAllBytesAsync(lgplPath);
        Assert.Equal(7_652, lgplBytes.Length);
        Assert.Equal(LgplSha256, Convert.ToHexString(SHA256.HashData(lgplBytes)));
        Assert.Contains(
            "GNU LESSER GENERAL PUBLIC LICENSE",
            System.Text.Encoding.UTF8.GetString(lgplBytes),
            StringComparison.Ordinal);

        await using var archive = File.OpenRead(gplArchivePath);
        await using var gzip = new GZipStream(archive, CompressionMode.Decompress);
        await using var decoded = new MemoryStream();
        await gzip.CopyToAsync(decoded);
        var gplBytes = decoded.ToArray();

        Assert.Equal(35_149, gplBytes.Length);
        Assert.Equal(GplSha256, Convert.ToHexString(SHA256.HashData(gplBytes)));
        var gplText = System.Text.Encoding.UTF8.GetString(gplBytes);
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", gplText, StringComparison.Ordinal);
        Assert.Contains("END OF TERMS AND CONDITIONS", gplText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Notice_and_replacement_instructions_are_prominent_and_complete()
    {
        var legalDirectory = GetLegalDirectory();
        var notice = await File.ReadAllTextAsync(Path.Combine(legalDirectory, "NOTICE.md"));
        var replacement = await File.ReadAllTextAsync(
            Path.Combine(legalDirectory, "RUNTIME-REPLACEMENT.md"));
        var sums = await File.ReadAllTextAsync(Path.Combine(legalDirectory, "SHA256SUMS"));

        Assert.Contains("FFmpeg External Runtime Notice", notice, StringComparison.Ordinal);
        Assert.Contains("LGPL-3.0-or-later", notice, StringComparison.Ordinal);
        Assert.Contains("does not bundle", notice, StringComparison.Ordinal);
        Assert.Contains("reverse engineering", notice, StringComparison.Ordinal);
        Assert.Contains("COPYING.GPLv3.gz", notice, StringComparison.Ordinal);

        Assert.Contains(
            "PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied",
            replacement,
            StringComparison.Ordinal);
        Assert.Contains("avcodec-62.dll", replacement, StringComparison.Ordinal);
        Assert.Contains("avformat-62.dll", replacement, StringComparison.Ordinal);
        Assert.Contains("avutil-60.dll", replacement, StringComparison.Ordinal);
        Assert.Contains("swresample-6.dll", replacement, StringComparison.Ordinal);
        Assert.Contains("swscale-9.dll", replacement, StringComparison.Ordinal);
        Assert.Contains("AllowBundled = false", replacement, StringComparison.Ordinal);

        Assert.Contains(GplSha256, sums, StringComparison.Ordinal);
        Assert.Contains(LgplSha256, sums, StringComparison.Ordinal);
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
