using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveMultiTrackEpochInputBinderTests
{
    private const uint Ftyp = 0x66747970;
    private const uint Moov = 0x6d6f6f76;
    private const uint Trak = 0x7472616b;
    private const uint Tkhd = 0x746b6864;
    private const uint Mdia = 0x6d646961;
    private const uint Hdlr = 0x68646c72;
    private const uint Minf = 0x6d696e66;
    private const uint Stbl = 0x7374626c;
    private const uint Mvex = 0x6d766578;
    private const uint Trex = 0x74726578;
    private const uint Vide = 0x76696465;
    private const uint Soun = 0x736f756e;
    private const uint Moof = 0x6d6f6f66;
    private const uint Mfhd = 0x6d666864;
    private const uint Traf = 0x74726166;
    private const uint Tfhd = 0x74666864;
    private const uint Tfdt = 0x74666474;
    private const uint Trun = 0x7472756e;
    private const int TkhdBoxLength = 92;

    [Fact]
    public void Bind_PreservesProfileIdentityAndNormalizesResolvedRuns()
    {
        var defaults = ProjectInitializationDefaults();
        var fragment = MovieFragment(
            41,
            TrackFragment(9, 48_000, TrackRun(1)),
            TrackFragment(7, 90_000, TrackRun(1)));
        var resolvedRuns = ProjectResolvedRuns(defaults, fragment);

        var result = PlaybackFmp4LiveMultiTrackEpochInputBinder.Bind(
            resolvedRuns,
            out var input);

        Assert.Equal(
            PlaybackFmp4LiveMultiTrackEpochInputBindingResult.Bound,
            result);
        var bound = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochInput>(input);
        Assert.Same(defaults.Source, bound.InitializationProfile);
        Assert.Same(resolvedRuns, bound.ResolvedRuns);
        Assert.Equal(41U, bound.SequenceNumber);
        Assert.Collection(
            bound.Tracks,
            track =>
            {
                Assert.Equal(7U, track.TrackId);
                Assert.Equal(PlaybackIsoBmffHandlerKind.Video, track.MediaKind);
                Assert.Same(
                    bound.InitializationProfile.VideoIdentity,
                    track.InitializationIdentity);
                Assert.Equal(90_000UL, track.TimedRun.BaseMediaDecodeTime);
            },
            track =>
            {
                Assert.Equal(9U, track.TrackId);
                Assert.Equal(PlaybackIsoBmffHandlerKind.Audio, track.MediaKind);
                Assert.Same(
                    bound.InitializationProfile.AudioIdentity,
                    track.InitializationIdentity);
                Assert.Equal(48_000UL, track.TimedRun.BaseMediaDecodeTime);
            });

        Assert.Equal(
            bound.Tracks.Select(track => track.TimedRun.TrackId),
            bound.TimedRuns.Select(run => run.TrackId));
        Assert.Equal(
            PlaybackFmp4LiveEpochStateCreateResult.Created,
            PlaybackFmp4LiveMultiTrackEpochStateMachine.Create(
                bound,
                out var state));
        Assert.Equal(
            [7U, 9U],
            Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(state)
                .Tracks
                .Select(track => track.TrackId));
    }

    [Theory]
    [InlineData(7U)]
    [InlineData(9U)]
    public void Bind_PreservesLegalSingleTrackFragmentOmission(uint trackId)
    {
        var defaults = ProjectInitializationDefaults();
        var fragment = MovieFragment(
            42,
            TrackFragment(
                trackId,
                trackId == 7 ? 90_000UL : 48_000UL,
                TrackRun(1)));
        var resolvedRuns = ProjectResolvedRuns(defaults, fragment);

        var result = PlaybackFmp4LiveMultiTrackEpochInputBinder.Bind(
            resolvedRuns,
            out var input);

        Assert.Equal(
            PlaybackFmp4LiveMultiTrackEpochInputBindingResult.Bound,
            result);
        var bound = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochInput>(input);
        var track = Assert.Single(bound.Tracks);
        Assert.Equal(trackId, track.TrackId);
        Assert.Equal(
            trackId == 7
                ? PlaybackIsoBmffHandlerKind.Video
                : PlaybackIsoBmffHandlerKind.Audio,
            track.MediaKind);
    }

    [Fact]
    public void Bind_RejectsResolvedRunsFromDifferentProfileInstance()
    {
        var defaults = ProjectInitializationDefaults();
        var differentProfileDefaults = ProjectInitializationDefaults();
        var fragment = MovieFragment(
            43,
            TrackFragment(7, 90_000, TrackRun(1)));
        var resolvedRuns = ProjectResolvedRuns(
            defaults,
            fragment,
            differentProfileDefaults.Source);

        var result = PlaybackFmp4LiveMultiTrackEpochInputBinder.Bind(
            resolvedRuns,
            out var input);

        Assert.Equal(
            PlaybackFmp4LiveMultiTrackEpochInputBindingResult
                .InitializationProfileMismatch,
            result);
        Assert.Null(input);
    }

    [Fact]
    public void Bind_MissingProjectionFailsClosed()
    {
        var result = PlaybackFmp4LiveMultiTrackEpochInputBinder.Bind(
            null,
            out var input);

        Assert.Equal(
            PlaybackFmp4LiveMultiTrackEpochInputBindingResult
                .MissingResolvedRunProjection,
            result);
        Assert.Null(input);
    }

    [Fact]
    public void Advance_ProfileBoundInputPreservesSameProfileAssociation()
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                41,
                TrackFragment(9, 48_000, TrackRun(1)),
                TrackFragment(7, 90_000, TrackRun(1))));
        var second = BindInput(
            defaults,
            MovieFragment(
                42,
                TrackFragment(7, 91_001, TrackRun(1)),
                TrackFragment(9, 49_024, TrackRun(1))));

        Assert.Equal(
            PlaybackFmp4LiveEpochStateCreateResult.Created,
            PlaybackFmp4LiveMultiTrackEpochStateMachine.Create(
                first,
                out var state));
        var created = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(
            state);
        Assert.Same(defaults.Source, created.InitializationProfile);

        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            created,
            second,
            [
                BindContinuity(first, second, 9),
                BindContinuity(first, second, 7),
            ],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            result);
        var advanced = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(
            next);
        Assert.Same(defaults.Source, advanced.InitializationProfile);
        Assert.Equal(42U, advanced.SequenceNumber);
    }

    [Fact]
    public void Advance_DifferentProfileInstanceRejectsWithoutStateChange()
    {
        var firstDefaults = ProjectInitializationDefaults();
        var secondDefaults = ProjectInitializationDefaults();
        var first = BindInput(
            firstDefaults,
            MovieFragment(
                41,
                TrackFragment(7, 90_000, TrackRun(1)),
                TrackFragment(9, 48_000, TrackRun(1))));
        var second = BindInput(
            secondDefaults,
            MovieFragment(
                42,
                TrackFragment(7, 91_001, TrackRun(1)),
                TrackFragment(9, 49_024, TrackRun(1))));

        Assert.Equal(
            PlaybackFmp4LiveEpochStateCreateResult.Created,
            PlaybackFmp4LiveMultiTrackEpochStateMachine.Create(
                first,
                out var state));
        var created = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(
            state);

        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            created,
            second,
            [
                BindContinuity(first, second, 7),
                BindContinuity(first, second, 9),
            ],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch,
            result);
        Assert.Null(next);
        Assert.Same(first.TimedRuns[0], created.Tracks[0].TimedRun);
        Assert.Same(first.TimedRuns[1], created.Tracks[1].TimedRun);
    }

    [Theory]
    [InlineData(7U, 90_000UL, 91_001UL)]
    [InlineData(9U, 48_000UL, 49_024UL)]
    public void Advance_ProfileBoundLegalSingleTrackFragmentRemainsAccepted(
        uint trackId,
        ulong firstDecodeTime,
        ulong secondDecodeTime)
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                51,
                TrackFragment(trackId, firstDecodeTime, TrackRun(1))));
        var second = BindInput(
            defaults,
            MovieFragment(
                52,
                TrackFragment(trackId, secondDecodeTime, TrackRun(1))));

        Assert.Equal(
            PlaybackFmp4LiveEpochStateCreateResult.Created,
            PlaybackFmp4LiveMultiTrackEpochStateMachine.Create(
                first,
                out var state));

        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            second,
            [BindContinuity(first, second, trackId)],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            result);
        Assert.NotNull(next);
    }

    [Fact]
    public void Advance_VideoOnlyInitialAcceptsAudioFirstEntry()
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                141,
                TrackFragment(7, 90_000, TrackRun(1))));
        var second = BindInput(
            defaults,
            MovieFragment(
                142,
                TrackFragment(9, 48_000, TrackRun(1)),
                TrackFragment(7, 91_001, TrackRun(1))));
        var state = CreateState(first);

        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            second,
            [BindContinuity(state, second, 7)],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            result);
        var advanced = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(
            next);
        Assert.Equal(0UL, advanced.EpochId);
        Assert.Equal([7U, 9U], advanced.Tracks.Select(track => track.TrackId));
        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityKind.Continuous,
            FindTrack(advanced, 7).EntryContinuity);
        var audio = FindTrack(advanced, 9);
        Assert.Same(
            Assert.Single(second.TimedRuns, run => run.TrackId == 9),
            audio.TimedRun);
        Assert.Null(audio.EntryContinuity);
        Assert.True(audio.CurrentEpochMember);
        Assert.True(audio.ContinuityAnchorValid);
        Assert.True(audio.PresentInCurrentFragment);
    }

    [Fact]
    public void Advance_AudioOnlyInitialAcceptsVideoFirstEntrySymmetrically()
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                151,
                TrackFragment(9, 48_000, TrackRun(1))));
        var second = BindInput(
            defaults,
            MovieFragment(
                152,
                TrackFragment(7, 90_000, TrackRun(1)),
                TrackFragment(9, 49_024, TrackRun(1))));
        var state = CreateState(first);

        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            second,
            [BindContinuity(state, second, 9)],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            result);
        var advanced = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(
            next);
        Assert.Equal(0UL, advanced.EpochId);
        Assert.Equal([7U, 9U], advanced.Tracks.Select(track => track.TrackId));
        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityKind.Continuous,
            FindTrack(advanced, 9).EntryContinuity);
        Assert.Null(FindTrack(advanced, 7).EntryContinuity);
    }

    [Fact]
    public void Advance_DelayedFirstEntryAfterContinuousOmissionsIsAccepted()
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                161,
                TrackFragment(7, 90_000, TrackRun(1))));
        var second = BindInput(
            defaults,
            MovieFragment(
                162,
                TrackFragment(7, 91_001, TrackRun(1))));
        var third = BindInput(
            defaults,
            MovieFragment(
                163,
                TrackFragment(7, 92_002, TrackRun(1))));
        var fourth = BindInput(
            defaults,
            MovieFragment(
                164,
                TrackFragment(7, 93_003, TrackRun(1)),
                TrackFragment(9, 48_000, TrackRun(1))));
        var state = CreateState(first);

        state = AdvanceState(
            state,
            second,
            [BindContinuity(state, second, 7)]);
        state = AdvanceState(
            state,
            third,
            [BindContinuity(state, third, 7)]);
        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            fourth,
            [BindContinuity(state, fourth, 7)],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            result);
        var advanced = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(
            next);
        Assert.Equal(0UL, advanced.EpochId);
        Assert.Equal([7U, 9U], advanced.Tracks.Select(track => track.TrackId));
        Assert.Null(FindTrack(advanced, 9).EntryContinuity);
    }

    [Fact]
    public void Advance_ExistingTrackDiscontinuityAndFirstEntryIncrementEpochOnce()
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                171,
                TrackFragment(7, 90_000, TrackRun(1))));
        var second = BindInput(
            defaults,
            MovieFragment(
                172,
                TrackFragment(7, 92_000, TrackRun(1)),
                TrackFragment(9, 48_000, TrackRun(1))));
        var state = CreateState(first);

        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            second,
            [BindContinuity(state, second, 7)],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            result);
        var advanced = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(
            next);
        Assert.Equal(1UL, advanced.EpochId);
        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityKind.DecodeDiscontinuity,
            FindTrack(advanced, 7).EntryContinuity);
        Assert.Null(FindTrack(advanced, 9).EntryContinuity);
    }

    [Fact]
    public void Advance_FirstEntryOnlyWithExistingTrackOmittedDoesNotIncrementEpoch()
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                181,
                TrackFragment(7, 90_000, TrackRun(1))));
        var second = BindInput(
            defaults,
            MovieFragment(
                182,
                TrackFragment(9, 48_000, TrackRun(1))));
        var state = CreateState(first);

        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            second,
            Array.Empty<PlaybackIsoBmffFragmentContinuityBinding>(),
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            result);
        var advanced = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(
            next);
        Assert.Equal(0UL, advanced.EpochId);
        var video = FindTrack(advanced, 7);
        Assert.Same(first.TimedRuns[0], video.TimedRun);
        Assert.True(video.CurrentEpochMember);
        Assert.True(video.ContinuityAnchorValid);
        Assert.False(video.PresentInCurrentFragment);
        var audio = FindTrack(advanced, 9);
        Assert.Same(second.TimedRuns[0], audio.TimedRun);
        Assert.True(audio.CurrentEpochMember);
        Assert.True(audio.ContinuityAnchorValid);
        Assert.True(audio.PresentInCurrentFragment);
        Assert.Null(audio.EntryContinuity);
    }

    [Fact]
    public void Advance_UnknownProfileTrackRejectsWithoutStateChange()
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                191,
                TrackFragment(7, 90_000, TrackRun(1))));
        var state = CreateState(first);
        var unknown = TimedRun(11, 1_000);
        var malformed = CreateInputWithTimedRuns(
            first,
            192,
            [unknown]);

        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            malformed,
            Array.Empty<PlaybackIsoBmffFragmentContinuityBinding>(),
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch,
            result);
        Assert.Null(next);
        Assert.Single(state.Tracks);
        Assert.Same(first.TimedRuns[0], state.Tracks[0].TimedRun);
    }

    [Fact]
    public void Advance_DuplicateIncomingTrackRejectsWithoutStateChange()
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                201,
                TrackFragment(7, 90_000, TrackRun(1))));
        var state = CreateState(first);
        var malformed = CreateInputWithTimedRuns(
            first,
            202,
            [first.TimedRuns[0], first.TimedRuns[0]]);

        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            malformed,
            Array.Empty<PlaybackIsoBmffFragmentContinuityBinding>(),
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch,
            result);
        Assert.Null(next);
        Assert.Single(state.Tracks);
        Assert.Same(first.TimedRuns[0], state.Tracks[0].TimedRun);
    }

    [Fact]
    public void Advance_ObservedTrackCannotBypassContinuityAsFirstEntry()
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                211,
                TrackFragment(7, 90_000, TrackRun(1))));
        var second = BindInput(
            defaults,
            MovieFragment(
                212,
                TrackFragment(7, 91_001, TrackRun(1))));
        var state = CreateState(first);

        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            second,
            Array.Empty<PlaybackIsoBmffFragmentContinuityBinding>(),
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch,
            result);
        Assert.Null(next);
        Assert.Same(first.TimedRuns[0], state.Tracks[0].TimedRun);
    }

    [Fact]
    public void Advance_VideoOnlyContinuousOmissionRetainsAudioAnchor()
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                61,
                TrackFragment(7, 90_000, TrackRun(1)),
                TrackFragment(9, 48_000, TrackRun(1))));
        var videoOnly = BindInput(
            defaults,
            MovieFragment(
                62,
                TrackFragment(7, 91_001, TrackRun(1))));
        var state = CreateState(first);

        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            videoOnly,
            [BindContinuity(first, videoOnly, 7)],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            result);
        var advanced = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(
            next);
        Assert.Equal(0UL, advanced.EpochId);
        Assert.Equal(62U, advanced.SequenceNumber);
        var video = FindTrack(advanced, 7);
        Assert.Same(videoOnly.TimedRuns[0], video.TimedRun);
        Assert.True(video.CurrentEpochMember);
        Assert.True(video.ContinuityAnchorValid);
        Assert.True(video.PresentInCurrentFragment);
        var audio = FindTrack(advanced, 9);
        Assert.Same(first.TimedRuns[1], audio.TimedRun);
        Assert.True(audio.CurrentEpochMember);
        Assert.True(audio.ContinuityAnchorValid);
        Assert.False(audio.PresentInCurrentFragment);
    }

    [Fact]
    public void Advance_AudioOnlyContinuousOmissionRetainsVideoAnchor()
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                71,
                TrackFragment(7, 90_000, TrackRun(1)),
                TrackFragment(9, 48_000, TrackRun(1))));
        var audioOnly = BindInput(
            defaults,
            MovieFragment(
                72,
                TrackFragment(9, 49_024, TrackRun(1))));
        var state = CreateState(first);

        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            audioOnly,
            [BindContinuity(first, audioOnly, 9)],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            result);
        var advanced = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(
            next);
        Assert.Equal(0UL, advanced.EpochId);
        var video = FindTrack(advanced, 7);
        Assert.Same(first.TimedRuns[0], video.TimedRun);
        Assert.True(video.CurrentEpochMember);
        Assert.True(video.ContinuityAnchorValid);
        Assert.False(video.PresentInCurrentFragment);
        var audio = FindTrack(advanced, 9);
        Assert.Same(audioOnly.TimedRuns[0], audio.TimedRun);
        Assert.True(audio.CurrentEpochMember);
        Assert.True(audio.ContinuityAnchorValid);
        Assert.True(audio.PresentInCurrentFragment);
    }

    [Fact]
    public void Advance_ContinuousOmissionThenReentryUsesLastObservedAnchor()
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                81,
                TrackFragment(7, 90_000, TrackRun(1)),
                TrackFragment(9, 48_000, TrackRun(1))));
        var videoOnly = BindInput(
            defaults,
            MovieFragment(
                82,
                TrackFragment(7, 91_001, TrackRun(1))));
        var reentry = BindInput(
            defaults,
            MovieFragment(
                83,
                TrackFragment(7, 92_002, TrackRun(1)),
                TrackFragment(9, 49_024, TrackRun(1))));
        var state = CreateState(first);

        state = AdvanceState(
            state,
            videoOnly,
            [BindContinuity(first, videoOnly, 7)]);
        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            reentry,
            [
                BindContinuity(state, reentry, 7),
                BindContinuity(state, reentry, 9),
            ],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            result);
        var advanced = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(
            next);
        Assert.Equal(0UL, advanced.EpochId);
        Assert.Same(reentry.TimedRuns[1], FindTrack(advanced, 9).TimedRun);
        Assert.True(FindTrack(advanced, 9).CurrentEpochMember);
        Assert.True(FindTrack(advanced, 9).ContinuityAnchorValid);
        Assert.True(FindTrack(advanced, 9).PresentInCurrentFragment);
    }

    [Fact]
    public void Advance_MultipleContinuousOmissionsDoNotInvalidateAnchor()
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                91,
                TrackFragment(7, 90_000, TrackRun(1)),
                TrackFragment(9, 48_000, TrackRun(1))));
        var firstVideoOnly = BindInput(
            defaults,
            MovieFragment(
                92,
                TrackFragment(7, 91_001, TrackRun(1))));
        var secondVideoOnly = BindInput(
            defaults,
            MovieFragment(
                93,
                TrackFragment(7, 92_002, TrackRun(1))));
        var reentry = BindInput(
            defaults,
            MovieFragment(
                94,
                TrackFragment(7, 93_003, TrackRun(1)),
                TrackFragment(9, 49_024, TrackRun(1))));
        var state = CreateState(first);

        state = AdvanceState(
            state,
            firstVideoOnly,
            [BindContinuity(first, firstVideoOnly, 7)]);
        state = AdvanceState(
            state,
            secondVideoOnly,
            [BindContinuity(state, secondVideoOnly, 7)]);
        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            reentry,
            [
                BindContinuity(state, reentry, 7),
                BindContinuity(state, reentry, 9),
            ],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            result);
        var advanced = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(
            next);
        Assert.Equal(0UL, advanced.EpochId);
        Assert.Same(reentry.TimedRuns[1], FindTrack(advanced, 9).TimedRun);
    }

    [Fact]
    public void Advance_VideoDiscontinuityWithAudioOmissionInvalidatesAudioAnchor()
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                101,
                TrackFragment(7, 90_000, TrackRun(1)),
                TrackFragment(9, 48_000, TrackRun(1))));
        var discontinuousVideoOnly = BindInput(
            defaults,
            MovieFragment(
                102,
                TrackFragment(7, 92_000, TrackRun(1))));
        var state = CreateState(first);

        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            discontinuousVideoOnly,
            [BindContinuity(first, discontinuousVideoOnly, 7)],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            result);
        var advanced = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(
            next);
        Assert.Equal(1UL, advanced.EpochId);
        var video = FindTrack(advanced, 7);
        Assert.Same(discontinuousVideoOnly.TimedRuns[0], video.TimedRun);
        Assert.True(video.CurrentEpochMember);
        Assert.True(video.ContinuityAnchorValid);
        Assert.True(video.PresentInCurrentFragment);
        var audio = FindTrack(advanced, 9);
        Assert.Same(first.TimedRuns[1], audio.TimedRun);
        Assert.False(audio.CurrentEpochMember);
        Assert.False(audio.ContinuityAnchorValid);
        Assert.False(audio.PresentInCurrentFragment);
    }

    [Fact]
    public void Advance_AudioDiscontinuityWithVideoOmissionIsSymmetric()
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                111,
                TrackFragment(7, 90_000, TrackRun(1)),
                TrackFragment(9, 48_000, TrackRun(1))));
        var discontinuousAudioOnly = BindInput(
            defaults,
            MovieFragment(
                112,
                TrackFragment(9, 50_000, TrackRun(1))));
        var state = CreateState(first);

        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            discontinuousAudioOnly,
            [BindContinuity(first, discontinuousAudioOnly, 9)],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            result);
        var advanced = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(
            next);
        Assert.Equal(1UL, advanced.EpochId);
        Assert.False(FindTrack(advanced, 7).CurrentEpochMember);
        Assert.False(FindTrack(advanced, 7).ContinuityAnchorValid);
        Assert.True(FindTrack(advanced, 9).CurrentEpochMember);
        Assert.True(FindTrack(advanced, 9).ContinuityAnchorValid);
    }

    [Fact]
    public void Advance_InvalidAnchorReentryUsesFreshEntryWithoutExtraEpoch()
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                121,
                TrackFragment(7, 90_000, TrackRun(1)),
                TrackFragment(9, 48_000, TrackRun(1))));
        var discontinuousVideoOnly = BindInput(
            defaults,
            MovieFragment(
                122,
                TrackFragment(7, 92_000, TrackRun(1))));
        var reentry = BindInput(
            defaults,
            MovieFragment(
                123,
                TrackFragment(7, 93_001, TrackRun(1)),
                TrackFragment(9, 50_000, TrackRun(1))));
        var state = CreateState(first);

        state = AdvanceState(
            state,
            discontinuousVideoOnly,
            [BindContinuity(first, discontinuousVideoOnly, 7)]);
        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            reentry,
            [BindContinuity(state, reentry, 7)],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            result);
        var advanced = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(
            next);
        Assert.Equal(1UL, advanced.EpochId);
        var audio = FindTrack(advanced, 9);
        Assert.Same(reentry.TimedRuns[1], audio.TimedRun);
        Assert.True(audio.CurrentEpochMember);
        Assert.True(audio.ContinuityAnchorValid);
        Assert.True(audio.PresentInCurrentFragment);
        Assert.Null(audio.EntryContinuity);
    }

    [Fact]
    public void Advance_ValidAnchorDiscontinuousReentryAdvancesEpochOnce()
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                126,
                TrackFragment(7, 90_000, TrackRun(1)),
                TrackFragment(9, 48_000, TrackRun(1))));
        var videoOnly = BindInput(
            defaults,
            MovieFragment(
                127,
                TrackFragment(7, 91_001, TrackRun(1))));
        var reentry = BindInput(
            defaults,
            MovieFragment(
                128,
                TrackFragment(7, 92_002, TrackRun(1)),
                TrackFragment(9, 50_000, TrackRun(1))));
        var state = CreateState(first);

        state = AdvanceState(
            state,
            videoOnly,
            [BindContinuity(first, videoOnly, 7)]);
        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            reentry,
            [
                BindContinuity(state, reentry, 7),
                BindContinuity(state, reentry, 9),
            ],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            result);
        var advanced = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(
            next);
        Assert.Equal(1UL, advanced.EpochId);
        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityKind.DecodeDiscontinuity,
            FindTrack(advanced, 9).EntryContinuity);
        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityKind.Continuous,
            FindTrack(advanced, 7).EntryContinuity);
    }

    [Fact]
    public void Advance_InvalidAnchorRejectsStaleNormalContinuity()
    {
        var defaults = ProjectInitializationDefaults();
        var first = BindInput(
            defaults,
            MovieFragment(
                131,
                TrackFragment(7, 90_000, TrackRun(1)),
                TrackFragment(9, 48_000, TrackRun(1))));
        var discontinuousVideoOnly = BindInput(
            defaults,
            MovieFragment(
                132,
                TrackFragment(7, 92_000, TrackRun(1))));
        var reentry = BindInput(
            defaults,
            MovieFragment(
                133,
                TrackFragment(7, 93_001, TrackRun(1)),
                TrackFragment(9, 50_000, TrackRun(1))));
        var state = CreateState(first);

        state = AdvanceState(
            state,
            discontinuousVideoOnly,
            [BindContinuity(first, discontinuousVideoOnly, 7)]);
        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            reentry,
            [
                BindContinuity(state, reentry, 7),
                BindContinuity(
                    state.FragmentHeader,
                    reentry.FragmentHeader,
                    first.TimedRuns[1],
                    reentry.TimedRuns[1]),
            ],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch,
            result);
        Assert.Null(next);
        var audio = FindTrack(state, 9);
        Assert.Same(first.TimedRuns[1], audio.TimedRun);
        Assert.False(audio.CurrentEpochMember);
        Assert.False(audio.ContinuityAnchorValid);
        Assert.False(audio.PresentInCurrentFragment);
    }

    private static PlaybackFmp4LiveMultiTrackEpochState CreateState(
        PlaybackFmp4LiveMultiTrackEpochInput input)
    {
        Assert.Equal(
            PlaybackFmp4LiveEpochStateCreateResult.Created,
            PlaybackFmp4LiveMultiTrackEpochStateMachine.Create(
                input,
                out var state));
        return Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(state);
    }

    private static PlaybackFmp4LiveMultiTrackEpochState AdvanceState(
        PlaybackFmp4LiveMultiTrackEpochState state,
        PlaybackFmp4LiveMultiTrackEpochInput input,
        IReadOnlyList<PlaybackIsoBmffFragmentContinuityBinding> continuities)
    {
        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
                state,
                input,
                continuities,
                out var next));
        return Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(next);
    }

    private static PlaybackFmp4LiveMultiTrackEpochTrackState FindTrack(
        PlaybackFmp4LiveMultiTrackEpochState state,
        uint trackId) =>
        Assert.Single(state.Tracks, track => track.TrackId == trackId);

    private static PlaybackFmp4LiveMultiTrackEpochInput CreateInputWithTimedRuns(
        PlaybackFmp4LiveMultiTrackEpochInput template,
        uint sequenceNumber,
        IReadOnlyList<PlaybackIsoBmffTimedFragmentRun> timedRuns) =>
        new(
            template.InitializationProfile,
            template.ResolvedRuns,
            new PlaybackIsoBmffMovieFragmentHeaderBox(sequenceNumber),
            new ReadOnlyCollection<PlaybackFmp4LiveMultiTrackEpochInputTrack>(
                template.Tracks.ToArray()),
            new ReadOnlyCollection<PlaybackIsoBmffTimedFragmentRun>(
                timedRuns.ToArray()));

    private static PlaybackIsoBmffTimedFragmentRun TimedRun(
        uint trackId,
        ulong decodeTime) =>
        new(
            trackId,
            sampleDescriptionIndex: 1,
            decodeTime,
            decodeTime + 1,
            new ReadOnlyCollection<PlaybackIsoBmffTimedFragmentSample>(
                [new(
                    SampleIndex: 0,
                    SampleDuration: 1,
                    SampleSize: 1,
                    SampleFlags: 0,
                    DecodeTime: decodeTime,
                    DecodeEndTimeExclusive: decodeTime + 1,
                    PresentationTime: decodeTime)]));

    private static PlaybackFmp4LiveMultiTrackEpochInput BindInput(
        PlaybackFmp4LiveInitializationFragmentDefaultsProjection defaults,
        byte[] fragment)
    {
        var resolvedRuns = ProjectResolvedRuns(defaults, fragment);
        Assert.Equal(
            PlaybackFmp4LiveMultiTrackEpochInputBindingResult.Bound,
            PlaybackFmp4LiveMultiTrackEpochInputBinder.Bind(
                resolvedRuns,
                out var input));
        return Assert.IsType<PlaybackFmp4LiveMultiTrackEpochInput>(input);
    }

    private static PlaybackIsoBmffFragmentContinuityBinding BindContinuity(
        PlaybackFmp4LiveMultiTrackEpochInput previous,
        PlaybackFmp4LiveMultiTrackEpochInput current,
        uint trackId)
    {
        var previousRun = Assert.Single(
            previous.TimedRuns,
            run => run.TrackId == trackId);
        var currentRun = Assert.Single(
            current.TimedRuns,
            run => run.TrackId == trackId);
        return BindContinuity(
            previous.FragmentHeader,
            current.FragmentHeader,
            previousRun,
            currentRun);
    }

    private static PlaybackIsoBmffFragmentContinuityBinding BindContinuity(
        PlaybackFmp4LiveMultiTrackEpochState previous,
        PlaybackFmp4LiveMultiTrackEpochInput current,
        uint trackId)
    {
        var previousRun = FindTrack(previous, trackId).TimedRun;
        var currentRun = Assert.Single(
            current.TimedRuns,
            run => run.TrackId == trackId);
        return BindContinuity(
            previous.FragmentHeader,
            current.FragmentHeader,
            previousRun,
            currentRun);
    }

    private static PlaybackIsoBmffFragmentContinuityBinding BindContinuity(
        PlaybackIsoBmffMovieFragmentHeaderBox previousHeader,
        PlaybackIsoBmffMovieFragmentHeaderBox currentHeader,
        PlaybackIsoBmffTimedFragmentRun previousRun,
        PlaybackIsoBmffTimedFragmentRun currentRun)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieFragmentSequenceContinuityResult.Classified,
            PlaybackIsoBmffMovieFragmentSequenceContinuityClassifier.Classify(
                previousHeader,
                currentHeader,
                out var sequence));
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentDecodeContinuityResult.Classified,
            PlaybackIsoBmffTrackFragmentDecodeContinuityClassifier.Classify(
                previousRun,
                currentRun,
                out var decode));
        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityBindingResult.Bound,
            PlaybackIsoBmffFragmentContinuityBinder.Bind(
                Assert.IsType<PlaybackIsoBmffMovieFragmentSequenceContinuity>(
                    sequence),
                Assert.IsType<PlaybackIsoBmffTrackFragmentDecodeContinuity>(
                    decode),
                out var binding));
        return Assert.IsType<PlaybackIsoBmffFragmentContinuityBinding>(
            binding);
    }

    private static PlaybackFmp4LiveMediaFragmentResolvedRunProjection
        ProjectResolvedRuns(
            PlaybackFmp4LiveInitializationFragmentDefaultsProjection defaults,
            byte[] fragment,
            PlaybackFmp4LiveInitializationProfileProjection? profile = null)
    {
        var binding = Bind(profile ?? defaults.Source, fragment);
        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult.Projected,
            PlaybackFmp4LiveMediaFragmentResolvedRunProjector.Project(
                fragment,
                defaults,
                binding,
                out var projection));
        return Assert.IsType<
            PlaybackFmp4LiveMediaFragmentResolvedRunProjection>(projection);
    }

    private static PlaybackFmp4LiveInitializationFragmentDefaultsProjection
        ProjectInitializationDefaults(
            uint videoTrackId = 7,
            uint audioTrackId = 9)
    {
        var segment = Concat(
            Box(Ftyp),
            Box(
                Moov,
                InitializationTrack(
                    videoTrackId,
                    Vide,
                    ValidVideoSampleDescription()),
                InitializationTrack(
                    audioTrackId,
                    Soun,
                    ValidAudioSampleDescription()),
                Box(
                    Mvex,
                    TrackExtends(
                        videoTrackId,
                        1,
                        1_001,
                        4_096,
                        0x02000000),
                    TrackExtends(audioTrackId, 1, 1_024, 1_024, 0))));

        Assert.Equal(
            PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                .Projected,
            PlaybackFmp4LiveInitializationFragmentDefaultsProjector.Project(
                segment,
                segment.Length,
                out var defaults));
        return Assert.IsType<
            PlaybackFmp4LiveInitializationFragmentDefaultsProjection>(defaults);
    }

    private static PlaybackFmp4LiveMediaFragmentProfileBinding Bind(
        PlaybackFmp4LiveInitializationProfileProjection profile,
        byte[] fragment)
    {
        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected,
            PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
                fragment,
                out var identity));
        var projectedIdentity = Assert.IsType<
            PlaybackFmp4LiveMediaFragmentIdentityProjection>(identity);
        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult.Projected,
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjector.Project(
                fragment,
                projectedIdentity,
                out var timeline));
        var projectedTimeline = Assert.IsType<
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjection>(timeline);
        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentProfileBindingResult.Bound,
            PlaybackFmp4LiveMediaFragmentProfileBinder.Bind(
                profile,
                projectedIdentity,
                projectedTimeline,
                out var binding));
        return Assert.IsType<PlaybackFmp4LiveMediaFragmentProfileBinding>(binding);
    }

    private static byte[] ValidVideoSampleDescription()
    {
        var (box, _) = PlaybackIsoBmffAvcDecoderConfigurationParserTests
            .CreateVisualEntry(
                PlaybackIsoBmffAvcDecoderConfigurationParserTests
                    .BaselineRecord());
        return box;
    }

    private static byte[] ValidAudioSampleDescription()
    {
        var (box, _) = PlaybackIsoBmffElementaryStreamDescriptorParserTests
            .CreateAudioEntry(
                PlaybackIsoBmffElementaryStreamDescriptorParserTests
                    .ElementaryStreamBox(
                        PlaybackIsoBmffElementaryStreamDescriptorParserTests
                            .BaselineElementaryStream()));
        return box;
    }

    private static byte[] InitializationTrack(
        uint trackId,
        uint handlerType,
        byte[] sampleDescription) =>
        Box(
            Trak,
            TrackHeader(trackId),
            Box(
                Mdia,
                Handler(handlerType),
                Box(Minf, Box(Stbl, sampleDescription))));

    private static byte[] TrackHeader(uint trackId)
    {
        var box = new byte[TkhdBoxLength];
        WriteHeader(box, Tkhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), trackId);
        return box;
    }

    private static byte[] Handler(uint handlerType)
    {
        var box = new byte[33];
        WriteHeader(box, Hdlr);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(16, 4), handlerType);
        return box;
    }

    private static byte[] TrackExtends(
        uint trackId,
        uint sampleDescriptionIndex,
        uint sampleDuration,
        uint sampleSize,
        uint sampleFlags)
    {
        var box = new byte[32];
        WriteHeader(box, Trex);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), trackId);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(16, 4),
            sampleDescriptionIndex);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(20, 4),
            sampleDuration);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(24, 4), sampleSize);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(28, 4), sampleFlags);
        return box;
    }

    private static byte[] MovieFragment(
        uint sequenceNumber,
        params byte[][] tracks)
    {
        var children = new byte[tracks.Length + 1][];
        children[0] = MovieFragmentHeader(sequenceNumber);
        tracks.CopyTo(children, 1);
        return Box(Moof, children);
    }

    private static byte[] MovieFragmentHeader(uint sequenceNumber)
    {
        var box = new byte[16];
        WriteHeader(box, Mfhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), sequenceNumber);
        return box;
    }

    private static byte[] TrackFragment(
        uint trackId,
        ulong decodeTime,
        byte[] trackRun) =>
        Box(
            Traf,
            TrackFragmentHeader(trackId),
            TrackDecodeTime(decodeTime),
            trackRun);

    private static byte[] TrackFragmentHeader(uint trackId)
    {
        var box = new byte[16];
        WriteHeader(box, Tfhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), trackId);
        return box;
    }

    private static byte[] TrackDecodeTime(ulong decodeTime)
    {
        var box = new byte[16];
        WriteHeader(box, Tfdt);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(12, 4),
            checked((uint)decodeTime));
        return box;
    }

    private static byte[] TrackRun(uint sampleCount)
    {
        var box = new byte[16];
        WriteHeader(box, Trun);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), sampleCount);
        return box;
    }

    private static byte[] Box(uint type, params byte[][] payloads)
    {
        var box = new byte[8 + payloads.Sum(payload => payload.Length)];
        WriteHeader(box, type);
        var offset = 8;
        foreach (var payload in payloads)
        {
            payload.CopyTo(box, offset);
            offset += payload.Length;
        }

        return box;
    }

    private static byte[] Concat(params byte[][] values)
    {
        var result = new byte[values.Sum(value => value.Length)];
        var offset = 0;
        foreach (var value in values)
        {
            value.CopyTo(result, offset);
            offset += value.Length;
        }

        return result;
    }

    private static void WriteHeader(byte[] box, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            checked((uint)box.Length));
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
    }
}
