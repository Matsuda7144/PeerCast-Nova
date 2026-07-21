using System.Runtime.CompilerServices;

namespace PeerCast.Nova.Playback;

internal interface IPlaybackProductionDecoderGenerationIngressForwarder
{
    PlaybackSourceBoundOneFrameDecodeSnapshot? DecodeSnapshot { get; }

    PlaybackFiniteOneEventPacketSubmissionResult BeginSubmission(
        PlaybackBoundedDemuxReadCompletion? sourceCompletion,
        CancellationToken cancellationToken);

    PlaybackSourceBoundOneFrameDecodeOperation ReceiveOne(
        PlaybackBoundedDemuxMediaKind mediaKind,
        int streamId,
        PlaybackSourceBoundOneFrameDecodeMode mode,
        CancellationToken cancellationToken,
        PlaybackSourceBoundOneFrameDecodeBudget budget);
}

/// <summary>
/// Internal-only finite forwarding into the singular decode boundary already owned by
/// a production generation. No raw decoder, native context, ABI gate, runtime lease, or
/// Windows resource is returned to the composition. The field accessors are confined to
/// this assembly bridge and every operation revalidates the exact live source generation.
/// </summary>
internal static class PlaybackProductionDecoderGenerationIngressExtensions
{
    extension(PlaybackProductionDecoderGeneration generation)
    {
        internal PlaybackSourceBoundOneFrameDecodeSnapshot? DecodeSnapshot
        {
            get
            {
                ArgumentNullException.ThrowIfNull(generation);
                var root = ReadRoot(generation);
                if (root is IPlaybackProductionDecoderGenerationIngressForwarder forwarding)
                {
                    return forwarding.DecodeSnapshot;
                }

                var decode = ResolveDecodeBoundary(generation, root);
                return decode?.Snapshot;
            }
        }

        internal PlaybackFiniteOneEventPacketSubmissionResult BeginSubmission(
            PlaybackBoundedDemuxReadCompletion? sourceCompletion,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(generation);
            var root = ReadRoot(generation);
            if (root is IPlaybackProductionDecoderGenerationIngressForwarder forwarding)
            {
                return forwarding.BeginSubmission(sourceCompletion, cancellationToken);
            }

            var decode = ResolveDecodeBoundary(generation, root);
            if (decode is null)
            {
                sourceCompletion?.Dispose();
                return PlaybackFiniteOneEventPacketSubmissionResult.Disposed;
            }

            return decode.BeginSubmission(sourceCompletion, cancellationToken);
        }

        internal PlaybackSourceBoundOneFrameDecodeOperation ReceiveOne(
            PlaybackBoundedDemuxMediaKind mediaKind,
            int streamId,
            PlaybackSourceBoundOneFrameDecodeMode mode,
            CancellationToken cancellationToken,
            PlaybackSourceBoundOneFrameDecodeBudget budget)
        {
            ArgumentNullException.ThrowIfNull(generation);
            ArgumentNullException.ThrowIfNull(budget);
            var root = ReadRoot(generation);
            if (root is IPlaybackProductionDecoderGenerationIngressForwarder forwarding)
            {
                return forwarding.ReceiveOne(
                    mediaKind,
                    streamId,
                    mode,
                    cancellationToken,
                    budget);
            }

            var decode = ResolveDecodeBoundary(generation, root);
            return decode is null
                ? new(
                    PlaybackSourceBoundOneFrameDecodeResult.Disposed,
                    null,
                    default)
                : decode.ReceiveOne(
                    generation.SourceSession,
                    generation.DecoderGeneration,
                    mediaKind,
                    streamId,
                    mode,
                    cancellationToken,
                    budget);
        }
    }

    private static IPlaybackProductionDecoderGenerationRoot? ReadRoot(
        PlaybackProductionDecoderGeneration generation)
    {
        ref var root = ref RootField(generation);
        return Volatile.Read(ref root);
    }

    private static PlaybackSourceBoundOneFrameDecodeBoundary? ResolveDecodeBoundary(
        PlaybackProductionDecoderGeneration generation,
        IPlaybackProductionDecoderGenerationRoot? root)
    {
        if (generation.IsDisposed || generation.SourceSession.IsDisposed ||
            root is not PlaybackProductionDecoderGenerationRoot productionRoot ||
            root.IsDisposed || !root.ReferencesSourceSession(generation.SourceSession) ||
            root.DecoderGeneration != generation.DecoderGeneration ||
            root.DiscontinuityGeneration != generation.DiscontinuityGeneration)
        {
            return null;
        }

        ref var deliveryField = ref DeliveryField(productionRoot);
        var delivery = Volatile.Read(ref deliveryField);
        if (delivery is null)
        {
            return null;
        }

        ref var decodeField = ref DecodeBoundaryField(delivery);
        var decode = decodeField;
        if (decode.IsDisposed || !decode.ReferencesSourceSession(generation.SourceSession))
        {
            return null;
        }

        var snapshot = decode.Snapshot;
        return snapshot.DecoderGeneration == generation.DecoderGeneration &&
            snapshot.DiscontinuityGeneration == generation.DiscontinuityGeneration
                ? decode
                : null;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_root")]
    private static extern ref IPlaybackProductionDecoderGenerationRoot? RootField(
        PlaybackProductionDecoderGeneration generation);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_delivery")]
    private static extern ref PlaybackGenerationMatchedSchedulerDeliveryBoundary? DeliveryField(
        PlaybackProductionDecoderGenerationRoot root);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_decodeBoundary")]
    private static extern ref PlaybackSourceBoundOneFrameDecodeBoundary DecodeBoundaryField(
        PlaybackGenerationMatchedSchedulerDeliveryBoundary delivery);
}
