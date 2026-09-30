using System.Security.Cryptography;
using System.Text.Json;
using Xunit.Abstractions;

namespace PeerCast.Nova.Playback.Tests;

public sealed class EncodingPhysicalAcceptanceFactAttribute : FactAttribute
{
    public EncodingPhysicalAcceptanceFactAttribute()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000) ||
            !Environment.Is64BitProcess ||
            Environment.GetEnvironmentVariable("NOVA_ENCODING_PHYSICAL_RUN_ONCE") != "RUN-ONCE" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NOVA_ENCODING_ACCEPTANCE_ARTIFACT_DIRECTORY")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NOVA_FMP4_FFMPEG_TEST_RUNTIME")))
        {
            Skip = "Requires explicit one-run authorization, a semantically accepted synthetic encoding artifact, and verified Windows runtime.";
        }
    }
}

public sealed class MediaEncodingPhysicalPresentationAcceptanceTests(ITestOutputHelper output)
{
    [EncodingPhysicalAcceptanceFact]
    public async Task SyntheticEncodedFmp4PresentsThroughNovaWindowsOutput()
    {
        var directory = Environment.GetEnvironmentVariable("NOVA_ENCODING_ACCEPTANCE_ARTIFACT_DIRECTORY")!;
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        var root = manifest.RootElement;
        var head = root.GetProperty("head").GetString()!;
        Assert.Equal(40, head.Length);
        Assert.All(head, character => Assert.True(Uri.IsHexDigit(character)));
        Assert.Equal(Environment.GetEnvironmentVariable("NOVA_ENCODING_ACCEPTANCE_HEAD"), head);
        Assert.False(root.GetProperty("retainedEncodedFixtureBytesUsedAsEncoderOutput").GetBoolean());
        Assert.Equal("PASS", root.GetProperty("directVideoDecode").GetString());
        Assert.Equal("PASS", root.GetProperty("directAudioDecode").GetString());
        Assert.Equal("PASS", root.GetProperty("semanticReparse").GetString());
        var videoFrames = root.GetProperty("muxedNativeVideoFrames").GetInt32();
        var audioFrames = root.GetProperty("muxedNativeAudioFrames").GetInt32();
        Assert.Equal(96, videoFrames);
        Assert.Equal(151, audioFrames);

        var expected = new List<byte>();
        var init = VerifyComponent(Path.Combine(directory, "init.mp4"), root.GetProperty("init"));
        expected.AddRange(init);
        var fragmentCount = root.GetProperty("fragments").GetArrayLength();
        Assert.Equal(4, fragmentCount);
        foreach (var fragment in root.GetProperty("fragments").EnumerateArray())
        {
            var sequence = fragment.GetProperty("sequence").GetInt32();
            expected.AddRange(VerifyComponent(Path.Combine(directory, $"fragment-{sequence:D4}.m4s"), fragment));
        }

        var combined = VerifyComponent(Path.Combine(directory, "nova-raw-encoded-playback.mp4"),
            root.GetProperty("combined"));
        Assert.Equal(expected.ToArray(), combined);
        var hash = Convert.ToHexString(SHA256.HashData(combined));
        var runId = $"NOVA-RAW-ENCODE-PRESENT-{DateTimeOffset.UtcNow:yyyyMMdd'T'HHmmssfff'Z'}-" +
            Guid.NewGuid().ToString("N")[..8];
        output.WriteLine($"Generation-ID: {root.GetProperty("generationId").GetString()}");
        output.WriteLine("Retained encoded fixture bytes used as encoder output: NO");
        await new PlaybackFmp4PhysicalPresentationAcceptanceTests(output)
            .PresentAsync(combined, hash, head, runId, videoFrames, audioFrames, fragmentCount);
    }

    private static byte[] VerifyComponent(string path, JsonElement metadata)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(metadata.GetProperty("length").GetInt32(), bytes.Length);
        Assert.Equal(metadata.GetProperty("sha256").GetString(),
            Convert.ToHexString(SHA256.HashData(bytes)));
        return bytes;
    }
}
