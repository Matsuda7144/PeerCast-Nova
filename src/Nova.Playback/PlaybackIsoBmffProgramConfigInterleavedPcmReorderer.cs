namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffProgramConfigInterleavedPcmReorderResult
{
    Reordered,
    InvalidConfiguration,
    MissingPlan,
    MissingFrame,
    ChannelCountMismatch,
    PayloadLimitExceeded,
}

public sealed record PlaybackIsoBmffProgramConfigInterleavedPcmReorderOptions
{
    public long MaximumPayloadBytes { get; init; } =
        PlaybackSourceBoundOneFrameDecodeOptions.DefaultMaximumManagedFrameBytes;
}

public sealed class PlaybackIsoBmffProgramConfigReorderedAudioFrame
{
    internal PlaybackIsoBmffProgramConfigReorderedAudioFrame(
        PlaybackIsoBmffProgramConfigChannelReorderPlan plan,
        PlaybackDecodedAudioFrame sourceFrame,
        PlaybackDecodedAudioFrame frame)
    {
        Plan = plan;
        SourceFrame = sourceFrame;
        Frame = frame;
    }

    public PlaybackIsoBmffProgramConfigChannelReorderPlan Plan { get; }

    public PlaybackDecodedAudioFrame SourceFrame { get; }

    public PlaybackDecodedAudioFrame Frame { get; }
}

public static class PlaybackIsoBmffProgramConfigInterleavedPcmReorderer
{
    public static PlaybackIsoBmffProgramConfigInterleavedPcmReorderResult Reorder(
        PlaybackIsoBmffProgramConfigChannelReorderPlan? plan,
        PlaybackDecodedAudioFrame? sourceFrame,
        out PlaybackIsoBmffProgramConfigReorderedAudioFrame? reorderedFrame,
        PlaybackIsoBmffProgramConfigInterleavedPcmReorderOptions? options = null)
    {
        reorderedFrame = null;
        options ??= new();
        if (options.MaximumPayloadBytes is < 1 or >
            PlaybackSourceBoundOneFrameDecodeOptions
                .MaximumManagedFrameBytesHardLimit)
        {
            return PlaybackIsoBmffProgramConfigInterleavedPcmReorderResult
                .InvalidConfiguration;
        }

        if (plan is null)
        {
            return PlaybackIsoBmffProgramConfigInterleavedPcmReorderResult
                .MissingPlan;
        }

        if (sourceFrame is null)
        {
            return PlaybackIsoBmffProgramConfigInterleavedPcmReorderResult
                .MissingFrame;
        }

        var source = sourceFrame.Value;
        if (source.ChannelCount != plan.Channels.Count)
        {
            return PlaybackIsoBmffProgramConfigInterleavedPcmReorderResult
                .ChannelCountMismatch;
        }

        if (source.Payload.Length > options.MaximumPayloadBytes)
        {
            return PlaybackIsoBmffProgramConfigInterleavedPcmReorderResult
                .PayloadLimitExceeded;
        }

        var bytesPerSample = source.SampleFormat switch
        {
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved => 2,
            PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved => 4,
            _ => throw new InvalidOperationException(
                "The validated decoded frame has an undefined sample format."),
        };
        var bytes = new byte[source.Payload.Length];
        var input = source.Payload.Span;
        for (var sampleIndex = 0;
            sampleIndex < source.SampleCount;
            sampleIndex++)
        {
            var frameOffset = sampleIndex * source.ChannelCount * bytesPerSample;
            foreach (var channel in plan.Channels)
            {
                var sourceOffset = frameOffset +
                    (channel.SourceChannelIndex * bytesPerSample);
                var destinationOffset = frameOffset +
                    (channel.DestinationChannelIndex * bytesPerSample);
                input.Slice(sourceOffset, bytesPerSample).CopyTo(
                    bytes.AsSpan(destinationOffset, bytesPerSample));
            }
        }

        var reordered = PlaybackDecodedAudioFrame.Create(
            source.Sequence,
            source.Timestamp,
            source.Duration,
            source.Discontinuity,
            source.SampleFormat,
            source.SampleRate,
            source.ChannelCount,
            source.SampleCount,
            bytes);
        reorderedFrame = new(plan, source, reordered);
        return PlaybackIsoBmffProgramConfigInterleavedPcmReorderResult.Reordered;
    }
}
