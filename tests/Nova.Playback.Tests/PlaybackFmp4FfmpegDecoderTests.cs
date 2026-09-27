using Microsoft.Win32.SafeHandles;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4FfmpegDecoderTests
{
    private static readonly byte[] AacLc48KhzStereoAsc = [0x11, 0x90];

    [Fact]
    public void TrackProfile_AcceptsAvc1AacLcAndReducesTimebasesExactly()
    {
        var description = CreateStreamDescription();

        Assert.Equal(
            PlaybackFmp4FfmpegTrackProfileResult.Supported,
            PlaybackFmp4FfmpegTrackProfile.Validate(description));
        Assert.True(PlaybackFmp4FfmpegTrackProfile.TryReduceTimeBase(
            new PlaybackEncodedTimeBase(2, 180_000),
            out var reduced));
        Assert.Equal(new PlaybackBoundedDemuxTimeBase(1, 90_000), reduced);
    }

    [Fact]
    public void TrackProfile_RejectsAvc3AndMalformedCodecConfiguration()
    {
        Assert.Equal(
            PlaybackFmp4FfmpegTrackProfileResult.Supported,
            PlaybackFmp4FfmpegTrackProfile.Validate(
                CreateStreamDescription(
                    audioConfiguration: [0x11, 0x90, 0x56, 0xe5, 0x00])));
        Assert.Equal(
            PlaybackFmp4FfmpegTrackProfileResult.UnsupportedTrack,
            PlaybackFmp4FfmpegTrackProfile.Validate(
                CreateStreamDescription(videoVariant: PlaybackEncodedCodecVariant.Avc3)));
        Assert.Equal(
            PlaybackFmp4FfmpegTrackProfileResult.InvalidConfiguration,
            PlaybackFmp4FfmpegTrackProfile.Validate(
                CreateStreamDescription(videoConfiguration: [1, 2, 3])));
        Assert.Equal(
            PlaybackFmp4FfmpegTrackProfileResult.InvalidConfiguration,
            PlaybackFmp4FfmpegTrackProfile.Validate(
                CreateStreamDescription(audioConfiguration: [0x29, 0x90])));
        Assert.Equal(
            PlaybackFmp4FfmpegTrackProfileResult.InvalidConfiguration,
            PlaybackFmp4FfmpegTrackProfile.Validate(
                CreateStreamDescription(audioConfiguration: [0x11, 0x92])));
        Assert.Equal(
            PlaybackFmp4FfmpegTrackProfileResult.InvalidConfiguration,
            PlaybackFmp4FfmpegTrackProfile.Validate(
                CreateStreamDescription(
                    audioConfiguration: [0x11, 0x90, 0x56, 0xe5, 0x80])));
        Assert.Equal(
            PlaybackFmp4FfmpegTrackProfileResult.InvalidConfiguration,
            PlaybackFmp4FfmpegTrackProfile.Validate(
                CreateStreamDescription(audioConfiguration: [0x11, 0x90, 0x00])));
    }

    [Fact]
    public void TrackProfile_RejectsInvalidTimebaseAndIncompleteTrackInventory()
    {
        Assert.Equal(
            PlaybackFmp4FfmpegTrackProfileResult.InvalidTimeBase,
            PlaybackFmp4FfmpegTrackProfile.Validate(
                CreateStreamDescription(
                    videoTimeBase: (PlaybackEncodedTimeBase?)default(PlaybackEncodedTimeBase))));

        var video = CreateVideoTrack();
        Assert.Equal(
            PlaybackFmp4FfmpegTrackProfileResult.InvalidTrackInventory,
            PlaybackFmp4FfmpegTrackProfile.Validate(
                new PlaybackEncodedStreamDescription(Guid.NewGuid(), [video])));
    }

    [Fact]
    public void Factory_FailsClosedForWrongContainerAndMissingStreamDescription()
    {
        using var runtime = RuntimeLeaseFixture.Create();
        var factory = new PlaybackFmp4FfmpegDecoderFactory(runtime.Lease);

        var wrongContainer = new PlaybackDecoderCreationContext(
            PlaybackMediaContainer.FlashVideo,
            PlaybackMediaClassificationBasis.FileHeader,
            NullPlaybackDecodedFrameSink.Instance);
        var missingDescription = new PlaybackDecoderCreationContext(
            PlaybackMediaContainer.FragmentedMp4,
            PlaybackMediaClassificationBasis.Fmp4InitializationProfile,
            NullPlaybackDecodedFrameSink.Instance);

        Assert.Null(factory.Create(wrongContainer));
        Assert.Null(factory.Create(missingDescription));
        Assert.False(runtime.Lease.IsDisposed);
    }

    [Fact]
    public void Factory_FailsClosedForUnsupportedProfileBeforeRuntimeOwnershipTransfers()
    {
        using var runtime = RuntimeLeaseFixture.Create();
        var factory = new PlaybackFmp4FfmpegDecoderFactory(runtime.Lease);
        var context = PlaybackDecoderCreationContext.CreateForFmp4(
            CreateStreamDescription(videoVariant: PlaybackEncodedCodecVariant.Avc3),
            NullPlaybackDecodedFrameSink.Instance);

        Assert.Null(factory.Create(context));
        Assert.False(runtime.Lease.IsDisposed);
    }

    [Theory]
    [InlineData(PlaybackEncodedRandomAccessClassification.IndependentlyDecodable, 1)]
    [InlineData(PlaybackEncodedRandomAccessClassification.NotIndependentlyDecodable, 0)]
    [InlineData(PlaybackEncodedRandomAccessClassification.Indeterminate, 0)]
    public void PacketMapper_PreservesAvccAndMetadataWithoutPromotingRandomAccess(
        PlaybackEncodedRandomAccessClassification randomAccess,
        int expectedFlags)
    {
        var description = CreateStreamDescription();
        var track = description.TrackDescriptions.Single(static value =>
            value.MediaRole == PlaybackEncodedMediaRole.Video);
        var input = CreateInput(
            description,
            track,
            pts: -90,
            dts: -180,
            duration: 3_003,
            randomAccess: randomAccess);

        var result = PlaybackFmp4FfmpegPacketMapper.TryMap(
            input,
            description.StreamGenerationId,
            track,
            streamIndex: 7,
            out var packet);

        Assert.Equal(PlaybackFmp4FfmpegPacketMapResult.Mapped, result);
        Assert.Equal(input.Payload.ToArray(), packet.Payload.ToArray());
        Assert.Equal(-90, packet.PresentationTimestamp);
        Assert.Equal(-180, packet.DecodeTimestamp);
        Assert.Equal(3_003, packet.Duration);
        Assert.Equal(7, packet.StreamIndex);
        Assert.Equal(expectedFlags, packet.Flags);
        Assert.Equal(new PlaybackBoundedDemuxTimeBase(1, 90_000), packet.TimeBase);
    }

    [Fact]
    public void PacketMapper_AacUsesRawAccessUnitAndNoVideoKeyframeFlag()
    {
        var description = CreateStreamDescription();
        var track = description.TrackDescriptions.Single(static value =>
            value.MediaRole == PlaybackEncodedMediaRole.Audio);
        var input = CreateInput(
            description,
            track,
            payload: [0x21, 0x10, 0x56],
            pts: 1_024,
            dts: 1_024,
            duration: 1_024,
            randomAccess: PlaybackEncodedRandomAccessClassification.NotApplicable);

        var result = PlaybackFmp4FfmpegPacketMapper.TryMap(
            input,
            description.StreamGenerationId,
            track,
            streamIndex: 3,
            out var packet);

        Assert.Equal(PlaybackFmp4FfmpegPacketMapResult.Mapped, result);
        Assert.Equal(new byte[] { 0x21, 0x10, 0x56 }, packet.Payload.ToArray());
        Assert.Equal(0, packet.Flags);
        Assert.Equal(new PlaybackBoundedDemuxTimeBase(1, 48_000), packet.TimeBase);
    }

    [Fact]
    public void PacketMapper_RejectsUnknownTrackAndMismatchedMetadata()
    {
        var description = CreateStreamDescription();
        var video = description.TrackDescriptions.Single(static value =>
            value.MediaRole == PlaybackEncodedMediaRole.Video);
        var audio = description.TrackDescriptions.Single(static value =>
            value.MediaRole == PlaybackEncodedMediaRole.Audio);
        var input = CreateInput(description, video);

        Assert.Equal(
            PlaybackFmp4FfmpegPacketMapResult.UnknownTrack,
            PlaybackFmp4FfmpegPacketMapper.TryMap(
                input,
                description.StreamGenerationId,
                track: null,
                streamIndex: -1,
                out _));
        Assert.Equal(
            PlaybackFmp4FfmpegPacketMapResult.TrackMetadataMismatch,
            PlaybackFmp4FfmpegPacketMapper.TryMap(
                input,
                description.StreamGenerationId,
                audio,
                streamIndex: 1,
                out _));
        Assert.Equal(
            PlaybackFmp4FfmpegPacketMapResult.StreamGenerationMismatch,
            PlaybackFmp4FfmpegPacketMapper.TryMap(
                input,
                Guid.NewGuid(),
                video,
                streamIndex: 0,
                out _));
    }

    [Fact]
    public async Task PendingPacketSubmission_DrainsAndResendsTheSamePacketOwner()
    {
        var packetOwner = new object();
        var sentOwners = new List<object>();
        var sendResults = new Queue<PlaybackMetadataAwareNativeSendResult>(
        [
            PlaybackMetadataAwareNativeSendResult.Again,
            PlaybackMetadataAwareNativeSendResult.Success,
        ]);
        var drainCount = 0;

        await PlaybackFmp4FfmpegPendingPacketSubmission.SubmitAsync(
            () =>
            {
                sentOwners.Add(packetOwner);
                return sendResults.Dequeue();
            },
            _ =>
            {
                drainCount++;
                return ValueTask.FromResult(1);
            },
            CancellationToken.None);

        Assert.Equal(2, sentOwners.Count);
        Assert.All(sentOwners, owner => Assert.Same(packetOwner, owner));
        Assert.Equal(1, drainCount);
    }

    [Fact]
    public async Task PendingPacketSubmission_FailsInsteadOfDroppingPacketWithoutDrainProgress()
    {
        var sendCount = 0;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await PlaybackFmp4FfmpegPendingPacketSubmission.SubmitAsync(
                () =>
                {
                    sendCount++;
                    return PlaybackMetadataAwareNativeSendResult.Again;
                },
                _ => ValueTask.FromResult(0),
                CancellationToken.None));

        Assert.Contains("produced no frame", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, sendCount);
    }

    [Theory]
    [InlineData("GateRejected")]
    [InlineData("AllocationFailed")]
    [InlineData("Rejected")]
    public async Task PendingPacketSubmission_PropagatesNativeSendFailureWithoutRetry(
        string failureName)
    {
        var failure = Enum.Parse<PlaybackMetadataAwareNativeSendResult>(failureName);
        var sendCount = 0;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await PlaybackFmp4FfmpegPendingPacketSubmission.SubmitAsync(
                () =>
                {
                    sendCount++;
                    return failure;
                },
                _ => ValueTask.FromResult(0),
                CancellationToken.None));

        Assert.Contains(failure.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, sendCount);
    }

    private static PlaybackEncodedStreamDescription CreateStreamDescription(
        PlaybackEncodedCodecVariant videoVariant = PlaybackEncodedCodecVariant.Avc1,
        byte[]? videoConfiguration = null,
        byte[]? audioConfiguration = null,
        PlaybackEncodedTimeBase? videoTimeBase = null)
    {
        var video = new PlaybackEncodedTrackDescription(
            1,
            PlaybackEncodedMediaRole.Video,
            PlaybackEncodedCodecKind.Avc,
            videoVariant,
            videoConfiguration ?? PlaybackIsoBmffAvcDecoderConfigurationParserTests.BaselineRecord(),
            videoTimeBase ?? new PlaybackEncodedTimeBase(1, 90_000),
            nalLengthSize: 4,
            width: 64,
            height: 48);
        var audio = new PlaybackEncodedTrackDescription(
            2,
            PlaybackEncodedMediaRole.Audio,
            PlaybackEncodedCodecKind.Aac,
            PlaybackEncodedCodecVariant.Aac,
            audioConfiguration ?? AacLc48KhzStereoAsc,
            new PlaybackEncodedTimeBase(1, 48_000),
            sampleRate: 48_000,
            channelCount: 2);
        return new(Guid.NewGuid(), [video, audio]);
    }

    private static PlaybackEncodedTrackDescription CreateVideoTrack() =>
        new(
            1,
            PlaybackEncodedMediaRole.Video,
            PlaybackEncodedCodecKind.Avc,
            PlaybackEncodedCodecVariant.Avc1,
            PlaybackIsoBmffAvcDecoderConfigurationParserTests.BaselineRecord(),
            new PlaybackEncodedTimeBase(1, 90_000),
            nalLengthSize: 4,
            width: 64,
            height: 48);

    private static PlaybackEncodedInput CreateInput(
        PlaybackEncodedStreamDescription description,
        PlaybackEncodedTrackDescription track,
        byte[]? payload = null,
        long pts = 0,
        long dts = 0,
        long duration = 1,
        PlaybackEncodedRandomAccessClassification randomAccess =
            PlaybackEncodedRandomAccessClassification.IndependentlyDecodable)
    {
        var metadata = new PlaybackFmp4SampleMetadata(
            description.StreamGenerationId,
            track.TrackId,
            track.MediaRole,
            track.CodecKind,
            track.CodecVariant,
            track.TimeBase,
            EpochId: 0,
            FragmentSequence: 1,
            SampleOrdinal: 0,
            DtsTicks: dts,
            PtsTicks: pts,
            DurationTicks: duration,
            RandomAccessClassification: randomAccess);
        return PlaybackEncodedInput.CreateFmp4Sample(
            0,
            PlaybackDiscontinuityKind.None,
            payload ?? [0, 0, 0, 1, 0x65],
            metadata);
    }

    private sealed class RuntimeLeaseFixture : IDisposable
    {
        private readonly string _path;

        private RuntimeLeaseFixture(string path, VerifiedPlaybackDecoderRuntimeLease lease)
        {
            _path = path;
            Lease = lease;
        }

        public VerifiedPlaybackDecoderRuntimeLease Lease { get; }

        public static RuntimeLeaseFixture Create()
        {
            var path = Path.Combine(Path.GetTempPath(), $"nova-runtime-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, [0]);
            var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var lease = new VerifiedPlaybackDecoderRuntimeLease(
                new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runtime.dll"] = file,
                },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runtime.dll"] = path,
                },
                new List<SafeFileHandle>());
            return new(path, lease);
        }

        public void Dispose()
        {
            Lease.Dispose();
            File.Delete(_path);
        }
    }
}
