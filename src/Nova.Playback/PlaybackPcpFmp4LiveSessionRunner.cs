using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Playback;

public sealed record PlaybackPcpFmp4LiveSessionOptions
{
    public TimeSpan MaximumRunDuration { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public PcpChannelSessionOptions Session { get; init; } = new();

    public PlaybackPcpFmp4CompositionOptions Composition { get; init; } = new();

    internal bool IsValid =>
        MaximumRunDuration > TimeSpan.Zero &&
        MaximumRunDuration <= TimeSpan.FromMinutes(15) &&
        ConnectTimeout > TimeSpan.Zero &&
        ConnectTimeout <= TimeSpan.FromSeconds(30) &&
        Session is not null && Session.Stream is not null &&
        Composition is not null;
}

public sealed record PlaybackPcpFmp4LiveSessionRunResult(
    PcpChannelSessionResult? Session,
    bool DurationLimitReached,
    bool CallerCancelled,
    PlaybackPcpFmp4CompositionSnapshot Composition,
    PlaybackPcpFmp4CompositionResult? CompletionFailure);

/// <summary>
/// Connects one explicit direct PCP source through the existing network session
/// and the bounded fMP4 playback consumer. A duration stop or connection loss
/// does not synthesize decoder end-of-stream.
/// </summary>
public sealed class PlaybackPcpFmp4LiveSessionRunner : IAsyncDisposable
{
    private readonly PcpChannelSession _session;
    private readonly PlaybackPcpFmp4CompositionBoundary _composition;
    private readonly PlaybackPcpFmp4LiveSource _source;
    private readonly TimeSpan _maximumRunDuration;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _started;
    private int _disposed;

    public PlaybackPcpFmp4LiveSessionRunner(
        PlaybackPcpFmp4LiveSource source,
        IPlaybackDecoderFactory decoderFactory,
        IPlaybackPcpFmp4PresentationOutputGenerationFactory presentationFactory,
        PlaybackPcpFmp4LiveSessionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.ChannelId);
        ArgumentNullException.ThrowIfNull(decoderFactory);
        ArgumentNullException.ThrowIfNull(presentationFactory);

        options ??= new PlaybackPcpFmp4LiveSessionOptions();
        if (!options.IsValid ||
            string.IsNullOrWhiteSpace(source.Host) ||
            source.Port is < 1 or > 65_535 ||
            source.InitialPosition < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        _source = source;
        _maximumRunDuration = options.MaximumRunDuration;
        var tracker = new ChannelStatusTracker(
            source.ChannelId,
            new ChannelSessionId(Guid.NewGuid()));
        _composition = new PlaybackPcpFmp4CompositionBoundary(
            source.ChannelId,
            tracker.Snapshot.SessionId,
            decoderFactory,
            presentationFactory,
            () => tracker.Snapshot.Metadata,
            options.Composition);
        var sessionOptions = options.Session with
        {
            Stream = options.Session.Stream with { },
        };
        sessionOptions.Validate();
        _session = new PcpChannelSession(
            new TcpTransportConnector(new TcpTransportOptions
            {
                ConnectTimeout = options.ConnectTimeout,
            }),
            tracker,
            _composition,
            sessionOptions);
    }

    public async Task<PlaybackPcpFmp4LiveSessionRunResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException(
                "A PCP fMP4 live session runner can only be run once.");
        }

        PcpChannelSessionResult? sessionResult = null;
        var durationLimitReached = false;
        var callerCancelled = false;
        PlaybackPcpFmp4CompositionResult? completionFailure = null;
        try
        {
            var runTask = _session.RunAsync(
                _source.Host,
                _source.Port,
                _source.InitialPosition,
                cancellationToken);
            using var durationSource = new CancellationTokenSource();
            var durationTask = Task.Delay(
                _maximumRunDuration,
                durationSource.Token);
            var completed = await Task.WhenAny(runTask, durationTask)
                .ConfigureAwait(false);
            if (ReferenceEquals(completed, durationTask) && !runTask.IsCompleted)
            {
                durationLimitReached = true;
                _session.RequestStop();
            }
            else
            {
                durationSource.Cancel();
            }

            try
            {
                sessionResult = await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                callerCancelled = true;
            }

            if (sessionResult?.EndReason == PcpChannelSessionEndReason.QuitReceived)
            {
                try
                {
                    await _composition.CompleteInputAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (PlaybackPcpFmp4CompositionException exception)
                {
                    completionFailure = exception.Result;
                }
            }

            return new(
                sessionResult,
                durationLimitReached,
                callerCancelled,
                _composition.Snapshot,
                completionFailure);
        }
        finally
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                await _session.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await _composition.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
