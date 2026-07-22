namespace PeerCast.Nova.Playback;

public enum PlaybackControlledWindowsOutputValidationKind
{
    None,
    Video,
    Audio,
}

public enum PlaybackControlledWindowsOutputEvidenceSource
{
    None,
    Injected,
    SystemWindows,
}

public enum PlaybackControlledWindowsOutputEvidenceState
{
    Created,
    Accepted,
    Rejected,
    Disposed,
}

public enum PlaybackControlledWindowsOutputEvidenceResult
{
    None,
    Accepted,
    InvalidEvidence,
    SourceRejected,
    RuntimeRejected,
    FixtureRejected,
    ConfigurationRejected,
    GenerationMismatch,
    DeliveryEvidenceRequired,
    PhysicalAcknowledgementRequired,
    RetryPolicyRejected,
    CleanupRejected,
    AlreadyCompleted,
    Disposed,
}

public sealed record PlaybackControlledWindowsVideoOutputEvidence(
    PlaybackDecodedVideoPixelFormat PixelFormat,
    int Width,
    int Height,
    int Stride,
    long PresentAttemptCount,
    long PresentAcknowledgementCount,
    long AcceptedByteCount,
    long ResizeCount,
    long DeviceLostCount);

public sealed record PlaybackControlledWindowsAudioOutputEvidence(
    PlaybackDecodedAudioSampleFormat SampleFormat,
    int SampleRate,
    int ChannelCount,
    TimeSpan BufferDuration,
    long WriteAttemptCount,
    long WriteAcknowledgementCount,
    long AcceptedFrameCount,
    long AcceptedByteCount,
    long StartAcknowledgementCount,
    long DeviceLostCount);

public readonly record struct PlaybackControlledWindowsOutputCleanupEvidence(
    int ProductionStopOrder,
    int BufferDisposeOrder,
    int StreamDisposeOrder,
    int BindingDisposeOrder,
    int ActivationDisposeOrder,
    bool OwnerThreadTeardown,
    bool BorrowedWindowPreserved,
    bool BorrowedDispatcherPreserved);

public sealed record PlaybackControlledWindowsOutputEvidence(
    PlaybackControlledWindowsOutputValidationKind Kind,
    PlaybackControlledWindowsOutputEvidenceSource Source,
    string RuntimeBuildId,
    string FixtureSha256,
    long DecoderGeneration,
    long OutputGeneration,
    long DeliveryAttemptCount,
    long DeliveredCount,
    long RetainedWouldBlockCount,
    long HiddenRetryCount,
    long DeviceFallbackCount,
    long DeviceRecreationCount,
    PlaybackControlledWindowsVideoOutputEvidence? Video,
    PlaybackControlledWindowsAudioOutputEvidence? Audio,
    PlaybackControlledWindowsOutputCleanupEvidence Cleanup);

public sealed record PlaybackControlledWindowsOutputEvidenceSnapshot(
    PlaybackControlledWindowsOutputEvidenceState State,
    PlaybackControlledWindowsOutputEvidenceResult Result,
    PlaybackControlledWindowsOutputValidationKind Kind,
    string RuntimeBuildId,
    string FixtureSha256,
    long DecoderGeneration,
    long OutputGeneration,
    bool HasAcceptedEvidence);

/// <summary>
/// One-shot admission gate for sanitized deterministic evidence produced by a
/// finite controlled Windows output run. The gate owns no window, dispatcher,
/// native resource, decoded payload, runtime path, device identifier, or retry.
/// </summary>
public sealed class PlaybackControlledWindowsOutputEvidenceBoundary : IDisposable
{
    private readonly object _sync = new();
    private readonly PlaybackControlledWindowsOutputValidationKind _kind;
    private readonly string? _expectedFixtureSha256;
    private PlaybackControlledWindowsOutputEvidenceState _state =
        PlaybackControlledWindowsOutputEvidenceState.Created;
    private PlaybackControlledWindowsOutputEvidenceResult _result =
        PlaybackControlledWindowsOutputEvidenceResult.None;
    private PlaybackControlledWindowsOutputEvidence? _acceptedEvidence;
    private bool _isDisposed;

    public PlaybackControlledWindowsOutputEvidenceBoundary(
        PlaybackControlledWindowsOutputValidationKind kind,
        string expectedFixtureSha256)
    {
        _kind = kind;
        _expectedFixtureSha256 = TryNormalizeSha256(expectedFixtureSha256, out var normalized)
            ? normalized
            : null;
    }

    public PlaybackControlledWindowsOutputEvidenceSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _state,
                    _result,
                    _kind,
                    _acceptedEvidence?.RuntimeBuildId ?? string.Empty,
                    _acceptedEvidence?.FixtureSha256 ?? _expectedFixtureSha256 ?? string.Empty,
                    _acceptedEvidence?.DecoderGeneration ?? -1,
                    _acceptedEvidence?.OutputGeneration ?? -1,
                    _acceptedEvidence is not null);
            }
        }
    }

    public PlaybackControlledWindowsOutputEvidenceResult ValidateAndConsume(
        PlaybackControlledWindowsOutputEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        lock (_sync)
        {
            if (_isDisposed)
            {
                return SetResult(PlaybackControlledWindowsOutputEvidenceResult.Disposed);
            }

            if (_state != PlaybackControlledWindowsOutputEvidenceState.Created)
            {
                return SetResult(PlaybackControlledWindowsOutputEvidenceResult.AlreadyCompleted);
            }

            var result = Validate(evidence);
            if (result == PlaybackControlledWindowsOutputEvidenceResult.Accepted)
            {
                _acceptedEvidence = Copy(evidence);
                _state = PlaybackControlledWindowsOutputEvidenceState.Accepted;
            }
            else
            {
                _state = PlaybackControlledWindowsOutputEvidenceState.Rejected;
            }

            return SetResult(result);
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
            _acceptedEvidence = null;
            _state = PlaybackControlledWindowsOutputEvidenceState.Disposed;
            _result = PlaybackControlledWindowsOutputEvidenceResult.Disposed;
        }
    }

    private PlaybackControlledWindowsOutputEvidenceResult Validate(
        PlaybackControlledWindowsOutputEvidence evidence)
    {
        if (!Enum.IsDefined(_kind) || _kind == PlaybackControlledWindowsOutputValidationKind.None ||
            !Enum.IsDefined(evidence.Kind) || evidence.Kind != _kind ||
            _expectedFixtureSha256 is null)
        {
            return PlaybackControlledWindowsOutputEvidenceResult.InvalidEvidence;
        }

        if (!Enum.IsDefined(evidence.Source) ||
            evidence.Source != PlaybackControlledWindowsOutputEvidenceSource.SystemWindows)
        {
            return PlaybackControlledWindowsOutputEvidenceResult.SourceRejected;
        }

        if (!string.Equals(
                evidence.RuntimeBuildId,
                PlaybackFfmpegBackendCandidate.BuildId,
                StringComparison.Ordinal))
        {
            return PlaybackControlledWindowsOutputEvidenceResult.RuntimeRejected;
        }

        if (!TryNormalizeSha256(evidence.FixtureSha256, out var observedFixture) ||
            !string.Equals(
                observedFixture,
                _expectedFixtureSha256,
                StringComparison.Ordinal))
        {
            return PlaybackControlledWindowsOutputEvidenceResult.FixtureRejected;
        }

        if (evidence.DecoderGeneration < 0 || evidence.OutputGeneration < 0 ||
            evidence.DecoderGeneration != evidence.OutputGeneration)
        {
            return PlaybackControlledWindowsOutputEvidenceResult.GenerationMismatch;
        }

        if (evidence.DeliveryAttemptCount <= 0 || evidence.DeliveredCount <= 0 ||
            evidence.RetainedWouldBlockCount < 0 ||
            evidence.DeliveredCount > evidence.DeliveryAttemptCount ||
            evidence.RetainedWouldBlockCount > evidence.DeliveryAttemptCount ||
            evidence.DeliveredCount >
                evidence.DeliveryAttemptCount - evidence.RetainedWouldBlockCount)
        {
            return PlaybackControlledWindowsOutputEvidenceResult.DeliveryEvidenceRequired;
        }

        if (evidence.HiddenRetryCount != 0 || evidence.DeviceFallbackCount != 0 ||
            evidence.DeviceRecreationCount != 0)
        {
            return PlaybackControlledWindowsOutputEvidenceResult.RetryPolicyRejected;
        }

        var output = _kind == PlaybackControlledWindowsOutputValidationKind.Video
            ? ValidateVideo(evidence)
            : ValidateAudio(evidence);
        if (output != PlaybackControlledWindowsOutputEvidenceResult.Accepted)
        {
            return output;
        }

        return ValidateCleanup(evidence.Cleanup)
            ? PlaybackControlledWindowsOutputEvidenceResult.Accepted
            : PlaybackControlledWindowsOutputEvidenceResult.CleanupRejected;
    }

    private static PlaybackControlledWindowsOutputEvidenceResult ValidateVideo(
        PlaybackControlledWindowsOutputEvidence evidence)
    {
        if (evidence.Video is not { } video || evidence.Audio is not null ||
            video.PixelFormat != PlaybackDecodedVideoPixelFormat.Bgra32 ||
            video.Width <= 0 || video.Height <= 0 || video.Stride <= 0 ||
            video.PresentAttemptCount <= 0 || video.PresentAcknowledgementCount <= 0 ||
            video.AcceptedByteCount <= 0 || video.ResizeCount < 0 ||
            video.DeviceLostCount != 0)
        {
            return PlaybackControlledWindowsOutputEvidenceResult.ConfigurationRejected;
        }

        try
        {
            var frameBytes = checked((long)video.Width * 4 * video.Height);
            var expectedBytes = checked(
                frameBytes * video.PresentAcknowledgementCount);
            if (video.Stride != checked(video.Width * 4) ||
                video.PresentAcknowledgementCount > video.PresentAttemptCount ||
                video.PresentAcknowledgementCount != evidence.DeliveredCount ||
                video.AcceptedByteCount != expectedBytes)
            {
                return PlaybackControlledWindowsOutputEvidenceResult.PhysicalAcknowledgementRequired;
            }
        }
        catch (OverflowException)
        {
            return PlaybackControlledWindowsOutputEvidenceResult.ConfigurationRejected;
        }

        return PlaybackControlledWindowsOutputEvidenceResult.Accepted;
    }

    private static PlaybackControlledWindowsOutputEvidenceResult ValidateAudio(
        PlaybackControlledWindowsOutputEvidence evidence)
    {
        if (evidence.Audio is not { } audio || evidence.Video is not null ||
            audio.SampleFormat is not (
                PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved or
                PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved) ||
            audio.SampleRate <= 0 || audio.ChannelCount <= 0 ||
            audio.BufferDuration < TimeSpan.FromMilliseconds(1) ||
            audio.BufferDuration >
                PlaybackWindowsOutputStreamInitializationBoundary.MaximumAudioBufferDuration ||
            audio.WriteAttemptCount <= 0 || audio.WriteAcknowledgementCount <= 0 ||
            audio.AcceptedFrameCount <= 0 || audio.AcceptedByteCount <= 0 ||
            audio.StartAcknowledgementCount != 1 || audio.DeviceLostCount != 0)
        {
            return PlaybackControlledWindowsOutputEvidenceResult.ConfigurationRejected;
        }

        try
        {
            var bytesPerSample = audio.SampleFormat ==
                PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved ? 2 : 4;
            var expectedBytes = checked(
                audio.AcceptedFrameCount * audio.ChannelCount * bytesPerSample);
            if (audio.WriteAcknowledgementCount > audio.WriteAttemptCount ||
                audio.WriteAcknowledgementCount != evidence.DeliveredCount ||
                audio.AcceptedByteCount != expectedBytes)
            {
                return PlaybackControlledWindowsOutputEvidenceResult.PhysicalAcknowledgementRequired;
            }
        }
        catch (OverflowException)
        {
            return PlaybackControlledWindowsOutputEvidenceResult.ConfigurationRejected;
        }

        return PlaybackControlledWindowsOutputEvidenceResult.Accepted;
    }

    private static bool ValidateCleanup(
        PlaybackControlledWindowsOutputCleanupEvidence cleanup) =>
        cleanup.ProductionStopOrder > 0 &&
        cleanup.ProductionStopOrder < cleanup.BufferDisposeOrder &&
        cleanup.BufferDisposeOrder < cleanup.StreamDisposeOrder &&
        cleanup.StreamDisposeOrder < cleanup.BindingDisposeOrder &&
        cleanup.BindingDisposeOrder < cleanup.ActivationDisposeOrder &&
        cleanup.OwnerThreadTeardown &&
        cleanup.BorrowedWindowPreserved &&
        cleanup.BorrowedDispatcherPreserved;

    private PlaybackControlledWindowsOutputEvidenceResult SetResult(
        PlaybackControlledWindowsOutputEvidenceResult result)
    {
        _result = result;
        return result;
    }

    private static PlaybackControlledWindowsOutputEvidence Copy(
        PlaybackControlledWindowsOutputEvidence evidence) => evidence with
        {
            FixtureSha256 = evidence.FixtureSha256.ToUpperInvariant(),
            Video = evidence.Video is null ? null : evidence.Video with { },
            Audio = evidence.Audio is null ? null : evidence.Audio with { },
        };

    private static bool TryNormalizeSha256(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (value is null || value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        normalized = value.ToUpperInvariant();
        return true;
    }
}
