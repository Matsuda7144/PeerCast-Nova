namespace PeerCast.Nova.Playback;

public readonly record struct PlaybackWindowsOutputNativeEvidenceSnapshot(
    PlaybackControlledWindowsOutputEvidenceSource Source,
    bool CounterOverflowed,
    bool VideoConfigurationConflict,
    PlaybackDecodedVideoPixelFormat VideoPixelFormat,
    int VideoWidth,
    int VideoHeight,
    int VideoStride,
    long VideoGeneration,
    long VideoPresentAttemptCount,
    long VideoPresentAcknowledgementCount,
    long VideoAcceptedByteCount,
    long VideoWouldBlockCount,
    long VideoResizeCount,
    long VideoDeviceLostCount,
    bool AudioConfigurationConflict,
    PlaybackDecodedAudioSampleFormat AudioSampleFormat,
    int AudioSampleRate,
    int AudioChannelCount,
    long AudioGeneration,
    long AudioWriteAttemptCount,
    long AudioWriteAcknowledgementCount,
    long AudioAcceptedFrameCount,
    long AudioAcceptedByteCount,
    long AudioWouldBlockCount,
    long AudioStartAcknowledgementCount,
    long AudioDeviceLostCount)
{
    public PlaybackWindowsAudioRenderTraceSnapshot AudioRenderTrace { get; init; }
}

public sealed record PlaybackControlledWindowsOutputCaptureContext(
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
    TimeSpan AudioBufferDuration,
    PlaybackWindowsOutputBufferSnapshot Buffers,
    PlaybackControlledWindowsOutputCleanupEvidence Cleanup);

/// <summary>
/// Converts one sanitized native-output snapshot plus finite lifecycle counters
/// into the exact Phase 1 evidence contract. It owns no native resource, window,
/// dispatcher, runtime path, fixture bytes, decoded payload, queue, or retry.
/// </summary>
public sealed class PlaybackControlledWindowsOutputCaptureBoundary : IDisposable
{
    private readonly PlaybackControlledWindowsOutputValidationKind _kind;
    private readonly PlaybackControlledWindowsOutputEvidenceBoundary _admission;

    public PlaybackControlledWindowsOutputCaptureBoundary(
        PlaybackControlledWindowsOutputValidationKind kind,
        string expectedFixtureSha256)
    {
        _kind = kind;
        _admission = new(kind, expectedFixtureSha256);
    }

    public PlaybackControlledWindowsOutputEvidenceSnapshot Snapshot => _admission.Snapshot;

    public PlaybackControlledWindowsOutputEvidenceResult CaptureAndConsume(
        PlaybackControlledWindowsOutputCaptureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var native = context.Buffers.NativeEvidence;
        PlaybackControlledWindowsVideoOutputEvidence? video = null;
        PlaybackControlledWindowsAudioOutputEvidence? audio = null;

        if (_kind == PlaybackControlledWindowsOutputValidationKind.Video)
        {
            var conflict = native.CounterOverflowed || native.VideoConfigurationConflict ||
                native.VideoGeneration != context.OutputGeneration ||
                context.Buffers.VideoGeneration != context.OutputGeneration ||
                native.VideoWouldBlockCount != context.RetainedWouldBlockCount;
            video = new(
                native.VideoPixelFormat,
                conflict ? 0 : native.VideoWidth,
                native.VideoHeight,
                native.VideoStride,
                native.VideoPresentAttemptCount,
                native.VideoPresentAcknowledgementCount,
                native.VideoAcceptedByteCount,
                native.VideoResizeCount,
                native.VideoDeviceLostCount);
        }
        else if (_kind == PlaybackControlledWindowsOutputValidationKind.Audio)
        {
            var conflict = native.CounterOverflowed || native.AudioConfigurationConflict ||
                native.AudioGeneration != context.OutputGeneration ||
                context.Buffers.AudioGeneration != context.OutputGeneration ||
                native.AudioWouldBlockCount != context.RetainedWouldBlockCount;
            audio = new(
                native.AudioSampleFormat,
                conflict ? 0 : native.AudioSampleRate,
                native.AudioChannelCount,
                context.AudioBufferDuration,
                native.AudioWriteAttemptCount,
                native.AudioWriteAcknowledgementCount,
                native.AudioAcceptedFrameCount,
                native.AudioAcceptedByteCount,
                native.AudioStartAcknowledgementCount,
                native.AudioDeviceLostCount);
        }

        return _admission.ValidateAndConsume(new(
            _kind,
            native.Source,
            context.RuntimeBuildId,
            context.FixtureSha256,
            context.DecoderGeneration,
            context.OutputGeneration,
            context.DeliveryAttemptCount,
            context.DeliveredCount,
            context.RetainedWouldBlockCount,
            context.HiddenRetryCount,
            context.DeviceFallbackCount,
            context.DeviceRecreationCount,
            video,
            audio,
            context.Cleanup));
    }

    public void Dispose() => _admission.Dispose();
}

internal interface IPlaybackWindowsOutputNativeEvidenceSource
{
    PlaybackWindowsOutputNativeEvidenceSnapshot Snapshot { get; }
}

internal sealed class PlaybackWindowsOutputNativeEvidenceProcessor :
    IPlaybackWindowsOutputNativeBufferProcessor,
    IPlaybackWindowsOutputNativeEvidenceSource
{
    private readonly object _sync = new();
    private readonly IPlaybackWindowsOutputNativeBufferProcessor _inner;
    private readonly PlaybackControlledWindowsOutputEvidenceSource _source;
    private bool _counterOverflowed;
    private bool _videoConfigurationConflict;
    private PlaybackDecodedVideoPixelFormat _videoPixelFormat;
    private int _videoWidth;
    private int _videoHeight;
    private int _videoStride;
    private long _videoGeneration = -1;
    private long _videoPresentAttemptCount;
    private long _videoPresentAcknowledgementCount;
    private long _videoAcceptedByteCount;
    private long _videoWouldBlockCount;
    private long _videoResizeCount;
    private long _videoDeviceLostCount;
    private bool _audioConfigurationConflict;
    private PlaybackDecodedAudioSampleFormat _audioSampleFormat;
    private int _audioSampleRate;
    private int _audioChannelCount;
    private long _audioGeneration = -1;
    private long _audioWriteAttemptCount;
    private long _audioWriteAcknowledgementCount;
    private long _audioAcceptedFrameCount;
    private long _audioAcceptedByteCount;
    private long _audioWouldBlockCount;
    private long _audioStartAcknowledgementCount;
    private long _audioDeviceLostCount;

    internal PlaybackWindowsOutputNativeEvidenceProcessor(
        IPlaybackWindowsOutputNativeBufferProcessor inner,
        PlaybackControlledWindowsOutputEvidenceSource source)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (source is not (
            PlaybackControlledWindowsOutputEvidenceSource.Injected or
            PlaybackControlledWindowsOutputEvidenceSource.SystemWindows))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        _inner = inner;
        _source = source;
    }

    public PlaybackWindowsOutputNativeEvidenceSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _source,
                    _counterOverflowed,
                    _videoConfigurationConflict,
                    _videoPixelFormat,
                    _videoWidth,
                    _videoHeight,
                    _videoStride,
                    _videoGeneration,
                    _videoPresentAttemptCount,
                    _videoPresentAcknowledgementCount,
                    _videoAcceptedByteCount,
                    _videoWouldBlockCount,
                    _videoResizeCount,
                    _videoDeviceLostCount,
                    _audioConfigurationConflict,
                    _audioSampleFormat,
                    _audioSampleRate,
                    _audioChannelCount,
                    _audioGeneration,
                    _audioWriteAttemptCount,
                    _audioWriteAcknowledgementCount,
                    _audioAcceptedFrameCount,
                    _audioAcceptedByteCount,
                    _audioWouldBlockCount,
                    _audioStartAcknowledgementCount,
                    _audioDeviceLostCount)
                {
                    AudioRenderTrace =
                        (_inner as IPlaybackWindowsAudioRenderTraceSource)?.AudioRenderTrace ??
                        default,
                };
            }
        }
    }

    public PlaybackWindowsOutputNativeBufferResult ProcessVideo(
        IDisposable borrowedResource,
        PlaybackWindowsVideoFrameBuffer frame)
    {
        lock (_sync)
        {
            Add(ref _videoPresentAttemptCount, 1);
        }

        PlaybackWindowsOutputNativeBufferResult result;
        try
        {
            result = _inner.ProcessVideo(borrowedResource, frame);
        }
        catch
        {
            result = PlaybackWindowsOutputNativeBufferResult.Failure;
        }

        lock (_sync)
        {
            RecordVideo(result, frame);
        }

        return result;
    }

    public PlaybackWindowsOutputNativeBufferResult ProcessAudio(
        IDisposable borrowedResource,
        PlaybackWindowsAudioSampleBuffer samples,
        bool startStream)
    {
        lock (_sync)
        {
            Add(ref _audioWriteAttemptCount, 1);
        }

        PlaybackWindowsOutputNativeBufferResult result;
        try
        {
            result = _inner.ProcessAudio(borrowedResource, samples, startStream);
        }
        catch
        {
            result = PlaybackWindowsOutputNativeBufferResult.Failure;
        }

        lock (_sync)
        {
            RecordAudio(result, samples, startStream);
        }

        return result;
    }

    public void ResetVideo(IDisposable borrowedResource) => _inner.ResetVideo(borrowedResource);

    public void ResetAudio(IDisposable borrowedResource) => _inner.ResetAudio(borrowedResource);

    private void RecordVideo(
        PlaybackWindowsOutputNativeBufferResult result,
        PlaybackWindowsVideoFrameBuffer frame)
    {
        switch (result)
        {
            case PlaybackWindowsOutputNativeBufferResult.Processed:
                if (_videoPresentAcknowledgementCount == 0)
                {
                    _videoPixelFormat = frame.PixelFormat;
                    _videoWidth = frame.Width;
                    _videoHeight = frame.Height;
                    _videoStride = frame.Stride;
                    _videoGeneration = frame.Generation;
                }
                else if (_videoPixelFormat != frame.PixelFormat ||
                    _videoWidth != frame.Width || _videoHeight != frame.Height ||
                    _videoStride != frame.Stride || _videoGeneration != frame.Generation)
                {
                    _videoConfigurationConflict = true;
                }

                Add(ref _videoPresentAcknowledgementCount, 1);
                Add(ref _videoAcceptedByteCount, frame.Buffer.Length);
                break;
            case PlaybackWindowsOutputNativeBufferResult.WouldBlock:
                Add(ref _videoWouldBlockCount, 1);
                break;
            case PlaybackWindowsOutputNativeBufferResult.ResizeRequired:
                Add(ref _videoResizeCount, 1);
                break;
            case PlaybackWindowsOutputNativeBufferResult.DeviceLost:
                Add(ref _videoDeviceLostCount, 1);
                break;
        }
    }

    private void RecordAudio(
        PlaybackWindowsOutputNativeBufferResult result,
        PlaybackWindowsAudioSampleBuffer samples,
        bool startStream)
    {
        switch (result)
        {
            case PlaybackWindowsOutputNativeBufferResult.Processed:
                if (_audioWriteAcknowledgementCount == 0)
                {
                    _audioSampleFormat = samples.SampleFormat;
                    _audioSampleRate = samples.SampleRate;
                    _audioChannelCount = samples.ChannelCount;
                    _audioGeneration = samples.Generation;
                }
                else if (_audioSampleFormat != samples.SampleFormat ||
                    _audioSampleRate != samples.SampleRate ||
                    _audioChannelCount != samples.ChannelCount ||
                    _audioGeneration != samples.Generation)
                {
                    _audioConfigurationConflict = true;
                }

                Add(ref _audioWriteAcknowledgementCount, 1);
                Add(ref _audioAcceptedFrameCount, samples.FrameCount);
                Add(ref _audioAcceptedByteCount, samples.Buffer.Length);
                if (startStream)
                {
                    Add(ref _audioStartAcknowledgementCount, 1);
                }
                break;
            case PlaybackWindowsOutputNativeBufferResult.WouldBlock:
                Add(ref _audioWouldBlockCount, 1);
                break;
            case PlaybackWindowsOutputNativeBufferResult.DeviceLost:
                Add(ref _audioDeviceLostCount, 1);
                break;
        }
    }

    private void Add(ref long target, long value)
    {
        if (value < 0 || target > long.MaxValue - value)
        {
            _counterOverflowed = true;
            return;
        }

        target += value;
    }
}
