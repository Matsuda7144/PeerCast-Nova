using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffRegularMovieManagedDecoderPreparationTests
{
    [Fact]
    public void AvcPreparationInput_CopiesMutableConfigurationRecordAtConstruction()
    {
        var (movie, _, _) = MovieWithValidAac(new(0, null));
        var videoTrack = movie.Tracks[0];
        var codecConfiguration =
            videoTrack.PacketComposition.Source.Entries[0];
        var sourceRecord = codecConfiguration.AvcConfiguration!
            .ConfigurationRecord;
        var expected = sourceRecord.ToArray();
        var input = new PlaybackIsoBmffRegularAvcDecoderPreparationInput(
            videoTrack,
            codecConfiguration,
            videoTrack.PacketComposition.AvcRequirements!);

        Assert.True(MemoryMarshal.TryGetArray(
            sourceRecord,
            out var sourceSegment));
        Assert.NotNull(sourceSegment.Array);
        sourceSegment.Array![sourceSegment.Offset] = 0x02;

        Assert.Equal(expected, input.ConfigurationRecord.ToArray());
    }

    [Fact]
    public void AvcPreparationInput_PublicGetterReturnsIndependentSnapshots()
    {
        var (movie, _, _) = MovieWithValidAac(new(0, null));
        Assert.Equal(
            Result.Prepared,
            PlaybackIsoBmffRegularMovieManagedDecoderPreparationComposer
                .Compose(movie, out var preparation, Options()));
        using var prepared = Assert.IsType<
            PlaybackIsoBmffRegularMovieManagedDecoderPreparation>(preparation);
        var input = Assert.IsType<
            PlaybackIsoBmffAvcManagedDecoderPreparation>(prepared.Video).Input;
        var expected = input.ConfigurationRecord.ToArray();
        var first = input.ConfigurationRecord;
        var second = input.ConfigurationRecord;

        Assert.True(MemoryMarshal.TryGetArray(first, out var firstSegment));
        Assert.True(MemoryMarshal.TryGetArray(second, out var secondSegment));
        Assert.NotNull(firstSegment.Array);
        Assert.NotNull(secondSegment.Array);
        Assert.NotSame(firstSegment.Array, secondSegment.Array);

        firstSegment.Array![firstSegment.Offset] = 0x02;
        Assert.Equal(expected, second.ToArray());
        Assert.Equal(expected, input.ConfigurationRecord.ToArray());

        secondSegment.Array![secondSegment.Offset + 1] ^= 0xFF;
        Assert.Equal(expected, input.ConfigurationRecord.ToArray());
    }

    [Fact]
    public void AvcPreparationInput_PostValidationMutationDoesNotAlterResolverOrCodecParameters()
    {
        var (movie, _, _) = MovieWithValidAac(new(0, null));
        Assert.Equal(
            Result.Prepared,
            PlaybackIsoBmffRegularMovieManagedDecoderPreparationComposer
                .Compose(movie, out var preparation, Options()));
        using var prepared = Assert.IsType<
            PlaybackIsoBmffRegularMovieManagedDecoderPreparation>(preparation);
        var input = Assert.IsType<
            PlaybackIsoBmffAvcManagedDecoderPreparation>(prepared.Video).Input;
        var original = input.ConfigurationRecord.ToArray();
        Assert.Equal((byte)0x01, original[0]);

        Assert.True(MemoryMarshal.TryGetArray(
            input.ConfigurationRecord,
            out var publicSegment));
        Assert.NotNull(publicSegment.Array);
        publicSegment.Array![publicSegment.Offset] = 0x02;
        Assert.Equal((byte)0x02, publicSegment.Array[publicSegment.Offset]);

        Assert.Equal(original, input.ConfigurationRecord.ToArray());
        Assert.Equal(
            PlaybackIsoBmffAvcFfmpegCodecParametersInputResolutionResult
                .Resolved,
            PlaybackIsoBmffAvcFfmpegCodecParametersInputResolver.Resolve(
                input,
                out var resolution));
        var resolved = Assert.IsType<
            PlaybackIsoBmffAvcFfmpegCodecParametersInputResolution>(resolution);
        Assert.Equal(original, resolved.Input.Extradata.ToArray());

        var creation = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            resolved.Input);
        Assert.Equal(
            PlaybackDecoderFfmpegCodecParametersResult.Created,
            creation.Result);
        using var lease = Assert.IsType<
            PlaybackDecoderFfmpegCodecParametersLease>(creation.Lease);
        Assert.Equal(
            original,
            lease.UseOwnedParameters(
                static parameters => parameters.Extradata.ToArray()));
    }

    [Fact]
    public void AvcPreparationInput_PublicMutationDoesNotChangeDeterministicResolution()
    {
        var (firstMovie, _, _) = MovieWithValidAac(new(0, null));
        var (secondMovie, _, _) = MovieWithValidAac(new(0, null));
        Assert.Equal(
            Result.Prepared,
            PlaybackIsoBmffRegularMovieManagedDecoderPreparationComposer
                .Compose(firstMovie, out var firstPreparation, Options()));
        Assert.Equal(
            Result.Prepared,
            PlaybackIsoBmffRegularMovieManagedDecoderPreparationComposer
                .Compose(secondMovie, out var secondPreparation, Options()));
        using var firstPrepared = Assert.IsType<
            PlaybackIsoBmffRegularMovieManagedDecoderPreparation>(
                firstPreparation);
        using var secondPrepared = Assert.IsType<
            PlaybackIsoBmffRegularMovieManagedDecoderPreparation>(
                secondPreparation);
        var firstInput = Assert.IsType<
            PlaybackIsoBmffAvcManagedDecoderPreparation>(
                firstPrepared.Video).Input;
        var secondInput = Assert.IsType<
            PlaybackIsoBmffAvcManagedDecoderPreparation>(
                secondPrepared.Video).Input;
        var expected = firstInput.ConfigurationRecord.ToArray();
        Assert.Equal(expected, secondInput.ConfigurationRecord.ToArray());

        Assert.True(MemoryMarshal.TryGetArray(
            firstInput.ConfigurationRecord,
            out var publicSegment));
        Assert.NotNull(publicSegment.Array);
        publicSegment.Array![publicSegment.Offset] = 0x02;

        Assert.Equal(
            PlaybackIsoBmffAvcFfmpegCodecParametersInputResolutionResult
                .Resolved,
            PlaybackIsoBmffAvcFfmpegCodecParametersInputResolver.Resolve(
                firstInput,
                out var firstResolution));
        Assert.Equal(
            PlaybackIsoBmffAvcFfmpegCodecParametersInputResolutionResult
                .Resolved,
            PlaybackIsoBmffAvcFfmpegCodecParametersInputResolver.Resolve(
                secondInput,
                out var secondResolution));
        var firstResolved = Assert.IsType<
            PlaybackIsoBmffAvcFfmpegCodecParametersInputResolution>(
                firstResolution);
        var secondResolved = Assert.IsType<
            PlaybackIsoBmffAvcFfmpegCodecParametersInputResolution>(
                secondResolution);
        Assert.Equal(expected, firstResolved.Input.Extradata.ToArray());
        Assert.Equal(expected, secondResolved.Input.Extradata.ToArray());
    }

    [Fact]
    public async Task Compose_OwnsSelectedAvcInputAndAacManagedPreparation()
    {
        var (movie, videoBox, audioBox) = MovieWithValidAac(new(0, 1));
        var options = Options();

        Assert.Equal(
            Result.Prepared,
            PlaybackIsoBmffRegularMovieManagedDecoderPreparationComposer
                .Compose(movie, out var preparation, options));
        var prepared = Assert.IsType<
            PlaybackIsoBmffRegularMovieManagedDecoderPreparation>(preparation);

        Assert.Same(movie, prepared.Source);
        Assert.Same(options, prepared.Options);
        var video = Assert.IsType<
            PlaybackIsoBmffAvcManagedDecoderPreparation>(prepared.Video);
        Assert.Same(movie.Tracks[0], video.Input.Source);
        Assert.Same(
            movie.Tracks[0].PacketComposition.AvcRequirements,
            video.Input.Requirements);
        Assert.Same(
            video.Input.CodecConfiguration.AvcConfiguration,
            video.Input.AvcConfiguration);
        var expectedConfiguration =
            video.Input.AvcConfiguration.ConfigurationRecord.ToArray();
        Assert.Equal(
            expectedConfiguration,
            video.Input.ConfigurationRecord.ToArray());
        Assert.Equal(
            PlaybackDecoderFfmpegCodecId.H264,
            video.CapabilityBinding.Plan.CodecId);
        Assert.Equal(
            video.Input.AvcConfiguration.VisualEntry.Width,
            video.CapabilityBinding.Plan.Width);
        Assert.Equal(
            video.Input.AvcConfiguration.VisualEntry.Height,
            video.CapabilityBinding.Plan.Height);

        var audio = Assert.IsType<
            PlaybackIsoBmffAacSampleEntryManagedDecoderPreparation>(
                prepared.Audio);
        Assert.Equal(48_000u, audio.FormatIntent.EffectiveSamplingFrequency);
        Assert.Equal(2, audio.FormatIntent.EffectiveChannelCount);
        Assert.Equal(
            PlaybackDecoderFfmpegCodecId.Aac,
            audio.DecoderPreparation.CapabilityBinding.Plan.CodecId);
        Assert.Equal(
            48_000,
            audio.DecoderPreparation.CapabilityBinding.Plan.SampleRate);
        Assert.Equal(
            2,
            audio.DecoderPreparation.CapabilityBinding.Plan.ChannelCount);

        videoBox.AsSpan().Clear();
        audioBox.AsSpan().Clear();
        Assert.Equal(
            expectedConfiguration,
            video.Input.ConfigurationRecord.ToArray());

        var videoLease = video.CapabilityBinding.Plan.ManagedParameters.Lease;
        var lease = audio.DecoderPreparation.CapabilityBinding.Plan
            .ManagedParameters.Lease;
        await prepared.DisposeAsync();
        Assert.True(videoLease.IsDisposed);
        Assert.True(lease.IsDisposed);
        prepared.Dispose();
    }

    [Fact]
    public void Compose_VideoOnlyDoesNotRequireAacCapabilityInput()
    {
        var (movie, _, _) = MovieWithValidAac(new(0, null));

        Assert.Equal(
            Result.Prepared,
            PlaybackIsoBmffRegularMovieManagedDecoderPreparationComposer
                .Compose(movie, out var preparation, Options()));
        using var prepared = Assert.IsType<
            PlaybackIsoBmffRegularMovieManagedDecoderPreparation>(preparation);
        Assert.NotNull(prepared.Video);
        Assert.Null(prepared.Audio);
    }

    [Fact]
    public void Compose_FailsClosedAtTheRejectedPreparationStage()
    {
        Assert.Equal(
            Result.MissingMovieComposition,
            PlaybackIsoBmffRegularMovieManagedDecoderPreparationComposer
                .Compose(null, out var missing));
        Assert.Null(missing);

        var (validMovie, _, _) = MovieWithValidAac(new(0, 1));
        Assert.Equal(
            Result.InvalidConfiguration,
            PlaybackIsoBmffRegularMovieManagedDecoderPreparationComposer
                .Compose(
                    validMovie,
                    out var invalid,
                    Options() with { AacDecoderConfig = null! }));
        Assert.Null(invalid);

        var (invalidAacMovie, _, _) = MovieWithBaselineAac();
        Assert.Equal(
            Result.AudioDecoderConfigRejected,
            PlaybackIsoBmffRegularMovieManagedDecoderPreparationComposer
                .Compose(invalidAacMovie, out var rejected, Options()));
        Assert.Null(rejected);

        Assert.Equal(
            Result.VideoPreparationRejected,
            PlaybackIsoBmffRegularMovieManagedDecoderPreparationComposer
                .Compose(
                    validMovie,
                    out var incompleteAvcSymbols,
                    Options() with
                    {
                        DeclaredAvcSymbols =
                            Array.AsReadOnly(["avcodec_parameters_alloc"]),
                    }));
        Assert.Null(incompleteAvcSymbols);

        Assert.Equal(
            Result.AudioPreparationRejected,
            PlaybackIsoBmffRegularMovieManagedDecoderPreparationComposer
                .Compose(
                    validMovie,
                    out var incompleteSymbols,
                    Options() with
                    {
                        DeclaredAacSymbols =
                            Array.AsReadOnly(["avcodec_parameters_alloc"]),
                    }));
        Assert.Null(incompleteSymbols);
    }

    [Fact]
    public void ComposeAvcPreparation_RejectsMissingSourceAndCapabilityMismatch()
    {
        Assert.Equal(
            PlaybackIsoBmffAvcManagedDecoderPreparationResult
                .InputResolutionRejected,
            PlaybackIsoBmffAvcManagedDecoderPreparationComposer.Compose(
                null,
                PlaybackIsoBmffAvcParameterTransferPlan.RequiredSymbols,
                Options().AvcCapabilityDeclaration,
                out var missing));
        Assert.Null(missing);

        var (movie, _, _) = MovieWithValidAac(new(0, null));
        Assert.Equal(
            Result.Prepared,
            PlaybackIsoBmffRegularMovieManagedDecoderPreparationComposer
                .Compose(movie, out var aggregate, Options()));
        using var prepared = Assert.IsType<
            PlaybackIsoBmffRegularMovieManagedDecoderPreparation>(aggregate);
        var input = Assert.IsType<PlaybackIsoBmffAvcManagedDecoderPreparation>(
            prepared.Video).Input;

        Assert.Equal(
            PlaybackIsoBmffAvcManagedDecoderPreparationResult
                .DeclaredCapabilityRejected,
            PlaybackIsoBmffAvcManagedDecoderPreparationComposer.Compose(
                input,
                PlaybackIsoBmffAvcParameterTransferPlan.RequiredSymbols,
                new(
                    PlaybackDecoderFfmpegCodecId.Aac,
                    PlaybackDecoderFfmpegCodecParametersFactory
                        .MaximumVideoDimension,
                    PlaybackDecoderFfmpegCodecParametersFactory
                        .MaximumVideoDimension,
                    PlaybackDecoderFfmpegCodecParametersFactory
                        .MaximumVideoPixelCount),
                out var mismatch));
        Assert.Null(mismatch);
    }

    [Fact]
    public void BindVerifiedRuntime_RetainsExactGatesAndOwnsPreparationLifetime()
    {
        var (movie, _, _) = MovieWithValidAac(new(0, 1));
        Assert.Equal(
            Result.Prepared,
            PlaybackIsoBmffRegularMovieManagedDecoderPreparationComposer
                .Compose(movie, out var preparation, Options()));
        var prepared = Assert.IsType<
            PlaybackIsoBmffRegularMovieManagedDecoderPreparation>(preparation);
        using var videoGate = new GateFixture(
            PlaybackIsoBmffAvcParameterTransferPlan.RequiredSymbols);
        using var audioGate = new GateFixture(
            PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                .Bound,
            PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinder.Bind(
                prepared,
                videoGate.Gate,
                audioGate.Gate,
                out var binding));
        var bound = Assert.IsType<
            PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinding>(
                binding);

        Assert.Same(prepared, bound.Source);
        Assert.Same(videoGate.Gate, bound.Video!.Gate);
        Assert.Same(audioGate.Gate, bound.Audio!.Gate);
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video,
            bound.Video.Role);
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Audio,
            bound.Audio.Role);
        Assert.Same(
            movie.Selection.VideoSource,
            bound.Video.Track.SequenceProjection);
        Assert.Same(
            movie.Selection.AudioSource,
            bound.Audio.Track.SequenceProjection);
        Assert.Same(
            prepared.Video!.CapabilityBinding.Plan.ManagedParameters.Lease,
            bound.Video.Parameters);
        Assert.Same(
            prepared.Audio!.DecoderPreparation.CapabilityBinding.Plan
                .ManagedParameters.Lease,
            bound.Audio.Parameters);
        Assert.True(bound.IsAvailable);

        bound.Dispose();
        Assert.False(bound.IsAvailable);
        Assert.False(videoGate.Gate.IsDisposed);
        Assert.False(audioGate.Gate.IsDisposed);
    }

    [Fact]
    public void BindVerifiedRuntime_FailsClosedForMissingOrIncompleteGate()
    {
        Assert.Equal(
            PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                .MissingPreparation,
            PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinder.Bind(
                null,
                null,
                null,
                out var missing));
        Assert.Null(missing);

        var (movie, _, _) = MovieWithValidAac(new(0, 1));
        Assert.Equal(
            Result.Prepared,
            PlaybackIsoBmffRegularMovieManagedDecoderPreparationComposer
                .Compose(movie, out var preparation, Options()));
        using var prepared = Assert.IsType<
            PlaybackIsoBmffRegularMovieManagedDecoderPreparation>(preparation);
        using var incompleteVideoGate = new GateFixture(
            ["avcodec_parameters_alloc"]);
        using var audioGate = new GateFixture(
            PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols);
        using var sharedGate = new GateFixture(
            PlaybackIsoBmffAvcParameterTransferPlan.RequiredSymbols
                .Concat(PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols)
                .Distinct(StringComparer.Ordinal)
                .ToArray());

        Assert.Equal(
            PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                .MissingVideoGate,
            PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinder.Bind(
                prepared,
                null,
                audioGate.Gate,
                out var noVideo));
        Assert.Null(noVideo);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                .DuplicateGate,
            PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinder.Bind(
                prepared,
                sharedGate.Gate,
                sharedGate.Gate,
                out var duplicate));
        Assert.Null(duplicate);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                .RequiredSymbolMissing,
            PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinder.Bind(
                prepared,
                incompleteVideoGate.Gate,
                audioGate.Gate,
                out var incomplete));
        Assert.Null(incomplete);

        incompleteVideoGate.Gate.Dispose();
        Assert.Equal(
            PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                .PreparationUnavailable,
            PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinder.Bind(
                prepared,
                incompleteVideoGate.Gate,
                audioGate.Gate,
                out var unavailable));
        Assert.Null(unavailable);
    }

    [Fact]
    public void OpenNativeSession_UsesExactRoleOrderAndTrackTimeBases()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var operations = new FakeNativeSessionOperations();
        var factory =
            new PlaybackIsoBmffRegularMovieNativeDecoderSessionFactory(
                operations);

        var opening = factory.Open(fixture.Binding);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecoderSessionResult.Opened,
            opening.Result);
        using var session = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecoderSession>(opening.Session);
        Assert.Same(fixture.Binding, session.Source);
        Assert.True(session.HasVideo);
        Assert.True(session.HasAudio);
        Assert.Equal(new(1, 1), session.VideoTimeBase);
        Assert.Equal(new(1, 1), session.AudioTimeBase);
        Assert.Equal(
            [
                "create:H264",
                "transfer:H264",
                "time-base:H264:1/1",
                "open:H264",
                "create:Aac",
                "transfer:Aac",
                "time-base:Aac:1/1",
                "open:Aac",
            ],
            operations.Calls);
        Assert.False(fixture.Binding.IsAvailable);
        Assert.False(fixture.VideoGate.Gate.IsDisposed);
        Assert.False(fixture.AudioGate.Gate.IsDisposed);

        session.Dispose();
        Assert.True(fixture.VideoGate.Gate.IsDisposed);
        Assert.True(fixture.AudioGate.Gate.IsDisposed);
    }

    [Fact]
    public void OpenNativeSession_FailsClosedAndDisposesPartialContexts()
    {
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecoderSessionResult
                .MissingBinding,
            new PlaybackIsoBmffRegularMovieNativeDecoderSessionFactory(
                new FakeNativeSessionOperations()).Open(null).Result);

        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var operations = new FakeNativeSessionOperations
        {
            AudioOpenResult =
                PlaybackDecoderFfmpegCodecOpenResult.NativeOpenRejected,
        };

        var opening =
            new PlaybackIsoBmffRegularMovieNativeDecoderSessionFactory(
                operations).Open(fixture.Binding);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecoderSessionResult
                .AudioOpenRejected,
            opening.Result);
        Assert.Null(opening.Session);
        Assert.All(operations.Owners, owner => Assert.True(owner.IsDisposed));
        Assert.True(fixture.VideoGate.Gate.IsDisposed);
        Assert.True(fixture.AudioGate.Gate.IsDisposed);
        Assert.False(fixture.Binding.IsAvailable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OpenNativeSession_SupportsOneExplicitSelectedRole(
        bool selectVideo)
    {
        var selection = selectVideo
            ? new PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest(
                0,
                null)
            : new PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest(
                null,
                1);
        using var fixture = new VerifiedBindingFixture(selection);
        var operations = new FakeNativeSessionOperations();

        var opening =
            new PlaybackIsoBmffRegularMovieNativeDecoderSessionFactory(
                operations).Open(fixture.Binding);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecoderSessionResult.Opened,
            opening.Result);
        using var session = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecoderSession>(opening.Session);
        Assert.Equal(selectVideo, session.HasVideo);
        Assert.Equal(!selectVideo, session.HasAudio);
        Assert.Equal(
            selectVideo ? new(1, 1) : null,
            session.VideoTimeBase);
        Assert.Equal(
            selectVideo ? null : new(1, 1),
            session.AudioTimeBase);
        Assert.Equal(
            selectVideo
                ?
                [
                    "create:H264",
                    "transfer:H264",
                    "time-base:H264:1/1",
                    "open:H264",
                ]
                :
                [
                    "create:Aac",
                    "transfer:Aac",
                    "time-base:Aac:1/1",
                    "open:Aac",
                ],
            operations.Calls);

        session.Dispose();
        Assert.Equal(selectVideo, fixture.VideoGate.Gate.IsDisposed);
        Assert.Equal(!selectVideo, fixture.AudioGate.Gate.IsDisposed);
    }

    [Fact]
    public async Task ResetNativeDecoder_FlushesSelectedRolesAudioThenVideo()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        using var session = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecoderSession>(
                new PlaybackIsoBmffRegularMovieNativeDecoderSessionFactory(
                    new FakeNativeSessionOperations()).Open(fixture.Binding)
                    .Session);
        var operations = new FakeNativeDecoderResetOperations(
            PlaybackDecoderFfmpegDiscontinuityResetResult.Reset,
            PlaybackDecoderFfmpegDiscontinuityResetResult.Reset);
        using var reset = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecoderReset>(
                new PlaybackIsoBmffRegularMovieNativeDecoderResetFactory(
                    operations).Create(session).Reset);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecoderResetResult.Reset,
            await reset.ResetAsync());

        Assert.Equal(
            [
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Audio,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video,
            ],
            operations.Roles);
        Assert.False(session.IsDisposed);
        Assert.Equal(1, reset.Snapshot.ResetCount);
        Assert.False(reset.Snapshot.HasActiveReset);
    }

    [Fact]
    public async Task ResetNativeDecoder_FailsClosedAfterPartialRoleReset()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var session = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecoderSession>(
                new PlaybackIsoBmffRegularMovieNativeDecoderSessionFactory(
                    new FakeNativeSessionOperations()).Open(fixture.Binding)
                    .Session);
        var operations = new FakeNativeDecoderResetOperations(
            PlaybackDecoderFfmpegDiscontinuityResetResult.Reset,
            PlaybackDecoderFfmpegDiscontinuityResetResult.GateRejected);
        using var reset = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecoderReset>(
                new PlaybackIsoBmffRegularMovieNativeDecoderResetFactory(
                    operations).Create(session).Reset);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecoderResetResult
                .PartialResetFault,
            await reset.ResetAsync());

        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecoderResetState.Faulted,
            reset.Snapshot.State);
        Assert.True(session.IsDisposed);
        Assert.True(fixture.VideoGate.Gate.IsDisposed);
        Assert.True(fixture.AudioGate.Gate.IsDisposed);
    }

    [Fact]
    public async Task ResetNativeDecoder_PreCancelledCallRetainsSession()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        using var session = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecoderSession>(
                new PlaybackIsoBmffRegularMovieNativeDecoderSessionFactory(
                    new FakeNativeSessionOperations()).Open(fixture.Binding)
                    .Session);
        var operations = new FakeNativeDecoderResetOperations();
        using var reset = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecoderReset>(
                new PlaybackIsoBmffRegularMovieNativeDecoderResetFactory(
                    operations).Create(session).Reset);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecoderResetResult.Cancelled,
            await reset.ResetAsync(cancellation.Token));

        Assert.Empty(operations.Roles);
        Assert.False(session.IsDisposed);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecoderResetState.Ready,
            reset.Snapshot.State);
    }

    [Fact]
    public void SubmitNativePackets_UsesExactMergeOrderAndPacketMetadata()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var sessionOpening =
            new PlaybackIsoBmffRegularMovieNativeDecoderSessionFactory(
                new FakeNativeSessionOperations()).Open(fixture.Binding);
        var session = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecoderSession>(
                sessionOpening.Session);
        var operations = new FakePacketSubmissionOperations();
        var creation =
            new PlaybackIsoBmffRegularMovieNativePacketSubmissionFactory(
                operations).Create(session);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativePacketSubmissionCreationResult
                .Created,
            creation.Result);
        using var submission = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativePacketSubmission>(
                creation.Submission);
        var steps = fixture.Binding.Source.Source.MergeTraversal.Traversal
            .Steps;
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var advance = submission.SubmitNext();

            Assert.Equal(
                PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                    .Submitted,
                advance.Result);
            var snapshot = Assert.IsType<
                PlaybackIsoBmffRegularMovieNativePacketSubmissionSnapshot>(
                    advance.Snapshot);
            var packet = step.Packet.TrackPacket.Packet;
            Assert.Equal(step.MergeIndex, snapshot.MergeIndex);
            Assert.Equal(step.Role, snapshot.Role);
            Assert.Equal(step.StreamIndex, snapshot.StreamIndex);
            Assert.Equal(step.TrackId, snapshot.TrackId);
            Assert.Equal(step.SampleNumber, snapshot.SampleNumber);
            Assert.Equal(
                (long)packet.Candidate.Presentation.StartTimeUnits,
                snapshot.PresentationTimestamp);
            Assert.Equal(
                (long)(packet.Candidate.Presentation.StartTimeUnits -
                    packet.Candidate.Presentation.CompositionOffsetUnits),
                snapshot.DecodeTimestamp);
            Assert.Equal(
                packet.Candidate.Presentation.DurationUnits,
                (ulong)snapshot.Duration);
            Assert.Equal(new(1, 1), snapshot.TimeBase);
            Assert.Equal(packet.Payload.Length, snapshot.PayloadLength);
            Assert.Equal(
                packet.AvcPayloadComposition?.Selection.Classification
                    .ContainsIdr == true,
                snapshot.IsKeyframe);
            Assert.Equal(index + 1, submission.NextMergeIndex);
        }

        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativePacketSubmissionResult.Exhausted,
            submission.SubmitNext().Result);
        Assert.Equal(steps.Count, operations.Inputs.Count);
        Assert.Equal(
            steps.Select(step => step.Role),
            operations.Roles);
        for (var index = 0; index < steps.Count; index++)
        {
            Assert.True(
                steps[index].Packet.TrackPacket.Packet.Payload.Span
                    .SequenceEqual(operations.Inputs[index].Payload.Span));
        }

        Assert.False(fixture.VideoGate.Gate.IsDisposed);
        Assert.False(fixture.AudioGate.Gate.IsDisposed);
        submission.Dispose();
        Assert.True(fixture.VideoGate.Gate.IsDisposed);
        Assert.True(fixture.AudioGate.Gate.IsDisposed);
    }

    [Fact]
    public void SubmitNativePackets_RetainsBackpressureAndFailsClosed()
    {
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativePacketSubmissionCreationResult
                .MissingSession,
            new PlaybackIsoBmffRegularMovieNativePacketSubmissionFactory(
                new FakePacketSubmissionOperations()).Create(null).Result);

        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var session = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecoderSession>(
                new PlaybackIsoBmffRegularMovieNativeDecoderSessionFactory(
                    new FakeNativeSessionOperations()).Open(fixture.Binding)
                    .Session);
        var operations = new FakePacketSubmissionOperations(
            PlaybackMetadataAwareNativeSendResult.Again,
            PlaybackMetadataAwareNativeSendResult.Success,
            PlaybackMetadataAwareNativeSendResult.Rejected);
        using var submission = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativePacketSubmission>(
                new PlaybackIsoBmffRegularMovieNativePacketSubmissionFactory(
                    operations).Create(session).Submission);

        var blocked = submission.SubmitNext();
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                .NeedFrameDrain,
            blocked.Result);
        Assert.True(submission.HasPendingPacket);
        Assert.Equal(0, submission.NextMergeIndex);

        var accepted = submission.SubmitNext();
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativePacketSubmissionResult.Submitted,
            accepted.Result);
        Assert.Same(blocked.Snapshot, accepted.Snapshot);
        Assert.False(submission.HasPendingPacket);
        Assert.Equal(1, submission.NextMergeIndex);
        Assert.Single(operations.Owners);
        Assert.Equal(2, operations.Owners[0].SendCount);
        Assert.True(operations.Owners[0].IsDisposed);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                .NativeRejected,
            submission.SubmitNext().Result);
        Assert.True(submission.IsDisposed);
        Assert.True(fixture.VideoGate.Gate.IsDisposed);
        Assert.True(fixture.AudioGate.Gate.IsDisposed);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativePacketSubmissionResult.Faulted,
            submission.SubmitNext().Result);
    }

    [Fact]
    public void AdvanceNativeDecode_CopiesOneFrameBeforeRetryingPendingPacket()
    {
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressCreationResult
                .MissingSubmission,
            new PlaybackIsoBmffRegularMovieNativeDecodeProgressFactory(
                new FakeDecodeProgressOperations()).Create(null).Result);

        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var session = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecoderSession>(
                new PlaybackIsoBmffRegularMovieNativeDecoderSessionFactory(
                    new FakeNativeSessionOperations()).Open(fixture.Binding)
                    .Session);
        var packetOperations = new FakePacketSubmissionOperations(
            PlaybackMetadataAwareNativeSendResult.Again,
            PlaybackMetadataAwareNativeSendResult.Success);
        var submission = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativePacketSubmission>(
                new PlaybackIsoBmffRegularMovieNativePacketSubmissionFactory(
                    packetOperations).Create(session).Submission);
        var decodeOperations = new FakeDecodeProgressOperations(
            receives:
            [
                new(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole
                        .Video,
                    PlaybackDecoderFfmpegManagedFrameCopyResult.Copied),
            ]);
        using var progress = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecodeProgress>(
                new PlaybackIsoBmffRegularMovieNativeDecodeProgressFactory(
                    decodeOperations).Create(submission).Progress);

        var blocked = progress.Advance();
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .PacketBackpressure,
            blocked.Result);
        Assert.Equal(0, blocked.Packet!.MergeIndex);

        var copied = progress.Advance();
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult.FrameCopied,
            copied.Result);
        var completion = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecodedFrameCompletion>(
                copied.Frame);
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video,
            completion.Role);
        Assert.Equal(0, completion.Sequence);
        Assert.False(completion.Frame.IsDisposed);
        Assert.True(progress.HasActiveFrame);
        Assert.Same(completion, progress.Advance().Frame);

        var frame = completion.Frame;
        completion.Dispose();
        Assert.True(frame.IsDisposed);
        Assert.False(progress.HasActiveFrame);

        var accepted = progress.Advance();
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .PacketSubmitted,
            accepted.Result);
        Assert.Equal(0, accepted.Packet!.MergeIndex);
        Assert.Equal(1, submission.NextMergeIndex);
        Assert.Equal(2, packetOperations.Owners[0].SendCount);
        Assert.Equal(1, progress.NextFrameSequence);
    }

    [Fact]
    public void AdvanceNativeDecode_FlushesAndDrainsExactSelectedRoleOrder()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var session = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecoderSession>(
                new PlaybackIsoBmffRegularMovieNativeDecoderSessionFactory(
                    new FakeNativeSessionOperations()).Open(fixture.Binding)
                    .Session);
        var packetOperations = new FakePacketSubmissionOperations();
        var submission = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativePacketSubmission>(
                new PlaybackIsoBmffRegularMovieNativePacketSubmissionFactory(
                    packetOperations).Create(session).Submission);
        var decodeOperations = new FakeDecodeProgressOperations(
            flushes:
            [
                PlaybackMetadataAwareNativeSendResult.Again,
                PlaybackMetadataAwareNativeSendResult.Success,
                PlaybackMetadataAwareNativeSendResult.Success,
            ],
            receives:
            [
                new(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole
                        .Video,
                    PlaybackDecoderFfmpegManagedFrameCopyResult.Copied),
                new(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole
                        .Video,
                    PlaybackDecoderFfmpegManagedFrameCopyResult.Copied),
                new(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole
                        .Video,
                    PlaybackDecoderFfmpegManagedFrameCopyResult.EndOfStream),
                new(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole
                        .Audio,
                    PlaybackDecoderFfmpegManagedFrameCopyResult.Copied),
                new(
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole
                        .Audio,
                    PlaybackDecoderFfmpegManagedFrameCopyResult.EndOfStream),
            ]);
        using var progress = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecodeProgress>(
                new PlaybackIsoBmffRegularMovieNativeDecodeProgressFactory(
                    decodeOperations).Create(submission).Progress);

        for (var index = 0; index < 4; index++)
        {
            Assert.Equal(
                PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                    .PacketSubmitted,
                progress.Advance().Result);
        }

        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .PacketsExhausted,
            progress.Advance().Result);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .VideoFlushBackpressure,
            progress.Advance().Result);
        AssertFrame(progress, 0, PlaybackDecoderFfmpegFrameMediaKind.Video);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .VideoFlushAccepted,
            progress.Advance().Result);
        AssertFrame(progress, 1, PlaybackDecoderFfmpegFrameMediaKind.Video);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .VideoEndOfStream,
            progress.Advance().Result);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .AudioFlushAccepted,
            progress.Advance().Result);
        AssertFrame(progress, 2, PlaybackDecoderFfmpegFrameMediaKind.Audio);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .AudioEndOfStream,
            progress.Advance().Result);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult.Completed,
            progress.Advance().Result);
        Assert.Equal(
            [
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Audio,
            ],
            decodeOperations.FlushRoles);
        Assert.Equal(3, progress.NextFrameSequence);

        progress.Dispose();
        Assert.True(fixture.VideoGate.Gate.IsDisposed);
        Assert.True(fixture.AudioGate.Gate.IsDisposed);
    }

    [Fact]
    public void TransferSchedulerIngress_RetainsCancelledThenTransfersExactFrame()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var progress = CreateVideoFrameProgress(fixture);
        var scheduler = CreateScheduler();
        var operations = new FakeSchedulerIngressOperations(
            PlaybackSchedulerEnqueueResult.Enqueued);
        using var ingress = Assert.IsType<
            PlaybackIsoBmffRegularMovieSchedulerIngress>(
                new PlaybackIsoBmffRegularMovieSchedulerIngressFactory(
                    operations).Create(progress, scheduler, 41).Ingress);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .PacketBackpressure,
            progress.Advance().Result);
        var copied = progress.Advance();
        var completion = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecodedFrameCompletion>(
                copied.Frame);
        var frame = completion.Frame;

        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerIngressResult.Cancelled,
            ingress.Transfer(copied, new(true)));
        Assert.False(completion.IsDisposed);
        Assert.False(frame.IsDisposed);
        Assert.True(progress.HasActiveFrame);
        Assert.Empty(operations.Calls);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerIngressResult.Transferred,
            ingress.Transfer(copied));
        Assert.True(completion.IsDisposed);
        Assert.True(frame.IsDisposed);
        Assert.False(progress.HasActiveFrame);
        var call = Assert.Single(operations.Calls);
        Assert.Same(frame, call.Frame);
        Assert.Equal(41, call.Sequence);
        Assert.Equal(PlaybackDecodedFrameDiscontinuityKind.None, call.Discontinuity);
        Assert.Equal(
            new PlaybackIsoBmffRegularMovieSchedulerIngressSnapshot(
                PlaybackIsoBmffRegularMovieSchedulerIngressState.Accepting,
                PlaybackIsoBmffRegularMovieSchedulerIngressResult.Transferred,
                1,
                42,
                1,
                scheduler.GetSnapshot()),
            ingress.Snapshot);
    }

    [Fact]
    public void TransferSchedulerIngress_FailsClosedWhenSchedulerRejectsFrame()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var progress = CreateVideoFrameProgress(fixture);
        var scheduler = CreateScheduler();
        var operations = new FakeSchedulerIngressOperations(
            PlaybackSchedulerEnqueueResult.Rejected);
        using var ingress = Assert.IsType<
            PlaybackIsoBmffRegularMovieSchedulerIngress>(
                new PlaybackIsoBmffRegularMovieSchedulerIngressFactory(
                    operations).Create(progress, scheduler).Ingress);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .PacketBackpressure,
            progress.Advance().Result);
        var copied = progress.Advance();
        var completion = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecodedFrameCompletion>(
                copied.Frame);
        var frame = completion.Frame;

        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerIngressResult.EnqueueRejected,
            ingress.Transfer(copied));
        Assert.True(completion.IsDisposed);
        Assert.True(frame.IsDisposed);
        Assert.True(progress.IsDisposed);
        Assert.True(scheduler.IsDisposed);
        Assert.True(fixture.VideoGate.Gate.IsDisposed);
        Assert.True(fixture.AudioGate.Gate.IsDisposed);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerIngressState.Faulted,
            ingress.Snapshot.State);
        Assert.Equal(0, ingress.Snapshot.AcceptedTransferCount);
    }

    [Fact]
    public async Task DeliverSchedulerFrame_ConsumesExactCanonicalDeliveryOnce()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var progress = CreateVideoFrameProgress(fixture);
        var scheduler = CreateScheduler();
        var ingress = Assert.IsType<
            PlaybackIsoBmffRegularMovieSchedulerIngress>(
                new PlaybackIsoBmffRegularMovieSchedulerIngressFactory()
                    .Create(progress, scheduler, 7).Ingress);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .PacketBackpressure,
            progress.Advance().Result);
        var copied = progress.Advance();
        var completion = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecodedFrameCompletion>(
                copied.Frame);
        var frame = completion.Frame;
        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerIngressResult.Transferred,
            ingress.Transfer(copied));

        var sink = new NoOpSink();
        var operations = new FakeSchedulerDeliveryOperations(
            PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered);
        using var delivery = Assert.IsType<
            PlaybackIsoBmffRegularMovieSchedulerDelivery>(
                new PlaybackIsoBmffRegularMovieSchedulerDeliveryFactory(
                    operations).Create(ingress, sink).Delivery);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.VideoDelivered,
            await delivery.DeliverOneAsync());
        var call = Assert.Single(operations.Calls);
        Assert.Same(frame, call.Frame);
        Assert.Equal(7, call.Sequence);
        Assert.Equal(PlaybackDecodedFrameDiscontinuityKind.None, call.Discontinuity);
        Assert.Same(sink, call.Sink);
        Assert.True(frame.IsDisposed);
        Assert.Equal(0, scheduler.GetSnapshot().QueueLength);
        Assert.Equal(1, scheduler.GetSnapshot().DeliveryCount);
        Assert.Equal(1, delivery.Snapshot.DeliveredFrameCount);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerPreferredMediaKind.Audio,
            delivery.Snapshot.PreferredMediaKind);
    }

    [Fact]
    public async Task DeliverSchedulerFrame_RetainsCancellationForExactRetry()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var progress = CreateVideoFrameProgress(fixture);
        var scheduler = CreateScheduler();
        var ingress = Assert.IsType<
            PlaybackIsoBmffRegularMovieSchedulerIngress>(
                new PlaybackIsoBmffRegularMovieSchedulerIngressFactory()
                    .Create(progress, scheduler).Ingress);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .PacketBackpressure,
            progress.Advance().Result);
        var copied = progress.Advance();
        var frame = copied.Frame!.Frame;
        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerIngressResult.Transferred,
            ingress.Transfer(copied));
        var operations = new FakeSchedulerDeliveryOperations(
            PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Cancelled,
            PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered);
        using var delivery = Assert.IsType<
            PlaybackIsoBmffRegularMovieSchedulerDelivery>(
                new PlaybackIsoBmffRegularMovieSchedulerDeliveryFactory(
                    operations).Create(ingress, new NoOpSink()).Delivery);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.Cancelled,
            await delivery.DeliverOneAsync());
        Assert.False(frame.IsDisposed);
        Assert.Equal(1, scheduler.GetSnapshot().QueueLength);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.VideoDelivered,
            await delivery.DeliverOneAsync());
        Assert.True(frame.IsDisposed);
        Assert.Equal(2, operations.Calls.Count);
        Assert.Same(operations.Calls[0].Frame, operations.Calls[1].Frame);
        Assert.Equal(operations.Calls[0].Sequence, operations.Calls[1].Sequence);
    }

    [Fact]
    public async Task DeliverSchedulerFrame_FailsClosedOnSinkRejection()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var progress = CreateVideoFrameProgress(fixture);
        var scheduler = CreateScheduler();
        var ingress = Assert.IsType<
            PlaybackIsoBmffRegularMovieSchedulerIngress>(
                new PlaybackIsoBmffRegularMovieSchedulerIngressFactory()
                    .Create(progress, scheduler).Ingress);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .PacketBackpressure,
            progress.Advance().Result);
        var copied = progress.Advance();
        var frame = copied.Frame!.Frame;
        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerIngressResult.Transferred,
            ingress.Transfer(copied));
        var operations = new FakeSchedulerDeliveryOperations(
            PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.SinkFailed);
        using var delivery = Assert.IsType<
            PlaybackIsoBmffRegularMovieSchedulerDelivery>(
                new PlaybackIsoBmffRegularMovieSchedulerDeliveryFactory(
                    operations).Create(ingress, new NoOpSink()).Delivery);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                .DeliveryRejected,
            await delivery.DeliverOneAsync());
        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Faulted,
            delivery.Snapshot.State);
        Assert.True(frame.IsDisposed);
        Assert.True(progress.IsDisposed);
        Assert.True(scheduler.IsDisposed);
        Assert.True(operations.IsDisposed);
        Assert.True(fixture.VideoGate.Gate.IsDisposed);
        Assert.True(fixture.AudioGate.Gate.IsDisposed);
    }

    [Fact]
    public async Task DeliverSchedulerFrame_AllowsOnlyOneActiveReservation()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var progress = CreateVideoFrameProgress(fixture);
        var scheduler = CreateScheduler();
        var ingress = Assert.IsType<
            PlaybackIsoBmffRegularMovieSchedulerIngress>(
                new PlaybackIsoBmffRegularMovieSchedulerIngressFactory()
                    .Create(progress, scheduler).Ingress);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .PacketBackpressure,
            progress.Advance().Result);
        var copied = progress.Advance();
        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerIngressResult.Transferred,
            ingress.Transfer(copied));
        var operations = new BlockingSchedulerDeliveryOperations();
        using var delivery = Assert.IsType<
            PlaybackIsoBmffRegularMovieSchedulerDelivery>(
                new PlaybackIsoBmffRegularMovieSchedulerDeliveryFactory(
                    operations).Create(ingress, new NoOpSink()).Delivery);

        var active = delivery.DeliverOneAsync().AsTask();
        await operations.Entered;
        Assert.True(delivery.Snapshot.HasActiveDelivery);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.AlreadyRunning,
            await delivery.DeliverOneAsync());
        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerIngressResult.Cancelled,
            delivery.Transfer(null));

        operations.Release();
        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.VideoDelivered,
            await active);
        Assert.False(delivery.Snapshot.HasActiveDelivery);
    }

    [Fact]
    public async Task TimedSchedulerDelivery_RetainsWaitThenDeliversExactFrame()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var progress = CreateVideoFrameProgress(fixture);
        var scheduler = CreateScheduler();
        var ingress = Assert.IsType<
            PlaybackIsoBmffRegularMovieSchedulerIngress>(
                new PlaybackIsoBmffRegularMovieSchedulerIngressFactory()
                    .Create(progress, scheduler).Ingress);
        var frame = TimingFrame(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            TimeSpan.FromMilliseconds(100));
        Assert.Equal(
            PlaybackSchedulerEnqueueResult.Enqueued,
            scheduler.Enqueue(
                frame,
                5,
                PlaybackDecodedFrameDiscontinuityKind.None));
        var timing = new FakeSchedulerTimingOperations(
            new(
                PlaybackAvTimingDecisionResult.Wait,
                TimeSpan.FromMilliseconds(25)),
            new(PlaybackAvTimingDecisionResult.ProcessNow, TimeSpan.Zero));
        var operations = new FakeSchedulerDeliveryOperations(
            PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered);
        using var delivery = new PlaybackIsoBmffRegularMovieSchedulerDelivery(
            ingress,
            scheduler,
            ingress.SourceSession,
            new NoOpSink(),
            operations,
            timing,
            9,
            11);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.RetainedWait,
            await delivery.DeliverOneAsync());
        Assert.False(frame.IsDisposed);
        Assert.Equal(1, scheduler.GetSnapshot().QueueLength);
        Assert.Empty(operations.Calls);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.VideoDelivered,
            await delivery.DeliverOneAsync());
        Assert.True(frame.IsDisposed);
        Assert.Equal(2, timing.Items.Count);
        Assert.All(timing.Items, item =>
        {
            Assert.Equal(PlaybackPolicyMediaKind.Video, item.MediaKind);
            Assert.Equal(5, item.Sequence);
            Assert.Equal(9, item.Generation);
            Assert.Equal(
                PlaybackPolicyTime.Known(TimeSpan.FromMilliseconds(100)),
                item.PresentationTimestamp);
        });
        Assert.Single(operations.Calls);
        Assert.True(delivery.Snapshot.HasTimingAdmission);
        Assert.Equal(9, delivery.Snapshot.VideoGeneration);
        Assert.Equal(11, delivery.Snapshot.AudioGeneration);
    }

    [Fact]
    public async Task TimedSchedulerDelivery_DropsLateVideoAndRetainsLateAudio()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var progress = CreateVideoFrameProgress(fixture);
        var scheduler = CreateScheduler();
        var ingress = Assert.IsType<
            PlaybackIsoBmffRegularMovieSchedulerIngress>(
                new PlaybackIsoBmffRegularMovieSchedulerIngressFactory()
                    .Create(progress, scheduler).Ingress);
        var video = TimingFrame(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            TimeSpan.FromMilliseconds(100));
        var audio = TimingFrame(
            PlaybackDecoderFfmpegFrameMediaKind.Audio,
            TimeSpan.FromMilliseconds(120));
        Assert.Equal(
            PlaybackSchedulerEnqueueResult.Enqueued,
            scheduler.Enqueue(
                video,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None));
        Assert.Equal(
            PlaybackSchedulerEnqueueResult.Enqueued,
            scheduler.Enqueue(
                audio,
                1,
                PlaybackDecodedFrameDiscontinuityKind.None));
        var timing = new FakeSchedulerTimingOperations(
            new(PlaybackAvTimingDecisionResult.VideoLate, TimeSpan.Zero),
            new(PlaybackAvTimingDecisionResult.AudioLate, TimeSpan.Zero));
        var operations = new FakeSchedulerDeliveryOperations();
        using var delivery = new PlaybackIsoBmffRegularMovieSchedulerDelivery(
            ingress,
            scheduler,
            ingress.SourceSession,
            new NoOpSink(),
            operations,
            timing,
            9,
            11);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.VideoDropped,
            await delivery.DeliverOneAsync());
        Assert.True(video.IsDisposed);
        Assert.False(audio.IsDisposed);
        Assert.Equal(1, scheduler.GetSnapshot().DropCount);
        Assert.Equal(1, delivery.Snapshot.DroppedVideoCount);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerPreferredMediaKind.Audio,
            delivery.Snapshot.PreferredMediaKind);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                .RetainedAudioRecoveryRequired,
            await delivery.DeliverOneAsync());
        Assert.False(audio.IsDisposed);
        Assert.Equal(1, scheduler.GetSnapshot().QueueLength);
        Assert.Equal(
            [
                PlaybackPolicyMediaKind.Video,
                PlaybackPolicyMediaKind.Audio,
            ],
            timing.Items.Select(item => item.MediaKind));
        Assert.Equal(11, timing.Items[1].Generation);
        Assert.Empty(operations.Calls);
    }

    [Fact]
    public async Task TimedDeliveryDriver_WaitsWithinBoundsThenDeliversExactReservation()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var progress = CreateVideoFrameProgress(fixture);
        var scheduler = CreateScheduler();
        var ingress = Assert.IsType<
            PlaybackIsoBmffRegularMovieSchedulerIngress>(
                new PlaybackIsoBmffRegularMovieSchedulerIngressFactory()
                    .Create(progress, scheduler).Ingress);
        var frame = TimingFrame(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            TimeSpan.FromMilliseconds(100));
        Assert.Equal(
            PlaybackSchedulerEnqueueResult.Enqueued,
            scheduler.Enqueue(
                frame,
                5,
                PlaybackDecodedFrameDiscontinuityKind.None));
        var timing = new FakeSchedulerTimingOperations(
            new(
                PlaybackAvTimingDecisionResult.Wait,
                TimeSpan.FromMilliseconds(10)),
            new(
                PlaybackAvTimingDecisionResult.Wait,
                TimeSpan.FromMilliseconds(20)),
            new(PlaybackAvTimingDecisionResult.ProcessNow, TimeSpan.Zero));
        var operations = new FakeSchedulerDeliveryOperations(
            PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered);
        var delivery = new PlaybackIsoBmffRegularMovieSchedulerDelivery(
            ingress,
            scheduler,
            ingress.SourceSession,
            new NoOpSink(),
            operations,
            timing,
            9,
            11);
        var delay = new FakeTimedDeliveryDelay();
        using var driver = Assert.IsType<
            PlaybackIsoBmffRegularMovieTimedDeliveryDriver>(
                new PlaybackIsoBmffRegularMovieTimedDeliveryDriverFactory(
                    delay).Create(delivery).Driver);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                .VideoDelivered,
            await driver.DriveOneAsync());

        Assert.True(frame.IsDisposed);
        Assert.Equal(
            [
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(20),
            ],
            delay.Delays);
        Assert.Equal(3, timing.Items.Count);
        Assert.All(timing.Items, item =>
        {
            Assert.Equal(PlaybackPolicyMediaKind.Video, item.MediaKind);
            Assert.Equal(5, item.Sequence);
            Assert.Equal(9, item.Generation);
            Assert.Equal(
                PlaybackPolicyTime.Known(TimeSpan.FromMilliseconds(100)),
                item.PresentationTimestamp);
        });
        Assert.Single(operations.Calls);
        var snapshot = driver.Snapshot;
        Assert.Equal(3, snapshot.DecisionCount);
        Assert.Equal(2, snapshot.WaitCount);
        Assert.Equal(TimeSpan.FromMilliseconds(30), snapshot.TotalWait);
        Assert.False(snapshot.HasActiveDrive);
    }

    [Fact]
    public async Task TimedDeliveryDriver_FailsClosedWhenWaitBoundExceeded()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var progress = CreateVideoFrameProgress(fixture);
        var scheduler = CreateScheduler();
        var ingress = Assert.IsType<
            PlaybackIsoBmffRegularMovieSchedulerIngress>(
                new PlaybackIsoBmffRegularMovieSchedulerIngressFactory()
                    .Create(progress, scheduler).Ingress);
        var frame = TimingFrame(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            TimeSpan.FromMilliseconds(100));
        Assert.Equal(
            PlaybackSchedulerEnqueueResult.Enqueued,
            scheduler.Enqueue(
                frame,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None));
        var timing = new FakeSchedulerTimingOperations(
            new PlaybackAvTimingDecision(
                PlaybackAvTimingDecisionResult.Wait,
                TimeSpan.FromMilliseconds(75)));
        var operations = new FakeSchedulerDeliveryOperations();
        var delivery = new PlaybackIsoBmffRegularMovieSchedulerDelivery(
            ingress,
            scheduler,
            ingress.SourceSession,
            new NoOpSink(),
            operations,
            timing,
            9,
            11);
        var delay = new FakeTimedDeliveryDelay();
        using var driver = Assert.IsType<
            PlaybackIsoBmffRegularMovieTimedDeliveryDriver>(
                new PlaybackIsoBmffRegularMovieTimedDeliveryDriverFactory(
                    delay).Create(
                        delivery,
                        new()
                        {
                            MaximumSingleDelay =
                                TimeSpan.FromMilliseconds(50),
                            MaximumTotalWait =
                                TimeSpan.FromMilliseconds(100),
                        }).Driver);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                .WaitLimitReached,
            await driver.DriveOneAsync());

        Assert.Equal(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverState.Faulted,
            driver.Snapshot.State);
        Assert.Equal(1, driver.Snapshot.DecisionCount);
        Assert.Equal(0, driver.Snapshot.WaitCount);
        Assert.Empty(delay.Delays);
        Assert.Empty(operations.Calls);
        Assert.True(frame.IsDisposed);
        Assert.True(progress.IsDisposed);
        Assert.True(scheduler.IsDisposed);
        Assert.True(operations.IsDisposed);
    }

    [Fact]
    public async Task TimedDeliveryDriver_CancellationRetainsReservationForNextDrive()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var progress = CreateVideoFrameProgress(fixture);
        var scheduler = CreateScheduler();
        var ingress = Assert.IsType<
            PlaybackIsoBmffRegularMovieSchedulerIngress>(
                new PlaybackIsoBmffRegularMovieSchedulerIngressFactory()
                    .Create(progress, scheduler).Ingress);
        var frame = TimingFrame(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            TimeSpan.FromMilliseconds(100));
        Assert.Equal(
            PlaybackSchedulerEnqueueResult.Enqueued,
            scheduler.Enqueue(
                frame,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None));
        var timing = new FakeSchedulerTimingOperations(
            new(
                PlaybackAvTimingDecisionResult.Wait,
                TimeSpan.FromMilliseconds(10)),
            new(PlaybackAvTimingDecisionResult.ProcessNow, TimeSpan.Zero));
        var operations = new FakeSchedulerDeliveryOperations(
            PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered);
        var delivery = new PlaybackIsoBmffRegularMovieSchedulerDelivery(
            ingress,
            scheduler,
            ingress.SourceSession,
            new NoOpSink(),
            operations,
            timing,
            9,
            11);
        var delay = new BlockingTimedDeliveryDelay();
        using var driver = Assert.IsType<
            PlaybackIsoBmffRegularMovieTimedDeliveryDriver>(
                new PlaybackIsoBmffRegularMovieTimedDeliveryDriverFactory(
                    delay).Create(delivery).Driver);
        using var cancellation = new CancellationTokenSource();

        var active = driver.DriveOneAsync(cancellation.Token).AsTask();
        await delay.Entered;
        Assert.True(driver.Snapshot.HasActiveDrive);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerIngressResult.Cancelled,
            driver.Transfer(null));
        cancellation.Cancel();

        Assert.Equal(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult.Cancelled,
            await active);
        Assert.False(frame.IsDisposed);
        Assert.Equal(1, scheduler.GetSnapshot().QueueLength);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverState.Accepting,
            driver.Snapshot.State);
        Assert.Equal(1, driver.Snapshot.WaitCount);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                .VideoDelivered,
            await driver.DriveOneAsync());
        Assert.True(frame.IsDisposed);
        Assert.Equal(2, timing.Items.Count);
        Assert.Single(operations.Calls);
    }

    [Fact]
    public async Task TimedDeliveryDriver_CoordinatesResetBeforeRetainedDelivery()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var progress = CreateVideoFrameProgress(fixture);
        var scheduler = CreateScheduler();
        var ingress = Assert.IsType<
            PlaybackIsoBmffRegularMovieSchedulerIngress>(
                new PlaybackIsoBmffRegularMovieSchedulerIngressFactory()
                    .Create(progress, scheduler).Ingress);
        var frame = TimingFrame(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            TimeSpan.FromMilliseconds(100));
        Assert.Equal(
            PlaybackSchedulerEnqueueResult.Enqueued,
            scheduler.Enqueue(
                frame,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None));
        var timing = new FakeSchedulerTimingOperations(
            new(
                PlaybackAvTimingDecisionResult.GenerationMismatch,
                TimeSpan.Zero),
            new(PlaybackAvTimingDecisionResult.ProcessNow, TimeSpan.Zero));
        var deliveryOperations = new FakeSchedulerDeliveryOperations(
            PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered);
        var delivery = new PlaybackIsoBmffRegularMovieSchedulerDelivery(
            ingress,
            scheduler,
            ingress.SourceSession,
            new NoOpSink(),
            deliveryOperations,
            timing,
            9,
            11);
        var reset = new FakeTimedDeliveryResetOperations();
        using var driver =
            new PlaybackIsoBmffRegularMovieTimedDeliveryDriver(
                delivery,
                new FakeTimedDeliveryDelay(),
                new(),
                reset);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult.ResetRequired,
            await driver.DriveOneAsync());
        Assert.False(frame.IsDisposed);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult.Reset,
            await driver.ResetAsync(13, 17));

        Assert.Equal([(13L, 17L)], reset.Reconfigurations);
        Assert.Equal([(13L, 17L)], reset.Commits);
        Assert.Equal([(13L, 17L)], timing.Resets);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Accepting,
            driver.Snapshot.Delivery.State);
        Assert.Equal(13, driver.Snapshot.Delivery.VideoGeneration);
        Assert.Equal(17, driver.Snapshot.Delivery.AudioGeneration);
        Assert.Equal(1, driver.Snapshot.ResetCount);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                .VideoDelivered,
            await driver.DriveOneAsync());
        Assert.True(frame.IsDisposed);
        Assert.Equal(13, timing.Items[1].Generation);
        Assert.Single(deliveryOperations.Calls);
    }

    [Fact]
    public async Task TimedDeliveryDriver_RollsBackOutputWhenTimingResetRejects()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var progress = CreateVideoFrameProgress(fixture);
        var scheduler = CreateScheduler();
        var ingress = Assert.IsType<
            PlaybackIsoBmffRegularMovieSchedulerIngress>(
                new PlaybackIsoBmffRegularMovieSchedulerIngressFactory()
                    .Create(progress, scheduler).Ingress);
        var frame = TimingFrame(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            TimeSpan.FromMilliseconds(100));
        Assert.Equal(
            PlaybackSchedulerEnqueueResult.Enqueued,
            scheduler.Enqueue(
                frame,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None));
        var timing = new FakeSchedulerTimingOperations(
            new PlaybackAvTimingDecision(
                PlaybackAvTimingDecisionResult.GenerationMismatch,
                TimeSpan.Zero))
        {
            ResetResult = PlaybackAvTimingResetResult.Failure,
        };
        var delivery = new PlaybackIsoBmffRegularMovieSchedulerDelivery(
            ingress,
            scheduler,
            ingress.SourceSession,
            new NoOpSink(),
            new FakeSchedulerDeliveryOperations(),
            timing,
            9,
            11);
        var reset = new FakeTimedDeliveryResetOperations();
        using var driver =
            new PlaybackIsoBmffRegularMovieTimedDeliveryDriver(
                delivery,
                new FakeTimedDeliveryDelay(),
                new(),
                reset);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult.ResetRequired,
            await driver.DriveOneAsync());

        Assert.Equal(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult
                .TimingRejected,
            await driver.ResetAsync(13, 17));

        Assert.Equal([(13L, 17L), (9L, 11L)], reset.Reconfigurations);
        Assert.Equal([(13L, 17L), (9L, 11L)], reset.Commits);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverState.Accepting,
            driver.Snapshot.State);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieSchedulerDeliveryState.ResetRequired,
            driver.Snapshot.Delivery.State);
        Assert.Equal(9, driver.Snapshot.Delivery.VideoGeneration);
        Assert.Equal(11, driver.Snapshot.Delivery.AudioGeneration);
        Assert.False(frame.IsDisposed);
    }

    [Fact]
    public async Task TimedDeliveryDriver_FaultsWhenResetRollbackCannotRestoreOutput()
    {
        using var fixture = new VerifiedBindingFixture(new(0, 1));
        var progress = CreateVideoFrameProgress(fixture);
        var scheduler = CreateScheduler();
        var ingress = Assert.IsType<
            PlaybackIsoBmffRegularMovieSchedulerIngress>(
                new PlaybackIsoBmffRegularMovieSchedulerIngressFactory()
                    .Create(progress, scheduler).Ingress);
        var frame = TimingFrame(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            TimeSpan.FromMilliseconds(100));
        Assert.Equal(
            PlaybackSchedulerEnqueueResult.Enqueued,
            scheduler.Enqueue(
                frame,
                0,
                PlaybackDecodedFrameDiscontinuityKind.None));
        var timing = new FakeSchedulerTimingOperations(
            new PlaybackAvTimingDecision(
                PlaybackAvTimingDecisionResult.GenerationMismatch,
                TimeSpan.Zero))
        {
            ResetResult = PlaybackAvTimingResetResult.Failure,
        };
        var deliveryOperations = new FakeSchedulerDeliveryOperations();
        var delivery = new PlaybackIsoBmffRegularMovieSchedulerDelivery(
            ingress,
            scheduler,
            ingress.SourceSession,
            new NoOpSink(),
            deliveryOperations,
            timing,
            9,
            11);
        var reset = new FakeTimedDeliveryResetOperations(
            PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                .Reconfigured,
            PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                .RollbackFaulted);
        using var driver =
            new PlaybackIsoBmffRegularMovieTimedDeliveryDriver(
                delivery,
                new FakeTimedDeliveryDelay(),
                new(),
                reset);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult.ResetRequired,
            await driver.DriveOneAsync());

        Assert.Equal(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult
                .RollbackFaulted,
            await driver.ResetAsync(13, 17));

        Assert.Equal(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverState.Faulted,
            driver.Snapshot.State);
        Assert.True(frame.IsDisposed);
        Assert.True(progress.IsDisposed);
        Assert.True(scheduler.IsDisposed);
        Assert.True(deliveryOperations.IsDisposed);
        Assert.True(reset.IsDisposed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AdvanceNativeDecode_FlushesOnlyTheExplicitSelectedRole(
        bool selectVideo)
    {
        var selection = selectVideo
            ? new PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest(
                0,
                null)
            : new PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest(
                null,
                1);
        using var fixture = new VerifiedBindingFixture(selection);
        var session = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecoderSession>(
                new PlaybackIsoBmffRegularMovieNativeDecoderSessionFactory(
                    new FakeNativeSessionOperations()).Open(fixture.Binding)
                    .Session);
        var submission = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativePacketSubmission>(
                new PlaybackIsoBmffRegularMovieNativePacketSubmissionFactory(
                    new FakePacketSubmissionOperations()).Create(session)
                    .Submission);
        var role = selectVideo
            ? PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video
            : PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Audio;
        var operations = new FakeDecodeProgressOperations(
            receives:
            [
                new(
                    role,
                    PlaybackDecoderFfmpegManagedFrameCopyResult.EndOfStream),
            ]);
        using var progress = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecodeProgress>(
                new PlaybackIsoBmffRegularMovieNativeDecodeProgressFactory(
                    operations).Create(submission).Progress);

        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .PacketSubmitted,
            progress.Advance().Result);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .PacketSubmitted,
            progress.Advance().Result);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                .PacketsExhausted,
            progress.Advance().Result);
        Assert.Equal(
            selectVideo
                ? PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                    .VideoFlushAccepted
                : PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                    .AudioFlushAccepted,
            progress.Advance().Result);
        Assert.Equal(
            selectVideo
                ? PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                    .VideoEndOfStream
                : PlaybackIsoBmffRegularMovieNativeDecodeProgressResult
                    .AudioEndOfStream,
            progress.Advance().Result);
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult.Completed,
            progress.Advance().Result);
        Assert.Equal([role], operations.FlushRoles);
    }

    private static void AssertFrame(
        PlaybackIsoBmffRegularMovieNativeDecodeProgress progress,
        long sequence,
        PlaybackDecoderFfmpegFrameMediaKind mediaKind)
    {
        var advance = progress.Advance();
        Assert.Equal(
            PlaybackIsoBmffRegularMovieNativeDecodeProgressResult.FrameCopied,
            advance.Result);
        var completion = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecodedFrameCompletion>(
                advance.Frame);
        Assert.Equal(sequence, completion.Sequence);
        Assert.Equal(mediaKind, completion.Frame.Metadata.MediaKind);
        completion.Dispose();
    }

    private static PlaybackIsoBmffRegularMovieNativeDecodeProgress
        CreateVideoFrameProgress(VerifiedBindingFixture fixture)
    {
        var session = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecoderSession>(
                new PlaybackIsoBmffRegularMovieNativeDecoderSessionFactory(
                    new FakeNativeSessionOperations()).Open(fixture.Binding)
                    .Session);
        var submission = Assert.IsType<
            PlaybackIsoBmffRegularMovieNativePacketSubmission>(
                new PlaybackIsoBmffRegularMovieNativePacketSubmissionFactory(
                    new FakePacketSubmissionOperations(
                        PlaybackMetadataAwareNativeSendResult.Again,
                        PlaybackMetadataAwareNativeSendResult.Success))
                    .Create(session).Submission);
        return Assert.IsType<
            PlaybackIsoBmffRegularMovieNativeDecodeProgress>(
                new PlaybackIsoBmffRegularMovieNativeDecodeProgressFactory(
                    new FakeDecodeProgressOperations(
                        receives:
                        [
                            new(
                                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole
                                    .Video,
                                PlaybackDecoderFfmpegManagedFrameCopyResult
                                    .Copied),
                        ])).Create(submission).Progress);
    }

    private static BoundedPlaybackScheduler CreateScheduler() =>
        new(
            new PlaybackSchedulerOptions
            {
                Capacity = 2,
                MaximumOwnedBytes = 1024,
                FullPolicy = PlaybackSchedulerFullPolicy.Reject,
            },
            new PlaybackPolicyClockBoundary(
                new FakeClock(),
                new PlaybackPolicyOptions()));

    private static PlaybackDecoderFfmpegManagedFrame TimingFrame(
        PlaybackDecoderFfmpegFrameMediaKind mediaKind,
        TimeSpan timestamp)
    {
        var video = mediaKind == PlaybackDecoderFfmpegFrameMediaKind.Video;
        return new(
            new PlaybackDecoderFfmpegFrameMetadata(
                mediaKind,
                video
                    ? PlaybackDecoderFfmpegFrameFormat.VideoBgra32
                    : PlaybackDecoderFfmpegFrameFormat.AudioSigned16,
                video ? 1 : 0,
                video ? 1 : 0,
                video ? 0 : 8_000,
                video ? 0 : 1,
                1,
                PlaybackDecoderFfmpegFrameTime.Known(timestamp),
                PlaybackDecoderFfmpegFrameTime.Known(timestamp),
                PlaybackDecoderFfmpegFrameTime.Known(
                    TimeSpan.FromMilliseconds(20))),
            [
                new PlaybackDecoderFfmpegManagedPlane(
                    0,
                    video ? 4 : 2,
                    1,
                    video ? 4 : 2,
                    new byte[video ? 4 : 2]),
            ]);
    }

    private static PlaybackIsoBmffRegularMovieManagedDecoderPreparationOptions
        Options() =>
        new()
        {
            DeclaredAvcSymbols =
                PlaybackIsoBmffAvcParameterTransferPlan.RequiredSymbols,
            AvcCapabilityDeclaration = new(
                PlaybackDecoderFfmpegCodecId.H264,
                PlaybackDecoderFfmpegCodecParametersFactory
                    .MaximumVideoDimension,
                PlaybackDecoderFfmpegCodecParametersFactory
                    .MaximumVideoDimension,
                PlaybackDecoderFfmpegCodecParametersFactory
                    .MaximumVideoPixelCount),
            DeclaredAacSymbols =
                PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
            AacCapabilityDeclaration = new(
                PlaybackDecoderFfmpegCodecId.Aac,
                8_000,
                96_000,
                6),
        };

    private static (
        PlaybackIsoBmffRegularMovieDemuxComposition Movie,
        byte[] VideoBox,
        byte[] AudioBox) MovieWithValidAac(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest
                selection)
    {
        var decoderSpecificInfo = Descriptor(0x05, [0x11, 0x90]);
        var decoderConfig = new byte[13 + decoderSpecificInfo.Length];
        decoderConfig[0] = 0x40;
        decoderConfig[1] = (byte)((5 << 2) | 1);
        decoderConfig[2] = 0x01;
        decoderConfig[3] = 0x02;
        decoderConfig[4] = 0x03;
        BinaryPrimitives.WriteUInt32BigEndian(
            decoderConfig.AsSpan(5, 4),
            128_000);
        BinaryPrimitives.WriteUInt32BigEndian(
            decoderConfig.AsSpan(9, 4),
            96_000);
        decoderSpecificInfo.CopyTo(decoderConfig, 13);
        var elementaryStream = ElementaryStream(
            Descriptor(0x04, decoderConfig),
            Descriptor(0x06, [2]));
        var (audioBox, _) =
            PlaybackIsoBmffElementaryStreamDescriptorParserTests
                .CreateAudioEntry(
                    PlaybackIsoBmffElementaryStreamDescriptorParserTests
                        .ElementaryStreamBox(elementaryStream));
        var audio = PlaybackIsoBmffRegularTrackDemuxCompositionTests
            .FixtureFor(audioBox, 0x736F756E);
        return ComposeMovie(audio, selection);
    }

    private static (
        PlaybackIsoBmffRegularMovieDemuxComposition Movie,
        byte[] VideoBox,
        byte[] AudioBox) MovieWithBaselineAac()
    {
        var audio = PlaybackIsoBmffRegularTrackDemuxCompositionTests
            .AudioFixture();
        return ComposeMovie(audio, new(0, 1));
    }

    private static (
        PlaybackIsoBmffRegularMovieDemuxComposition Movie,
        byte[] VideoBox,
        byte[] AudioBox) ComposeMovie(
            PlaybackIsoBmffRegularTrackDemuxCompositionTests.Fixture audio,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest
                selection)
    {
        var video = PlaybackIsoBmffRegularTrackDemuxCompositionTests
            .VideoFixture();
        audio = audio with
        {
            Sources = audio.Sources with
            {
                MovieTiming = video.Sources.MovieTiming,
                TrackIdentity = PlaybackIsoBmffTrackCodecPacketTests
                    .TrackIdentity(8),
            },
        };

        Assert.Equal(
            PlaybackIsoBmffRegularMovieDemuxCompositionResult.Composed,
            PlaybackIsoBmffRegularMovieDemuxComposer.Compose(
                video.File,
                [
                    new(
                        video.SampleDescriptionBox,
                        video.Sources,
                        new()
                        {
                            MaximumSampleCount = 2,
                            AvcRequirements = new(),
                        }),
                    new(
                        audio.SampleDescriptionBox,
                        audio.Sources,
                        new() { MaximumSampleCount = 2 }),
                ],
                selection,
                out var movie,
                new()
                {
                    MaximumTrackCount = 2,
                    MaximumMergeStepCount = 4,
                }));
        return (
            Assert.IsType<PlaybackIsoBmffRegularMovieDemuxComposition>(movie),
            video.SampleDescriptionBox,
            audio.SampleDescriptionBox);
    }

    private static byte[] ElementaryStream(params byte[][] children)
    {
        var payload = new List<byte> { 0, 1, 0 };
        foreach (var child in children)
        {
            payload.AddRange(child);
        }

        return Descriptor(0x03, payload.ToArray());
    }

    private static byte[] Descriptor(byte tag, byte[] payload)
    {
        Assert.True(payload.Length < 128);
        return [tag, (byte)payload.Length, .. payload];
    }

    private sealed class GateFixture : IDisposable
    {
        private readonly VerifiedPlaybackDecoderRuntimeLease _runtime;

        public GateFixture(IReadOnlyList<string> symbols)
        {
            var path = typeof(GateFixture).Assembly.Location;
            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            _runtime = new(
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["managed-test-module.dll"] = stream,
                },
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["managed-test-module.dll"] = path,
                },
                []);
            var addresses = new Dictionary<string, nint>(StringComparer.Ordinal);
            for (var index = 0; index < symbols.Count; index++)
            {
                addresses[symbols[index]] = (nint)(index + 1);
            }

            Gate = new(_runtime, new NoOpLoader(), [(nint)1], addresses);
        }

        public PlaybackDecoderFfmpegAbiSymbolGateLease Gate { get; }

        public void Dispose()
        {
            Gate.Dispose();
            _runtime.Dispose();
        }
    }

    private sealed class NoOpLoader : IPlaybackDecoderNativeLibraryLoader
    {
        public nint Load(
            string verifiedPath,
            PlaybackDecoderNativeSearchScope searchScope) =>
            throw new NotSupportedException();

        public nint GetSymbol(nint module, string symbol) =>
            throw new NotSupportedException();

        public uint InvokeVersion(nint symbol) =>
            throw new NotSupportedException();

        public void Free(nint module)
        {
        }
    }

    private sealed class VerifiedBindingFixture : IDisposable
    {
        public VerifiedBindingFixture(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest
                selection)
        {
            var (movie, _, _) = MovieWithValidAac(selection);
            Assert.Equal(
                Result.Prepared,
                PlaybackIsoBmffRegularMovieManagedDecoderPreparationComposer
                    .Compose(movie, out var preparation, Options()));
            var prepared = Assert.IsType<
                PlaybackIsoBmffRegularMovieManagedDecoderPreparation>(
                    preparation);
            VideoGate = new(
                PlaybackIsoBmffAvcParameterTransferPlan.RequiredSymbols);
            AudioGate = new(
                PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols);
            Assert.Equal(
                PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                    .Bound,
                PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinder.Bind(
                    prepared,
                    selection.VideoStreamIndex is null ? null : VideoGate.Gate,
                    selection.AudioStreamIndex is null ? null : AudioGate.Gate,
                    out var binding));
            Binding = Assert.IsType<
                PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinding>(
                    binding);
        }

        public PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinding
            Binding
        { get; }

        public GateFixture VideoGate { get; }

        public GateFixture AudioGate { get; }

        public void Dispose()
        {
            Binding.Dispose();
            VideoGate.Dispose();
            AudioGate.Dispose();
        }
    }

    private sealed class FakeNativeSessionOperations :
        IPlaybackIsoBmffRegularMovieNativeDecoderSessionOperations
    {
        public List<string> Calls { get; } = [];

        public List<FakeNativeContextOwner> Owners { get; } = [];

        public PlaybackDecoderFfmpegCodecOpenResult AudioOpenResult
        { get; init; } = PlaybackDecoderFfmpegCodecOpenResult.Opened;

        public PlaybackIsoBmffRegularMovieNativeDecoderContextCreation
            CreateContext(
                object gate,
                PlaybackDecoderFfmpegCodecId codecId)
        {
            Calls.Add($"create:{codecId}");
            var owner = new FakeNativeContextOwner(codecId, (IDisposable)gate);
            Owners.Add(owner);
            return new(
                PlaybackDecoderFfmpegContextLeaseResult.Created,
                owner);
        }

        public PlaybackDecoderFfmpegParameterTransferResult Transfer(
            IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner context,
            PlaybackDecoderFfmpegCodecParametersLease parameters)
        {
            Calls.Add($"transfer:{context.CodecId}");
            Assert.Equal(context.CodecId, parameters.CodecId);
            parameters.Dispose();
            return PlaybackDecoderFfmpegParameterTransferResult.Applied;
        }

        public PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult
            ConfigureTimeBase(
                IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner context,
                PlaybackBoundedDemuxTimeBase timeBase)
        {
            Calls.Add(
                $"time-base:{context.CodecId}:{timeBase.Numerator}/" +
                timeBase.Denominator);
            return PlaybackDecoderFfmpegPacketTimeBaseConfigurationResult
                .Configured;
        }

        public PlaybackDecoderFfmpegCodecOpenResult Open(
            IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner context)
        {
            Calls.Add($"open:{context.CodecId}");
            return context.CodecId == PlaybackDecoderFfmpegCodecId.Aac
                ? AudioOpenResult
                : PlaybackDecoderFfmpegCodecOpenResult.Opened;
        }

        public void DisposeUnusedGate(object gate) =>
            ((IDisposable)gate).Dispose();
    }

    private sealed class FakeNativeDecoderResetOperations :
        IPlaybackIsoBmffRegularMovieNativeDecoderResetOperations
    {
        private readonly Queue<
            PlaybackDecoderFfmpegDiscontinuityResetResult> _results;

        public FakeNativeDecoderResetOperations(
            params PlaybackDecoderFfmpegDiscontinuityResetResult[] results)
        {
            _results = new(results);
        }

        public List<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole> Roles
        { get; } = [];

        public PlaybackDecoderFfmpegDiscontinuityResetResult Reset(
            PlaybackIsoBmffRegularMovieNativeDecoderSession session,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role)
        {
            Assert.False(session.IsDisposed);
            Roles.Add(role);
            return _results.Count == 0
                ? PlaybackDecoderFfmpegDiscontinuityResetResult.Reset
                : _results.Dequeue();
        }
    }

    private sealed class FakeClock : IPlaybackMonotonicClock
    {
        public TimeSpan GetTimestamp() => TimeSpan.Zero;
    }

    private sealed class FakeSchedulerIngressOperations(
        PlaybackSchedulerEnqueueResult result) :
        IPlaybackIsoBmffRegularMovieSchedulerIngressOperations
    {
        public List<(
            PlaybackDecoderFfmpegManagedFrame Frame,
            long Sequence,
            PlaybackDecodedFrameDiscontinuityKind Discontinuity)> Calls
        { get; } = [];

        public PlaybackSchedulerEnqueueResult Enqueue(
            BoundedPlaybackScheduler scheduler,
            PlaybackDecoderFfmpegManagedFrame frame,
            long sequence,
            PlaybackDecodedFrameDiscontinuityKind discontinuity)
        {
            Assert.False(scheduler.IsDisposed);
            Calls.Add((frame, sequence, discontinuity));
            frame.Dispose();
            return result;
        }
    }

    private sealed class FakeSchedulerDeliveryOperations :
        IPlaybackIsoBmffRegularMovieSchedulerDeliveryOperations
    {
        private readonly Queue<
            PlaybackDecoderFfmpegAwaitedSinkDeliveryResult> _results;

        public FakeSchedulerDeliveryOperations(
            params PlaybackDecoderFfmpegAwaitedSinkDeliveryResult[] results)
        {
            _results = new(results);
        }

        public List<(
            PlaybackDecoderFfmpegManagedFrame Frame,
            long Sequence,
            PlaybackDecodedFrameDiscontinuityKind Discontinuity,
            IPlaybackDecodedFrameSink Sink)> Calls
        { get; } = [];

        public bool IsDisposed { get; private set; }

        public ValueTask<PlaybackDecoderFfmpegAwaitedSinkDeliveryResult>
            DeliverAsync(
                PlaybackIsoBmffRegularMovieNativeDecoderSession session,
                PlaybackDecoderFfmpegManagedFrame frame,
                long sequence,
                PlaybackDecodedFrameDiscontinuityKind discontinuity,
                IPlaybackDecodedFrameSink sink,
                CancellationToken cancellationToken)
        {
            Assert.False(IsDisposed);
            Assert.False(session.IsDisposed);
            Assert.False(frame.IsDisposed);
            Calls.Add((frame, sequence, discontinuity, sink));
            return ValueTask.FromResult(_results.Dequeue());
        }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class NoOpSink : IPlaybackDecodedFrameSink
    {
        public ValueTask WriteAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask WriteVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class BlockingSchedulerDeliveryOperations :
        IPlaybackIsoBmffRegularMovieSchedulerDeliveryOperations
    {
        private readonly TaskCompletionSource _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public async ValueTask<
            PlaybackDecoderFfmpegAwaitedSinkDeliveryResult> DeliverAsync(
                PlaybackIsoBmffRegularMovieNativeDecoderSession session,
                PlaybackDecoderFfmpegManagedFrame frame,
                long sequence,
                PlaybackDecodedFrameDiscontinuityKind discontinuity,
                IPlaybackDecodedFrameSink sink,
                CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered;
        }

        public void Release() => _release.TrySetResult();

        public void Dispose() => _release.TrySetCanceled();
    }

    private sealed class FakeSchedulerTimingOperations :
        IPlaybackIsoBmffRegularMovieSchedulerTimingOperations
    {
        private readonly Queue<PlaybackAvTimingDecision> _decisions;

        public FakeSchedulerTimingOperations(
            params PlaybackAvTimingDecision[] decisions)
        {
            _decisions = new(decisions);
        }

        public List<PlaybackAvTimingItem> Items { get; } = [];

        public List<(long VideoGeneration, long AudioGeneration)> Resets
        { get; } = [];

        public PlaybackAvTimingResetResult ResetResult { get; set; } =
            PlaybackAvTimingResetResult.Reset;

        public ValueTask<PlaybackAvTimingDecision> DecideAsync(
            PlaybackAvTimingItem item,
            CancellationToken cancellationToken)
        {
            Items.Add(item);
            return ValueTask.FromResult(_decisions.Dequeue());
        }

        public ValueTask<PlaybackAvTimingResetResult> ResetAsync(
            long videoGeneration,
            long audioGeneration,
            CancellationToken cancellationToken)
        {
            Resets.Add((videoGeneration, audioGeneration));
            return ValueTask.FromResult(ResetResult);
        }
    }

    private sealed class FakeTimedDeliveryDelay :
        IPlaybackIsoBmffRegularMovieTimedDeliveryDelay
    {
        public List<TimeSpan> Delays { get; } = [];

        public ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingTimedDeliveryDelay :
        IPlaybackIsoBmffRegularMovieTimedDeliveryDelay
    {
        private readonly TaskCompletionSource _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public async ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class FakeTimedDeliveryResetOperations :
        IPlaybackIsoBmffRegularMovieTimedDeliveryResetOperations
    {
        private readonly Queue<
            PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult>
            _results;

        public FakeTimedDeliveryResetOperations(
            params PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult[]
                results)
        {
            _results = new(results);
        }

        public List<(long VideoGeneration, long AudioGeneration)> Reconfigurations
        { get; } = [];

        public List<(long VideoGeneration, long AudioGeneration)> Commits
        { get; } = [];

        public bool IsDisposed { get; private set; }

        public ValueTask<
            PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult>
            ReconfigureOutputAsync(
                long videoGeneration,
                long audioGeneration,
                CancellationToken cancellationToken)
        {
            Assert.False(IsDisposed);
            Reconfigurations.Add((videoGeneration, audioGeneration));
            return ValueTask.FromResult(
                _results.Count == 0
                    ? PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                        .Reconfigured
                    : _results.Dequeue());
        }

        public bool CommitSink(long videoGeneration, long audioGeneration)
        {
            Assert.False(IsDisposed);
            Commits.Add((videoGeneration, audioGeneration));
            return true;
        }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class FakeNativeContextOwner(
        PlaybackDecoderFfmpegCodecId codecId,
        IDisposable gate) :
        IPlaybackIsoBmffRegularMovieNativeDecoderContextOwner
    {
        public PlaybackDecoderFfmpegCodecId CodecId { get; } = codecId;

        public PlaybackDecoderFfmpegContextLease? NativeLease => null;

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            gate.Dispose();
        }
    }

    private sealed class FakePacketSubmissionOperations :
        IPlaybackIsoBmffRegularMovieNativePacketSubmissionOperations
    {
        private readonly Queue<PlaybackMetadataAwareNativeSendResult>
            _sendResults;

        public FakePacketSubmissionOperations(
            params PlaybackMetadataAwareNativeSendResult[] sendResults)
        {
            _sendResults = new(sendResults);
        }

        public List<PlaybackMetadataAwareNativePacketInput> Inputs { get; } =
            [];

        public List<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole> Roles
        { get; } = [];

        public List<FakePacketOwner> Owners { get; } = [];

        public PlaybackMetadataAwareNativePacketCreation CreatePacket(
            PlaybackIsoBmffRegularMovieNativeDecoderSession session,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role,
            PlaybackMetadataAwareNativePacketInput input)
        {
            Assert.False(session.IsDisposed);
            Inputs.Add(input);
            Roles.Add(role);
            var owner = new FakePacketOwner();
            Owners.Add(owner);
            return new(
                PlaybackMetadataAwareNativeSendResult.Success,
                owner);
        }

        public PlaybackMetadataAwareNativeSendResult SendOnce(
            IPlaybackMetadataAwareNativePacketOwner packet,
            PlaybackIsoBmffRegularMovieNativeDecoderSession session,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role)
        {
            Assert.False(session.IsDisposed);
            Assert.Same(Owners[^1], packet);
            var owner = Assert.IsType<FakePacketOwner>(packet);
            owner.SendCount++;
            return _sendResults.Count == 0
                ? PlaybackMetadataAwareNativeSendResult.Success
                : _sendResults.Dequeue();
        }
    }

    private sealed class FakePacketOwner :
        IPlaybackMetadataAwareNativePacketOwner
    {
        public int SendCount { get; set; }

        public bool IsDisposed { get; private set; }

        public PlaybackMetadataAwareNativeSendResult SendOnce(
            PlaybackBoundedFlvNativeDecoderSession session,
            PlaybackBoundedDemuxMediaKind mediaKind) =>
            throw new NotSupportedException();

        public void Dispose() => IsDisposed = true;
    }

    private readonly record struct FakeReceive(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole Role,
        PlaybackDecoderFfmpegManagedFrameCopyResult Result);

    private sealed class FakeDecodeProgressOperations :
        IPlaybackIsoBmffRegularMovieNativeDecodeProgressOperations
    {
        private readonly Queue<PlaybackMetadataAwareNativeSendResult>
            _flushes;
        private readonly Queue<FakeReceive> _receives;

        public FakeDecodeProgressOperations(
            PlaybackMetadataAwareNativeSendResult[]? flushes = null,
            FakeReceive[]? receives = null)
        {
            _flushes = new(flushes ?? []);
            _receives = new(receives ?? []);
        }

        public List<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole>
                FlushRoles
        { get; } = [];

        public PlaybackMetadataAwareNativeSendResult SendFlush(
            PlaybackIsoBmffRegularMovieNativeDecoderSession session,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role)
        {
            Assert.False(session.IsDisposed);
            FlushRoles.Add(role);
            return _flushes.Count == 0
                ? PlaybackMetadataAwareNativeSendResult.Success
                : _flushes.Dequeue();
        }

        public PlaybackIsoBmffRegularMovieNativeDecodeReceive ReceiveOne(
            PlaybackIsoBmffRegularMovieNativeDecoderSession session,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role)
        {
            Assert.False(session.IsDisposed);
            var instruction = _receives.Dequeue();
            Assert.Equal(instruction.Role, role);
            return new(
                instruction.Result,
                instruction.Result ==
                    PlaybackDecoderFfmpegManagedFrameCopyResult.Copied
                        ? Frame(role)
                        : null);
        }

        private static PlaybackDecoderFfmpegManagedFrame Frame(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role)
        {
            var isVideo = role ==
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video;
            var metadata = new PlaybackDecoderFfmpegFrameMetadata(
                isVideo
                    ? PlaybackDecoderFfmpegFrameMediaKind.Video
                    : PlaybackDecoderFfmpegFrameMediaKind.Audio,
                isVideo
                    ? PlaybackDecoderFfmpegFrameFormat.VideoBgra32
                    : PlaybackDecoderFfmpegFrameFormat.AudioSigned16,
                isVideo ? 1 : 0,
                isVideo ? 1 : 0,
                isVideo ? 0 : 8_000,
                isVideo ? 0 : 1,
                isVideo ? 0 : 1,
                PlaybackDecoderFfmpegFrameTime.Unknown,
                PlaybackDecoderFfmpegFrameTime.Unknown,
                PlaybackDecoderFfmpegFrameTime.Unknown);
            return new(
                metadata,
                [
                    new PlaybackDecoderFfmpegManagedPlane(
                        0,
                        isVideo ? 4 : 2,
                        1,
                        isVideo ? 4 : 2,
                        new byte[isVideo ? 4 : 2]),
                ]);
        }
    }
}
