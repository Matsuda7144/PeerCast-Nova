namespace PeerCast.Nova.Core;

/// <summary>
/// Defines the lifecycle contract implemented by independently replaceable Nova modules.
/// Large media payloads must not be transported through this interface.
/// </summary>
public interface INovaModule
{
    string Name { get; }

    ValueTask StartAsync(CancellationToken cancellationToken);

    ValueTask StopAsync(CancellationToken cancellationToken);
}
