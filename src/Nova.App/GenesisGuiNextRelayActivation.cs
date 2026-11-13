using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App;

internal enum GenesisRelayActivationState { Stopped, Preparing, Starting, Running, Stopping, Failed }
internal enum GenesisRelayActivationError
{
    None, NoSelection, UnresolvedChannel, AlreadyStarting, AlreadyRunning, Stopping,
    Closed, StartFailed, BindFailed, UpstreamFailed, NegotiationFailed, Canceled,
    ReadinessTimedOut, ShutdownFailed, OwnerSuperseded,
}

internal sealed record GenesisRelayActivationSnapshot(
    GenesisRelayActivationState State = GenesisRelayActivationState.Stopped,
    GenesisRelayActivationError Error = GenesisRelayActivationError.None,
    long Generation = 0, ChannelId? ChannelId = null, ChannelSessionId? SessionId = null,
    string? EndPoint = null, bool ListenerBound = false, bool UpstreamReady = false, bool ReceivingReady = false,
    bool CleanupConfirmed = true, PcpSmartRelayState Recovery = PcpSmartRelayState.Disabled, string? PublicStatus = null,
    string? CapacityStatus = null)
{
    public string Text => "ローカルRelay: " + (State switch
    {
        GenesisRelayActivationState.Stopped => "停止",
        GenesisRelayActivationState.Preparing => "準備中",
        GenesisRelayActivationState.Starting => UpstreamReady ? "PCP接続済・受信待ち" : ListenerBound ? "待受開始・接続確認中" : "開始中",
        GenesisRelayActivationState.Running => Recovery == PcpSmartRelayState.Degraded ? "メディア待ち" :
            Recovery is PcpSmartRelayState.Recovering or PcpSmartRelayState.Backoff or PcpSmartRelayState.Discovering or
                PcpSmartRelayState.Connecting or PcpSmartRelayState.Negotiating ? "上流再接続中" : "受信中",
        GenesisRelayActivationState.Stopping => "停止処理中",
        _ => "失敗",
    }) + (EndPoint is null ? "" : $" ({EndPoint})") + (Error == GenesisRelayActivationError.None ? "" : " — " + ErrorText(Error)) +
        (PublicStatus is null ? "" : $" / {PublicStatus}") + (CapacityStatus is null ? "" : $" / {CapacityStatus}");

    internal static string ErrorText(GenesisRelayActivationError error) => error switch
    {
        GenesisRelayActivationError.NoSelection => "チャンネルを選択してください。",
        GenesisRelayActivationError.UnresolvedChannel => "有効なPCPチャンネルを確認できません。",
        GenesisRelayActivationError.AlreadyStarting => "すでに開始処理中です。",
        GenesisRelayActivationError.AlreadyRunning => "すでに動作中です。",
        GenesisRelayActivationError.Stopping => "停止完了までお待ちください。",
        GenesisRelayActivationError.Closed => "終了処理中のため開始できません。",
        GenesisRelayActivationError.BindFailed => "待受を開始できませんでした。",
        GenesisRelayActivationError.UpstreamFailed => "上流への接続に失敗しました。",
        GenesisRelayActivationError.NegotiationFailed => "PCP接続の確認に失敗しました。",
        GenesisRelayActivationError.Canceled => "開始が取り消されました。",
        GenesisRelayActivationError.ReadinessTimedOut => "受信開始を確認できませんでした。",
        GenesisRelayActivationError.ShutdownFailed => "停止を確認できません。再開始せず終了してください。",
        GenesisRelayActivationError.OwnerSuperseded => "チャンネルの所有世代が切り替わりました。停止後に再開始してください。",
        _ => "Relayの開始に失敗しました。",
    };
}

/// <summary>
/// One explicit Viewing operation. No construction/startup bind, saved endpoint,
/// playback owner, producer, NAT or second policy. Only the existing composition
/// creates/owns transports. Running requires a real accepted upstream packet.
/// </summary>
internal sealed class GenesisGuiNextRelayActivation : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Func<GenesisChannelItem, CancellationToken, ValueTask<GenesisGuiNextPublicChannelComposition.Owner>> _prepare;
    private readonly TimeSpan _readinessTimeout;
    private readonly TimeSpan _shutdownTimeout;
    private Operation? _current;
    private long _generation;
    private bool _closed;
    private Task? _disposeTask;
    private Func<string>? _publicStatus;
    private Func<string>? _capacityStatus;
    private GenesisRelayActivationSnapshot _snapshot = new();

    public GenesisGuiNextRelayActivation(GenesisGuiNextPublicChannelComposition channels, PcpSmartRelayOptions? smartRelay = null)
        : this((channel, token) => channels.PrepareViewingAsync(channel, new(IPAddress.Loopback, 0),
            new PcpRelayCoordinatorOptions { UpstreamSession = new() { MaxReconnectAttempts = 0, SmartRelay = smartRelay } }, token),
            readinessTimeout: smartRelay is null ? null : Timeout.InfiniteTimeSpan)
    {
        _publicStatus = () => channels.PublicStatusText;
        _capacityStatus = () => channels.Registry.CapacityStatusText();
    }

    internal static PcpSmartRelayOptions? RecoveryPolicy(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "limited" => new(),
        "persistent" => new() { Mode = PcpSmartRelayMode.Persistent },
        _ => null,
    };

    // Narrow managed lifecycle seam; production always uses the constructor above.
    internal GenesisGuiNextRelayActivation(
        Func<GenesisChannelItem, CancellationToken, ValueTask<GenesisGuiNextPublicChannelComposition.Owner>> prepare,
        TimeSpan? readinessTimeout = null, TimeSpan? shutdownTimeout = null)
    {
        _prepare = prepare;
        _readinessTimeout = readinessTimeout ?? TimeSpan.FromSeconds(30);
        _shutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(15);
    }

    public event EventHandler? Changed;
    public GenesisRelayActivationSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot with
                {
                    Recovery = _current?.Owner?.ViewingRuntime?.SmartRelaySnapshot.State ?? PcpSmartRelayState.Disabled,
                    PublicStatus = _publicStatus?.Invoke(),
                    CapacityStatus = _current?.Owner?.CapacityStatusText ?? _capacityStatus?.Invoke()
                };
            }
        }
    }
    internal GenesisGuiNextPublicChannelComposition.Owner? CurrentOwner { get { lock (_gate) { return _current?.Owner; } } }
    public bool CanStart { get { lock (_gate) { return !_closed && _current is null; } } }
    public bool CanStop { get { lock (_gate) { return _current is not null; } } }

    internal static bool TryResolve(GenesisChannelItem? channel, out ChannelId? id)
    {
        id = null;
        if (channel is null || !DailyPcpPlaybackSourceParser.TryParse(channel.Source, out var source) || source is null)
            return false;
        // YP metadata never supplies an identity in place of the PCP URI. If it
        // supplies one as well, disagreement or invalid metadata fails closed.
        if (channel.ChannelId is { } text && (!Guid.TryParse(text, out var metadata) || metadata != source.ChannelId.Value))
            return false;
        id = source.ChannelId;
        return true;
    }

    public Task<bool> StartAsync(GenesisChannelItem? channel, CancellationToken cancellationToken = default)
    {
        Task<bool> result;
        lock (_gate)
        {
            var error = _closed ? GenesisRelayActivationError.Closed : _current is not null
                ? _snapshot.State == GenesisRelayActivationState.Running ? GenesisRelayActivationError.AlreadyRunning
                    : _current.StopRequested || _snapshot.State == GenesisRelayActivationState.Failed ? GenesisRelayActivationError.Stopping
                    : GenesisRelayActivationError.AlreadyStarting
                : channel is null ? GenesisRelayActivationError.NoSelection
                : !TryResolve(channel, out _) ? GenesisRelayActivationError.UnresolvedChannel
                : cancellationToken.IsCancellationRequested ? GenesisRelayActivationError.Canceled : GenesisRelayActivationError.None;
            if (error != GenesisRelayActivationError.None)
            {
                _snapshot = _snapshot with { Error = error };
                result = Task.FromResult(false);
            }
            else
            {
                TryResolve(channel, out var id);
                var operation = new Operation(++_generation, cancellationToken);
                _current = operation;
                _snapshot = new(GenesisRelayActivationState.Preparing, Generation: operation.Generation,
                    ChannelId: id, CleanupConfirmed: false);
                operation.Lifetime = RunAsync(operation, channel!);
                result = operation.Started.Task;
            }
        }
        Notify();
        return result;
    }

    public Task StopAsync()
    {
        Task task;
        lock (_gate)
        {
            if (_current is not { } operation) return Task.CompletedTask;
            if (operation.StopTask is not null) return operation.StopTask;
            if (operation.Lifetime?.IsCompleted == true) return Task.CompletedTask;
            operation.StopRequested = true;
            // Close admission before yielding to shutdown or late preparation.
            operation.Owner?.Retire();
            _snapshot = _snapshot with { State = GenesisRelayActivationState.Stopping, Error = GenesisRelayActivationError.None };
            task = operation.StopTask ??= StopCoreAsync(operation);
        }
        Notify();
        return task;
    }

    private async Task StopCoreAsync(Operation operation)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        operation.Cancel();
        try { await operation.Lifetime!.WaitAsync(_shutdownTimeout).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_current, operation))
                    _snapshot = _snapshot with { State = GenesisRelayActivationState.Failed, Error = GenesisRelayActivationError.ShutdownFailed };
            }
            Notify();
        }
    }

    private async Task RunAsync(Operation operation, GenesisChannelItem channel)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        var error = GenesisRelayActivationError.None;
        Task? run = null;
        try
        {
            var owner = await _prepare(channel, operation.Cancellation.Token).ConfigureAwait(false);
            lock (_gate)
            {
                operation.Owner = owner;
                if (operation.StopRequested || _closed) owner.Retire();
                operation.Cancellation.Token.ThrowIfCancellationRequested();
                if (operation.StopRequested || _closed) throw new OperationCanceledException();
                if (owner.Kind != PeerCast.Nova.Network.Policies.RelayChannelKind.Viewing ||
                    owner.ChannelId != _snapshot.ChannelId || owner.RequestedEndPoint.Port != 0 ||
                    !owner.RequestedEndPoint.Address.Equals(IPAddress.Loopback))
                    throw new InvalidOperationException("The prepared owner does not match the authorized local Viewing plan.");
                _snapshot = _snapshot with { State = GenesisRelayActivationState.Starting, SessionId = owner.SessionId };
                run = owner.RunAsync(operation.Cancellation.Token);
            }
            Notify();
            var runtime = owner.ViewingRuntime ?? throw new InvalidOperationException();
            await owner.PublicReady.WaitAsync(operation.Cancellation.Token).ConfigureAwait(false);
            var readiness = await runtime.WaitForReadinessAsync(operation.Cancellation.Token).ConfigureAwait(false);
            if (readiness != PcpRelayCoordinatorReadinessOutcome.Ready)
            {
                await run.ConfigureAwait(false);
                error = Classify(owner);
            }
            else
            {
                lock (_gate)
                {
                    if (!operation.StopRequested)
                        _snapshot = _snapshot with { ListenerBound = true, EndPoint = owner.LocalEndPoint.ToString() };
                }
                Notify();
                var negotiated = await Task.WhenAny(runtime.UpstreamNegotiation, run, owner.Retirement)
                    .WaitAsync(_readinessTimeout, operation.Cancellation.Token).ConfigureAwait(false);
                if (owner.IsRetired && runtime.Completion?.IsCompleted != true)
                    error = GenesisRelayActivationError.OwnerSuperseded;
                else if (ReferenceEquals(negotiated, run) || runtime.Completion?.IsCompleted == true)
                    error = Classify(owner);
                else if ((await runtime.UpstreamNegotiation.ConfigureAwait(false)).Outcome != PcpChannelSessionNegotiationOutcome.Accepted)
                    error = GenesisRelayActivationError.NegotiationFailed;
                else
                {
                    lock (_gate)
                    {
                        if (!operation.StopRequested) _snapshot = _snapshot with { UpstreamReady = true };
                    }
                    Notify();
                    var mediaReady = runtime.SmartRelayEnabled ? runtime.FirstSmartMediaProgress : runtime.FirstPacketAccepted;
                    var first = await Task.WhenAny(mediaReady, run, owner.Retirement)
                        .WaitAsync(_readinessTimeout, operation.Cancellation.Token).ConfigureAwait(false);
                    if (owner.IsRetired && runtime.Completion?.IsCompleted != true)
                        error = GenesisRelayActivationError.OwnerSuperseded;
                    else if (ReferenceEquals(first, run) || runtime.Completion?.IsCompleted == true)
                        error = Classify(owner);
                    else
                    {
                        await mediaReady.ConfigureAwait(false);
                        lock (_gate)
                        {
                            if (!operation.StopRequested && !_closed && !operation.Cancellation.IsCancellationRequested && !owner.IsRetired)
                            {
                                _snapshot = _snapshot with { State = GenesisRelayActivationState.Running, ReceivingReady = true, Error = GenesisRelayActivationError.None };
                                operation.Started.TrySetResult(true);
                            }
                        }
                        Notify();
                        await Task.WhenAny(run, owner.Retirement).ConfigureAwait(false);
                        error = owner.IsRetired && runtime.Completion?.IsCompleted != true ? GenesisRelayActivationError.OwnerSuperseded : Classify(owner);
                    }
                }
            }
        }
        catch (GenesisPublicRelayStartException) { error = GenesisRelayActivationError.BindFailed; }
        catch (OperationCanceledException)
        {
            error = operation.Owner?.IsRetired == true && !operation.StopRequested
            ? GenesisRelayActivationError.OwnerSuperseded : GenesisRelayActivationError.Canceled;
        }
        catch (TimeoutException) { error = operation.Owner?.UpstreamStatus?.Fault is not null ? Classify(operation.Owner) : GenesisRelayActivationError.ReadinessTimedOut; }
        catch (SocketException) { error = operation.Owner is null ? GenesisRelayActivationError.BindFailed : Classify(operation.Owner); }
        catch (Exception) { error = operation.Owner?.UpstreamStatus?.Fault is not null ? Classify(operation.Owner) : GenesisRelayActivationError.StartFailed; }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_current, operation))
                    _snapshot = _snapshot with { State = GenesisRelayActivationState.Stopping, Error = error };
            }
            operation.Started.TrySetResult(false);
            Notify();
            var clean = true;
            if (operation.Owner is { } owner)
            {
                owner.Retire();
                try
                {
                    await owner.DisposeAsync().ConfigureAwait(false);
                    if (run is not null) await run.ConfigureAwait(false);
                    clean = owner.CleanupConfirmed;
                }
                catch (Exception) { clean = false; }
            }
            lock (_gate)
            {
                // Late terminal callbacks can only update their own generation.
                if (ReferenceEquals(_current, operation))
                {
                    if (clean) _current = null;
                    _snapshot = _snapshot with
                    {
                        State = clean && operation.StopRequested ? GenesisRelayActivationState.Stopped : GenesisRelayActivationState.Failed,
                        Error = !clean ? GenesisRelayActivationError.ShutdownFailed : operation.StopRequested ? GenesisRelayActivationError.None : error,
                        CleanupConfirmed = clean,
                        ListenerBound = !clean && _snapshot.ListenerBound,
                        ReceivingReady = false,
                        UpstreamReady = false,
                        EndPoint = clean ? null : _snapshot.EndPoint,
                    };
                }
            }
            operation.Started.TrySetResult(false);
            Notify();
            operation.DisposeCancellation();
        }
    }

    private static GenesisRelayActivationError Classify(GenesisGuiNextPublicChannelComposition.Owner owner)
    {
        if (owner.UpstreamStatus?.Fault?.Stage == ChannelFaultStage.Negotiating)
            return GenesisRelayActivationError.NegotiationFailed;
        if (owner.UpstreamStatus?.Fault?.Stage == ChannelFaultStage.Connecting)
            return GenesisRelayActivationError.UpstreamFailed;
        var result = owner.ViewingRuntime?.Completion;
        return result?.IsCompletedSuccessfully == true ? result.Result.EndReason switch
        {
            PcpRelayCoordinatorEndReason.DownstreamFailed => GenesisRelayActivationError.BindFailed,
            PcpRelayCoordinatorEndReason.CallerCanceled => GenesisRelayActivationError.Canceled,
            PcpRelayCoordinatorEndReason.UpstreamEnded or PcpRelayCoordinatorEndReason.UpstreamFailed => GenesisRelayActivationError.UpstreamFailed,
            _ => GenesisRelayActivationError.StartFailed,
        } : GenesisRelayActivationError.StartFailed;
    }

    // Subscribers post presentation work to the native GUI thread; no UI work
    // or externally supplied callback runs under the lifecycle lock.
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _closed = true;
            if (_current is { } operation)
            {
                operation.StopRequested = true;
                operation.Owner?.Retire();
            }
            return new(_disposeTask ??= CloseAsync());
        }
    }

    private async Task CloseAsync()
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await StopAsync().ConfigureAwait(false);
    }

    private sealed class Operation(long generation, CancellationToken caller)
    {
        private readonly object _cancellationGate = new();
        private bool _cancellationDisposed;
        public long Generation { get; } = generation;
        public CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(caller);
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public GenesisGuiNextPublicChannelComposition.Owner? Owner { get; set; }
        public bool StopRequested { get; set; }
        public Task? Lifetime { get; set; }
        public Task? StopTask { get; set; }
        public void Cancel()
        {
            lock (_cancellationGate) { if (!_cancellationDisposed) Cancellation.Cancel(); }
        }
        public void DisposeCancellation()
        {
            lock (_cancellationGate)
            {
                _cancellationDisposed = true;
                Cancellation.Dispose();
            }
        }
    }
}
