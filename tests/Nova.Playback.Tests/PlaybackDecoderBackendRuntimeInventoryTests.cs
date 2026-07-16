using System.Security.Cryptography;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderBackendRuntimeInventoryTests
{
    [Fact]
    public void PinnedInventory_ContainsOnlyRequiredSharedLibraries()
    {
        var files = PlaybackFfmpegBackendRuntimeInventory.PinnedCandidate.Files;

        Assert.Equal(5, files.Count);
        Assert.Equal([
            "avcodec-62.dll",
            "avformat-62.dll",
            "avutil-60.dll",
            "swresample-6.dll",
            "swscale-9.dll",
        ], files.Select(static file => file.Name));
        Assert.All(files, static file => Assert.Equal(64, file.Sha256.Length));
    }

    [Fact]
    public async Task Verify_ExactInventoryPassesWithoutClosingStreams()
    {
        var first = new byte[] { 1, 2, 3 };
        var second = new byte[] { 4, 5 };
        var inventory = CreateInventory(("first.dll", first), ("second.dll", second));
        var streams = new Dictionary<string, Stream>(StringComparer.Ordinal)
        {
            ["first.dll"] = new MemoryStream(first),
            ["second.dll"] = new MemoryStream(second),
        };

        var result = await PlaybackDecoderBackendRuntimeInventoryVerifier.VerifyAsync(
            inventory,
            streams);

        Assert.Equal(PlaybackDecoderBackendRuntimeInventoryVerification.Verified, result);
        Assert.All(streams.Values, static stream => Assert.True(stream.CanRead));
        foreach (var stream in streams.Values)
        {
            await stream.DisposeAsync();
        }
    }

    [Fact]
    public async Task Verify_MissingAndUnexpectedFilesReturnFixedResults()
    {
        var bytes = new byte[] { 1 };
        var inventory = CreateInventory(("required.dll", bytes));
        var missing = new Dictionary<string, Stream>();
        var unexpected = new Dictionary<string, Stream>
        {
            ["required.dll"] = new MemoryStream(bytes),
            ["extra.dll"] = new MemoryStream(bytes),
        };

        Assert.Equal(
            PlaybackDecoderBackendRuntimeInventoryVerification.MissingFile,
            await PlaybackDecoderBackendRuntimeInventoryVerifier.VerifyAsync(
                inventory,
                missing));
        Assert.Equal(
            PlaybackDecoderBackendRuntimeInventoryVerification.UnexpectedFile,
            await PlaybackDecoderBackendRuntimeInventoryVerifier.VerifyAsync(
                inventory,
                unexpected));

        foreach (var stream in unexpected.Values)
        {
            await stream.DisposeAsync();
        }
    }

    [Fact]
    public async Task Verify_SizeAndDigestMismatchReturnFixedResults()
    {
        var expected = new byte[] { 1, 2, 3 };
        var inventory = CreateInventory(("required.dll", expected));
        await using var shortStream = new MemoryStream([1, 2]);
        await using var changedStream = new MemoryStream([3, 2, 1]);

        Assert.Equal(
            PlaybackDecoderBackendRuntimeInventoryVerification.SizeMismatch,
            await PlaybackDecoderBackendRuntimeInventoryVerifier.VerifyAsync(
                inventory,
                new Dictionary<string, Stream> { ["required.dll"] = shortStream }));
        Assert.Equal(
            PlaybackDecoderBackendRuntimeInventoryVerification.DigestMismatch,
            await PlaybackDecoderBackendRuntimeInventoryVerifier.VerifyAsync(
                inventory,
                new Dictionary<string, Stream> { ["required.dll"] = changedStream }));
    }

    [Fact]
    public void Inventory_RejectsCaseVariantDuplicateNames()
    {
        var bytes = new byte[] { 1 };

        Assert.Throws<ArgumentException>(() =>
            CreateInventory(("runtime.dll", bytes), ("RUNTIME.DLL", bytes)));
    }

    [Fact]
    public async Task Verify_MatchesNamesUsingWindowsCaseRules()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var inventory = CreateInventory(("runtime.dll", bytes));
        await using var stream = new MemoryStream(bytes);

        var result = await PlaybackDecoderBackendRuntimeInventoryVerifier.VerifyAsync(
            inventory,
            new Dictionary<string, Stream> { ["RUNTIME.DLL"] = stream });

        Assert.Equal(PlaybackDecoderBackendRuntimeInventoryVerification.Verified, result);
    }

    private static PlaybackDecoderBackendRuntimeInventory CreateInventory(
        params (string Name, byte[] Bytes)[] files)
    {
        return new PlaybackDecoderBackendRuntimeInventory(files.Select(static file =>
            new PlaybackDecoderBackendRuntimeFile(
                file.Name,
                file.Bytes.Length,
                Convert.ToHexString(SHA256.HashData(file.Bytes)))));
    }
}
