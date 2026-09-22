using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveEpochStateTests
{
    [Fact]
    public void Create_InitialStateStartsAtEpochZero()
    {
        var header = new PlaybackIsoBmffMovieFragmentHeaderBox(10);
        var run = PlaybackIsoBmffTrackFragmentDecodeContinuityTests.Run(
            7,
            100,
            110);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateCreateResult.Created,
            PlaybackFmp4LiveEpochStateMachine.Create(
                header,
                run,
                out var state));
        var created = Assert.IsType<PlaybackFmp4LiveEpochState>(state);
        Assert.Equal(0UL, created.EpochId);
        Assert.Same(header, created.FragmentHeader);
        Assert.Same(run, created.TimedRun);
        Assert.Equal(10U, created.SequenceNumber);
        Assert.Equal(7U, created.TrackId);
        Assert.Null(created.EntryContinuity);
        Assert.False(created.BeganAfterDiscontinuity);
    }

    [Fact]
    public void Advance_ContinuousTransitionKeepsEpoch()
    {
        var previousRun = PlaybackIsoBmffTrackFragmentDecodeContinuityTests.Run(
            7,
            100,
            110);
        var currentRun = PlaybackIsoBmffTrackFragmentDecodeContinuityTests.Run(
            7,
            110,
            120);
        var state = CreateState(10, previousRun);
        var continuity = Bind(10, 11, previousRun, currentRun);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            PlaybackFmp4LiveEpochStateMachine.Advance(
                state,
                continuity,
                out var next));
        var advanced = Assert.IsType<PlaybackFmp4LiveEpochState>(next);
        Assert.Equal(0UL, advanced.EpochId);
        Assert.Equal(11U, advanced.SequenceNumber);
        Assert.Same(currentRun, advanced.TimedRun);
        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityKind.Continuous,
            advanced.EntryContinuity);
        Assert.False(advanced.BeganAfterDiscontinuity);
    }

    [Theory]
    [InlineData(
        12U,
        110,
        PlaybackIsoBmffFragmentContinuityKind.SequenceDiscontinuity)]
    [InlineData(
        11U,
        115,
        PlaybackIsoBmffFragmentContinuityKind.DecodeDiscontinuity)]
    [InlineData(
        12U,
        115,
        PlaybackIsoBmffFragmentContinuityKind.SequenceAndDecodeDiscontinuity)]
    public void Advance_DiscontinuityStartsNextEpoch(
        uint currentSequence,
        int currentDecodeStart,
        PlaybackIsoBmffFragmentContinuityKind expectedKind)
    {
        var previousRun = PlaybackIsoBmffTrackFragmentDecodeContinuityTests.Run(
            7,
            100,
            110);
        var currentRun = PlaybackIsoBmffTrackFragmentDecodeContinuityTests.Run(
            7,
            currentDecodeStart,
            currentDecodeStart + 10);
        var state = CreateState(10, previousRun);
        var continuity = Bind(
            10,
            currentSequence,
            previousRun,
            currentRun);

        Assert.Equal(expectedKind, continuity.Kind);
        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            PlaybackFmp4LiveEpochStateMachine.Advance(
                state,
                continuity,
                out var next));
        var advanced = Assert.IsType<PlaybackFmp4LiveEpochState>(next);
        Assert.Equal(1UL, advanced.EpochId);
        Assert.Equal(currentSequence, advanced.SequenceNumber);
        Assert.Same(currentRun, advanced.TimedRun);
        Assert.Equal(expectedKind, advanced.EntryContinuity);
        Assert.True(advanced.BeganAfterDiscontinuity);
    }

    [Fact]
    public void Advance_SequenceStateMismatchIsRejected()
    {
        var previousRun = PlaybackIsoBmffTrackFragmentDecodeContinuityTests.Run(
            7,
            100,
            110);
        var state = CreateState(9, previousRun);
        var continuity = Bind(
            10,
            11,
            previousRun,
            PlaybackIsoBmffTrackFragmentDecodeContinuityTests.Run(7, 110, 120));

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch,
            PlaybackFmp4LiveEpochStateMachine.Advance(
                state,
                continuity,
                out var next));
        Assert.Null(next);
    }

    [Fact]
    public void Advance_DecodeStateMismatchIsRejected()
    {
        var stateRun = PlaybackIsoBmffTrackFragmentDecodeContinuityTests.Run(
            7,
            99,
            110);
        var bindingPreviousRun =
            PlaybackIsoBmffTrackFragmentDecodeContinuityTests.Run(7, 100, 110);
        var state = CreateState(10, stateRun);
        var continuity = Bind(
            10,
            11,
            bindingPreviousRun,
            PlaybackIsoBmffTrackFragmentDecodeContinuityTests.Run(7, 110, 120));

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch,
            PlaybackFmp4LiveEpochStateMachine.Advance(
                state,
                continuity,
                out var next));
        Assert.Null(next);
    }

    [Fact]
    public void Advance_SameRunSampleFactsRemainContinuous()
    {
        var previousRun = RunWithSampleFacts(7, 100, 110, 4);
        var currentRun = RunWithSampleFacts(7, 110, 120, 5);
        var state = CreateState(10, previousRun);
        var continuity = Bind(10, 11, previousRun, currentRun);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            PlaybackFmp4LiveEpochStateMachine.Advance(
                state,
                continuity,
                out var next));
        Assert.Same(currentRun, Assert.IsType<PlaybackFmp4LiveEpochState>(
            next).TimedRun);
    }

    [Fact]
    public void Advance_SameRunTimingWithDifferentSampleFactsIsRejected()
    {
        var stateRun = RunWithSampleFacts(7, 100, 110, 4);
        var bindingPreviousRun = RunWithSampleFacts(7, 100, 110, 5);
        var currentRun = RunWithSampleFacts(7, 110, 120, 6);
        var state = CreateState(10, stateRun);
        var continuity = Bind(10, 11, bindingPreviousRun, currentRun);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch,
            PlaybackFmp4LiveEpochStateMachine.Advance(
                state,
                continuity,
                out var next));
        Assert.Null(next);
    }

    [Fact]
    public void Advance_MaxEpochAllowsContinuityButRejectsDiscontinuity()
    {
        var previousRun = PlaybackIsoBmffTrackFragmentDecodeContinuityTests.Run(
            7,
            100,
            110);
        var state = new PlaybackFmp4LiveEpochState(
            ulong.MaxValue,
            new PlaybackIsoBmffMovieFragmentHeaderBox(10),
            previousRun,
            null);
        var currentRun = PlaybackIsoBmffTrackFragmentDecodeContinuityTests.Run(
            7,
            110,
            120);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            PlaybackFmp4LiveEpochStateMachine.Advance(
                state,
                Bind(10, 11, previousRun, currentRun),
                out var continuousNext));
        Assert.Equal(
            ulong.MaxValue,
            Assert.IsType<PlaybackFmp4LiveEpochState>(continuousNext).EpochId);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.EpochOverflow,
            PlaybackFmp4LiveEpochStateMachine.Advance(
                state,
                Bind(10, 12, previousRun, currentRun),
                out var overflowNext));
        Assert.Null(overflowNext);
    }

    [Fact]
    public void NullInputsAreRejectedWithoutState()
    {
        Assert.Equal(
            PlaybackFmp4LiveEpochStateCreateResult.InvalidInput,
            PlaybackFmp4LiveEpochStateMachine.Create(
                null,
                null,
                out var created));
        Assert.Null(created);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.InvalidInput,
            PlaybackFmp4LiveEpochStateMachine.Advance(
                null,
                null,
                out var advanced));
        Assert.Null(advanced);
    }

    private static PlaybackFmp4LiveEpochState CreateState(
        uint sequenceNumber,
        PlaybackIsoBmffTimedFragmentRun timedRun)
    {
        Assert.Equal(
            PlaybackFmp4LiveEpochStateCreateResult.Created,
            PlaybackFmp4LiveEpochStateMachine.Create(
                new PlaybackIsoBmffMovieFragmentHeaderBox(sequenceNumber),
                timedRun,
                out var state));
        return Assert.IsType<PlaybackFmp4LiveEpochState>(state);
    }

    private static PlaybackIsoBmffFragmentContinuityBinding Bind(
        uint previousSequence,
        uint currentSequence,
        PlaybackIsoBmffTimedFragmentRun previousRun,
        PlaybackIsoBmffTimedFragmentRun currentRun)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieFragmentSequenceContinuityResult.Classified,
            PlaybackIsoBmffMovieFragmentSequenceContinuityClassifier.Classify(
                new PlaybackIsoBmffMovieFragmentHeaderBox(previousSequence),
                new PlaybackIsoBmffMovieFragmentHeaderBox(currentSequence),
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
                Assert.IsType<PlaybackIsoBmffTrackFragmentDecodeContinuity>(decode),
                out var binding));
        return Assert.IsType<PlaybackIsoBmffFragmentContinuityBinding>(binding);
    }

    private static PlaybackIsoBmffTimedFragmentRun RunWithSampleFacts(
        uint trackId,
        BigInteger start,
        BigInteger end,
        uint sampleSize) =>
        new(
            trackId,
            sampleDescriptionIndex: 1,
            start,
            end,
            new ReadOnlyCollection<PlaybackIsoBmffTimedFragmentSample>(
                [new(
                    SampleIndex: 0,
                    SampleDuration: checked((uint)(end - start)),
                    SampleSize: sampleSize,
                    SampleFlags: 0,
                    DecodeTime: start,
                    DecodeEndTimeExclusive: end,
                    PresentationTime: start)]));
}
