using System.Buffers.Binary;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisManualRecordingFlvFormatIntegrationTests : IDisposable
{
    private const string ChannelIdText = "531DC8DFC7FB42928AC2C0A626517A87";
    private const string Source =
        "pcp://tracker.example:7144/channel/531DC8DFC7FB42928AC2C0A626517A87";
    private const uint FirstLiveTimestamp = 13_487_800;
    private const uint FinalLiveTimestamp = 13_563_700;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "PeerCast-Nova-ManualRecording-FlvFormatIntegrationTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StartManual_FlvCatalogType_UsesFlvPathAndRebasesLiveTimeline()
    {
        var sourceBytes = BuildLiveTimelineFlv();
        var channel = new GenesisChannelItem(
            "たかし",
            Source,
            Type: "FLV",
            ChannelId: ChannelIdText);
        var sourceProvider = new FixtureSourceProvider(sourceBytes);
        await using var runtime = new RecordingRuntime(
            sourceProvider,
            new DefaultRecordingFilePathProvider(
                _directory,
                static () => new DateTimeOffset(
                    2026,
                    9,
                    29,
                    2,
                    19,
                    57,
                    TimeSpan.Zero)));
        var ownerProvider = new FixtureOwnerProvider(runtime);
        await using var controller = new GenesisManualRecordingSessionController(
            ownerProvider,
            runtime);

        Assert.True(GenesisRecordingCandidateAdapter.TryCreate(
            channel,
            out var adapted));
        Assert.NotNull(adapted);
        Assert.Equal("FLV", adapted.Candidate.Metadata.StreamType);
        Assert.Equal("flv", adapted.Candidate.Metadata.StreamExtension);

        var result = await controller.StartAsync(channel);

        Assert.Equal(GenesisManualRecordingCommandKind.Started, result.Kind);
        Assert.Equal(1, ownerProvider.AttachCount);
        Assert.Equal(1, sourceProvider.OpenCount);
        Assert.Equal("flv", sourceProvider.LastRequest!.Candidate.Metadata.StreamExtension);

        var path = await WaitForFinalRecordingAsync(_directory);
        Assert.Equal(".flv", Path.GetExtension(path));

        var recordedBytes = await File.ReadAllBytesAsync(path);
        Assert.True(recordedBytes.Length < sourceBytes.Length);
        Assert.Equal("FLV", System.Text.Encoding.ASCII.GetString(recordedBytes, 0, 3));

        var sourceTimestamps = ReadMediaTimestamps(sourceBytes);
        var recordedTimestamps = ReadMediaTimestamps(recordedBytes);
        Assert.Equal(
            new uint[]
            {
                FirstLiveTimestamp,
                FirstLiveTimestamp,
                FirstLiveTimestamp + 10,
                FinalLiveTimestamp - 10,
                FinalLiveTimestamp,
            },
            sourceTimestamps);
        Assert.Equal(new uint[] { 0, 75_880, 75_890 }, recordedTimestamps);
        Assert.Equal(75_890u, recordedTimestamps[^1] - recordedTimestamps[0]);
        Assert.True(recordedTimestamps[0] < 1_000);
        Assert.DoesNotContain(FirstLiveTimestamp, recordedTimestamps);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static async Task<string> WaitForFinalRecordingAsync(string directory)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();

            if (Directory.Exists(directory))
            {
                var recordings = Directory.GetFiles(directory, "*.flv");
                if (recordings.Length == 1)
                {
                    return recordings[0];
                }
            }

            await Task.Delay(10, timeout.Token);
        }
    }

    private static byte[] BuildLiveTimelineFlv()
    {
        var header = new byte[]
        {
            (byte)'F', (byte)'L', (byte)'V', 1, 0x05,
            0, 0, 0, 9,
            0, 0, 0, 0,
        };
        var tags = new[]
        {
            CreateTag(8, 0, [0xAF, 0, 0x12, 0x10]),
            CreateTag(9, 0, AvcConfigurationBody()),
            CreateTag(8, FirstLiveTimestamp, [0xAF, 1, 0x11, 0x22]),
            CreateTag(9, FirstLiveTimestamp, AvcNaluBody(isIdr: false)),
            CreateTag(9, FirstLiveTimestamp + 10, AvcNaluBody(isIdr: true)),
            CreateTag(8, FinalLiveTimestamp - 10, [0xAF, 1, 0x33, 0x44]),
            CreateTag(9, FinalLiveTimestamp, AvcNaluBody(isIdr: false)),
        };

        return [.. header, .. tags.SelectMany(static tag => tag)];
    }

    private static byte[] AvcConfigurationBody() =>
    [
        0x17, 0, 0, 0, 0,
        1, 0x64, 0, 0x1F, 0xFF, 0xE1,
        0, 4, 0x67, 0x64, 0, 0x1F,
        1, 0, 2, 0x68, 0xEE,
    ];

    private static byte[] AvcNaluBody(bool isIdr) =>
    [
        isIdr ? (byte)0x17 : (byte)0x27,
        1, 0, 0, 0,
        0, 0, 0, 2,
        isIdr ? (byte)0x65 : (byte)0x41,
        0x91,
    ];

    private static byte[] CreateTag(byte type, uint timestamp, byte[] body)
    {
        var tag = new byte[11 + body.Length + 4];
        tag[0] = type;
        tag[1] = (byte)(body.Length >> 16);
        tag[2] = (byte)(body.Length >> 8);
        tag[3] = (byte)body.Length;
        tag[4] = (byte)(timestamp >> 16);
        tag[5] = (byte)(timestamp >> 8);
        tag[6] = (byte)timestamp;
        tag[7] = (byte)(timestamp >> 24);
        body.CopyTo(tag, 11);
        BinaryPrimitives.WriteUInt32BigEndian(
            tag.AsSpan(11 + body.Length, 4),
            checked((uint)(11 + body.Length)));
        return tag;
    }

    private static uint[] ReadMediaTimestamps(byte[] bytes)
    {
        Assert.True(bytes.Length >= 13);
        Assert.Equal("FLV", System.Text.Encoding.ASCII.GetString(bytes, 0, 3));
        var timestamps = new List<uint>();
        var offset = 13;
        while (offset < bytes.Length)
        {
            Assert.True(bytes.Length - offset >= 15);
            var tagType = bytes[offset];
            var dataSize = (bytes[offset + 1] << 16) |
                (bytes[offset + 2] << 8) |
                bytes[offset + 3];
            var timestamp = ((uint)bytes[offset + 7] << 24) |
                ((uint)bytes[offset + 4] << 16) |
                ((uint)bytes[offset + 5] << 8) |
                bytes[offset + 6];
            var next = checked(offset + 11 + dataSize + 4);
            Assert.True(next <= bytes.Length);
            Assert.Equal(0, bytes[offset + 8] | bytes[offset + 9] | bytes[offset + 10]);
            Assert.Equal(
                checked((uint)(11 + dataSize)),
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 11 + dataSize, 4)));

            if (tagType is 8 or 9 && bytes[offset + 12] == 1)
            {
                timestamps.Add(timestamp);
            }

            offset = next;
        }

        Assert.Equal(bytes.Length, offset);
        return [.. timestamps];
    }

    private sealed class FixtureSourceProvider(byte[] bytes) :
        IRecordingStreamSourceProvider
    {
        public int OpenCount { get; private set; }

        public RecordingSinkStartRequest? LastRequest { get; private set; }

        public ValueTask<Stream> OpenReadAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCount++;
            LastRequest = request;
            return ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }
    }

    private sealed class FixtureOwnerProvider(RecordingRuntime recording) :
        IGenesisSharedPcpRecordingOwnerProvider
    {
        public int AttachCount { get; private set; }

        public Task<GenesisSharedPcpRecordingOwnerAttachment> AttachRecordingOwnerAsync(
            DailyPcpPlaybackSource source,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(ChannelIdText, source.ChannelId.ToString());
            AttachCount++;
            return Task.FromResult(new GenesisSharedPcpRecordingOwnerAttachment(
                recording,
                Task.FromResult(PcpRelayCoordinatorReadinessOutcome.Ready),
                Task.FromResult<PcpRelayCoordinatorResult>(null!),
                NoopAsyncDisposable.Instance));
        }
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public static NoopAsyncDisposable Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
