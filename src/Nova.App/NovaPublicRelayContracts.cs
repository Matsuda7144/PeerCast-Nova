using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.App;

internal enum NovaPublicRelayBindPolicy { AnyIpv4Ephemeral }
internal enum NovaPublicRelayProtocol { PcpTcp }
internal enum NovaPublicRelayScope { SingleChannelPublic }
internal enum NovaPublicRelayState { Stopped, Starting, Listening, Stopping, Failed, Disposed }
internal enum NovaPublicRelayFailure
{
    None, NotInitialized, Closing, InvalidRequest, IncompatibleIdentity,
    Canceled, ListenFailed, DriverFailed, DriverEnded, CleanupFailed,
}

internal sealed record NovaPublicRelayRequest(
    ChannelId ChannelId, ChannelSessionId SessionId, string Source,
    NovaPublicRelayBindPolicy BindPolicy = NovaPublicRelayBindPolicy.AnyIpv4Ephemeral,
    NovaPublicRelayProtocol Protocol = NovaPublicRelayProtocol.PcpTcp,
    NovaPublicRelayScope Scope = NovaPublicRelayScope.SingleChannelPublic);

// Only the internal driver factory receives this borrowed capability. It does
// not own, initialize, or dispose the runtime. It is never returned to a view.
internal sealed record NovaBorrowedNatManager(Guid OwnerId, NatPortMappingManager Manager);

internal sealed record NovaPublicRelayIdentity(
    Guid RuntimeOwnerId, ChannelId ChannelId, ChannelSessionId SessionId,
    string Host, int Port, NovaPublicRelayBindPolicy BindPolicy,
    NovaPublicRelayProtocol Protocol, NovaPublicRelayScope Scope);

internal sealed record NovaPublicRelayEndpoint(string Address, int Port);
internal sealed record NovaPublicRelayEnd(bool CleanupConfirmed);
internal sealed record NovaPublicRelayStartResult(NovaPublicRelayEndpoint? Endpoint, NovaPublicRelayFailure Failure);
internal sealed record NovaPublicRelayAcquireResult(NovaPublicRelayLease? Lease, NovaPublicRelayFailure Failure);
internal sealed record NovaPublicRelaySnapshot(
    Guid RuntimeOwnerId, long Epoch, long Revision, NovaPublicRelayState State, int ReferenceCount,
    ChannelId? ChannelId, ChannelSessionId? SessionId,
    NovaPublicRelayEndpoint? Endpoint, NovaPublicRelayFailure Failure);

internal interface INovaPublicRelayDriver : IAsyncDisposable
{
    Task<NovaPublicRelayStartResult> StartAsync(NovaPublicRelayIdentity identity, CancellationToken cancellationToken);
    Task<NovaPublicRelayEnd> Completion { get; }
    Task<NovaPublicRelayFailure> StopAsync();
}

/// <summary>One logical reference. Repeated release joins actual cleanup.</summary>
internal sealed class NovaPublicRelayLease(Func<Task<NovaPublicRelayFailure>> release) : IAsyncDisposable
{
    private readonly object gate = new();
    private Task<NovaPublicRelayFailure>? completion;
    public Task<NovaPublicRelayFailure> ReleaseAsync()
    {
        lock (gate) { return completion ??= release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (await ReleaseAsync().ConfigureAwait(false) == NovaPublicRelayFailure.CleanupFailed)
            throw new InvalidOperationException("Public relay cleanup was not confirmed.");
    }
}
