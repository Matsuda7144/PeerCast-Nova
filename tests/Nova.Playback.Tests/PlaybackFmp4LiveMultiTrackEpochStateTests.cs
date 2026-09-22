using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveMultiTrackEpochStateTests
{
    [Fact]
    public void Create_NormalizesTrackOrder()
    {
        var video = RunWithSampleFacts(7, 0, 10, 4);
        var audio = RunWithSampleFacts(9, 0, 10, 5);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateCreateResult.Created,
            PlaybackFmp4LiveMultiTrackEpochStateMachine.Create(
                new PlaybackIsoBmffMovieFragmentHeaderBox(10),
                [audio, video],
                out var state));
        var created = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(
            state);

        Assert.Collection(
            created.Tracks,
            track =>
            {
                Assert.Equal(7U, track.TrackId);
                Assert.Same(video, track.TimedRun);
                Assert.Null(track.EntryContinuity);
            },
            track =>
            {
                Assert.Equal(9U, track.TrackId);
                Assert.Same(audio, track.TimedRun);
                Assert.Null(track.EntryContinuity);
            });
        Assert.False(created.BeganAfterDiscontinuity);
    }

    [Fact]
    public void Advance_CommitsVideoAndAudioTogetherRegardlessOfInputOrder()
    {
        var videoPrevious = RunWithSampleFacts(7, 0, 10, 4);
        var audioPrevious = RunWithSampleFacts(9, 0, 10, 5);
        var videoCurrent = RunWithSampleFacts(7, 10, 20, 6);
        var audioCurrent = RunWithSampleFacts(9, 10, 20, 7);
        var state = CreateState(10, videoPrevious, audioPrevious);

        var audioContinuity = Bind(10, 11, audioPrevious, audioCurrent);
        var videoContinuity = Bind(10, 11, videoPrevious, videoCurrent);
        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            [audioContinuity, videoContinuity],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.Advanced,
            result);
        var advanced = Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(
            next);
        Assert.Equal(0UL, advanced.EpochId);
        Assert.Equal(11U, advanced.SequenceNumber);
        Assert.Same(videoCurrent, advanced.Tracks[0].TimedRun);
        Assert.Same(audioCurrent, advanced.Tracks[1].TimedRun);
        Assert.False(advanced.BeganAfterDiscontinuity);
    }

    [Fact]
    public void Advance_AudioMismatchRejectsWithoutPartialVideoCommit()
    {
        var videoPrevious = RunWithSampleFacts(7, 0, 10, 4);
        var audioPrevious = RunWithSampleFacts(9, 0, 10, 5);
        var mismatchedAudioPrevious = RunWithSampleFacts(9, 0, 10, 50);
        var state = CreateState(10, videoPrevious, audioPrevious);

        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            [
                Bind(
                    10,
                    11,
                    videoPrevious,
                    RunWithSampleFacts(7, 10, 20, 6)),
                Bind(
                    10,
                    11,
                    mismatchedAudioPrevious,
                    RunWithSampleFacts(9, 10, 20, 7)),
            ],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch,
            result);
        Assert.Null(next);
        Assert.Same(videoPrevious, state.Tracks[0].TimedRun);
        Assert.Same(audioPrevious, state.Tracks[1].TimedRun);
    }

    [Fact]
    public void Advance_VideoMismatchRejectsWithoutPartialAudioCommit()
    {
        var videoPrevious = RunWithSampleFacts(7, 0, 10, 4);
        var audioPrevious = RunWithSampleFacts(9, 0, 10, 5);
        var mismatchedVideoPrevious = RunWithSampleFacts(7, 0, 10, 40);
        var state = CreateState(10, videoPrevious, audioPrevious);

        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            [
                Bind(
                    10,
                    11,
                    mismatchedVideoPrevious,
                    RunWithSampleFacts(7, 10, 20, 6)),
                Bind(
                    10,
                    11,
                    audioPrevious,
                    RunWithSampleFacts(9, 10, 20, 7)),
            ],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch,
            result);
        Assert.Null(next);
        Assert.Same(videoPrevious, state.Tracks[0].TimedRun);
        Assert.Same(audioPrevious, state.Tracks[1].TimedRun);
    }

    [Fact]
    public void Advance_ChangedTrackSetRejectsWithoutStateChange()
    {
        var videoPrevious = RunWithSampleFacts(7, 0, 10, 4);
        var audioPrevious = RunWithSampleFacts(9, 0, 10, 5);
        var state = CreateState(10, videoPrevious, audioPrevious);

        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            [
                Bind(
                    10,
                    11,
                    videoPrevious,
                    RunWithSampleFacts(7, 10, 20, 6)),
                Bind(
                    10,
                    11,
                    RunWithSampleFacts(11, 0, 10, 8),
                    RunWithSampleFacts(11, 10, 20, 9)),
            ],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch,
            result);
        Assert.Null(next);
        Assert.Same(videoPrevious, state.Tracks[0].TimedRun);
        Assert.Same(audioPrevious, state.Tracks[1].TimedRun);
    }

    [Fact]
    public void Advance_MissingTrackRejectsWithoutStateChange()
    {
        var videoPrevious = RunWithSampleFacts(7, 0, 10, 4);
        var audioPrevious = RunWithSampleFacts(9, 0, 10, 5);
        var state = CreateState(10, videoPrevious, audioPrevious);

        var result = PlaybackFmp4LiveMultiTrackEpochStateMachine.Advance(
            state,
            [Bind(
                10,
                11,
                videoPrevious,
                RunWithSampleFacts(7, 10, 20, 6))],
            out var next);

        Assert.Equal(
            PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch,
            result);
        Assert.Null(next);
        Assert.Same(videoPrevious, state.Tracks[0].TimedRun);
        Assert.Same(audioPrevious, state.Tracks[1].TimedRun);
    }

    private static PlaybackFmp4LiveMultiTrackEpochState CreateState(
        uint sequenceNumber,
        params PlaybackIsoBmffTimedFragmentRun[] runs)
    {
        Assert.Equal(
            PlaybackFmp4LiveEpochStateCreateResult.Created,
            PlaybackFmp4LiveMultiTrackEpochStateMachine.Create(
                new PlaybackIsoBmffMovieFragmentHeaderBox(sequenceNumber),
                runs,
                out var state));
        return Assert.IsType<PlaybackFmp4LiveMultiTrackEpochState>(state);
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
                Assert.IsType<PlaybackIsoBmffTrackFragmentDecodeContinuity>(
                    decode),
                out var binding));
        return Assert.IsType<PlaybackIsoBmffFragmentContinuityBinding>(
            binding);
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
