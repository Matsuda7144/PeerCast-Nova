namespace PeerCast.Nova.Playback;

public enum PlaybackDecoderFfmpegAwaitedSinkDeliveryResult
{
    Delivered,
    ContextUnavailable,
    DisposedFrame,
    UnsupportedFormat,
    InvalidFrame,
    AllocationFailed,
    ConversionFailed,
    SinkRejected,
    Cancelled,
    SinkFailed,
    IntegrationFailed,
}

/// <summary>
/// Serial, awaited delivery from an owned managed FFmpeg frame to the canonical
/// decoded-frame sink. The boundary owns no output queue. Payload memory remains
/// valid only until the awaited sink callback completes.
/// </summary>
public sealed class PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary : IDisposable
{
    private readonly SemaphoreSlim _delivery = new(1, 1);
    private readonly PlaybackDecoderFfmpegDecodedOutputAdapter _adapter;
    private readonly PlaybackDecoderFfmpegManagedFormatConversionBoundary _conversion;
    private int _isDisposed;

    public PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary()
        : this(
            new PlaybackDecoderFfmpegDecodedOutputAdapter(),
            new PlaybackDecoderFfmpegManagedFormatConversionBoundary())
    {
    }

    internal PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary(
        PlaybackDecoderFfmpegDecodedOutputAdapter adapter,
        PlaybackDecoderFfmpegManagedFormatConversionBoundary conversion)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(conversion);
        _adapter = adapter;
        _conversion = conversion;
    }

    public async ValueTask<PlaybackDecoderFfmpegAwaitedSinkDeliveryResult> DeliverAsync(
        PlaybackDecoderFfmpegContextLease contextLease,
        PlaybackDecoderFfmpegManagedFrame frame,
        long sequence,
        PlaybackDecodedFrameDiscontinuityKind discontinuity,
        IPlaybackDecodedFrameSink sink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contextLease);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(sink);
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.IntegrationFailed;
        }

        try
        {
            await _delivery.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Cancelled;
        }

        try
        {
            if (Volatile.Read(ref _isDisposed) != 0)
            {
                return PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.IntegrationFailed;
            }

            if (contextLease.IsDisposed)
            {
                return PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.ContextUnavailable;
            }

            var consumer = new SinkConsumer(sink);
            try
            {
                var adapted = await _adapter.AdaptAsync(
                    contextLease,
                    frame,
                    sequence,
                    discontinuity,
                    consumer,
                    cancellationToken).ConfigureAwait(false);
                if (adapted != PlaybackDecoderFfmpegDecodedOutputAdapterResult.ConversionRequired)
                {
                    return Map(adapted);
                }

                var converted = await _conversion.ConvertAsync(
                    contextLease,
                    frame,
                    sequence,
                    discontinuity,
                    consumer,
                    cancellationToken).ConfigureAwait(false);
                return Map(converted);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Cancelled;
            }
            catch (PlaybackDecodedSinkException)
            {
                return PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.SinkFailed;
            }
            catch
            {
                contextLease.Dispose();
                return PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.IntegrationFailed;
            }
        }
        finally
        {
            _delivery.Release();
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _isDisposed, 1);
        GC.SuppressFinalize(this);
    }

    private static PlaybackDecoderFfmpegAwaitedSinkDeliveryResult Map(
        PlaybackDecoderFfmpegDecodedOutputAdapterResult result) => result switch
        {
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.Converted =>
                PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered,
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.ContextUnavailable =>
                PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.ContextUnavailable,
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.DisposedFrame =>
                PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.DisposedFrame,
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.InvalidFrame =>
                PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.InvalidFrame,
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.AllocationFailed =>
                PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.AllocationFailed,
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.ConsumerRejected =>
                PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.SinkRejected,
            _ => PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.IntegrationFailed,
        };

    private static PlaybackDecoderFfmpegAwaitedSinkDeliveryResult Map(
        PlaybackDecoderFfmpegManagedFormatConversionResult result) => result switch
        {
            PlaybackDecoderFfmpegManagedFormatConversionResult.Converted =>
                PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered,
            PlaybackDecoderFfmpegManagedFormatConversionResult.ContextUnavailable =>
                PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.ContextUnavailable,
            PlaybackDecoderFfmpegManagedFormatConversionResult.DisposedFrame =>
                PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.DisposedFrame,
            PlaybackDecoderFfmpegManagedFormatConversionResult.UnsupportedFormat =>
                PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.UnsupportedFormat,
            PlaybackDecoderFfmpegManagedFormatConversionResult.InvalidFrame =>
                PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.InvalidFrame,
            PlaybackDecoderFfmpegManagedFormatConversionResult.AllocationFailed =>
                PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.AllocationFailed,
            PlaybackDecoderFfmpegManagedFormatConversionResult.ConversionFailed =>
                PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.ConversionFailed,
            PlaybackDecoderFfmpegManagedFormatConversionResult.ConsumerRejected =>
                PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.SinkRejected,
            _ => PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.IntegrationFailed,
        };

    private sealed class SinkConsumer(IPlaybackDecodedFrameSink sink) :
        IPlaybackDecoderFfmpegDecodedOutputAsyncConsumer
    {
        public async ValueTask<bool> ConsumeAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken)
        {
            try
            {
                await sink.WriteAudioAsync(frame, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new PlaybackDecodedSinkException(exception);
            }
        }

        public async ValueTask<bool> ConsumeVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken)
        {
            try
            {
                await sink.WriteVideoAsync(frame, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new PlaybackDecodedSinkException(exception);
            }
        }
    }

    private sealed class PlaybackDecodedSinkException(Exception innerException) :
        Exception("The decoded sink callback failed.", innerException);
}
