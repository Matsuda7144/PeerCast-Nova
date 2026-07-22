using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public enum PlaybackControlledWindowsIndependentRunSetState
{
    Created,
    AwaitingVideo,
    AwaitingAudio,
    Completed,
    Rejected,
    Disposed,
}

public enum PlaybackControlledWindowsIndependentRunSetResult
{
    None,
    VideoAccepted,
    AudioAccepted,
    Completed,
    InvalidRecord,
    RuntimeRejected,
    FixtureRejected,
    EvidenceRejected,
    CleanupRejected,
    DuplicateKind,
    AlreadyCompleted,
    AlreadyRejected,
    Disposed,
}

public sealed record PlaybackControlledWindowsIndependentRunSetSnapshot(
    PlaybackControlledWindowsIndependentRunSetState State,
    PlaybackControlledWindowsIndependentRunSetResult Result,
    string RuntimeBuildId,
    string FixtureSha256,
    bool HasVideoRecord,
    bool HasAudioRecord,
    bool IsComplete);

/// <summary>
/// Fail-closed admission for the two independently executed physical records.
/// It retains only sanitized identity and presence flags. It owns no runner,
/// runtime, fixture, output resource, local path, device identity, or payload.
/// </summary>
public sealed class PlaybackControlledWindowsIndependentRunSetBoundary : IDisposable
{
    private readonly object _sync = new();
    private readonly string _expectedRuntimeBuildId;
    private readonly string? _expectedFixtureSha256;
    private PlaybackControlledWindowsIndependentRunSetState _state =
        PlaybackControlledWindowsIndependentRunSetState.Created;
    private PlaybackControlledWindowsIndependentRunSetResult _result;
    private bool _hasVideoRecord;
    private bool _hasAudioRecord;
    private bool _isDisposed;

    public PlaybackControlledWindowsIndependentRunSetBoundary(string expectedFixtureSha256)
        : this(PlaybackFfmpegBackendCandidate.BuildId, expectedFixtureSha256)
    {
    }

    public PlaybackControlledWindowsIndependentRunSetBoundary(
        string expectedRuntimeBuildId,
        string expectedFixtureSha256)
    {
        _expectedRuntimeBuildId = expectedRuntimeBuildId ?? string.Empty;
        _expectedFixtureSha256 =
            PlaybackControlledWindowsPhysicalRunRecordCodec.TryNormalizeSha256(
                expectedFixtureSha256,
                out var normalized)
                ? normalized
                : null;
    }

    public PlaybackControlledWindowsIndependentRunSetSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _state,
                    _result,
                    _expectedRuntimeBuildId,
                    _expectedFixtureSha256 ?? string.Empty,
                    _hasVideoRecord,
                    _hasAudioRecord,
                    _state == PlaybackControlledWindowsIndependentRunSetState.Completed);
            }
        }
    }

    public PlaybackControlledWindowsIndependentRunSetResult ValidateAndConsume(
        PlaybackControlledWindowsPhysicalRunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_sync)
        {
            if (_isDisposed)
            {
                return SetResult(PlaybackControlledWindowsIndependentRunSetResult.Disposed);
            }

            if (_state == PlaybackControlledWindowsIndependentRunSetState.Completed)
            {
                return SetResult(PlaybackControlledWindowsIndependentRunSetResult.AlreadyCompleted);
            }

            if (_state == PlaybackControlledWindowsIndependentRunSetState.Rejected)
            {
                return SetResult(PlaybackControlledWindowsIndependentRunSetResult.AlreadyRejected);
            }

            var validation = ValidateRecord(record);
            if (validation != PlaybackControlledWindowsIndependentRunSetResult.None)
            {
                return Reject(validation);
            }

            if (record.Kind == PlaybackControlledWindowsOutputValidationKind.Video)
            {
                if (_hasVideoRecord)
                {
                    return Reject(
                        PlaybackControlledWindowsIndependentRunSetResult.DuplicateKind);
                }

                _hasVideoRecord = true;
                if (_hasAudioRecord)
                {
                    _state = PlaybackControlledWindowsIndependentRunSetState.Completed;
                    return SetResult(
                        PlaybackControlledWindowsIndependentRunSetResult.Completed);
                }

                _state = PlaybackControlledWindowsIndependentRunSetState.AwaitingAudio;
                return SetResult(
                    PlaybackControlledWindowsIndependentRunSetResult.VideoAccepted);
            }

            if (_hasAudioRecord)
            {
                return Reject(PlaybackControlledWindowsIndependentRunSetResult.DuplicateKind);
            }

            _hasAudioRecord = true;
            if (_hasVideoRecord)
            {
                _state = PlaybackControlledWindowsIndependentRunSetState.Completed;
                return SetResult(PlaybackControlledWindowsIndependentRunSetResult.Completed);
            }

            _state = PlaybackControlledWindowsIndependentRunSetState.AwaitingVideo;
            return SetResult(PlaybackControlledWindowsIndependentRunSetResult.AudioAccepted);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _state = PlaybackControlledWindowsIndependentRunSetState.Disposed;
            _result = PlaybackControlledWindowsIndependentRunSetResult.Disposed;
            _hasVideoRecord = false;
            _hasAudioRecord = false;
        }
    }

    private PlaybackControlledWindowsIndependentRunSetResult ValidateRecord(
        PlaybackControlledWindowsPhysicalRunRecord record)
    {
        if (_expectedFixtureSha256 is null ||
            string.IsNullOrWhiteSpace(_expectedRuntimeBuildId) ||
            record.SchemaVersion !=
                PlaybackControlledWindowsPhysicalRunRecordCodec.CurrentSchemaVersion ||
            !string.Equals(
                record.Milestone,
                PlaybackControlledWindowsPhysicalRunRecordCodec.CurrentMilestone,
                StringComparison.Ordinal) ||
            record.Kind is not (
                PlaybackControlledWindowsOutputValidationKind.Video or
                PlaybackControlledWindowsOutputValidationKind.Audio))
        {
            return PlaybackControlledWindowsIndependentRunSetResult.InvalidRecord;
        }

        if (!string.Equals(
                record.RuntimeBuildId,
                _expectedRuntimeBuildId,
                StringComparison.Ordinal))
        {
            return PlaybackControlledWindowsIndependentRunSetResult.RuntimeRejected;
        }

        if (!PlaybackControlledWindowsPhysicalRunRecordCodec.TryNormalizeSha256(
                record.FixtureSha256,
                out var normalizedDigest) ||
            !string.Equals(
                normalizedDigest,
                _expectedFixtureSha256,
                StringComparison.Ordinal))
        {
            return PlaybackControlledWindowsIndependentRunSetResult.FixtureRejected;
        }

        if (record.Result != PlaybackControlledWindowsPhysicalRunResult.Completed ||
            record.EvidenceResult != PlaybackControlledWindowsOutputEvidenceResult.Accepted ||
            record.FixtureByteCount <= 0 || record.FixturePacketCount <= 0 ||
            record.DecodeOperationCount <= 0 || record.DeliveryAttemptCount <= 0 ||
            record.DeliveredCount != 1 || record.RetainedWouldBlockCount != 0 ||
            record.DeliveryAttemptCount < record.DeliveredCount)
        {
            return PlaybackControlledWindowsIndependentRunSetResult.EvidenceRejected;
        }

        if (record.RunnerState != PlaybackWindowsPhysicalRunnerState.Stopped ||
            record.LifecycleState !=
                PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed ||
            !record.BorrowedWindowPreserved ||
            !record.BorrowedDispatcherPreserved ||
            record.ExternalBinaryOrPayloadPublished)
        {
            return PlaybackControlledWindowsIndependentRunSetResult.CleanupRejected;
        }

        return PlaybackControlledWindowsIndependentRunSetResult.None;
    }

    private PlaybackControlledWindowsIndependentRunSetResult Reject(
        PlaybackControlledWindowsIndependentRunSetResult result)
    {
        _state = PlaybackControlledWindowsIndependentRunSetState.Rejected;
        return SetResult(result);
    }

    private PlaybackControlledWindowsIndependentRunSetResult SetResult(
        PlaybackControlledWindowsIndependentRunSetResult result)
    {
        _result = result;
        return result;
    }
}
