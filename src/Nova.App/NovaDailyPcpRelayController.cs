using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.App;

internal interface INovaDailyRelayController : IAsyncDisposable
{
    Task StartAsync(GenesisChannelItem channel, CancellationToken cancellationToken);

    Task StopAsync();
}

internal interface INovaDailyRelayControllerFactory
{
    INovaDailyRelayController Create(Action<string> setStatus, Action<string> setEndpoint, Action<string> showError);
}

internal sealed class SystemNovaDailyRelayControllerFactory : INovaDailyRelayControllerFactory
{
    public static SystemNovaDailyRelayControllerFactory Instance { get; } = new();

    private SystemNovaDailyRelayControllerFactory()
    {
    }

    public INovaDailyRelayController Create(
        Action<string> setStatus,
        Action<string> setEndpoint,
        Action<string> showError) =>
        new NovaDailyPcpRelayController(setStatus, setEndpoint, showError);
}

internal sealed class NovaDailyPcpRelayController : INovaDailyRelayController
{
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly Action<string> _setStatus;
    private readonly Action<string> _setEndpoint;
    private readonly Action<string> _showError;
    private ActiveRelay? _active;
    private CancellationTokenSource? _startupCancellation;
    private bool _disposed;

    public NovaDailyPcpRelayController(
        Action<string> setStatus,
        Action<string> setEndpoint,
        Action<string> showError)
    {
        ArgumentNullException.ThrowIfNull(setStatus);
        ArgumentNullException.ThrowIfNull(setEndpoint);
        ArgumentNullException.ThrowIfNull(showError);
        _setStatus = setStatus;
        _setEndpoint = setEndpoint;
        _showError = showError;
    }

    public async Task StartAsync(
        GenesisChannelItem channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        await _commands.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        ActiveRelay? startingRelay = null;

        try
        {
            lock (_stateGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _startupCancellation = startupCancellation;
            }

            if (!DailyPcpPlaybackSourceParser.TryParse(channel.Source, out var source) ||
                source is null)
            {
                throw new InvalidOperationException(
                    "The selected channel does not provide a valid pcp://.../channel/<id> source.");
            }

            await StopActiveCoreAsync().ConfigureAwait(false);
            startupCancellation.Token.ThrowIfCancellationRequested();

            _setEndpoint(string.Empty);
            _setStatus($"{channel.Name}: PCP Relay listenerを開始しています…");

            var tracker = new ChannelStatusTracker(
                source.ChannelId,
                new ChannelSessionId(Guid.NewGuid()));
            var coordinator = new PcpRelayCoordinator(
                new TcpTransportConnector(),
                tracker,
                new IPEndPoint(IPAddress.Any, 0));
            var active = new ActiveRelay(coordinator);
            startingRelay = active;
            lock (_stateGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _active = active;
            }

            active.RunTask = coordinator.RunAsync(
                source.Host,
                source.Port,
                cancellationToken: startupCancellation.Token);
            var readiness = await coordinator.WaitForReadinessAsync(
                startupCancellation.Token).ConfigureAwait(false);
            if (readiness != PcpRelayCoordinatorReadinessOutcome.Ready)
            {
                var result = await active.RunTask.ConfigureAwait(false);
                throw new InvalidOperationException(
                    readiness == PcpRelayCoordinatorReadinessOutcome.Failed
                        ? "The PCP Relay listener could not start. Check that the selected channel is valid and that Nova can open a local TCP port."
                        : $"The PCP Relay listener stopped before it became ready ({result.EndReason}).");
            }

            var endpoints = CreateLanEndpoints(
                source.ChannelId,
                coordinator.LocalEndPoint.Port,
                GetLocalIpv4Addresses());
            if (endpoints.Count == 0)
            {
                throw new InvalidOperationException(
                    "The PCP Relay listener started, but Nova could not find a LAN IPv4 address to display. Check the network adapter and stop the Relay.");
            }

            startupCancellation.Token.ThrowIfCancellationRequested();
            _setEndpoint(string.Join(Environment.NewLine, endpoints));
            _setStatus(
                $"{channel.Name}: Relay待受中。上流PCPへ接続中です。表示したendpointをLAN peerに共有してください。");
            active.ObservationTask = ObserveRelayEndAsync(active, channel.Name);
            active.ActivityTask = ObserveRelayActivityAsync(active, channel.Name);
        }
        catch
        {
            if (startingRelay is not null)
            {
                await StopActiveCoreAsync().ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            lock (_stateGate)
            {
                if (ReferenceEquals(_startupCancellation, startupCancellation))
                {
                    _startupCancellation = null;
                }
            }

            _commands.Release();
        }
    }

    public async Task StopAsync()
    {
        bool relayWasActive;
        lock (_stateGate)
        {
            relayWasActive = _active is not null || _startupCancellation is not null;
            try
            {
                _startupCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Startup completed while Stop was waiting for ownership.
            }
        }

        if (relayWasActive)
        {
            _setStatus("PCP Relayを停止しています…");
        }

        await _commands.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopActiveCoreAsync().ConfigureAwait(false);
            _setEndpoint(string.Empty);
            if (relayWasActive)
            {
                _setStatus("Relay stopped.");
            }
        }
        finally
        {
            _commands.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _startupCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Startup completed while shutdown was requested.
            }
        }

        await _commands.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopActiveCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _commands.Release();
        }
    }

    internal static IReadOnlyList<string> CreateLanEndpoints(
        ChannelId channelId,
        int port,
        IEnumerable<IPAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(addresses);
        if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        return addresses
            .Where(static address => address.AddressFamily == AddressFamily.InterNetwork)
            .Where(static address => !IPAddress.IsLoopback(address))
            .Where(static address =>
            {
                var bytes = address.GetAddressBytes();
                return bytes[0] != 169 || bytes[1] != 254;
            })
            .Distinct()
            .OrderBy(static address => address.ToString(), StringComparer.Ordinal)
            .Select(address =>
                $"pcp://{address}:{port}/channel/{channelId}")
            .ToArray();
    }

    private static IPAddress[] GetLocalIpv4Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(static network => network.OperationalStatus == OperationalStatus.Up)
            .Where(static network => network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(static network => network.GetIPProperties().UnicastAddresses)
            .Select(static address => address.Address)
            .Where(static address => address.AddressFamily == AddressFamily.InterNetwork)
            .Where(static address => !IPAddress.IsLoopback(address))
            .ToArray();

    private async Task StopActiveCoreAsync()
    {
        ActiveRelay? active;
        lock (_stateGate)
        {
            active = _active;
            _active = null;
        }

        if (active is null)
        {
            return;
        }

        List<Exception>? failures = null;
        try
        {
            active.Coordinator.RequestStop();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            await active.Coordinator.StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            if (active.RunTask is not null)
            {
                await active.RunTask.ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            await active.Coordinator.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        if (failures is { Count: > 0 })
        {
            throw new AggregateException("The PCP Relay did not clean up completely.", failures);
        }
    }

    private async Task ObserveRelayEndAsync(ActiveRelay active, string channelName)
    {
        PcpRelayCoordinatorResult? result = null;
        Exception? failure = null;
        try
        {
            result = await active.RunTask!.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            await active.Coordinator.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        await _commands.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_stateGate)
            {
                if (_disposed || !ReferenceEquals(_active, active))
                {
                    return;
                }

                _active = null;
            }

            _setEndpoint(string.Empty);
            _setStatus("Relay stopped.");
            if (failure is not null)
            {
                _showError($"{channelName}: PCP Relayが終了しました。{failure.Message}");
                return;
            }

            if (result?.EndReason is not PcpRelayCoordinatorEndReason.Stopped and
                not PcpRelayCoordinatorEndReason.CallerCanceled)
            {
                _showError(
                    $"{channelName}: PCP Relayが終了しました ({result?.EndReason})。上流channelまたは接続状態を確認してください。");
            }
        }
        finally
        {
            _commands.Release();
        }
    }

    private async Task ObserveRelayActivityAsync(ActiveRelay active, string channelName)
    {
        var first = await Task.WhenAny(
            active.Coordinator.FirstPacketAccepted,
            active.RunTask!).ConfigureAwait(false);
        if (!ReferenceEquals(first, active.Coordinator.FirstPacketAccepted))
        {
            return;
        }

        await active.Coordinator.FirstPacketAccepted.ConfigureAwait(false);
        await _commands.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_stateGate)
            {
                if (_disposed || !ReferenceEquals(_active, active))
                {
                    return;
                }
            }

            _setStatus($"{channelName}: Relay中 — upstream content is flowing");
        }
        finally
        {
            _commands.Release();
        }
    }

    private sealed class ActiveRelay(PcpRelayCoordinator coordinator)
    {
        public PcpRelayCoordinator Coordinator { get; } = coordinator;

        public Task<PcpRelayCoordinatorResult>? RunTask { get; set; }

        public Task? ObservationTask { get; set; }

        public Task? ActivityTask { get; set; }
    }
}
