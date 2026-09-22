using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public sealed class PlaybackFmp4LiveMultiTrackEpochTrackState
{
    internal PlaybackFmp4LiveMultiTrackEpochTrackState(
        PlaybackIsoBmffTimedFragmentRun timedRun,
        PlaybackIsoBmffFragmentContinuityKind? entryContinuity,
        bool currentEpochMember,
        bool continuityAnchorValid,
        bool presentInCurrentFragment)
    {
        TimedRun = timedRun;
        EntryContinuity = entryContinuity;
        CurrentEpochMember = currentEpochMember;
        ContinuityAnchorValid = continuityAnchorValid;
        PresentInCurrentFragment = presentInCurrentFragment;
    }

    public PlaybackIsoBmffTimedFragmentRun TimedRun { get; }

    public uint TrackId => TimedRun.TrackId;

    public PlaybackIsoBmffFragmentContinuityKind? EntryContinuity { get; }

    internal bool CurrentEpochMember { get; }

    internal bool ContinuityAnchorValid { get; }

    internal bool PresentInCurrentFragment { get; }
}

public sealed class PlaybackFmp4LiveMultiTrackEpochState
{
    internal PlaybackFmp4LiveMultiTrackEpochState(
        ulong epochId,
        PlaybackIsoBmffMovieFragmentHeaderBox fragmentHeader,
        PlaybackFmp4LiveInitializationProfileProjection?
            initializationProfile,
        ReadOnlyCollection<PlaybackFmp4LiveMultiTrackEpochTrackState> tracks)
    {
        EpochId = epochId;
        FragmentHeader = fragmentHeader;
        InitializationProfile = initializationProfile;
        Tracks = tracks;
    }

    public ulong EpochId { get; }

    public PlaybackIsoBmffMovieFragmentHeaderBox FragmentHeader { get; }

    internal PlaybackFmp4LiveInitializationProfileProjection?
        InitializationProfile
    { get; }

    public IReadOnlyList<PlaybackFmp4LiveMultiTrackEpochTrackState> Tracks
    { get; }

    public uint SequenceNumber => FragmentHeader.SequenceNumber;

    public bool BeganAfterDiscontinuity => Tracks.Any(
        track => track.EntryContinuity is { } continuity &&
            continuity != PlaybackIsoBmffFragmentContinuityKind.Continuous);
}

public static class PlaybackFmp4LiveMultiTrackEpochStateMachine
{
    public const int MaximumTrackCount = 2;

    public static PlaybackFmp4LiveEpochStateCreateResult Create(
        PlaybackFmp4LiveMultiTrackEpochInput? input,
        out PlaybackFmp4LiveMultiTrackEpochState? state)
    {
        state = null;
        if (input is null)
        {
            return PlaybackFmp4LiveEpochStateCreateResult.InvalidInput;
        }

        return Create(
            input.FragmentHeader,
            input.TimedRuns,
            input.InitializationProfile,
            out state);
    }

    public static PlaybackFmp4LiveEpochStateCreateResult Create(
        PlaybackIsoBmffMovieFragmentHeaderBox? fragmentHeader,
        IReadOnlyList<PlaybackIsoBmffTimedFragmentRun>? timedRuns,
        out PlaybackFmp4LiveMultiTrackEpochState? state)
    {
        state = null;
        return Create(
            fragmentHeader,
            timedRuns,
            initializationProfile: null,
            out state);
    }

    private static PlaybackFmp4LiveEpochStateCreateResult Create(
        PlaybackIsoBmffMovieFragmentHeaderBox? fragmentHeader,
        IReadOnlyList<PlaybackIsoBmffTimedFragmentRun>? timedRuns,
        PlaybackFmp4LiveInitializationProfileProjection?
            initializationProfile,
        out PlaybackFmp4LiveMultiTrackEpochState? state)
    {
        state = null;
        if (fragmentHeader is null || !TryNormalizeRuns(timedRuns, out var runs))
        {
            return PlaybackFmp4LiveEpochStateCreateResult.InvalidInput;
        }

        state = new(
            0,
            fragmentHeader,
            initializationProfile,
            CreateTrackStates(runs, entryContinuity: null));
        return PlaybackFmp4LiveEpochStateCreateResult.Created;
    }

    public static PlaybackFmp4LiveEpochStateTransitionResult Advance(
        PlaybackFmp4LiveMultiTrackEpochState? state,
        PlaybackFmp4LiveMultiTrackEpochInput? input,
        IReadOnlyList<PlaybackIsoBmffFragmentContinuityBinding>? continuities,
        out PlaybackFmp4LiveMultiTrackEpochState? next)
    {
        next = null;
        if (state is null || input is null || continuities is null)
        {
            return PlaybackFmp4LiveEpochStateTransitionResult.InvalidInput;
        }

        if (state.InitializationProfile is null ||
            !ReferenceEquals(state.InitializationProfile, input.InitializationProfile))
        {
            return PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch;
        }

        if (input.TimedRuns.Count is < 1 or > MaximumTrackCount)
        {
            return PlaybackFmp4LiveEpochStateTransitionResult.InvalidInput;
        }

        for (var index = 0; index < state.Tracks.Count; index++)
        {
            var track = state.Tracks[index];
            if ((!track.CurrentEpochMember && track.ContinuityAnchorValid) ||
                (!track.CurrentEpochMember && track.PresentInCurrentFragment) ||
                (track.CurrentEpochMember && !track.ContinuityAnchorValid))
            {
                return PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch;
            }
        }

        if (PlaybackIsoBmffMovieFragmentSequenceContinuityClassifier.Classify(
                state.FragmentHeader,
                input.FragmentHeader,
                out var sequence) !=
                PlaybackIsoBmffMovieFragmentSequenceContinuityResult.Classified ||
            sequence is null)
        {
            return PlaybackFmp4LiveEpochStateTransitionResult.InvalidInput;
        }

        var present = new bool[state.Tracks.Count];
        var inputRuns = new PlaybackIsoBmffTimedFragmentRun?[state.Tracks.Count];
        var newInputRuns = new List<PlaybackIsoBmffTimedFragmentRun>();
        var inputTrackIds = new HashSet<uint>();
        for (var index = 0; index < input.TimedRuns.Count; index++)
        {
            var timedRun = input.TimedRuns[index];
            if (!inputTrackIds.Add(timedRun.TrackId))
            {
                return PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch;
            }

            var trackIndex = FindTrackIndex(state.Tracks, timedRun.TrackId);
            if (trackIndex < 0)
            {
                if (!IsProfileTrack(
                        state.InitializationProfile,
                        timedRun.TrackId))
                {
                    return PlaybackFmp4LiveEpochStateTransitionResult
                        .StateMismatch;
                }

                newInputRuns.Add(timedRun);
                continue;
            }

            if (present[trackIndex])
            {
                return PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch;
            }

            present[trackIndex] = true;
            inputRuns[trackIndex] = timedRun;
        }

        if (state.Tracks.Count + newInputRuns.Count > MaximumTrackCount)
        {
            return PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch;
        }

        var matched = new PlaybackIsoBmffFragmentContinuityBinding?[
            state.Tracks.Count];
        for (var index = 0; index < continuities.Count; index++)
        {
            var continuity = continuities[index];
            if (continuity is null)
            {
                return PlaybackFmp4LiveEpochStateTransitionResult.InvalidInput;
            }

            var trackIndex = FindTrackIndex(state.Tracks, continuity.TrackId);
            if (trackIndex < 0 || !present[trackIndex] ||
                matched[trackIndex] is not null ||
                continuity.Sequence.Previous.SequenceNumber !=
                    state.SequenceNumber ||
                continuity.Sequence.Current.SequenceNumber !=
                    input.SequenceNumber ||
                continuity.Decode.Previous.TrackId != continuity.TrackId ||
                continuity.Decode.Current.TrackId != continuity.TrackId ||
                !SameRun(inputRuns[trackIndex]!, continuity.Decode.Current) ||
                continuity.Sequence.IsContinuous != sequence.IsContinuous)
            {
                return PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch;
            }

            matched[trackIndex] = continuity;
        }

        for (var index = 0; index < state.Tracks.Count; index++)
        {
            if (!present[index])
            {
                continue;
            }

            var track = state.Tracks[index];
            if (track.CurrentEpochMember)
            {
                if (matched[index] is null ||
                    !SameRun(
                        track.TimedRun,
                        matched[index]!.Decode.Previous))
                {
                    return PlaybackFmp4LiveEpochStateTransitionResult
                        .StateMismatch;
                }
            }
            else if (matched[index] is not null)
            {
                return PlaybackFmp4LiveEpochStateTransitionResult
                    .StateMismatch;
            }
        }

        var beginsNewEpoch = !sequence.IsContinuous;
        for (var index = 0; index < matched.Length; index++)
        {
            if (matched[index] is { IsContinuous: false })
            {
                beginsNewEpoch = true;
            }
        }

        var epochId = state.EpochId;
        if (beginsNewEpoch)
        {
            if (epochId == ulong.MaxValue)
            {
                return PlaybackFmp4LiveEpochStateTransitionResult.EpochOverflow;
            }

            epochId++;
        }

        var nextTracks = new List<PlaybackFmp4LiveMultiTrackEpochTrackState>(
            state.Tracks.Count + newInputRuns.Count);
        for (var index = 0; index < state.Tracks.Count; index++)
        {
            var track = state.Tracks[index];
            if (!present[index])
            {
                nextTracks.Add(beginsNewEpoch
                    ? new(
                        track.TimedRun,
                        entryContinuity: null,
                        currentEpochMember: false,
                        continuityAnchorValid: false,
                        presentInCurrentFragment: false)
                    : new(
                        track.TimedRun,
                        track.EntryContinuity,
                        track.CurrentEpochMember,
                        track.ContinuityAnchorValid,
                        presentInCurrentFragment: false));
                continue;
            }

            if (matched[index] is { } continuity)
            {
                nextTracks.Add(new(
                    continuity.Decode.Current,
                    continuity.Kind,
                    currentEpochMember: true,
                    continuityAnchorValid: true,
                    presentInCurrentFragment: true));
                continue;
            }

            nextTracks.Add(new(
                inputRuns[index]!,
                entryContinuity: null,
                currentEpochMember: true,
                continuityAnchorValid: true,
                presentInCurrentFragment: true));
        }

        for (var index = 0; index < newInputRuns.Count; index++)
        {
            nextTracks.Add(new(
                newInputRuns[index],
                entryContinuity: null,
                currentEpochMember: true,
                continuityAnchorValid: true,
                presentInCurrentFragment: true));
        }

        nextTracks.Sort(static (left, right) =>
            left.TrackId.CompareTo(right.TrackId));

        next = new(
            epochId,
            input.FragmentHeader,
            state.InitializationProfile,
            Array.AsReadOnly(nextTracks.ToArray()));
        return PlaybackFmp4LiveEpochStateTransitionResult.Advanced;
    }

    public static PlaybackFmp4LiveEpochStateTransitionResult Advance(
        PlaybackFmp4LiveMultiTrackEpochState? state,
        IReadOnlyList<PlaybackIsoBmffFragmentContinuityBinding>? continuities,
        out PlaybackFmp4LiveMultiTrackEpochState? next)
    {
        next = null;
        if (state is null || continuities is null)
        {
            return PlaybackFmp4LiveEpochStateTransitionResult.InvalidInput;
        }

        if (continuities.Count != state.Tracks.Count)
        {
            return PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch;
        }

        var matched = new PlaybackIsoBmffFragmentContinuityBinding[
            state.Tracks.Count];
        var seen = new bool[state.Tracks.Count];
        uint? currentSequenceNumber = null;
        var beginsNewEpoch = false;
        for (var index = 0; index < continuities.Count; index++)
        {
            var continuity = continuities[index];
            if (continuity is null)
            {
                return PlaybackFmp4LiveEpochStateTransitionResult.InvalidInput;
            }

            var trackIndex = FindTrackIndex(state.Tracks, continuity.TrackId);
            if (trackIndex < 0 || seen[trackIndex])
            {
                return PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch;
            }

            if (continuity.Sequence.Previous.SequenceNumber !=
                    state.SequenceNumber ||
                !state.Tracks[trackIndex].CurrentEpochMember ||
                !state.Tracks[trackIndex].ContinuityAnchorValid ||
                continuity.Decode.Previous.TrackId != continuity.TrackId ||
                continuity.Decode.Current.TrackId != continuity.TrackId ||
                !SameRun(
                    state.Tracks[trackIndex].TimedRun,
                    continuity.Decode.Previous))
            {
                return PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch;
            }

            if (currentSequenceNumber is { } expectedSequenceNumber &&
                expectedSequenceNumber !=
                    continuity.Sequence.Current.SequenceNumber)
            {
                return PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch;
            }

            currentSequenceNumber ??= continuity.Sequence.Current.SequenceNumber;
            matched[trackIndex] = continuity;
            seen[trackIndex] = true;
            beginsNewEpoch |= !continuity.IsContinuous;
        }

        for (var index = 0; index < seen.Length; index++)
        {
            if (!seen[index])
            {
                return PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch;
            }
        }

        var epochId = state.EpochId;
        if (beginsNewEpoch)
        {
            if (epochId == ulong.MaxValue)
            {
                return PlaybackFmp4LiveEpochStateTransitionResult.EpochOverflow;
            }

            epochId++;
        }

        var nextTracks = new PlaybackFmp4LiveMultiTrackEpochTrackState[
            matched.Length];
        for (var index = 0; index < nextTracks.Length; index++)
        {
            var continuity = matched[index];
            nextTracks[index] = new(
                continuity.Decode.Current,
                continuity.Kind,
                currentEpochMember: true,
                continuityAnchorValid: true,
                presentInCurrentFragment: true);
        }

        next = new(
            epochId,
            matched[0].Sequence.Current,
            state.InitializationProfile,
            Array.AsReadOnly(nextTracks));
        return PlaybackFmp4LiveEpochStateTransitionResult.Advanced;
    }

    private static bool TryNormalizeRuns(
        IReadOnlyList<PlaybackIsoBmffTimedFragmentRun>? source,
        out PlaybackIsoBmffTimedFragmentRun[] runs)
    {
        runs = [];
        if (source is null ||
            source.Count is < 1 or > MaximumTrackCount)
        {
            return false;
        }

        var copied = new PlaybackIsoBmffTimedFragmentRun[source.Count];
        var trackIds = new HashSet<uint>();
        for (var index = 0; index < copied.Length; index++)
        {
            var run = source[index];
            if (run is null || !trackIds.Add(run.TrackId))
            {
                return false;
            }

            copied[index] = run;
        }

        Array.Sort(copied, static (left, right) =>
            left.TrackId.CompareTo(right.TrackId));
        runs = copied;
        return true;
    }

    private static ReadOnlyCollection<PlaybackFmp4LiveMultiTrackEpochTrackState>
        CreateTrackStates(
            PlaybackIsoBmffTimedFragmentRun[] runs,
            PlaybackIsoBmffFragmentContinuityKind? entryContinuity)
    {
        var tracks = new PlaybackFmp4LiveMultiTrackEpochTrackState[runs.Length];
        for (var index = 0; index < tracks.Length; index++)
        {
            tracks[index] = new(
                runs[index],
                entryContinuity,
                currentEpochMember: true,
                continuityAnchorValid: true,
                presentInCurrentFragment: true);
        }

        return Array.AsReadOnly(tracks);
    }

    private static int FindTrackIndex(
        IReadOnlyList<PlaybackFmp4LiveMultiTrackEpochTrackState> tracks,
        uint trackId)
    {
        for (var index = 0; index < tracks.Count; index++)
        {
            if (tracks[index].TrackId == trackId)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsProfileTrack(
        PlaybackFmp4LiveInitializationProfileProjection? profile,
        uint trackId) =>
        profile is not null &&
        (trackId == profile.VideoTrackId || trackId == profile.AudioTrackId);

    private static bool SameRun(
        PlaybackIsoBmffTimedFragmentRun left,
        PlaybackIsoBmffTimedFragmentRun right)
    {
        if (left.TrackId != right.TrackId ||
            left.SampleDescriptionIndex != right.SampleDescriptionIndex ||
            left.BaseMediaDecodeTime != right.BaseMediaDecodeTime ||
            left.NextDecodeTime != right.NextDecodeTime ||
            left.Samples.Count != right.Samples.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Samples.Count; index++)
        {
            if (left.Samples[index] != right.Samples[index])
            {
                return false;
            }
        }

        return true;
    }
}
