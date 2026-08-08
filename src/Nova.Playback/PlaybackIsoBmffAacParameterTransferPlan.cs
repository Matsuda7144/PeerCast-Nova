namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAacParameterTransferPlanningResult
{
    Planned,
    MissingManagedParameters,
    ParametersUnavailable,
    InconsistentParameters,
}

public sealed class PlaybackIsoBmffAacParameterTransferPlan :
    IDisposable,
    IAsyncDisposable
{
    private static readonly IReadOnlyList<string> RequiredSymbolsValue =
        Array.AsReadOnly(
        [
            "avcodec_parameters_alloc",
            "avcodec_parameters_free",
            "av_mallocz",
            "av_channel_layout_default",
            "avcodec_parameters_to_context",
        ]);

    internal PlaybackIsoBmffAacParameterTransferPlan(
        PlaybackIsoBmffAacManagedCodecParameters managedParameters)
    {
        ManagedParameters = managedParameters;
        CodecId = managedParameters.Lease.CodecId;
        SampleRate = managedParameters.Lease.SampleRate;
        ChannelCount = managedParameters.Lease.ChannelCount;
        ExtradataLength = managedParameters.Lease.ExtradataLength;
    }

    public PlaybackIsoBmffAacManagedCodecParameters ManagedParameters { get; }

    public PlaybackDecoderFfmpegCodecId CodecId { get; }

    public int SampleRate { get; }

    public int ChannelCount { get; }

    public int ExtradataLength { get; }

    public static IReadOnlyList<string> RequiredSymbols => RequiredSymbolsValue;

    public void Dispose() => ManagedParameters.Dispose();

    public ValueTask DisposeAsync() => ManagedParameters.DisposeAsync();
}

public static class PlaybackIsoBmffAacParameterTransferPlanner
{
    public static PlaybackIsoBmffAacParameterTransferPlanningResult Plan(
        PlaybackIsoBmffAacManagedCodecParameters? managedParameters,
        out PlaybackIsoBmffAacParameterTransferPlan? plan)
    {
        plan = null;
        if (managedParameters is null)
        {
            return PlaybackIsoBmffAacParameterTransferPlanningResult
                .MissingManagedParameters;
        }

        var lease = managedParameters.Lease;
        if (lease.IsDisposed)
        {
            return PlaybackIsoBmffAacParameterTransferPlanningResult
                .ParametersUnavailable;
        }

        var input = managedParameters.InputResolution.Input;
        if (lease.CodecId != PlaybackDecoderFfmpegCodecId.Aac ||
            input.CodecId != lease.CodecId ||
            input.SampleRate != lease.SampleRate ||
            input.ChannelCount != lease.ChannelCount ||
            input.Extradata.Length != lease.ExtradataLength)
        {
            return PlaybackIsoBmffAacParameterTransferPlanningResult
                .InconsistentParameters;
        }

        plan = new(managedParameters);
        return PlaybackIsoBmffAacParameterTransferPlanningResult.Planned;
    }
}
