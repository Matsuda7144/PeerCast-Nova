using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.PortMapping;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.App;

internal interface INovaSharedPublicRelayProvider
{
    GenesisSharedPublicRelay GetSharedRelay(GenesisGuiNextConnectionPreferences settings,
        Func<ChannelId, PcpRelayFanout, PcpRelayCoordinator?> resolveOwner);
}

internal enum GenesisSharedPublicRelayState { Off, Starting, Listening, Stopping, Stopped, Failed, Disposed }
internal sealed record GenesisSharedPublicRelaySnapshot(GenesisSharedPublicRelayState State, int Port,
    int Channels, NatPortMappingReadState? Nat, bool CleanupFailed, string? Error)
{
    // Bind and router mapping never prove Internet reachability.
    internal string Reachability { get; } = "外部到達性未確認";
    internal string ListenerText => State switch
    {
        GenesisSharedPublicRelayState.Starting => "TCP待受準備中",
        GenesisSharedPublicRelayState.Listening => "TCP待受中",
        GenesisSharedPublicRelayState.Stopping => "公開停止中",
        GenesisSharedPublicRelayState.Failed => CleanupFailed ? "cleanup失敗（停止処理未確認）" : "設定エラー",
        GenesisSharedPublicRelayState.Off => "公開OFF",
        _ => "停止完了",
    };
    internal string MappingText
    {
        get
        {
            if (Nat?.Enabled != true) return "自動開放OFF（手動開放）";
            var map = Nat.Mappings.LastOrDefault(m => m.Identity.InternalPort == Port && m.Identity.Network == Nat.Network);
            return map?.Status switch
            {
                NatMappingStatus.Mapped => "NAT mapping成功",
                NatMappingStatus.Mapping => "NAT mapping中",
                NatMappingStatus.Unmapping => "NAT mapping停止中",
                NatMappingStatus.Rejected => "NAT mapping失敗",
                NatMappingStatus.ResultUnknown or NatMappingStatus.UnmapUnconfirmed => "NAT mapping未確認",
                _ => "NAT mapping未作成",
            };
        }
    }
}

/// <summary>Application integration of the existing shared listener; no PCP engine or NAT implementation.</summary>
internal sealed class GenesisSharedPublicRelay : IAsyncDisposable
{
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly GenesisGuiNextConnectionPreferences _settings;
    private readonly NatPortMappingManager _mapping;
    private readonly Func<ChannelId, PcpRelayFanout, PcpRelayCoordinator?> _resolveOwner;
    private readonly PcpWanIngressOptions _wan;
    private PcpSharedDownstreamRelayListener? _listener;
    private Task? _run;
    private IDisposable? _mappingLease;
    private GenesisSharedPublicRelayState _state = GenesisSharedPublicRelayState.Stopped;
    private bool _cleanupFailed;
    private string? _error;
    private int _references;
    private int _closed;
    private readonly object _disposeGate = new();
    private Task? _disposal;
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);

    internal GenesisSharedPublicRelay(GenesisGuiNextConnectionPreferences settings, NatPortMappingManager mapping,
        Func<ChannelId, PcpRelayFanout, PcpRelayCoordinator?> resolveOwner, PcpWanIngressOptions? wan = null)
    {
        settings.Validate();
        if (!settings.WanRelayEnabled || settings.UseIpv6) throw new ArgumentException("Shared WAN relay requires explicit IPv4 settings.");
        _settings = settings with { };
        _mapping = mapping;
        _resolveOwner = resolveOwner;
        _wan = wan ?? new();
    }

    internal GenesisSharedPublicRelaySnapshot Snapshot
    {
        get
        {
            var ended = _state == GenesisSharedPublicRelayState.Listening && _run?.IsCompleted == true;
            return new(ended ? GenesisSharedPublicRelayState.Failed : _state, _settings.ListenPort, _listener?.RouteCount ?? 0,
                _mapping.ReadState(), _cleanupFailed, ended ? "TCP待受が終了しました。" : _error);
        }
    }
    internal IPEndPoint? LocalEndPoint => _listener?.LocalEndPoint;

    internal async Task<Lease> AcquireAsync(Func<PcpSharedDownstreamRelayListener, IAsyncDisposable> register, CancellationToken token)
    {
        await _serial.WaitAsync(token).ConfigureAwait(false);
        IAsyncDisposable? route = null;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
            if (_cleanupFailed) throw new InvalidOperationException("Shared relay cleanup is unconfirmed.");
            if (_listener is not null && _run?.IsCompleted == true) throw new IOException("Public listener ended.");
            token.ThrowIfCancellationRequested();
            _listener ??= new(new(IPAddress.Any, _settings.ListenPort), _resolveOwner,
                new TcpTransportOptions { MaxConcurrentConnections = 128 }, wanOptions: _wan);
            route = register(_listener);
            token.ThrowIfCancellationRequested();
            if (_run is null)
            {
                _state = GenesisSharedPublicRelayState.Starting;
                _error = null;
                try
                {
                    _run = _listener.StartAsync(CancellationToken.None);
                    var bound = _listener.LocalEndPoint;
                    if (bound.Port == 0 || bound.Port != _settings.ListenPort) throw new IOException("Invalid public port.");
                    _mappingLease = _mapping.AcquirePublicTcpListener(bound);
                    _state = GenesisSharedPublicRelayState.Listening;
                }
                catch
                {
                    _state = GenesisSharedPublicRelayState.Failed;
                    _error = "TCP待受を開始できません。ポート設定を確認してください。";
                    await StopListenerAsync().ConfigureAwait(false);
                    _state = GenesisSharedPublicRelayState.Failed;
                    throw;
                }
            }
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
                token.ThrowIfCancellationRequested();
                _references++;
                return new(this, route);
            }
            catch { if (_references == 0) await StopListenerAsync().ConfigureAwait(false); throw; }
        }
        catch
        {
            if (_references == 0) await StopListenerAsync().ConfigureAwait(false);
            if (route is not null) await route.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally { _serial.Release(); }
    }

    private async Task ReleaseAsync(IAsyncDisposable route)
    {
        try { await route.DisposeAsync().AsTask().WaitAsync(CleanupTimeout).ConfigureAwait(false); }
        catch { _cleanupFailed = true; _state = GenesisSharedPublicRelayState.Failed; throw; }
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            _references--;
            if (_references == 0) await StopListenerAsync().ConfigureAwait(false);
        }
        finally { _serial.Release(); }
    }

    private async Task StopListenerAsync()
    {
        if (_listener is null) return;
        _state = GenesisSharedPublicRelayState.Stopping;
        try
        {
            await _listener.StopAsync().WaitAsync(CleanupTimeout).ConfigureAwait(false);
            await _listener.DisposeAsync().AsTask().WaitAsync(CleanupTimeout).ConfigureAwait(false);
            if (_run is { } run) await run.ConfigureAwait(false);
            if (_listener.CleanupFailureCount != 0)
                throw new IOException("Public transport cleanup failed.");
            _mappingLease?.Dispose();
            _mappingLease = null;
            _listener = null; _run = null;
            _state = GenesisSharedPublicRelayState.Stopped;
        }
        catch
        {
            _cleanupFailed = true; _state = GenesisSharedPublicRelayState.Failed;
            _error = "停止処理を確認できません。";
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _closed, 1);
        try { _listener?.RequestStop(); }
        catch (ObjectDisposedException) { /* The serialized listener stop already owns cleanup. */ }
        lock (_disposeGate) return new(_disposal ??= DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopListenerAsync().ConfigureAwait(false);
            if (_cleanupFailed) throw new IOException("Shared relay cleanup remains unconfirmed.");
            _state = GenesisSharedPublicRelayState.Disposed;
        }
        finally { _serial.Release(); }
    }

    internal sealed class Lease(GenesisSharedPublicRelay owner,
        IAsyncDisposable route) : IAsyncDisposable
    {
        private readonly object _gate = new();
        private Task? _release;
        public ValueTask DisposeAsync() { lock (_gate) return new(_release ??= owner.ReleaseAsync(route)); }
    }
}
