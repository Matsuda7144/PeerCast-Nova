namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffRegularMovieNativePacketSubmissionCreationResult
{
    Created,
    MissingSession,
    SessionUnavailable,
    SourceMismatch,
    EmptyTraversal,
}

public enum PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
{
    Submitted,
    NeedFrameDrain,
    Exhausted,
    Truncated,
    InvalidPacket,
    TimestampRejected,
    RoleUnavailable,
    GateRejected,
    NativeAllocationFailed,
    NativeRejected,
    NativeEndOfStream,
    NativeFailure,
    Faulted,
    Disposed,
}

public sealed record
    PlaybackIsoBmffRegularMovieNativePacketSubmissionSnapshot(
        int MergeIndex,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole Role,
        int StreamIndex,
        uint TrackId,
        ulong SampleNumber,
        long PresentationTimestamp,
        long DecodeTimestamp,
        long Duration,
        PlaybackBoundedDemuxTimeBase TimeBase,
        int PayloadLength,
        bool IsKeyframe);

public sealed record PlaybackIsoBmffRegularMovieNativePacketSubmissionAdvance(
    PlaybackIsoBmffRegularMovieNativePacketSubmissionResult Result,
    PlaybackIsoBmffRegularMovieNativePacketSubmissionSnapshot? Snapshot);

internal interface
    IPlaybackIsoBmffRegularMovieNativePacketSubmissionOperations
{
    PlaybackMetadataAwareNativePacketCreation CreatePacket(
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role,
        PlaybackMetadataAwareNativePacketInput input);

    PlaybackMetadataAwareNativeSendResult SendOnce(
        IPlaybackMetadataAwareNativePacketOwner packet,
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role);
}

internal sealed class
    PlaybackIsoBmffRegularMovieNativePacketSubmissionOperations :
    IPlaybackIsoBmffRegularMovieNativePacketSubmissionOperations
{
    public PlaybackMetadataAwareNativePacketCreation CreatePacket(
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role,
        PlaybackMetadataAwareNativePacketInput input)
    {
        var context = GetContext(session, role);
        return context is null
            ? new(PlaybackMetadataAwareNativeSendResult.Rejected, null)
            : PlaybackMetadataAwareNativePacketOperations.CreatePacket(
                context,
                input);
    }

    public PlaybackMetadataAwareNativeSendResult SendOnce(
        IPlaybackMetadataAwareNativePacketOwner packet,
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role)
    {
        var context = GetContext(session, role);
        return context is not null &&
            packet is WindowsPlaybackMetadataAwareNativePacketOwner owner
                ? owner.SendOnce(context)
                : PlaybackMetadataAwareNativeSendResult.Rejected;
    }

    private static PlaybackDecoderFfmpegContextLease? GetContext(
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role) =>
        role switch
        {
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video =>
                session.VideoContext,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Audio =>
                session.AudioContext,
            _ => null,
        };
}

public sealed class PlaybackIsoBmffRegularMovieNativePacketSubmission :
    IDisposable,
    IAsyncDisposable
{
    private const int AvPacketFlagKey = 0x0001;

    private readonly object _sync = new();
    private readonly
        IPlaybackIsoBmffRegularMovieNativePacketSubmissionOperations
            _operations;
    private PlaybackIsoBmffRegularMovieNativeDecoderSession? _session;
    private IPlaybackMetadataAwareNativePacketOwner? _pendingPacket;
    private PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep?
        _pendingStep;
    private PlaybackIsoBmffRegularMovieNativePacketSubmissionSnapshot?
        _pendingSnapshot;
    private int _nextMergeIndex;
    private bool _faulted;

    internal PlaybackIsoBmffRegularMovieNativePacketSubmission(
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        IPlaybackIsoBmffRegularMovieNativePacketSubmissionOperations
            operations)
    {
        _session = session;
        _operations = operations;
    }

    public PlaybackIsoBmffRegularMovieNativeDecoderSession SourceSession =>
        Volatile.Read(ref _session) ??
        throw new ObjectDisposedException(
            nameof(PlaybackIsoBmffRegularMovieNativePacketSubmission));

    public int NextMergeIndex
    {
        get
        {
            lock (_sync)
            {
                return _nextMergeIndex;
            }
        }
    }

    public bool HasPendingPacket
    {
        get
        {
            lock (_sync)
            {
                return _pendingPacket is not null;
            }
        }
    }

    public PlaybackIsoBmffRegularMovieNativePacketSubmissionSnapshot?
        PendingSnapshot
    {
        get
        {
            lock (_sync)
            {
                return _pendingSnapshot;
            }
        }
    }

    public bool IsDisposed => Volatile.Read(ref _session) is null;

    public PlaybackIsoBmffRegularMovieNativePacketSubmissionAdvance
        SubmitNext()
    {
        lock (_sync)
        {
            if (_faulted)
            {
                return Complete(
                    PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                        .Faulted);
            }

            var session = _session;
            if (session is null)
            {
                return Complete(
                    PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                        .Disposed);
            }

            if (session.IsDisposed)
            {
                return Fail(
                    PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                        .NativeFailure);
            }

            var source = session.Source.Source.Source;
            var steps = source.MergeTraversal.Traversal.Steps;
            if (_pendingPacket is { } pending)
            {
                return Send(
                    session,
                    pending,
                    _pendingStep!,
                    _pendingSnapshot!);
            }

            if (_nextMergeIndex == steps.Count)
            {
                return Complete(
                    source.IsMergeExhausted
                        ? PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                            .Exhausted
                        : PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                            .Truncated);
            }

            if ((uint)_nextMergeIndex >= (uint)steps.Count)
            {
                return Fail(
                    PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                        .NativeFailure);
            }

            var step = steps[_nextMergeIndex];
            if (!TryCreateInput(
                    session,
                    step,
                    out var input,
                    out var snapshot,
                    out var rejection))
            {
                return Fail(rejection);
            }

            try
            {
                var creation = _operations.CreatePacket(
                    session,
                    step.Role,
                    input);
                if (creation.Result !=
                        PlaybackMetadataAwareNativeSendResult.Success ||
                    creation.Owner is null)
                {
                    creation.Owner?.Dispose();
                    return Fail(MapCreationFailure(creation.Result), snapshot);
                }

                _pendingPacket = creation.Owner;
                _pendingStep = step;
                _pendingSnapshot = snapshot;
                return Send(session, creation.Owner, step, snapshot);
            }
            catch (Exception exception) when (exception is
                InvalidOperationException or NotSupportedException or
                OverflowException or ObjectDisposedException)
            {
                return Fail(
                    PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                        .NativeFailure,
                    snapshot);
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            DisposeOwnedState();
        }

        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private PlaybackIsoBmffRegularMovieNativePacketSubmissionAdvance Send(
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        IPlaybackMetadataAwareNativePacketOwner packet,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep step,
        PlaybackIsoBmffRegularMovieNativePacketSubmissionSnapshot snapshot)
    {
        PlaybackMetadataAwareNativeSendResult result;
        try
        {
            result = _operations.SendOnce(packet, session, step.Role);
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or NotSupportedException or
            OverflowException or ObjectDisposedException)
        {
            return Fail(
                PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                    .NativeFailure,
                snapshot);
        }

        if (result == PlaybackMetadataAwareNativeSendResult.Again)
        {
            return new(
                PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                    .NeedFrameDrain,
                snapshot);
        }

        _pendingPacket = null;
        _pendingStep = null;
        _pendingSnapshot = null;
        packet.Dispose();
        if (result == PlaybackMetadataAwareNativeSendResult.Success)
        {
            _nextMergeIndex++;
            return new(
                PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                    .Submitted,
                snapshot);
        }

        return Fail(MapSendFailure(result), snapshot);
    }

    private PlaybackIsoBmffRegularMovieNativePacketSubmissionAdvance Fail(
        PlaybackIsoBmffRegularMovieNativePacketSubmissionResult result,
        PlaybackIsoBmffRegularMovieNativePacketSubmissionSnapshot? snapshot =
            null)
    {
        _faulted = true;
        DisposeOwnedState();
        return new(result, snapshot);
    }

    private void DisposeOwnedState()
    {
        var packet = _pendingPacket;
        var session = _session;
        _pendingPacket = null;
        _pendingStep = null;
        _pendingSnapshot = null;
        _session = null;
        try
        {
            packet?.Dispose();
        }
        finally
        {
            session?.Dispose();
        }
    }

    private static bool TryCreateInput(
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeStep step,
        out PlaybackMetadataAwareNativePacketInput input,
        out PlaybackIsoBmffRegularMovieNativePacketSubmissionSnapshot snapshot,
        out PlaybackIsoBmffRegularMovieNativePacketSubmissionResult rejection)
    {
        input = default;
        snapshot = null!;
        rejection =
            PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                .InvalidPacket;
        var source = session.Source.Source.Source;
        if (step.MergeIndex < 0 ||
            step.MergeIndex >= source.MergeTraversal.Traversal.Steps.Count ||
            !ReferenceEquals(
                step,
                source.MergeTraversal.Traversal.Steps[step.MergeIndex]))
        {
            return false;
        }

        var binding = step.Role switch
        {
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video =>
                session.Source.Video,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Audio =>
                session.Source.Audio,
            _ => null,
        };
        var expectedStream = step.Role switch
        {
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video =>
                source.Selection.Selection.VideoStream,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Audio =>
                source.Selection.Selection.AudioStream,
            _ => null,
        };
        var timeBase = step.Role switch
        {
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video =>
                session.VideoTimeBase,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Audio =>
                session.AudioTimeBase,
            _ => null,
        };
        if (binding is null || expectedStream is null || timeBase is null ||
            !ReferenceEquals(step.Stream, expectedStream) ||
            !ReferenceEquals(step.Stream.Sequence, binding.Track.Sequence) ||
            binding.Track.Sources.MediaTiming?.Timescale is not { } timescale ||
            timescale == 0 || timeBase.Value != new PlaybackBoundedDemuxTimeBase(
                1,
                checked((int)timescale)))
        {
            rejection =
                PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                    .RoleUnavailable;
            return false;
        }

        var packet = step.Packet.TrackPacket.Packet;
        var candidate = packet.Candidate;
        if (packet.Payload.IsEmpty ||
            packet.Payload.Length >
                PlaybackMetadataAwareNativePacketBoundary.MaximumPacketBytes ||
            candidate.MediaTimescale != timescale ||
            candidate.SampleNumber != step.SampleNumber)
        {
            return false;
        }

        var presentation = candidate.Presentation;
        var presentationTimestamp = presentation.StartTimeUnits;
        var decodeTimestamp = presentationTimestamp -
            presentation.CompositionOffsetUnits;
        if (presentationTimestamp < long.MinValue ||
            presentationTimestamp > long.MaxValue ||
            decodeTimestamp < long.MinValue ||
            decodeTimestamp > long.MaxValue)
        {
            rejection =
                PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                    .TimestampRejected;
            return false;
        }

        var isKeyframe = step.Role ==
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video &&
            packet.AvcPayloadComposition?.Selection.Classification
                .ContainsIdr == true;
        var pts = (long)presentationTimestamp;
        var dts = (long)decodeTimestamp;
        var duration = (long)presentation.DurationUnits;
        var flags = isKeyframe ? AvPacketFlagKey : 0;
        input = new(
            packet.Payload,
            pts,
            dts,
            duration,
            step.StreamIndex,
            flags,
            timeBase.Value);
        snapshot = new(
            step.MergeIndex,
            step.Role,
            step.StreamIndex,
            step.TrackId,
            step.SampleNumber,
            pts,
            dts,
            duration,
            timeBase.Value,
            packet.Payload.Length,
            isKeyframe);
        return true;
    }

    private static
        PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
            MapCreationFailure(PlaybackMetadataAwareNativeSendResult result) =>
        result switch
        {
            PlaybackMetadataAwareNativeSendResult.GateRejected =>
                PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                    .GateRejected,
            PlaybackMetadataAwareNativeSendResult.AllocationFailed =>
                PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                    .NativeAllocationFailed,
            _ => PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                .NativeRejected,
        };

    private static
        PlaybackIsoBmffRegularMovieNativePacketSubmissionResult MapSendFailure(
            PlaybackMetadataAwareNativeSendResult result) =>
        result switch
        {
            PlaybackMetadataAwareNativeSendResult.EndOfStream =>
                PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                    .NativeEndOfStream,
            PlaybackMetadataAwareNativeSendResult.GateRejected =>
                PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                    .GateRejected,
            PlaybackMetadataAwareNativeSendResult.AllocationFailed =>
                PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                    .NativeAllocationFailed,
            _ => PlaybackIsoBmffRegularMovieNativePacketSubmissionResult
                .NativeRejected,
        };

    private static PlaybackIsoBmffRegularMovieNativePacketSubmissionAdvance
        Complete(
            PlaybackIsoBmffRegularMovieNativePacketSubmissionResult result) =>
        new(result, null);
}

public sealed record
    PlaybackIsoBmffRegularMovieNativePacketSubmissionCreation(
        PlaybackIsoBmffRegularMovieNativePacketSubmissionCreationResult Result,
        PlaybackIsoBmffRegularMovieNativePacketSubmission? Submission);

public sealed class PlaybackIsoBmffRegularMovieNativePacketSubmissionFactory
{
    private readonly
        IPlaybackIsoBmffRegularMovieNativePacketSubmissionOperations
            _operations;

    public PlaybackIsoBmffRegularMovieNativePacketSubmissionFactory()
        : this(
            new PlaybackIsoBmffRegularMovieNativePacketSubmissionOperations())
    {
    }

    internal PlaybackIsoBmffRegularMovieNativePacketSubmissionFactory(
        IPlaybackIsoBmffRegularMovieNativePacketSubmissionOperations
            operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public PlaybackIsoBmffRegularMovieNativePacketSubmissionCreation Create(
        PlaybackIsoBmffRegularMovieNativeDecoderSession? session)
    {
        if (session is null)
        {
            return new(
                PlaybackIsoBmffRegularMovieNativePacketSubmissionCreationResult
                    .MissingSession,
                null);
        }

        if (session.IsDisposed)
        {
            session.Dispose();
            return new(
                PlaybackIsoBmffRegularMovieNativePacketSubmissionCreationResult
                    .SessionUnavailable,
                null);
        }

        var source = session.Source.Source.Source;
        if (!IsExactSource(session, source))
        {
            session.Dispose();
            return new(
                PlaybackIsoBmffRegularMovieNativePacketSubmissionCreationResult
                    .SourceMismatch,
                null);
        }

        if (source.MergeTraversal.Traversal.Steps.Count == 0)
        {
            session.Dispose();
            return new(
                PlaybackIsoBmffRegularMovieNativePacketSubmissionCreationResult
                    .EmptyTraversal,
                null);
        }

        return new(
            PlaybackIsoBmffRegularMovieNativePacketSubmissionCreationResult
                .Created,
            new PlaybackIsoBmffRegularMovieNativePacketSubmission(
                session,
                _operations));
    }

    private static bool IsExactSource(
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        PlaybackIsoBmffRegularMovieDemuxComposition source)
    {
        if (!ReferenceEquals(session.Source.Source.Source, source) ||
            !ReferenceEquals(
                source.MergeTraversal.Source,
                source.CursorSet) ||
            session.HasVideo != (source.Selection.VideoSource is not null) ||
            session.HasAudio != (source.Selection.AudioSource is not null))
        {
            return false;
        }

        var videoMatches = MatchesRole(
            session.Source.Video,
            source.Selection.VideoSource,
            source.Selection.Selection.VideoStream,
            session.VideoTimeBase);
        var audioMatches = MatchesRole(
            session.Source.Audio,
            source.Selection.AudioSource,
            source.Selection.Selection.AudioStream,
            session.AudioTimeBase);
        if (!videoMatches || !audioMatches)
        {
            return false;
        }

        var steps = source.MergeTraversal.Traversal.Steps;
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var expectedStream = step.Role switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole
                    .Video => source.Selection.Selection.VideoStream,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole
                    .Audio => source.Selection.Selection.AudioStream,
                _ => null,
            };
            if (step.MergeIndex != index || expectedStream is null ||
                !ReferenceEquals(step.Stream, expectedStream))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesRole(
        PlaybackIsoBmffRegularMovieVerifiedRuntimeTrackCapabilityBinding?
            binding,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection? source,
        PlaybackIsoBmffIndexedTrackCodecPacketSequence? stream,
        PlaybackBoundedDemuxTimeBase? timeBase)
    {
        if (binding is null || source is null || stream is null ||
            timeBase is null)
        {
            return binding is null && source is null && stream is null &&
                timeBase is null;
        }

        var timescale = binding.Track.Sources.MediaTiming?.Timescale;
        return timescale is > 0 and <=
                PlaybackBoundedDemuxOptions.MaximumTimeBaseComponent &&
            ReferenceEquals(binding.Track.SequenceProjection, source) &&
            ReferenceEquals(binding.Track.Sequence, stream.Sequence) &&
            timeBase.Value == new PlaybackBoundedDemuxTimeBase(
                1,
                checked((int)timescale.Value));
    }
}
