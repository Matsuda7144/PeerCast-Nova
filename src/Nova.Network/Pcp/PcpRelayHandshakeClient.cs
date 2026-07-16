using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.IO.Pipelines;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Performs relay-entry and HELO/OLEH negotiation on caller-owned transport and
/// PipeReader instances. Accepted results leave the exact same PipeReader open
/// for post-OK CHAN processing, including bytes already buffered after OK.
/// </summary>
internal static class PcpRelayHandshakeClient
{
    public static async ValueTask<PcpRelayHandshakeResult> NegotiateAsync(
        TcpTransportConnection connection,
        PipeReader pipeReader,
        string host,
        int port,
        Guid channelId,
        Guid localSessionId,
        long streamPosition,
        int pcpVersion,
        PcpRelayHandshakeSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(pipeReader);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var request = new PcpRelayRequest
        {
            ChannelId = channelId,
            StreamPosition = streamPosition,
            PcpVersion = pcpVersion,
            UserAgent = settings.UserAgent,
            Host = FormatHostHeader(host, port),
        };

        await connection.WriteAsync(
            request.Encode(),
            cancellationToken).ConfigureAwait(false);

        var response = await PcpHttpHeaderBlockReader.ReadResponseAsync(
            pipeReader,
            settings.MaxHttpHeaderBytes,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is not (200 or 503))
        {
            return new PcpRelayHandshakeResult(
                PcpCompatibilityOutcome.HttpRejected,
                response.StatusCode,
                response.Server,
                response.ContentType,
                RemotePeer: null,
                TerminalAtom: null,
                TerminalCode: null,
                ReceivedAtomNames: ImmutableArray<PcpAtomName>.Empty);
        }

        var helo = PcpHeloCodec.CreateHelo(
            settings.UserAgent,
            localSessionId,
            servantVersion: settings.ServantVersion);
        var encodedHelo = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(encodedHelo, helo);

        await connection.WriteAsync(
            encodedHelo.WrittenMemory,
            cancellationToken).ConfigureAwait(false);

        await using var atomReader = new PcpAtomPipeMessageReader(
            pipeReader,
            new PcpProtocolLimits
            {
                MaxContentLength = settings.MaxHandshakeAtomBytes,
            },
            settings.MaxHandshakeAtomBytes);

        PcpOlehInfo? remotePeer = null;
        var receivedAtomNames =
            ImmutableArray.CreateBuilder<PcpAtomName>();

        for (var index = 0;
            index < settings.MaxAtomsBeforeTerminal;
            index++)
        {
            var atom = await atomReader.ReadNextAsync(
                cancellationToken).ConfigureAwait(false);
            receivedAtomNames.Add(atom.Name);

            if (remotePeer is null)
            {
                if (atom.Name == PcpKnownAtomNames.Oleh)
                {
                    remotePeer = PcpHeloCodec.ParseOleh(atom);
                    continue;
                }

                if (atom.Name == PcpKnownAtomNames.Quit)
                {
                    return CreateQuitResult(
                        response,
                        remotePeer,
                        atom,
                        receivedAtomNames.ToImmutable());
                }

                continue;
            }

            if (atom.Name == PcpKnownAtomNames.Okay)
            {
                return new PcpRelayHandshakeResult(
                    PcpCompatibilityOutcome.Accepted,
                    response.StatusCode,
                    response.Server,
                    response.ContentType,
                    remotePeer,
                    atom.Name,
                    DecodeOptionalUInt32(atom),
                    receivedAtomNames.ToImmutable());
            }

            if (atom.Name == PcpKnownAtomNames.Quit)
            {
                return CreateQuitResult(
                    response,
                    remotePeer,
                    atom,
                    receivedAtomNames.ToImmutable());
            }

            // HOST and other control atoms may precede a terminal Atom.
        }

        throw new InvalidDataException(
            "No terminal OK or QUIT atom was received within the configured limit.");
    }

    private static PcpRelayHandshakeResult CreateQuitResult(
        PcpHttpResponseHeader response,
        PcpOlehInfo? remotePeer,
        PcpAtom atom,
        ImmutableArray<PcpAtomName> receivedAtomNames)
    {
        return new PcpRelayHandshakeResult(
            response.StatusCode == 503
                ? PcpCompatibilityOutcome.RelayFull
                : PcpCompatibilityOutcome.ProtocolRejected,
            response.StatusCode,
            response.Server,
            response.ContentType,
            remotePeer,
            atom.Name,
            DecodeOptionalUInt32(atom),
            receivedAtomNames);
    }

    private static uint? DecodeOptionalUInt32(PcpAtom atom)
    {
        if (atom.IsParent)
        {
            return null;
        }

        if (atom.Content.Length != sizeof(uint))
        {
            throw new InvalidDataException(
                $"Atom '{atom.Name}' must contain four bytes when a code is present.");
        }

        return PcpWireValueCodec.DecodeUInt32(atom.Content.Span);
    }

    private static string FormatHostHeader(string host, int port)
    {
        var formattedHost = host.Contains(':') &&
            !(host.StartsWith('[') && host.EndsWith(']'))
            ? $"[{host}]"
            : host;

        return string.Concat(
            formattedHost,
            ":",
            port.ToString(CultureInfo.InvariantCulture));
    }
}
