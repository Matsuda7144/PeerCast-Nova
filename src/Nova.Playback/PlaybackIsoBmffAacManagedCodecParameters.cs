namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAacManagedCodecParametersCreationResult
{
    Created,
    MissingInputResolution,
    FactoryRejected,
}

public sealed class PlaybackIsoBmffAacManagedCodecParameters :
    IDisposable,
    IAsyncDisposable
{
    internal PlaybackIsoBmffAacManagedCodecParameters(
        PlaybackIsoBmffAacFfmpegCodecParametersInputResolution inputResolution,
        PlaybackDecoderFfmpegCodecParametersLease lease)
    {
        InputResolution = inputResolution;
        Lease = lease;
    }

    public PlaybackIsoBmffAacFfmpegCodecParametersInputResolution InputResolution
    {
        get;
    }

    public PlaybackDecoderFfmpegCodecParametersLease Lease { get; }

    public void Dispose() => Lease.Dispose();

    public ValueTask DisposeAsync() => Lease.DisposeAsync();
}

public static class PlaybackIsoBmffAacManagedCodecParametersFactory
{
    public static PlaybackIsoBmffAacManagedCodecParametersCreationResult Create(
        PlaybackIsoBmffAacFfmpegCodecParametersInputResolution? inputResolution,
        out PlaybackIsoBmffAacManagedCodecParameters? parameters)
    {
        parameters = null;
        if (inputResolution is null)
        {
            return PlaybackIsoBmffAacManagedCodecParametersCreationResult
                .MissingInputResolution;
        }

        var creation = PlaybackDecoderFfmpegCodecParametersFactory.Create(
            inputResolution.Input);
        if (creation.Result != PlaybackDecoderFfmpegCodecParametersResult.Created ||
            creation.Lease is null)
        {
            creation.Lease?.Dispose();
            return PlaybackIsoBmffAacManagedCodecParametersCreationResult
                .FactoryRejected;
        }

        parameters = new(inputResolution, creation.Lease);
        return PlaybackIsoBmffAacManagedCodecParametersCreationResult.Created;
    }
}
