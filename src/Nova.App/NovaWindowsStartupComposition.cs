using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App;

public enum NovaWindowsStartupResult
{
    None,
    Completed,
    PlatformUnsupported,
    StartupRejected,
    Cancelled,
    Faulted,
}

public sealed record NovaWindowsStartupOptions
{
    public PlaybackWindowsPhysicalRunnerOptions Host { get; init; } = new()
    {
        Title = "PeerCast Nova",
    };
}

/// <summary>
/// User-facing application startup boundary. It creates exactly one shared
/// Nova.Windows host, waits for close or cancellation, then stops and disposes
/// that host. It does not compose controlled commands or playback resources.
/// </summary>
public sealed class NovaWindowsStartupComposition
{
    private readonly NovaWindowsStartupOptions _options;
    private readonly INovaWindowsStartupHostFactory _hostFactory;

    public NovaWindowsStartupComposition(NovaWindowsStartupOptions? options = null)
        : this(
            options ?? new(),
            SystemNovaWindowsStartupHostFactory.Instance)
    {
    }

    internal NovaWindowsStartupComposition(
        NovaWindowsStartupOptions options,
        INovaWindowsStartupHostFactory hostFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(hostFactory);
        ArgumentNullException.ThrowIfNull(options.Host);
        _options = options with { Host = options.Host with { } };
        _hostFactory = hostFactory;
    }

    public async ValueTask<NovaWindowsStartupResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        await using var host = _hostFactory.Create(_options.Host);
        try
        {
            var start = await host.StartAsync(cancellationToken).ConfigureAwait(false);
            if (start != PlaybackWindowsPhysicalRunnerResult.Started)
            {
                return MapStartResult(start);
            }

            var exit = await host.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return exit switch
            {
                PlaybackWindowsPhysicalRunnerResult.Stopped =>
                    NovaWindowsStartupResult.Completed,
                PlaybackWindowsPhysicalRunnerResult.Cancelled =>
                    NovaWindowsStartupResult.Cancelled,
                _ => NovaWindowsStartupResult.Faulted,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return NovaWindowsStartupResult.Cancelled;
        }
        catch
        {
            return NovaWindowsStartupResult.Faulted;
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static NovaWindowsStartupResult MapStartResult(
        PlaybackWindowsPhysicalRunnerResult result) => result switch
        {
            PlaybackWindowsPhysicalRunnerResult.PlatformUnsupported =>
                NovaWindowsStartupResult.PlatformUnsupported,
            PlaybackWindowsPhysicalRunnerResult.Cancelled =>
                NovaWindowsStartupResult.Cancelled,
            PlaybackWindowsPhysicalRunnerResult.Faulted =>
                NovaWindowsStartupResult.Faulted,
            _ => NovaWindowsStartupResult.StartupRejected,
        };
}

internal interface INovaWindowsStartupHostFactory
{
    INovaWindowsStartupHost Create(PlaybackWindowsPhysicalRunnerOptions options);
}

internal interface INovaWindowsStartupHost : IAsyncDisposable
{
    ValueTask<PlaybackWindowsPhysicalRunnerResult> StartAsync(
        CancellationToken cancellationToken);

    ValueTask<PlaybackWindowsPhysicalRunnerResult> WaitForExitAsync(
        CancellationToken cancellationToken);

    ValueTask<PlaybackWindowsPhysicalRunnerResult> StopAsync(
        CancellationToken cancellationToken);
}

internal sealed class SystemNovaWindowsStartupHostFactory :
    INovaWindowsStartupHostFactory
{
    public static SystemNovaWindowsStartupHostFactory Instance { get; } = new();

    private SystemNovaWindowsStartupHostFactory()
    {
    }

    public INovaWindowsStartupHost Create(PlaybackWindowsPhysicalRunnerOptions options) =>
        new SystemNovaWindowsStartupHost(new(options));
}

internal sealed class SystemNovaWindowsStartupHost : INovaWindowsStartupHost
{
    private readonly PlaybackWindowsPhysicalRunnerHost _host;

    public SystemNovaWindowsStartupHost(PlaybackWindowsPhysicalRunnerHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    public ValueTask<PlaybackWindowsPhysicalRunnerResult> StartAsync(
        CancellationToken cancellationToken) => _host.StartAsync(cancellationToken);

    public ValueTask<PlaybackWindowsPhysicalRunnerResult> WaitForExitAsync(
        CancellationToken cancellationToken) => _host.WaitForExitAsync(cancellationToken);

    public ValueTask<PlaybackWindowsPhysicalRunnerResult> StopAsync(
        CancellationToken cancellationToken) => _host.StopAsync(cancellationToken);

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}
