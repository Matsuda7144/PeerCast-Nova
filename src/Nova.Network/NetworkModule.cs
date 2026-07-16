using PeerCast.Nova.Core;
using PeerCast.Nova.Foundation.Logging;

namespace PeerCast.Nova.Network;

/// <summary>
/// Owns network session lifecycle. Live channel sessions will compose the
/// verified TCP transport and PCP codecs behind this module boundary.
/// </summary>
public sealed class NetworkModule : INovaModule
{
    private readonly INovaLogger _logger;

    public NetworkModule(INovaLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public string Name => "Network";

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.Log(LogLevel.Information, "Network module initialized without opening sockets.");
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.Log(LogLevel.Information, "Network module stopped.");
        return ValueTask.CompletedTask;
    }
}
