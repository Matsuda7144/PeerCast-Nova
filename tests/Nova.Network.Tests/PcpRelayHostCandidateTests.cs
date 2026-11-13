using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Reflection;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpRelayHostCandidateTests
{
    private static readonly PcpAtomName Ip = PcpAtomName.FromAscii("ip");
    private static readonly PcpAtomName Unknown = PcpAtomName.FromAscii("what");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Parser_ParsesOneValidEndpoint()
    {
        var candidate = PcpRelayHostCandidateParser.TryParse(
            CreateHost(("192.0.2.1", 7144)));

        Assert.NotNull(candidate!.GlobalEndpoint);
        var endpoint = candidate.GlobalEndpoint!;
        Assert.Equal(IPAddress.Parse("192.0.2.1"), endpoint.Address);
        Assert.Equal(7144, endpoint.Port);
    }

    [Fact]
    public void Parser_PreservesTwoEndpointWireOrder()
    {
        var candidate = PcpRelayHostCandidateParser.TryParse(
            CreateHost(("192.0.2.1", 7144), ("198.51.100.2", 7145)));

        Assert.Equal(
            new PcpRelayHostEndpoint(IPAddress.Parse("192.0.2.1"), 7144),
            candidate!.GlobalEndpoint);
        Assert.Equal(
            new PcpRelayHostEndpoint(IPAddress.Parse("198.51.100.2"), 7145),
            candidate.LocalEndpoint);
    }

    [Fact]
    public void Parser_MalformedFirstPortDoesNotShiftSecondPair()
    {
        var candidate = PcpRelayHostCandidateParser.TryParse(
            CreateHostWithChildren(
                CreateIp("192.0.2.1"),
                CreateMalformedPort(),
                CreateIp("198.51.100.2"),
                CreatePort(7145)));

        Assert.NotNull(candidate!.LocalEndpoint);
        var endpoint = candidate.LocalEndpoint!;
        Assert.Equal(IPAddress.Parse("198.51.100.2"), endpoint.Address);
        Assert.Equal(7145, endpoint.Port);
    }

    [Fact]
    public void Parser_MalformedFirstIpDoesNotShiftSecondPair()
    {
        var candidate = PcpRelayHostCandidateParser.TryParse(
            CreateHostWithChildren(
                CreateMalformedIp(),
                CreatePort(7144),
                CreateIp("198.51.100.2"),
                CreatePort(7145)));

        Assert.NotNull(candidate!.LocalEndpoint);
        var endpoint = candidate.LocalEndpoint!;
        Assert.Equal(IPAddress.Parse("198.51.100.2"), endpoint.Address);
        Assert.Equal(7145, endpoint.Port);
    }

    [Fact]
    public void Parser_ZeroFirstPortDoesNotShiftSecondPair()
    {
        var candidate = PcpRelayHostCandidateParser.TryParse(
            CreateHostWithChildren(
                CreateIp("192.0.2.1"),
                CreatePort(0),
                CreateIp("198.51.100.2"),
                CreatePort(7145)));

        Assert.NotNull(candidate!.LocalEndpoint);
        var endpoint = candidate.LocalEndpoint!;
        Assert.Equal(IPAddress.Parse("198.51.100.2"), endpoint.Address);
        Assert.Equal(7145, endpoint.Port);
    }

    [Fact]
    public void Parser_MalformedFirstIpAndPortDoNotShiftSecondPair()
    {
        var candidate = PcpRelayHostCandidateParser.TryParse(
            CreateHostWithChildren(
                CreateMalformedIp(),
                CreateMalformedPort(),
                CreateIp("198.51.100.2"),
                CreatePort(7145)));

        Assert.NotNull(candidate!.LocalEndpoint);
        var endpoint = candidate.LocalEndpoint!;
        Assert.Equal(IPAddress.Parse("198.51.100.2"), endpoint.Address);
        Assert.Equal(7145, endpoint.Port);
    }

    [Fact]
    public void Parser_MalformedSecondIpKeepsFirstPairOnly()
    {
        var candidate = PcpRelayHostCandidateParser.TryParse(
            CreateHostWithChildren(
                CreateIp("192.0.2.1"),
                CreatePort(7144),
                CreateMalformedIp(),
                CreatePort(7145)));

        Assert.NotNull(candidate!.GlobalEndpoint);
        var endpoint = candidate.GlobalEndpoint!;
        Assert.Equal(IPAddress.Parse("192.0.2.1"), endpoint.Address);
        Assert.Equal(7144, endpoint.Port);
    }

    [Fact]
    public void Parser_MalformedSecondPortKeepsFirstPairOnly()
    {
        var candidate = PcpRelayHostCandidateParser.TryParse(
            CreateHostWithChildren(
                CreateIp("192.0.2.1"),
                CreatePort(7144),
                CreateIp("198.51.100.2"),
                CreateMalformedPort()));

        Assert.NotNull(candidate!.GlobalEndpoint);
        var endpoint = candidate.GlobalEndpoint!;
        Assert.Equal(IPAddress.Parse("192.0.2.1"), endpoint.Address);
        Assert.Equal(7144, endpoint.Port);
    }

    [Fact]
    public void Parser_UnknownChildrenDoNotAdvanceFieldOrdinals()
    {
        var candidate = PcpRelayHostCandidateParser.TryParse(
            CreateHostWithChildren(
                CreateIp("192.0.2.1"),
                PcpAtom.Leaf(Unknown, [0xFE]),
                CreatePort(7144),
                CreateIp("198.51.100.2"),
                CreatePort(7145)));

        Assert.Equal(
            new PcpRelayHostEndpoint(IPAddress.Parse("192.0.2.1"), 7144),
            candidate!.GlobalEndpoint);
        Assert.Equal(
            new PcpRelayHostEndpoint(IPAddress.Parse("198.51.100.2"), 7145),
            candidate.LocalEndpoint);
    }

    [Fact]
    public void Parser_NestedKnownFieldCountsAsMalformedOccurrence()
    {
        var candidate = PcpRelayHostCandidateParser.TryParse(
            CreateHostWithChildren(
                PcpAtom.Parent(Ip),
                CreateIp("198.51.100.2"),
                CreatePort(7144),
                CreatePort(7145)));

        Assert.NotNull(candidate!.LocalEndpoint);
        var endpoint = candidate.LocalEndpoint!;
        Assert.Equal(IPAddress.Parse("198.51.100.2"), endpoint.Address);
        Assert.Equal(7145, endpoint.Port);
    }

    [Fact]
    public void Parser_NestedKnownPortCountsAsMalformedOccurrence()
    {
        var candidate = PcpRelayHostCandidateParser.TryParse(
            CreateHostWithChildren(
                CreateIp("192.0.2.1"),
                PcpAtom.Parent(PcpKnownAtomNames.HeloPort),
                CreateIp("198.51.100.2"),
                CreatePort(7145)));

        Assert.NotNull(candidate!.LocalEndpoint);
        var endpoint = candidate.LocalEndpoint!;
        Assert.Equal(IPAddress.Parse("198.51.100.2"), endpoint.Address);
        Assert.Equal(7145, endpoint.Port);
        Assert.Null(candidate.GlobalEndpoint);
    }

    [Fact]
    public async Task Handshake_PreservesHostOrderAndRawCount()
    {
        var result = await NegotiateAsync(
            [
                CreateHost(("192.0.2.1", 7144)),
                CreateHost(
                    ("198.51.100.2", 7145),
                    ("198.51.100.22", 7147)),
                CreateHost(("203.0.113.3", 7146)),
            ]);

        Assert.Equal(PcpCompatibilityOutcome.RelayFull, result.Outcome);
        Assert.Equal(1003u, result.TerminalCode);
        Assert.Equal(3, result.RelayHostCandidates.Length);
        Assert.Equal(
            IPAddress.Parse("192.0.2.1"),
            result.RelayHostCandidates[0].GlobalEndpoint!.Address);
        Assert.Equal(
            IPAddress.Parse("198.51.100.2"),
            result.RelayHostCandidates[1].GlobalEndpoint!.Address);
        Assert.Equal(
            IPAddress.Parse("198.51.100.22"),
            result.RelayHostCandidates[1].LocalEndpoint!.Address);
        Assert.Equal(
            IPAddress.Parse("203.0.113.3"),
            result.RelayHostCandidates[2].GlobalEndpoint!.Address);
    }

    [Fact]
    public async Task Handshake_SixHostsPreservesSixCandidatesAndRawCount()
    {
        var result = await NegotiateAsync(
            Enumerable.Range(0, 6)
                .Select(index => CreateHost(("192.0.2." + (index + 1), 7144 + index)))
                .ToArray());

        Assert.Equal(PcpCompatibilityOutcome.RelayFull, result.Outcome);
        Assert.Equal(1003u, result.TerminalCode);
        Assert.Equal(6, result.RelayHostCandidates.Length);
    }

    [Fact]
    public async Task Handshake_HostLeafKeepsRelayFullAndRawCount()
    {
        var result = await NegotiateAsync(
            [PcpAtom.Leaf(PcpKnownAtomNames.Host, [0x01, 0x02])]);

        Assert.Equal(PcpCompatibilityOutcome.RelayFull, result.Outcome);
        Assert.Equal(1003u, result.TerminalCode);
        Assert.Empty(result.RelayHostCandidates);
    }

    [Fact]
    public async Task Handshake_HostMissingPortKeepsRelayFull()
    {
        var host = PcpAtom.Parent(
            PcpKnownAtomNames.Host,
            PcpAtom.Leaf(Ip, PcpWireValueCodec.EncodeLegacyIpv4(
                IPAddress.Parse("192.0.2.1"))));
        var result = await NegotiateAsync([host]);

        Assert.Equal(PcpCompatibilityOutcome.RelayFull, result.Outcome);
        Assert.Empty(result.RelayHostCandidates);
    }

    [Fact]
    public async Task Handshake_MalformedIpLengthKeepsRelayFull()
    {
        var host = PcpAtom.Parent(
            PcpKnownAtomNames.Host,
            PcpAtom.Leaf(Ip, [0x01, 0x02, 0x03]),
            PcpAtom.Leaf(PcpKnownAtomNames.HeloPort,
                PcpWireValueCodec.EncodeUInt16(7144)));
        var result = await NegotiateAsync([host]);

        Assert.Equal(PcpCompatibilityOutcome.RelayFull, result.Outcome);
        Assert.Empty(result.RelayHostCandidates);
    }

    [Fact]
    public async Task Handshake_MalformedPortLengthKeepsRelayFull()
    {
        var host = PcpAtom.Parent(
            PcpKnownAtomNames.Host,
            PcpAtom.Leaf(Ip, PcpWireValueCodec.EncodeLegacyIpv4(
                IPAddress.Parse("192.0.2.1"))),
            PcpAtom.Leaf(PcpKnownAtomNames.HeloPort, [0x01]));
        var result = await NegotiateAsync([host]);

        Assert.Equal(PcpCompatibilityOutcome.RelayFull, result.Outcome);
        Assert.Empty(result.RelayHostCandidates);
    }

    [Fact]
    public async Task Handshake_InvalidPortKeepsRelayFull()
    {
        var host = PcpAtom.Parent(
            PcpKnownAtomNames.Host,
            PcpAtom.Leaf(Ip, PcpWireValueCodec.EncodeLegacyIpv4(
                IPAddress.Parse("192.0.2.1"))),
            PcpAtom.Leaf(PcpKnownAtomNames.HeloPort,
                PcpWireValueCodec.EncodeUInt16(0)));
        var result = await NegotiateAsync([host]);

        Assert.Equal(PcpCompatibilityOutcome.RelayFull, result.Outcome);
        Assert.Empty(result.RelayHostCandidates);
    }

    [Fact]
    public async Task Handshake_UnknownChildrenAreIgnored()
    {
        var host = PcpAtom.Parent(
            PcpKnownAtomNames.Host,
            PcpAtom.Parent(Unknown),
            PcpAtom.Leaf(Unknown, [0xFE]),
            PcpAtom.Leaf(Ip, PcpWireValueCodec.EncodeLegacyIpv4(
                IPAddress.Parse("192.0.2.1"))),
            PcpAtom.Leaf(PcpKnownAtomNames.HeloPort,
                PcpWireValueCodec.EncodeUInt16(7144)));
        var result = await NegotiateAsync([host]);

        Assert.Equal(PcpCompatibilityOutcome.RelayFull, result.Outcome);
        var candidate = Assert.Single(result.RelayHostCandidates);
        Assert.NotNull(candidate.GlobalEndpoint);
        var endpoint = candidate.GlobalEndpoint!;
        Assert.Equal(IPAddress.Parse("192.0.2.1"), endpoint.Address);
    }

    [Fact]
    public void Parser_UnequalIpAndPortCountsKeepOnlyMatchedPairs()
    {
        var candidate = PcpRelayHostCandidateParser.TryParse(
            PcpAtom.Parent(
                PcpKnownAtomNames.Host,
                PcpAtom.Leaf(Ip, PcpWireValueCodec.EncodeLegacyIpv4(
                    IPAddress.Parse("192.0.2.1"))),
                PcpAtom.Leaf(PcpKnownAtomNames.HeloPort,
                    PcpWireValueCodec.EncodeUInt16(7144)),
                PcpAtom.Leaf(Ip, PcpWireValueCodec.EncodeLegacyIpv4(
                    IPAddress.Parse("198.51.100.2")))));

        Assert.NotNull(candidate!.GlobalEndpoint);
        var endpoint = candidate.GlobalEndpoint!;
        Assert.Equal(IPAddress.Parse("192.0.2.1"), endpoint.Address);
        Assert.Equal(7144, endpoint.Port);
    }

    [Fact]
    public void Parser_BoundsEndpointsPerHost()
    {
        var candidate = PcpRelayHostCandidateParser.TryParse(
            CreateHost(
                ("192.0.2.1", 7144),
                ("192.0.2.2", 7145),
                ("192.0.2.3", 7146)));

        Assert.NotNull(candidate!.GlobalEndpoint);
        Assert.NotNull(candidate.LocalEndpoint);
        Assert.Equal(
            IPAddress.Parse("192.0.2.2"),
            candidate.LocalEndpoint!.Address);
    }

    [Fact]
    public async Task Handshake_BoundsCandidatesButKeepsRawHostCount()
    {
        var result = await NegotiateAsync(
            Enumerable.Range(0, 12)
                .Select(index => CreateHost(("192.0.2." + ((index % 8) + 1), 7144)))
                .ToArray());

        Assert.Equal(PcpCompatibilityOutcome.RelayFull, result.Outcome);
        Assert.Equal(
            PcpRelayHostCandidateParser.MaximumCandidates,
            result.RelayHostCandidates.Length);
    }

    [Fact]
    public void Parser_PreservesDuplicateEndpoints()
    {
        var candidate = PcpRelayHostCandidateParser.TryParse(
            CreateHost(("192.0.2.1", 7144), ("192.0.2.1", 7144)));

        Assert.Equal(candidate!.GlobalEndpoint, candidate.LocalEndpoint);
        Assert.NotNull(candidate.GlobalEndpoint);
        Assert.NotNull(candidate.LocalEndpoint);
    }

    [Theory]
    [InlineData(0U, PcpCompatibilityOutcome.RelayFull)]
    [InlineData(1003U, PcpCompatibilityOutcome.RelayFull)]
    [InlineData(1004U, PcpCompatibilityOutcome.ProtocolRejected)]
    [InlineData(1005U, PcpCompatibilityOutcome.ProtocolRejected)]
    [InlineData(1007U, PcpCompatibilityOutcome.ProtocolRejected)]
    public async Task Handshake_QuitReasonTakesPrecedenceWhileKeepingHostCandidates(
        uint code, PcpCompatibilityOutcome outcome)
    {
        var result = await NegotiateAsync(
            [CreateHost(("192.0.2.1", 7144))],
            terminalCode: code);

        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(code, result.TerminalCode);
        var candidate = Assert.Single(result.RelayHostCandidates);
        Assert.NotNull(candidate.GlobalEndpoint);
        Assert.Null(candidate.LocalEndpoint);
    }

    [Fact]
    public async Task Handshake_Http200OkRemainsAccepted()
    {
        var result = await NegotiateAsync(
            [PcpAtom.Leaf(
                PcpKnownAtomNames.Okay,
                PcpWireValueCodec.EncodeUInt32(1))],
            statusCode: 200,
            terminalCode: 1,
            sendQuit: false);

        Assert.Equal(PcpCompatibilityOutcome.Accepted, result.Outcome);
        Assert.Equal(200, result.HttpStatusCode);
        Assert.Equal(PcpKnownAtomNames.Okay, result.TerminalAtom);
        Assert.Equal(1u, result.TerminalCode);
        Assert.Empty(result.RelayHostCandidates);
    }

    [Fact]
    public void PublicNegotiationResultDoesNotExposeEndpointData()
    {
        var properties = typeof(PcpChannelSessionNegotiationResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.DoesNotContain(
            properties,
            property => property.Name.Contains("Address", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Port", StringComparison.OrdinalIgnoreCase) ||
                property.PropertyType == typeof(IPAddress));
    }

    private static PcpAtom CreateHost(params (string Address, int Port)[] endpoints)
    {
        var children = new List<PcpAtom>();

        foreach (var (addressText, port) in endpoints)
        {
            children.Add(PcpAtom.Leaf(
                Ip,
                PcpWireValueCodec.EncodeLegacyIpv4(
                    IPAddress.Parse(addressText))));
            children.Add(PcpAtom.Leaf(
                PcpKnownAtomNames.HeloPort,
                PcpWireValueCodec.EncodeUInt16((ushort)port)));
        }

        return PcpAtom.Parent(PcpKnownAtomNames.Host, children);
    }

    private static PcpAtom CreateHostWithChildren(
        params PcpAtom[] children)
    {
        return PcpAtom.Parent(PcpKnownAtomNames.Host, children);
    }

    private static PcpAtom CreateIp(string address)
    {
        return PcpAtom.Leaf(
            Ip,
            PcpWireValueCodec.EncodeLegacyIpv4(IPAddress.Parse(address)));
    }

    private static PcpAtom CreateMalformedIp()
    {
        return PcpAtom.Leaf(Ip, [0x01, 0x02, 0x03]);
    }

    private static PcpAtom CreatePort(int port)
    {
        return PcpAtom.Leaf(
            PcpKnownAtomNames.HeloPort,
            PcpWireValueCodec.EncodeUInt16((ushort)port));
    }

    private static PcpAtom CreateMalformedPort()
    {
        return PcpAtom.Leaf(PcpKnownAtomNames.HeloPort, [0x01]);
    }

    private static async Task<PcpRelayHandshakeResult> NegotiateAsync(
        IReadOnlyList<PcpAtom> atoms,
        int statusCode = 503,
        uint terminalCode = 1003,
        bool sendQuit = true)
    {
        var transportOptions = new TcpTransportOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            MaxConcurrentConnections = 4,
        };

        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);
        listener.Start();

        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var serverTask = ServeAsync(
            listener,
            atoms,
            statusCode,
            terminalCode,
            sendQuit,
            timeoutSource.Token);

        await using var connection = await new TcpTransportConnector(
            transportOptions).ConnectAsync(
                listener.LocalEndPoint.Address.ToString(),
                listener.LocalEndPoint.Port,
                timeoutSource.Token);
        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            var result = await PcpRelayHandshakeClient.NegotiateAsync(
                connection,
                pipeReader,
                listener.LocalEndPoint.Address.ToString(),
                listener.LocalEndPoint.Port,
                Guid.NewGuid(),
                Guid.NewGuid(),
                streamPosition: 0,
                pcpVersion: 1,
                new PcpRelayHandshakeSettings(
                    MaxHttpHeaderBytes: 32 * 1024,
                    MaxHandshakeAtomBytes: 64 * 1024,
                    MaxAtomsBeforeTerminal: 32,
                    UserAgent: "PeerCastNova/Test",
                    ServantVersion: PcpHeloCodec.LegacyServantVersion),
                timeoutSource.Token);

            await serverTask;
            return result;
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task ServeAsync(
        TcpTransportListener listener,
        IReadOnlyList<PcpAtom> atoms,
        int statusCode,
        uint terminalCode,
        bool sendQuit,
        CancellationToken cancellationToken)
    {
        await using var connection = await listener.AcceptAsync(
            cancellationToken);
        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            _ = await PcpHttpHeaderBlockReader.ReadRequestAsync(
                pipeReader,
                32 * 1024,
                cancellationToken);
            await connection.WriteAsync(
                System.Text.Encoding.ASCII.GetBytes(
                    $"HTTP/1.0 {statusCode} Test\r\n" +
                    "Server: PeerCastStation/Test\r\n" +
                    "Content-Type:application/x-peercast-pcp\r\n\r\n"),
                cancellationToken);

            await using var atomReader = new PcpAtomPipeMessageReader(pipeReader);
            _ = await atomReader.ReadNextAsync(cancellationToken);

            var output = new ArrayBufferWriter<byte>();
            PcpAtomTreeCodec.Write(output, CreateOleh());
            foreach (var atom in atoms)
            {
                PcpAtomTreeCodec.Write(output, atom);
            }

            if (sendQuit)
            {
                PcpAtomTreeCodec.Write(
                    output,
                    PcpAtom.Leaf(
                        PcpKnownAtomNames.Quit,
                        PcpWireValueCodec.EncodeUInt32(terminalCode)));
            }

            await connection.WriteAsync(output.WrittenMemory, cancellationToken);
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static PcpAtom CreateOleh()
    {
        return PcpAtom.Parent(
            PcpKnownAtomNames.Oleh,
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloAgent,
                PcpWireValueCodec.EncodeNullTerminatedUtf8(
                    "PeerCastStation/Test")),
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloSessionId,
                PcpWireValueCodec.EncodeGuid(Guid.NewGuid())),
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloVersion,
                PcpWireValueCodec.EncodeUInt32(1218)));
    }
}
