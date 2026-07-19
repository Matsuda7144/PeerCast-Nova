namespace PeerCast.Nova.Playback;

public enum PlaybackDiagnosticNativeParameterTransactionResult
{
    Prepared,
    InvalidConfiguration,
    VideoContextRejected,
    VideoTransferRejected,
    AudioContextRejected,
    AudioTransferRejected,
    Faulted,
}

internal interface IPlaybackDiagnosticNativeContextOwner : IDisposable
{
    PlaybackDecoderFfmpegCodecId CodecId { get; }

    PlaybackDecoderFfmpegContextLease? NativeLease { get; }
}

internal sealed class PlaybackDiagnosticNativeContextOwner(
    PlaybackDecoderFfmpegContextLease lease) : IPlaybackDiagnosticNativeContextOwner
{
    public PlaybackDecoderFfmpegCodecId CodecId => lease.CodecId;

    public PlaybackDecoderFfmpegContextLease NativeLease => lease;

    public void Dispose() => lease.Dispose();
}

internal readonly record struct PlaybackDiagnosticNativeContextCreation(
    PlaybackDecoderFfmpegContextLeaseResult Result,
    IPlaybackDiagnosticNativeContextOwner? Owner);

internal interface IPlaybackDiagnosticNativeParameterOperations
{
    PlaybackDiagnosticNativeContextCreation CreateContext(
        object parent,
        PlaybackDecoderFfmpegCodecId codecId);

    PlaybackDecoderFfmpegParameterTransferResult Transfer(
        IPlaybackDiagnosticNativeContextOwner context,
        PlaybackDecoderFfmpegCodecParametersLease parameters);
}

internal sealed class PlaybackDiagnosticNativeParameterOperations :
    IPlaybackDiagnosticNativeParameterOperations
{
    private readonly PlaybackDecoderFfmpegContextLeaseFactory _contextFactory = new();
    private readonly PlaybackDecoderFfmpegParameterTransfer _parameterTransfer = new();

    public PlaybackDiagnosticNativeContextCreation CreateContext(
        object parent,
        PlaybackDecoderFfmpegCodecId codecId)
    {
        if (parent is not PlaybackDecoderFfmpegAbiSymbolGateLease gateLease)
        {
            return new(PlaybackDecoderFfmpegContextLeaseResult.ParentUnavailable, null);
        }

        var creation = _contextFactory.Create(gateLease, codecId);
        return new(
            creation.Result,
            creation.Lease is null ? null : new PlaybackDiagnosticNativeContextOwner(creation.Lease));
    }

    public PlaybackDecoderFfmpegParameterTransferResult Transfer(
        IPlaybackDiagnosticNativeContextOwner context,
        PlaybackDecoderFfmpegCodecParametersLease parameters)
    {
        var nativeLease = context.NativeLease;
        return nativeLease is null
            ? PlaybackDecoderFfmpegParameterTransferResult.ContextUnavailable
            : _parameterTransfer.Apply(nativeLease, parameters);
    }
}

public sealed class PlaybackDiagnosticNativeDecoderLease : IDisposable, IAsyncDisposable
{
    private IPlaybackDiagnosticNativeContextOwner? _video;
    private IPlaybackDiagnosticNativeContextOwner? _audio;

    internal PlaybackDiagnosticNativeDecoderLease(
        IPlaybackDiagnosticNativeContextOwner video,
        IPlaybackDiagnosticNativeContextOwner audio)
    {
        _video = video;
        _audio = audio;
    }

    public bool IsDisposed => _video is null || _audio is null;

    internal PlaybackDecoderFfmpegContextLease VideoContext =>
        _video?.NativeLease ??
        throw new ObjectDisposedException(nameof(PlaybackDiagnosticNativeDecoderLease));

    internal PlaybackDecoderFfmpegContextLease AudioContext =>
        _audio?.NativeLease ??
        throw new ObjectDisposedException(nameof(PlaybackDiagnosticNativeDecoderLease));

    public void Dispose()
    {
        var audio = Interlocked.Exchange(ref _audio, null);
        var video = Interlocked.Exchange(ref _video, null);
        try
        {
            audio?.Dispose();
        }
        finally
        {
            video?.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed record PlaybackDiagnosticNativeParameterTransactionCompletion(
    PlaybackDiagnosticNativeParameterTransactionResult Result,
    PlaybackDiagnosticNativeDecoderLease? Lease);

/// <summary>
/// Diagnostic-only finite transaction that creates independent H.264 and AAC
/// native contexts and applies the existing owned codec parameters. Each context
/// requires its own gate/runtime ownership chain. Codec open and packet submission
/// are intentionally outside this boundary.
/// </summary>
public sealed class PlaybackDiagnosticNativeParameterTransaction
{
    private readonly IPlaybackDiagnosticNativeParameterOperations _operations;

    public PlaybackDiagnosticNativeParameterTransaction()
        : this(new PlaybackDiagnosticNativeParameterOperations())
    {
    }

    internal PlaybackDiagnosticNativeParameterTransaction(
        IPlaybackDiagnosticNativeParameterOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public PlaybackDiagnosticNativeParameterTransactionCompletion Execute(
        PlaybackDecoderFfmpegAbiSymbolGateLease? videoGateLease,
        PlaybackDecoderFfmpegAbiSymbolGateLease? audioGateLease,
        PlaybackDiagnosticDecoderParameterLease? parameters)
    {
        if (videoGateLease is null || audioGateLease is null || parameters is null ||
            ReferenceEquals(videoGateLease, audioGateLease) || parameters.IsDisposed)
        {
            parameters?.Dispose();
            return new(
                PlaybackDiagnosticNativeParameterTransactionResult.InvalidConfiguration,
                null);
        }

        return ExecuteCore(videoGateLease, audioGateLease, parameters);
    }

    internal PlaybackDiagnosticNativeParameterTransactionCompletion ExecuteCore(
        object? videoParent,
        object? audioParent,
        PlaybackDiagnosticDecoderParameterLease? parameters)
    {
        if (videoParent is null || audioParent is null || parameters is null ||
            ReferenceEquals(videoParent, audioParent) || parameters.IsDisposed)
        {
            parameters?.Dispose();
            return new(
                PlaybackDiagnosticNativeParameterTransactionResult.InvalidConfiguration,
                null);
        }

        IPlaybackDiagnosticNativeContextOwner? video = null;
        IPlaybackDiagnosticNativeContextOwner? audio = null;
        try
        {
            var videoCreation = _operations.CreateContext(
                videoParent,
                PlaybackDecoderFfmpegCodecId.H264);
            if (videoCreation.Result != PlaybackDecoderFfmpegContextLeaseResult.Created ||
                videoCreation.Owner is null)
            {
                parameters.Dispose();
                return new(
                    PlaybackDiagnosticNativeParameterTransactionResult.VideoContextRejected,
                    null);
            }

            video = videoCreation.Owner;
            if (_operations.Transfer(video, parameters.Video) !=
                PlaybackDecoderFfmpegParameterTransferResult.Applied)
            {
                video.Dispose();
                video = null;
                parameters.Dispose();
                return new(
                    PlaybackDiagnosticNativeParameterTransactionResult.VideoTransferRejected,
                    null);
            }

            var audioCreation = _operations.CreateContext(
                audioParent,
                PlaybackDecoderFfmpegCodecId.Aac);
            if (audioCreation.Result != PlaybackDecoderFfmpegContextLeaseResult.Created ||
                audioCreation.Owner is null)
            {
                video.Dispose();
                video = null;
                parameters.Dispose();
                return new(
                    PlaybackDiagnosticNativeParameterTransactionResult.AudioContextRejected,
                    null);
            }

            audio = audioCreation.Owner;
            if (_operations.Transfer(audio, parameters.Audio) !=
                PlaybackDecoderFfmpegParameterTransferResult.Applied)
            {
                audio.Dispose();
                audio = null;
                video.Dispose();
                video = null;
                parameters.Dispose();
                return new(
                    PlaybackDiagnosticNativeParameterTransactionResult.AudioTransferRejected,
                    null);
            }

            parameters.Dispose();
            var lease = new PlaybackDiagnosticNativeDecoderLease(video, audio);
            video = null;
            audio = null;
            return new(PlaybackDiagnosticNativeParameterTransactionResult.Prepared, lease);
        }
        catch
        {
            try
            {
                audio?.Dispose();
            }
            finally
            {
                try
                {
                    video?.Dispose();
                }
                finally
                {
                    parameters.Dispose();
                }
            }

            return new(PlaybackDiagnosticNativeParameterTransactionResult.Faulted, null);
        }
    }
}
