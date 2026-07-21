using System.Security.Cryptography;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderBackendRuntimeInventoryTests
{
    [Fact]
    public void PinnedInventory_ContainsExactReviewedBinInventory()
    {
        var files = PlaybackFfmpegBackendRuntimeInventory.PinnedCandidate.Files;

        Assert.Equal(10, files.Count);
        Assert.Equal([
            "avcodec-62.dll",
            "avdevice-62.dll",
            "avfilter-11.dll",
            "avformat-62.dll",
            "avutil-60.dll",
            "ffmpeg.exe",
            "ffplay.exe",
            "ffprobe.exe",
            "swresample-6.dll",
            "swscale-9.dll",
        ], files.Select(static file => file.Name));
        Assert.All(files, static file => Assert.Equal(64, file.Sha256.Length));

        var byName = files.ToDictionary(static file => file.Name, StringComparer.OrdinalIgnoreCase);
        AssertFile(byName, "avcodec-62.dll", 70_875_136, "4936D6DD5C1615AC4166017A9843F4D8DC415EF6D3156A4138B84C2A4419818C");
        AssertFile(byName, "avdevice-62.dll", 3_699_712, "3C19667DAAB10CB42C0870D8199DA87AD64CFDFC692D00C5FD1D2191E495EBA8");
        AssertFile(byName, "avfilter-11.dll", 29_816_320, "FF4E9FF9ACB4B051D73A2561FDF724DE6295D60374DF5D309A714ADF699343D2");
        AssertFile(byName, "avformat-62.dll", 22_070_784, "198B582EE199BA16C973A438FA125A2448772755BC2E95CC01B28AB434270290");
        AssertFile(byName, "avutil-60.dll", 2_937_856, "419513335F9209499243C3C7F2EA46C0B3F477281D80DE90E696E2E1062D7452");
        AssertFile(byName, "ffmpeg.exe", 542_208, "02E08DE9538CFA13EDD8E4FD81F897C2308594DCBEB22FE3A7AD6E1A5937FA2A");
        AssertFile(byName, "ffplay.exe", 17_902_592, "2657F43E5C459B1655DA778922FE69C85C280D9419237AB3A146BA7835FC9758");
        AssertFile(byName, "ffprobe.exe", 227_840, "2CC6C3D9C49028355E030B46208AC0E630A2A7A84381659D935183B1EA205E47");
        AssertFile(byName, "swresample-6.dll", 723_968, "344CA8558DCED8431D54E692A171F5167510E940E860292F33CD77460DB0E83B");
        AssertFile(byName, "swscale-9.dll", 12_570_624, "7EBE9B9B4B8B23DA6E86631573E85AE484392CB7B3D0ECF506F621BDC1222229");
    }

    [Fact]
    public void New_manifest_rejects_superseded_candidate_digests()
    {
        var current = PlaybackFfmpegBackendRuntimeInventory.PinnedCandidate.Files
            .ToDictionary(static file => file.Name, StringComparer.OrdinalIgnoreCase);
        var superseded = PlaybackFfmpegBackendRuntimeInventory.SupersededCandidate20260715.Files;

        Assert.Equal(5, superseded.Count);
        Assert.All(superseded, file =>
        {
            Assert.True(current.TryGetValue(file.Name, out var approved));
            Assert.NotEqual(file.Sha256, approved!.Sha256);
        });
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

    private static void AssertFile(
        Dictionary<string, PlaybackDecoderBackendRuntimeFile> files,
        string name,
        long length,
        string sha256)
    {
        var file = files[name];
        Assert.Equal(length, file.Length);
        Assert.Equal(sha256, file.Sha256);
    }
}
