namespace PeerCast.Nova.Playback;

public readonly record struct PlaybackIsoBmffAacDecoderCapabilityDeclaration(
    PlaybackDecoderFfmpegCodecId CodecId,
    int MinimumSampleRate,
    int MaximumSampleRate,
    int MaximumChannelCount);

public enum PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult
{
    Bound,
    MissingSymbolCoverage,
    CoverageUnavailable,
    InvalidDeclaration,
    CodecMismatch,
    SampleRateUnsupported,
    ChannelCountUnsupported,
}

public sealed class PlaybackIsoBmffAacDeclaredDecoderCapabilityBinding :
    IDisposable,
    IAsyncDisposable
{
    internal PlaybackIsoBmffAacDeclaredDecoderCapabilityBinding(
        PlaybackIsoBmffAacParameterTransferSymbolCoverage symbolCoverage,
        PlaybackIsoBmffAacDecoderCapabilityDeclaration declaration)
    {
        SymbolCoverage = symbolCoverage;
        Declaration = declaration;
    }

    public PlaybackIsoBmffAacParameterTransferSymbolCoverage SymbolCoverage
    {
        get;
    }

    public PlaybackIsoBmffAacDecoderCapabilityDeclaration Declaration { get; }

    public PlaybackIsoBmffAacParameterTransferPlan Plan => SymbolCoverage.Plan;

    public void Dispose() => SymbolCoverage.Dispose();

    public ValueTask DisposeAsync() => SymbolCoverage.DisposeAsync();
}

public static class PlaybackIsoBmffAacDeclaredDecoderCapabilityBinder
{
    public static PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult Bind(
        PlaybackIsoBmffAacParameterTransferSymbolCoverage? symbolCoverage,
        PlaybackIsoBmffAacDecoderCapabilityDeclaration declaration,
        out PlaybackIsoBmffAacDeclaredDecoderCapabilityBinding? binding)
    {
        binding = null;
        if (symbolCoverage is null)
        {
            return PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult
                .MissingSymbolCoverage;
        }

        var plan = symbolCoverage.Plan;
        if (plan.ManagedParameters.Lease.IsDisposed)
        {
            return PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult
                .CoverageUnavailable;
        }

        if (!IsValidDeclaration(declaration))
        {
            return PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult
                .InvalidDeclaration;
        }

        if (declaration.CodecId != plan.CodecId)
        {
            return PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult
                .CodecMismatch;
        }

        if (plan.SampleRate < declaration.MinimumSampleRate ||
            plan.SampleRate > declaration.MaximumSampleRate)
        {
            return PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult
                .SampleRateUnsupported;
        }

        if (plan.ChannelCount > declaration.MaximumChannelCount)
        {
            return PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult
                .ChannelCountUnsupported;
        }

        binding = new(symbolCoverage, declaration);
        return PlaybackIsoBmffAacDeclaredDecoderCapabilityBindingResult.Bound;
    }

    private static bool IsValidDeclaration(
        PlaybackIsoBmffAacDecoderCapabilityDeclaration declaration) =>
        Enum.IsDefined(declaration.CodecId) &&
        declaration.MinimumSampleRate >=
            PlaybackDecoderFfmpegCodecParametersFactory.MinimumAudioSampleRate &&
        declaration.MaximumSampleRate <=
            PlaybackDecoderFfmpegCodecParametersFactory.MaximumAudioSampleRate &&
        declaration.MinimumSampleRate <= declaration.MaximumSampleRate &&
        declaration.MaximumChannelCount is >=
            PlaybackDecoderFfmpegCodecParametersFactory.MinimumAudioChannelCount and <=
            PlaybackDecoderFfmpegCodecParametersFactory.MaximumAudioChannelCount;
}
