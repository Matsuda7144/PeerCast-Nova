using System.Buffers;
using System.Globalization;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PeerCast.Nova.Cli;
using PeerCast.Nova.Core;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Foundation.Logging;
using PeerCast.Nova.Network;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

Console.WriteLine("Hello, Nova.");
Console.WriteLine("PeerCast Nova 0.0.6g Native Parameter Transfer Boundary");

INovaLogger logger = new ConsoleNovaLogger();

INovaModule[] modules =
[
    new NetworkModule(logger),
];

await using var application = new NovaApplication(modules, logger);

try
{
    await application.StartAsync();

    if (HasArgument("--transport-self-test"))
    {
        await RunTransportSelfTestAsync();
    }

    if (HasArgument("--pcp-framing-self-test"))
    {
        await RunPcpFramingSelfTestAsync();
    }

    if (HasArgument("--pcp-compatibility-probe"))
    {
        await RunPcpCompatibilityProbeAsync();
    }

    if (HasArgument("--pcp-channel-session-probe"))
    {
        await RunPcpChannelSessionProbeAsync();
    }

    if (HasArgument("--pcp-relay-diagnostics"))
    {
        await RunPcpRelayDiagnosticsAsync();
    }

    if (HasArgument("--playback-signature-probe"))
    {
        await RunPlaybackSignatureProbeAsync();
    }

    if (HasArgument("--playback-decoder-lifecycle-probe"))
    {
        await RunPlaybackDecoderLifecycleProbeAsync();
    }
}
finally
{
    await application.StopAsync();
}

bool HasArgument(string expected)
{
    return Array.Exists(
        args,
        argument => string.Equals(
            argument,
            expected,
            StringComparison.OrdinalIgnoreCase));
}

string? GetOption(string name)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(
            args[index],
            name,
            StringComparison.OrdinalIgnoreCase))
        {
            return args[index + 1];
        }
    }

    return null;
}

async Task RunPcpCompatibilityProbeAsync()
{
    var host = GetOption("--host") ?? "127.0.0.1";
    var portText = GetOption("--port") ?? "7144";
    var channelText = GetOption("--channel")
        ?? throw new ArgumentException(
            "--channel requires a 32-digit PeerCast channel ID.");

    if (!int.TryParse(portText, out var port))
    {
        throw new ArgumentException("--port must be an integer.");
    }

    if (!Guid.TryParseExact(channelText, "N", out var channelId))
    {
        throw new ArgumentException(
            "--channel must be a 32-digit hexadecimal channel ID.");
    }

    var positionText = GetOption("--position") ?? "0";

    if (!long.TryParse(positionText, out var position) || position < 0)
    {
        throw new ArgumentException(
            "--position must be a non-negative integer.");
    }

    var transportOptions = new TcpTransportOptions
    {
        ConnectTimeout = TimeSpan.FromSeconds(10),
    };

    var client = new PcpCompatibilityClient(
        new TcpTransportConnector(transportOptions));

    var observePostHandshake = HasArgument("--observe-post-handshake");
    PcpCompatibilityProbeResult result;

    if (observePostHandshake)
    {
        var observeAtomsText = GetOption("--observe-atoms") ?? "4";
        var observeTimeoutText =
            GetOption("--observe-timeout-ms") ?? "2000";
        var observeMaxBytesText =
            GetOption("--observe-max-atom-bytes") ?? "262144";

        if (!int.TryParse(
            observeAtomsText,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var observeAtoms) || observeAtoms < 1)
        {
            throw new ArgumentException(
                "--observe-atoms must be a positive integer.");
        }

        if (!int.TryParse(
            observeTimeoutText,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var observeTimeoutMilliseconds) ||
            observeTimeoutMilliseconds < 1)
        {
            throw new ArgumentException(
                "--observe-timeout-ms must be a positive integer.");
        }

        if (!int.TryParse(
            observeMaxBytesText,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var observeMaxAtomBytes) ||
            observeMaxAtomBytes < PcpAtomCodec.HeaderSize)
        {
            throw new ArgumentException(
                "--observe-max-atom-bytes must fit at least one atom header.");
        }

        result = await client.ProbeAndObserveAsync(
            host,
            port,
            channelId,
            new PcpPostHandshakeObservationOptions
            {
                Timeout = TimeSpan.FromMilliseconds(
                    observeTimeoutMilliseconds),
                MaxTopLevelAtoms = observeAtoms,
                MaxAtomBytes = observeMaxAtomBytes,
            },
            position);
    }
    else
    {
        result = await client.ProbeAsync(
            host,
            port,
            channelId,
            position);
    }

    Console.WriteLine($"Outcome: {result.Outcome}");
    Console.WriteLine($"HTTP: {result.HttpStatusCode}");
    Console.WriteLine($"Server: {result.Server ?? "(not supplied)"}");
    Console.WriteLine($"Content-Type: {result.ContentType ?? "(not supplied)"}");

    if (HasArgument("--trace-atoms"))
    {
        foreach (var atomName in result.ReceivedAtomNames)
        {
            Console.WriteLine($"Received Atom: {atomName}");
        }
    }

    if (result.RemotePeer is not null)
    {
        Console.WriteLine(observePostHandshake
            ? "Remote Session: (redacted during observation)"
            : $"Remote Session: {result.RemotePeer.SessionId:N}");
        Console.WriteLine(
            $"Remote Agent: {result.RemotePeer.Agent ?? "(not supplied)"}");
        Console.WriteLine(
            $"Remote Version: {result.RemotePeer.ServantVersion?.ToString(CultureInfo.InvariantCulture) ?? "(not supplied)"}");
        Console.WriteLine(observePostHandshake
            ? "Observed Address: (redacted during observation)"
            : $"Observed Address: {result.RemotePeer.RemoteAddress?.ToString() ?? "(not supplied)"}");
        Console.WriteLine(
            $"Observed Port: {result.RemotePeer.RemotePort?.ToString(CultureInfo.InvariantCulture) ?? "(not supplied)"}");
    }

    Console.WriteLine(
        $"Terminal Atom: {result.TerminalAtom?.ToString() ?? "(none)"}");
    Console.WriteLine(
        $"Terminal Code: {result.TerminalCode?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}");

    if (result.PostHandshakeObservation is not null)
    {
        var observation = result.PostHandshakeObservation;

        Console.WriteLine(
            $"Observation Stop: {observation.StopReason}");
        Console.WriteLine(
            $"Observed Wire Bytes: {observation.ObservedWireBytes.ToString(CultureInfo.InvariantCulture)}");

        foreach (var atom in observation.Atoms)
        {
            PrintObservedAtomShape(atom, depth: 0);
        }
    }
}

async Task RunPcpChannelSessionProbeAsync()
{
    var host = GetOption("--host") ?? "127.0.0.1";
    var portText = GetOption("--port") ?? "7144";
    var channelText = GetOption("--channel")
        ?? throw new ArgumentException(
            "--channel requires a 32-digit PeerCast channel ID.");
    var positionText = GetOption("--position") ?? "0";
    var packetLimitText = GetOption("--session-packets") ?? "4";
    var timeoutText = GetOption("--session-timeout-ms") ?? "10000";
    var idleTimeoutText =
        GetOption("--session-idle-timeout-ms") ?? "5000";
    var reconnectsText = GetOption("--session-reconnects") ?? "0";

    if (!int.TryParse(
        portText,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var port) || port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
    {
        throw new ArgumentException("--port must be a valid TCP port.");
    }

    if (!Guid.TryParseExact(channelText, "N", out var channelId))
    {
        throw new ArgumentException(
            "--channel must be a 32-digit hexadecimal channel ID.");
    }

    if (!long.TryParse(
        positionText,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var position) || position < 0)
    {
        throw new ArgumentException(
            "--position must be a non-negative integer.");
    }

    if (!int.TryParse(
        packetLimitText,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var packetLimit) || packetLimit is < 1 or > 1_000_000)
    {
        throw new ArgumentException(
            "--session-packets must be between 1 and 1000000.");
    }

    if (!int.TryParse(
        timeoutText,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var timeoutMilliseconds) ||
        timeoutMilliseconds is < 1 or > 600_000)
    {
        throw new ArgumentException(
            "--session-timeout-ms must be between 1 and 600000.");
    }

    if (!int.TryParse(
        idleTimeoutText,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var idleTimeoutMilliseconds) ||
        idleTimeoutMilliseconds is < 1 or > 600_000)
    {
        throw new ArgumentException(
            "--session-idle-timeout-ms must be between 1 and 600000.");
    }

    if (!int.TryParse(
        reconnectsText,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var reconnects) || reconnects is < 0 or > 10)
    {
        throw new ArgumentException(
            "--session-reconnects must be between 0 and 10.");
    }

    var transportOptions = new TcpTransportOptions
    {
        ConnectTimeout = TimeSpan.FromMilliseconds(
            Math.Min(timeoutMilliseconds, 10_000)),
    };
    var tracker = new ChannelStatusTracker(
        new ChannelId(channelId),
        new ChannelSessionId(Guid.NewGuid()));
    var consumer = new SessionProbePacketConsumer(packetLimit);
    await using var session = new PcpChannelSession(
        new TcpTransportConnector(transportOptions),
        tracker,
        consumer,
        new PcpChannelSessionOptions
        {
            MaxReconnectAttempts = reconnects,
            Stream = new PcpChannelStreamOptions
            {
                ReadIdleTimeout = TimeSpan.FromMilliseconds(
                    idleTimeoutMilliseconds),
            },
        });
    consumer.SetStopRequest(session.RequestStop);

    using var timeoutSource = new CancellationTokenSource(
        TimeSpan.FromMilliseconds(timeoutMilliseconds));
    using var timeoutRegistration = timeoutSource.Token.Register(
        session.RequestStop);

    PcpChannelSessionResult result;

    try
    {
        result = await session.RunAsync(host, port, position);
    }
    catch (Exception exception)
        when (exception is
            TimeoutException or
            SocketException or
            IOException or
            PcpProtocolException or
            PcpChannelConsumerException or
            InvalidOperationException or
            OverflowException)
    {
        Console.WriteLine("End: Faulted");
        Console.WriteLine("Stop Trigger: Failure");
        Console.WriteLine($"Final State: {tracker.Snapshot.State}");
        PrintChannelFault(tracker.Snapshot);
        Environment.ExitCode = 1;
        return;
    }

    var stopTrigger = result.EndReason == PcpChannelSessionEndReason.Stopped
        ? consumer.PacketLimitReached
            ? "PacketLimit"
            : timeoutSource.IsCancellationRequested
                ? "Timeout"
                : "Requested"
        : "Remote";

    Console.WriteLine($"End: {result.EndReason}");
    Console.WriteLine($"Stop Trigger: {stopTrigger}");
    Console.WriteLine(
        $"Attempts: {result.AttemptCount.ToString(CultureInfo.InvariantCulture)}");
    Console.WriteLine(
        $"Reconnects: {result.ReconnectCount.ToString(CultureInfo.InvariantCulture)}");
    Console.WriteLine(
        $"HTTP: {result.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}");
    Console.WriteLine(
        $"Channel Messages: {result.ChannelMessageCount.ToString(CultureInfo.InvariantCulture)}");
    Console.WriteLine(
        $"Packets: {result.PacketCount.ToString(CultureInfo.InvariantCulture)}");
    Console.WriteLine(
        $"Payload Bytes: {result.PayloadByteCount.ToString(CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Final State: {tracker.Snapshot.State}");
    PrintChannelFault(tracker.Snapshot);

    if (string.Equals(stopTrigger, "Timeout", StringComparison.Ordinal))
    {
        Environment.ExitCode = 2;
    }
}

async Task RunPcpRelayDiagnosticsAsync()
{
    var options = RelayDiagnosticCommandOptions.Parse(args);
    using var cancellationSource = new CancellationTokenSource();

    ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellationSource.Cancel();
    };
    Console.CancelKeyPress += cancelHandler;

    try
    {
        var result = await RelayDiagnosticCommand.RunAsync(
            options,
            Console.Out,
            cancellationSource.Token);

        if (result.ExitCode != 0)
        {
            Environment.ExitCode = result.ExitCode;
        }
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }
}

async Task RunPlaybackSignatureProbeAsync()
{
    var options = PlaybackSignatureCommandOptions.Parse(args);
    using var cancellationSource = new CancellationTokenSource();

    ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellationSource.Cancel();
    };
    Console.CancelKeyPress += cancelHandler;

    try
    {
        var result = await PlaybackSignatureCommand.RunAsync(
            options,
            Console.Out,
            cancellationSource.Token);

        if (result.ExitCode != 0)
        {
            Environment.ExitCode = result.ExitCode;
        }
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }
}

async Task RunPlaybackDecoderLifecycleProbeAsync()
{
    var options = PlaybackDecoderLifecycleProbeOptions.Parse(args);
    using var cancellationSource = new CancellationTokenSource();

    ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellationSource.Cancel();
    };
    Console.CancelKeyPress += cancelHandler;

    try
    {
        var result = await PlaybackDecoderLifecycleProbeCommand.RunAsync(
            options,
            Console.Out,
            cancellationSource.Token);

        if (result.ExitCode != 0)
        {
            Environment.ExitCode = result.ExitCode;
        }
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }
}

static void PrintChannelFault(ChannelStatusSnapshot snapshot)
{
    if (snapshot.Fault is null)
    {
        return;
    }

    Console.WriteLine($"Fault Category: {snapshot.Fault.Category}");
    Console.WriteLine($"Fault Stage: {snapshot.Fault.Stage}");
    Console.WriteLine(
        $"Fault Attempts: {snapshot.Fault.AttemptCount.ToString(CultureInfo.InvariantCulture)}");
    Console.WriteLine(
        $"Fault Reconnects: {snapshot.Fault.ReconnectCount.ToString(CultureInfo.InvariantCulture)}");
}

static void PrintObservedAtomShape(PcpAtomShape atom, int depth)
{
    var indent = new string(' ', depth * 2);

    if (atom.IsParent)
    {
        Console.WriteLine(
            $"{indent}+ {atom.Name} parent children={atom.Value.ToString(CultureInfo.InvariantCulture)} wire-bytes={atom.EncodedLength.ToString(CultureInfo.InvariantCulture)}");

        foreach (var child in atom.Children)
        {
            PrintObservedAtomShape(child, depth + 1);
        }

        return;
    }

    Console.WriteLine(
        $"{indent}- {atom.Name} leaf bytes={atom.Value.ToString(CultureInfo.InvariantCulture)} nul-utf8-candidate={(atom.LooksLikeNullTerminatedUtf8 ? "yes" : "no")} wire-bytes={atom.EncodedLength.ToString(CultureInfo.InvariantCulture)}");
}

static async Task RunTransportSelfTestAsync()
{
    var options = new TcpTransportOptions
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
        MaxConcurrentConnections = 4,
    };

    await using var listener = new TcpTransportListener(
        new IPEndPoint(IPAddress.Loopback, 0),
        options);

    listener.Start();

    using var timeoutSource =
        new CancellationTokenSource(TimeSpan.FromSeconds(10));

    var acceptTask = listener.AcceptAsync(timeoutSource.Token).AsTask();
    var connector = new TcpTransportConnector(options);

    await using var client = await connector.ConnectAsync(
        listener.LocalEndPoint,
        timeoutSource.Token);

    await using var server = await acceptTask;

    var payload = Encoding.UTF8.GetBytes("Hello over Nova transport.");
    await client.WriteAsync(payload, timeoutSource.Token);

    var received = new byte[payload.Length];
    var totalRead = 0;

    while (totalRead < received.Length)
    {
        var read = await server.ReadAsync(
            received.AsMemory(totalRead),
            timeoutSource.Token);

        if (read == 0)
        {
            throw new EndOfStreamException(
                "The loopback connection closed before the payload was complete.");
        }

        totalRead += read;
    }

    if (!received.AsSpan().SequenceEqual(payload))
    {
        throw new InvalidDataException(
            "The loopback transport payload did not round-trip.");
    }

    Console.WriteLine(
        $"Transport self-test passed: {client.LocalEndPoint} -> {client.RemoteEndPoint}");

    await listener.StopAsync();
}

static async Task RunPcpFramingSelfTestAsync()
{
    var encoded = new ArrayBufferWriter<byte>();

    PcpAtomWriter.WriteParentHeader(
        encoded,
        PcpAtomName.FromAscii("root"),
        childCount: 2);

    PcpAtomWriter.WriteLeaf(
        encoded,
        PcpAtomName.FromAscii("one1"),
        [0x01, 0x02, 0x03]);

    PcpAtomWriter.WriteParentHeader(
        encoded,
        PcpAtomName.FromAscii("nest"),
        childCount: 1);

    PcpAtomWriter.WriteLeaf(
        encoded,
        PcpAtomName.FromAscii("zero"),
        []);

    var pipe = new Pipe();
    var sink = new ConsolePcpAtomSink();
    var frameReader = new PcpAtomPipeReader(pipe.Reader);
    var readTask = frameReader.ReadToEndAsync(sink);

    var framingBytes = encoded.WrittenMemory.ToArray();

    foreach (var value in framingBytes)
    {
        var destination = pipe.Writer.GetSpan(1);
        destination[0] = value;
        pipe.Writer.Advance(1);

        var flushResult = await pipe.Writer.FlushAsync();

        if (flushResult.IsCompleted)
        {
            break;
        }
    }

    await pipe.Writer.CompleteAsync();
    await readTask;

    Console.WriteLine("PCP framing self-test passed.");
}

file sealed class ConsolePcpAtomSink : IPcpAtomSink
{
    public void OnParentStarted(PcpParentStarted atom)
    {
        Console.WriteLine(
            $"{new string(' ', atom.Depth * 2)}+ {atom.Header.Name} ({atom.Header.Value} children)");
    }

    public void OnLeafAtom(PcpLeafAtom atom)
    {
        Console.WriteLine(
            $"{new string(' ', atom.Depth * 2)}- {atom.Header.Name} ({atom.Header.Value} bytes)");
    }

    public void OnParentEnded(PcpParentEnded atom)
    {
        Console.WriteLine(
            $"{new string(' ', atom.Depth * 2)}  end {atom.Name}");
    }
}

file sealed class SessionProbePacketConsumer : IPcpChannelPacketConsumer
{
    private readonly int _packetLimit;
    private Action? _stopRequest;
    private int _packetLimitReached;
    private int _packetCount;

    public SessionProbePacketConsumer(int packetLimit)
    {
        _packetLimit = packetLimit;
    }

    public bool PacketLimitReached =>
        Volatile.Read(ref _packetLimitReached) != 0;

    public void SetStopRequest(Action stopRequest)
    {
        ArgumentNullException.ThrowIfNull(stopRequest);

        if (Interlocked.CompareExchange(
            ref _stopRequest,
            stopRequest,
            comparand: null) is not null)
        {
            throw new InvalidOperationException(
                "The session stop callback is already configured.");
        }
    }

    public ValueTask ConsumeAsync(
        PcpChannelPacket packet,
        CancellationToken cancellationToken)
    {
        _ = packet;
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Increment(ref _packetCount) >= _packetLimit &&
            Interlocked.Exchange(ref _packetLimitReached, 1) == 0)
        {
            _stopRequest?.Invoke();
        }

        return ValueTask.CompletedTask;
    }
}
