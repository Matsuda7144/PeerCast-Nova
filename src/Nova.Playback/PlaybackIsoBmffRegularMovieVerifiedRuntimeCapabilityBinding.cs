namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
{
    Bound,
    MissingPreparation,
    PreparationUnavailable,
    MissingVideoGate,
    UnexpectedVideoGate,
    MissingAudioGate,
    UnexpectedAudioGate,
    DuplicateGate,
    GateUnavailable,
    RequiredSymbolMissing,
    SourceMismatch,
}

public sealed class
    PlaybackIsoBmffRegularMovieVerifiedRuntimeTrackCapabilityBinding
{
    internal
        PlaybackIsoBmffRegularMovieVerifiedRuntimeTrackCapabilityBinding(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role,
            PlaybackDecoderFfmpegCodecId codecId,
            PlaybackIsoBmffRegularTrackDemuxComposition track,
            PlaybackDecoderFfmpegAbiSymbolGateLease gate,
            PlaybackDecoderFfmpegCodecParametersLease parameters,
            IReadOnlyList<string> requiredSymbols)
    {
        Role = role;
        CodecId = codecId;
        Track = track;
        Gate = gate;
        Parameters = parameters;
        RequiredSymbols = requiredSymbols;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole Role
    { get; }

    public PlaybackDecoderFfmpegCodecId CodecId { get; }

    public PlaybackIsoBmffRegularTrackDemuxComposition Track { get; }

    public PlaybackDecoderFfmpegAbiSymbolGateLease Gate { get; }

    public PlaybackDecoderFfmpegCodecParametersLease Parameters { get; }

    public IReadOnlyList<string> RequiredSymbols { get; }

    public bool IsAvailable =>
        !Gate.IsDisposed &&
        !Gate.RuntimeLease.IsDisposed &&
        !Parameters.IsDisposed;
}

public sealed class
    PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinding :
    IDisposable,
    IAsyncDisposable
{
    internal PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinding(
        PlaybackIsoBmffRegularMovieManagedDecoderPreparation source,
        PlaybackIsoBmffRegularMovieVerifiedRuntimeTrackCapabilityBinding?
            video,
        PlaybackIsoBmffRegularMovieVerifiedRuntimeTrackCapabilityBinding?
            audio)
    {
        Source = source;
        Video = video;
        Audio = audio;
    }

    public PlaybackIsoBmffRegularMovieManagedDecoderPreparation Source { get; }

    public PlaybackIsoBmffRegularMovieVerifiedRuntimeTrackCapabilityBinding?
        Video
    { get; }

    public PlaybackIsoBmffRegularMovieVerifiedRuntimeTrackCapabilityBinding?
        Audio
    { get; }

    public bool IsAvailable =>
        (Video is null || Video.IsAvailable) &&
        (Audio is null || Audio.IsAvailable);

    public void Dispose() => Source.Dispose();

    public ValueTask DisposeAsync() => Source.DisposeAsync();
}

public static class
    PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinder
{
    public static
        PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult Bind(
            PlaybackIsoBmffRegularMovieManagedDecoderPreparation? source,
            PlaybackDecoderFfmpegAbiSymbolGateLease? videoGate,
            PlaybackDecoderFfmpegAbiSymbolGateLease? audioGate,
            out PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBinding?
                binding)
    {
        binding = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                    .MissingPreparation;
        }

        var video = source.Video;
        if (video is null && videoGate is not null)
        {
            return
                PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                    .UnexpectedVideoGate;
        }

        if (video is not null && videoGate is null)
        {
            return
                PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                    .MissingVideoGate;
        }

        var audio = source.Audio;
        if (audio is null && audioGate is not null)
        {
            return
                PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                    .UnexpectedAudioGate;
        }

        if (audio is not null && audioGate is null)
        {
            return
                PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                    .MissingAudioGate;
        }

        if (videoGate is not null &&
            ReferenceEquals(videoGate, audioGate))
        {
            return
                PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                    .DuplicateGate;
        }

        PlaybackIsoBmffRegularMovieVerifiedRuntimeTrackCapabilityBinding?
            videoBinding = null;
        if (video is not null)
        {
            var plan = video.CapabilityBinding.Plan;
            var selectedSource = source.Source.Selection.VideoSource;
            if (selectedSource is null)
            {
                return
                    PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                        .SourceMismatch;
            }

            var result = BindTrack(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Video,
                PlaybackDecoderFfmpegCodecId.H264,
                plan.CodecId,
                video.Input.Source,
                selectedSource,
                plan.ManagedParameters.Lease,
                video.CapabilityBinding.SymbolCoverage.CoveredSymbols,
                PlaybackIsoBmffAvcParameterTransferPlan.RequiredSymbols,
                videoGate!,
                out videoBinding);
            if (result !=
                PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                    .Bound)
            {
                return result;
            }
        }

        PlaybackIsoBmffRegularMovieVerifiedRuntimeTrackCapabilityBinding?
            audioBinding = null;
        if (audio is not null)
        {
            var capability = audio.DecoderPreparation.CapabilityBinding;
            var plan = capability.Plan;
            var selectedSource = source.Source.Selection.AudioSource;
            var selectedTrack = FindTrack(source.Source, selectedSource);
            if (selectedSource is null || selectedTrack is null)
            {
                return
                    PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                        .SourceMismatch;
            }

            var result = BindTrack(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole.Audio,
                PlaybackDecoderFfmpegCodecId.Aac,
                plan.CodecId,
                selectedTrack,
                selectedSource,
                plan.ManagedParameters.Lease,
                capability.SymbolCoverage.CoveredSymbols,
                PlaybackIsoBmffAacParameterTransferPlan.RequiredSymbols,
                audioGate!,
                out audioBinding);
            if (result !=
                PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                    .Bound)
            {
                return result;
            }
        }

        binding = new(source, videoBinding, audioBinding);
        return
            PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                .Bound;
    }

    private static
        PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
            BindTrack(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole role,
                PlaybackDecoderFfmpegCodecId expectedCodecId,
                PlaybackDecoderFfmpegCodecId actualCodecId,
                PlaybackIsoBmffRegularTrackDemuxComposition track,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection
                    selectedSource,
                PlaybackDecoderFfmpegCodecParametersLease parameters,
                IReadOnlyList<string> coveredSymbols,
                IReadOnlyList<string> requiredSymbols,
                PlaybackDecoderFfmpegAbiSymbolGateLease gate,
                out
                    PlaybackIsoBmffRegularMovieVerifiedRuntimeTrackCapabilityBinding?
                        binding)
    {
        binding = null;
        if (actualCodecId != expectedCodecId ||
            parameters.CodecId != expectedCodecId ||
            !ReferenceEquals(track.SequenceProjection, selectedSource) ||
            coveredSymbols.Count != requiredSymbols.Count ||
            requiredSymbols.Any(symbol =>
                !coveredSymbols.Contains(symbol, StringComparer.Ordinal)))
        {
            return
                PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                    .SourceMismatch;
        }

        if (parameters.IsDisposed || gate.IsDisposed ||
            gate.RuntimeLease.IsDisposed)
        {
            return
                PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                    .PreparationUnavailable;
        }

        bool hasRequiredSymbols;
        try
        {
            hasRequiredSymbols = gate.UseSymbols(symbols =>
                requiredSymbols.All(symbol =>
                    symbols.TryGetValue(symbol, out var address) &&
                    address != 0));
        }
        catch (ObjectDisposedException)
        {
            return
                PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                    .GateUnavailable;
        }

        if (!hasRequiredSymbols)
        {
            return
                PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                    .RequiredSymbolMissing;
        }

        binding = new(
            role,
            expectedCodecId,
            track,
            gate,
            parameters,
            requiredSymbols);
        return
            PlaybackIsoBmffRegularMovieVerifiedRuntimeCapabilityBindingResult
                .Bound;
    }

    private static PlaybackIsoBmffRegularTrackDemuxComposition? FindTrack(
        PlaybackIsoBmffRegularMovieDemuxComposition movie,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection? selectedSource)
    {
        if (selectedSource is null)
        {
            return null;
        }

        PlaybackIsoBmffRegularTrackDemuxComposition? match = null;
        foreach (var track in movie.Tracks)
        {
            if (!ReferenceEquals(track.SequenceProjection, selectedSource))
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = track;
        }

        return match;
    }

}
