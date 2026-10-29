using System.Diagnostics.CodeAnalysis;
using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Authoritative channel-owner binding for validated downstream PCP requests.
/// The reservation does not own transport, readers, writers, or media consumers.
/// Its caller must retain it until transport and child-worker cleanup finish.
/// </summary>
public interface IPcpDownstreamRelayRegistration : IPcpDownstreamRelayAdmission, IDisposable
{
    ChannelSessionId SessionId { get; }
    int ReservationCount { get; }
}

public interface IPcpDownstreamRelayAdmission
{
    ChannelId ChannelId { get; }

    bool TryReserve(long connectionId,
        [NotNullWhen(true)] out IDisposable? reservation,
        CancellationToken cancellationToken = default);
}
