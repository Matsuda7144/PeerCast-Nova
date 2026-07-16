namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackMediaClassifierTests
{
    [Fact]
    public void FlashVideo_SplitHeaderIsRecognizedAndCleared()
    {
        using var classifier = new PlaybackMediaClassifier();

        var first = classifier.Observe("FL"u8);
        var result = classifier.Observe(
            [
                (byte)'V', 0x01, 0x05,
                0x00, 0x00, 0x00, 0x09,
            ]);

        Assert.Equal(
            PlaybackMediaClassificationState.Collecting,
            first.State);
        Assert.Equal(
            PlaybackMediaClassificationState.Recognized,
            result.State);
        Assert.Equal(PlaybackMediaContainer.FlashVideo, result.Container);
        Assert.Equal(
            PlaybackMediaClassificationBasis.FileHeader,
            result.Basis);
        Assert.Equal(0, result.RetainedPrefixByteCount);
    }

    [Fact]
    public void FlashVideo_InvalidReservedFlagsAreNotRecognized()
    {
        using var classifier = CreateSmallClassifier();
        byte[] candidate =
        [
            (byte)'F', (byte)'L', (byte)'V',
            0x01, 0x80,
            0x00, 0x00, 0x00, 0x09,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00,
        ];

        var result = classifier.Observe(candidate);

        Assert.Equal(
            PlaybackMediaClassificationState.LimitReached,
            result.State);
        Assert.Equal(PlaybackMediaContainer.Unknown, result.Container);
    }

    [Fact]
    public void Ogg_CapturePatternCanResynchronizeAfterLeadingBytes()
    {
        using var classifier = new PlaybackMediaClassifier();

        var result = classifier.Observe(
            [0xff, 0x00, 0x01, (byte)'O', (byte)'g', (byte)'g', (byte)'S', 0x00]);

        Assert.Equal(PlaybackMediaContainer.Ogg, result.Container);
        Assert.Equal(
            PlaybackMediaClassificationBasis.CapturePattern,
            result.Basis);
    }

    [Fact]
    public void Ogg_UnsupportedStreamVersionIsNotRecognized()
    {
        using var classifier = CreateSmallClassifier();

        var result = classifier.Observe(
            [
                (byte)'O', (byte)'g', (byte)'g', (byte)'S', 0x01,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
            ]);

        Assert.Equal(
            PlaybackMediaClassificationState.LimitReached,
            result.State);
        Assert.Equal(PlaybackMediaContainer.Unknown, result.Container);
    }

    [Fact]
    public void MpegTransportStream_RequiresThreeFixedIntervalSyncBytes()
    {
        using var classifier = new PlaybackMediaClassifier();
        var prefix = new byte[7 + (188 * 2) + 1];
        prefix[7] = 0x47;
        prefix[7 + 188] = 0x47;
        prefix[7 + (188 * 2)] = 0x47;

        var collecting = classifier.Observe(prefix.AsSpan(0, 200));
        var result = classifier.Observe(prefix.AsSpan(200));

        Assert.Equal(
            PlaybackMediaClassificationState.Collecting,
            collecting.State);
        Assert.Equal(
            PlaybackMediaContainer.MpegTransportStream,
            result.Container);
        Assert.Equal(
            PlaybackMediaClassificationBasis.RepeatedPacketSync,
            result.Basis);
    }

    [Fact]
    public void MpegTransportStream_TwoSyncBytesDoNotClassify()
    {
        using var classifier = new PlaybackMediaClassifier(
            new PlaybackMediaClassificationOptions
            {
                MaxProbeBytes = 400,
            });
        var prefix = new byte[400];
        prefix[0] = 0x47;
        prefix[188] = 0x47;

        var result = classifier.Observe(prefix);

        Assert.Equal(
            PlaybackMediaClassificationState.LimitReached,
            result.State);
        Assert.Equal(PlaybackMediaContainer.Unknown, result.Container);
    }

    [Fact]
    public void Asf_HeaderObjectGuidIsRecognized()
    {
        using var classifier = new PlaybackMediaClassifier();

        var result = classifier.Observe(
            [
                0x30, 0x26, 0xb2, 0x75,
                0x8e, 0x66, 0xcf, 0x11,
                0xa6, 0xd9, 0x00, 0xaa,
                0x00, 0x62, 0xce, 0x6c,
            ]);

        Assert.Equal(
            PlaybackMediaContainer.AdvancedSystemsFormat,
            result.Container);
        Assert.Equal(
            PlaybackMediaClassificationBasis.FileHeader,
            result.Basis);
    }

    [Theory]
    [InlineData("webm", PlaybackMediaContainer.WebM)]
    [InlineData("matroska", PlaybackMediaContainer.Matroska)]
    public void Ebml_DocumentTypeIsParsedWithinBoundedHeader(
        string documentType,
        PlaybackMediaContainer expected)
    {
        using var classifier = new PlaybackMediaClassifier();

        var result = classifier.Observe(CreateEbmlHeader(documentType));

        Assert.Equal(expected, result.Container);
        Assert.Equal(
            PlaybackMediaClassificationBasis.EbmlDocumentType,
            result.Basis);
    }

    [Fact]
    public void Ebml_UnknownDocumentTypeIsNotGuessed()
    {
        using var classifier = new PlaybackMediaClassifier(
            new PlaybackMediaClassificationOptions
            {
                MaxProbeBytes = 32,
            });
        var prefix = new byte[32];
        CreateEbmlHeader("nova").CopyTo(prefix, 0);

        var result = classifier.Observe(prefix);

        Assert.Equal(
            PlaybackMediaClassificationState.LimitReached,
            result.State);
        Assert.Equal(PlaybackMediaContainer.Unknown, result.Container);
    }

    [Fact]
    public void Discontinuity_ClearsPriorPrefixBeforeClassification()
    {
        using var classifier = new PlaybackMediaClassifier();
        _ = classifier.Observe([0xde, 0xad, 0xbe, 0xef]);

        var result = classifier.Observe(
            [
                (byte)'F', (byte)'L', (byte)'V',
                0x01, 0x05,
                0x00, 0x00, 0x00, 0x09,
            ],
            PlaybackDiscontinuityKind.LatencySkip);

        Assert.Equal(PlaybackMediaContainer.FlashVideo, result.Container);
        Assert.Equal(1, result.DiscontinuityCount);
        Assert.Equal(13, result.ExaminedByteCount);
    }

    [Fact]
    public void DiscontinuityLimit_TerminatesBeforeCopyingNewPayload()
    {
        using var classifier = new PlaybackMediaClassifier(
            new PlaybackMediaClassificationOptions
            {
                MaxDiscontinuities = 0,
            });

        var result = classifier.Observe(
            "FLV"u8,
            PlaybackDiscontinuityKind.Source);

        Assert.Equal(
            PlaybackMediaClassificationState.LimitReached,
            result.State);
        Assert.Equal(
            PlaybackMediaClassificationLimit.Discontinuities,
            result.Limit);
        Assert.Equal(0, result.ExaminedByteCount);
        Assert.Equal(0, result.RetainedPrefixByteCount);
    }

    [Fact]
    public void PrefixLimit_ClearsTransientContentAndBecomesTerminal()
    {
        using var classifier = CreateSmallClassifier();

        var result = classifier.Observe(new byte[32]);

        Assert.Equal(
            PlaybackMediaClassificationState.LimitReached,
            result.State);
        Assert.Equal(
            PlaybackMediaClassificationLimit.PrefixBytes,
            result.Limit);
        Assert.Equal(16, result.ExaminedByteCount);
        Assert.Equal(0, result.RetainedPrefixByteCount);
        Assert.Throws<InvalidOperationException>(
            () => classifier.Observe([0x00]));
    }

    [Fact]
    public void Complete_EndsUnknownWithoutRevealingPrefix()
    {
        using var classifier = new PlaybackMediaClassifier();
        _ = classifier.Observe([0x01, 0x02, 0x03]);

        var result = classifier.Complete();

        Assert.Equal(
            PlaybackMediaClassificationState.Ended,
            result.State);
        Assert.Equal(PlaybackMediaContainer.Unknown, result.Container);
        Assert.Equal(0, result.RetainedPrefixByteCount);
    }

    [Fact]
    public void Options_RejectInvalidResourceLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackMediaClassificationOptions
            {
                MaxProbeBytes =
                    PlaybackMediaClassificationOptions.MinimumProbeBytes - 1,
            }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackMediaClassificationOptions
            {
                MaxProbeBytes =
                    PlaybackMediaClassificationOptions.MaximumProbeBytesLimit + 1,
            }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackMediaClassificationOptions
            {
                MaxDiscontinuities = -1,
            }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackMediaClassificationOptions
            {
                MaxDiscontinuities =
                    PlaybackMediaClassificationOptions.MaximumDiscontinuitiesLimit + 1,
            }.Validate());
    }

    [Fact]
    public void Snapshot_ContainsOnlyFixedEnumsAndNumbers()
    {
        var allowedTypes = new HashSet<Type>
        {
            typeof(int),
            typeof(long),
            typeof(PlaybackMediaClassificationState),
            typeof(PlaybackMediaContainer),
            typeof(PlaybackMediaClassificationBasis?),
            typeof(PlaybackMediaClassificationLimit?),
        };

        var unexpected = typeof(PlaybackMediaClassificationSnapshot)
            .GetProperties()
            .Where(property => !allowedTypes.Contains(property.PropertyType))
            .Select(property => property.Name)
            .ToArray();

        Assert.Empty(unexpected);
    }

    private static PlaybackMediaClassifier CreateSmallClassifier()
    {
        return new PlaybackMediaClassifier(
            new PlaybackMediaClassificationOptions
            {
                MaxProbeBytes = 16,
            });
    }

    private static byte[] CreateEbmlHeader(string documentType)
    {
        var value = System.Text.Encoding.ASCII.GetBytes(documentType);
        var contentLength = 3 + value.Length;
        var result = new byte[5 + contentLength];
        result[0] = 0x1a;
        result[1] = 0x45;
        result[2] = 0xdf;
        result[3] = 0xa3;
        result[4] = checked((byte)(0x80 | contentLength));
        result[5] = 0x42;
        result[6] = 0x82;
        result[7] = checked((byte)(0x80 | value.Length));
        value.CopyTo(result, 8);
        return result;
    }
}
