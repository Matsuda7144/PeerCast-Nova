using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRecordingCandidateAdapterTests
{
    private const string ChannelIdText = "531DC8DFC7FB42928AC2C0A626517A87";
    private const string Source =
        "pcp://tracker.example:7144/channel/531DC8DFC7FB42928AC2C0A626517A87";

    [Fact]
    public void TryCreate_MapsOnlySelectedRowMetadataAndCanonicalIdentity()
    {
        var channel = new GenesisChannelItem(
            " selected station ",
            Source,
            Description: "Games - short description",
            BitrateKbps: 1280,
            Type: "FLV",
            ChannelId: ChannelIdText);

        Assert.True(GenesisRecordingCandidateAdapter.TryCreate(channel, out var adapted));
        Assert.NotNull(adapted);
        Assert.Equal(ChannelIdText, adapted.Candidate.ChannelId.ToString());
        Assert.Equal("selected station", adapted.Candidate.Metadata.Name);
        Assert.Equal("Games - short description", adapted.Candidate.Metadata.Description);
        Assert.Equal(1280, adapted.Candidate.Metadata.BitrateKbps);
        Assert.Equal("FLV", adapted.Candidate.Metadata.StreamType);
        Assert.Null(adapted.Candidate.Metadata.Genre);
        Assert.Null(adapted.Candidate.Metadata.ContentType);
        Assert.Null(adapted.Candidate.Metadata.ContactUrl);
        Assert.Equal("flv", adapted.Candidate.Metadata.StreamExtension);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("RAW")]
    [InlineData("WMV")]
    [InlineData("MP4")]
    [InlineData("video/x-flv")]
    [InlineData("../flv")]
    public void TryCreate_DoesNotInferExtensionFromUnmappedFormatLabels(
        string? streamType)
    {
        var channel = new GenesisChannelItem(
            "selected station",
            Source,
            Type: streamType,
            ChannelId: ChannelIdText);

        Assert.True(GenesisRecordingCandidateAdapter.TryCreate(channel, out var adapted));
        Assert.NotNull(adapted);
        Assert.Equal(streamType, adapted.Candidate.Metadata.StreamType);
        Assert.Null(adapted.Candidate.Metadata.StreamExtension);

        var pathProvider = new DefaultRecordingFilePathProvider(
            Path.GetTempPath(),
            static () => DateTimeOffset.UnixEpoch);
        var path = pathProvider.GetPath(
            RecordingSinkStartRequest.Manual(adapted.Candidate),
            new RecordingSessionId(Guid.ParseExact(
                "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                "N")));

        Assert.Equal(".bin", Path.GetExtension(path));
    }

    [Fact]
    public void TryCreate_RecognizesFlvTypeWithoutChangingStreamType()
    {
        var channel = new GenesisChannelItem(
            "selected station",
            Source,
            Type: " flv ",
            ChannelId: ChannelIdText);

        Assert.True(GenesisRecordingCandidateAdapter.TryCreate(channel, out var adapted));
        Assert.NotNull(adapted);
        Assert.Equal(" flv ", adapted.Candidate.Metadata.StreamType);
        Assert.Equal("flv", adapted.Candidate.Metadata.StreamExtension);
    }

    [Theory]
    [InlineData(null, Source)]
    [InlineData("", Source)]
    [InlineData("00000000000000000000000000000000", Source)]
    [InlineData("11111111111141118111111111111111", Source)]
    [InlineData(ChannelIdText, "https://tracker.example/channel/531DC8DFC7FB42928AC2C0A626517A87")]
    [InlineData(ChannelIdText, "pcp://tracker.example:7144/channel/11111111111141118111111111111111")]
    public void TryCreate_RejectsMissingMalformedOrMismatchedIdentity(
        string? channelId,
        string source)
    {
        var channel = new GenesisChannelItem(
            "station",
            source,
            ChannelId: channelId);

        Assert.False(GenesisRecordingCandidateAdapter.TryCreate(channel, out var adapted));
        Assert.Null(adapted);
    }

    [Fact]
    public void TryGetChannelId_RejectsInvalidSelectedIdentity()
    {
        Assert.False(GenesisRecordingCandidateAdapter.TryGetChannelId(
            new GenesisChannelItem("station", Source),
            out var missing));
        Assert.Null(missing);
        Assert.False(GenesisRecordingCandidateAdapter.TryGetChannelId(
            new GenesisChannelItem("station", Source, ChannelId: "not-a-guid"),
            out var malformed));
        Assert.Null(malformed);
    }
}
