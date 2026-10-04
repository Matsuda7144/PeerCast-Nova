using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.Network.PortMapping;

/// <summary>
/// RFC 6886 TCP mappings. Each operation uses a fresh connected UDP socket,
/// bound to the identified client address; no wildcard/all-ports deletion.
/// </summary>
public sealed class NatPmpMappingBackend : INatMappingBackend
{
    public async Task<NatBackendResult> ExecuteAsync(
        NatMappingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var packet = EncodeRequest(request);
        var sent = false;
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Parse(request.Identity.Network.InternalAddress), 0));
            socket.Connect(new IPEndPoint(IPAddress.Parse(request.Identity.Network.Device.GatewayAddress), 5351));
            var attempts = request.Operation == NatMappingOperation.Unmap ? 3 : 9;
            var buffer = new byte[64];
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // A canceled/failed send can still have put the datagram on the wire.
                sent = true;
                await socket.SendAsync(packet, SocketFlags.None, cancellationToken).ConfigureAwait(false);
                using var interval = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                interval.CancelAfter(TimeSpan.FromMilliseconds(250 * (1 << attempt)));
                try
                {
                    while (true)
                    {
                        // The connected socket admits replies only from the selected
                        // gateway address and port. Ignore unrelated or malformed replies.
                        var length = await socket.ReceiveAsync(buffer, SocketFlags.None, interval.Token)
                            .ConfigureAwait(false);
                        var result = DecodeResponse(request, buffer.AsSpan(0, length));
                        if (result is not null)
                        {
                            return result;
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Continue with the RFC's exponential retransmission interval.
                }
            }

            return new NatBackendResult(NatBackendOutcome.ResultUnknown, DetailCode: "NatPmpResponseTimeout");
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            return new NatBackendResult(sent ? NatBackendOutcome.ResultUnknown : NatBackendOutcome.NotSent,
                DetailCode: exception.GetType().Name);
        }
    }

    internal static byte[] EncodeRequest(NatMappingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Identity.Network.Validate();
        if (request.Identity.Protocol != NatMappingProtocol.Tcp ||
            request.Identity.InternalPort is < 1 or > 65535 ||
            request.SuggestedExternalPort is < 0 or > 65535 ||
            !Enum.IsDefined(request.Operation) ||
            (request.Operation == NatMappingOperation.Unmap
                ? request.RequestedLifetimeSeconds != 0 || request.SuggestedExternalPort != 0
                : request.RequestedLifetimeSeconds == 0))
        {
            throw new ArgumentException("Invalid finite TCP mapping request.", nameof(request));
        }

        var packet = new byte[12];
        packet[1] = 2; // NAT-PMP TCP opcode; version and reserved fields remain zero.
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), (ushort)request.Identity.InternalPort);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(6), (ushort)request.SuggestedExternalPort);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), request.RequestedLifetimeSeconds);
        return packet;
    }

    internal static NatBackendResult? DecodeResponse(NatMappingRequest request, ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 8 || packet[0] != 0 || packet[1] != 130)
        {
            return null;
        }

        var code = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
        var epoch = BinaryPrimitives.ReadUInt32BigEndian(packet[4..]);
        if (code != 0)
        {
            if (packet.Length != 16 ||
                BinaryPrimitives.ReadUInt16BigEndian(packet[8..]) != request.Identity.InternalPort)
            {
                return null;
            }

            return new NatBackendResult(NatBackendOutcome.Rejected, RouterEpochSeconds: epoch,
                DetailCode: "NatPmpResult" + code.ToString(CultureInfo.InvariantCulture));
        }

        if (packet.Length != 16 ||
            BinaryPrimitives.ReadUInt16BigEndian(packet[8..]) != request.Identity.InternalPort)
        {
            return null;
        }

        var externalPort = BinaryPrimitives.ReadUInt16BigEndian(packet[10..]);
        var lifetime = BinaryPrimitives.ReadUInt32BigEndian(packet[12..]);
        if (request.Operation == NatMappingOperation.Unmap
            ? externalPort != 0 || lifetime != 0 : externalPort == 0 || lifetime == 0)
        {
            return null;
        }

        return new NatBackendResult(NatBackendOutcome.Success, externalPort, lifetime, epoch);
    }
}
