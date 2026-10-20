using PeerCast.Nova.Foundation.Configuration;
using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.App;

internal interface INovaApplicationNetworkOwner : IAsyncDisposable
{
    Task<NovaApplicationNetworkInitializationResult> InitializeAsync();
    Task<NovaApplicationNetworkPersistenceResult> SetAutoPortMappingAsync(bool enabled);
    Task<NovaPublicRelayAcquireResult> AcquirePublicRelayAsync(NovaPublicRelayRequest request, CancellationToken cancellationToken = default);
    NovaApplicationNetworkSnapshot ReadSnapshot();
}

// An ownership seam, not another NAT implementation. Only the application
// factory sees this contract; consumers of the owner cannot mutate the manager.
internal interface INovaApplicationNatRuntime : IAsyncDisposable
{
    NatPortMappingManager Manager { get; }
    NatShutdownOutcome? ShutdownOutcome { get; }
}

internal sealed class NovaApplicationNatRuntime(NatPortMappingRuntime runtime) : INovaApplicationNatRuntime
{
    public NatPortMappingManager Manager => runtime.Manager;
    public NatShutdownOutcome? ShutdownOutcome => runtime.ShutdownOutcome;
    public ValueTask DisposeAsync() => runtime.DisposeAsync();
}

internal interface INovaApplicationPortMappingControl : IDisposable
{
    Task SetEnabledAsync(bool enabled);
}

internal sealed class NovaApplicationPortMappingControl(NovaPortMappingControl control) : INovaApplicationPortMappingControl
{
    public Task SetEnabledAsync(bool enabled) => control.SetEnabledAsync(enabled);
    public void Dispose() => control.Dispose();
}

/// <summary>
/// Application lifetime for NAT desired state and field-only persistence.
/// Construction and reads are inert. Public relay acquisition is explicit and
/// separate from NAT desired state; listener details stay behind a driver.
/// </summary>
internal sealed class NovaApplicationNetworkOwner : INovaApplicationNetworkOwner
{
    private readonly object gate = new();
    private readonly NovaSettingsStore store;
    private readonly Func<string?> readEnvironment;
    private readonly Func<NatPortMappingOptions, INovaApplicationNatRuntime> createRuntime;
    private readonly Func<NatPortMappingManager, NovaSettingsStore, NovaOptions, bool, INovaApplicationPortMappingControl> createControl;
    private readonly Func<NovaBorrowedNatManager, INovaPublicRelayDriver> createPublicRelayDriver;
    private readonly Guid runtimeOwnerId = Guid.NewGuid();
    private readonly Dictionary<long, Task<NovaApplicationNetworkPersistenceResult>> saves = [];
    private Task<NovaApplicationNetworkInitializationResult>? initialization;
    private Task? disposal;
    private INovaApplicationNatRuntime? runtime;
    private INovaApplicationPortMappingControl? control;
    private NovaSharedPublicRelayOwner? publicRelay;
    private Task? publicRelayShutdown;
    private bool initialized;
    private bool initializing;
    private bool disposing;
    private bool disposed;
    private long revision;
    private long lastAcceptedRequestId;
    private long lastPersistedRequestId;
    private bool? savedEnabled;
    private bool? environmentEnabled;
    private NovaApplicationNetworkPersistenceResult? lastPersistence;
    private NovaApplicationNetworkFailure initializationFailure;
    private NovaApplicationNetworkFailure shutdownFailure;
    private NatShutdownOutcome? shutdownOutcome;

    public NovaApplicationNetworkOwner(
        NovaSettingsStore store,
        Func<string?>? readEnvironment = null,
        Func<NatPortMappingOptions, INovaApplicationNatRuntime>? createRuntime = null,
        Func<NatPortMappingManager, NovaSettingsStore, NovaOptions, bool, INovaApplicationPortMappingControl>? createControl = null,
        Func<NovaBorrowedNatManager, INovaPublicRelayDriver>? createPublicRelayDriver = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
        this.readEnvironment = readEnvironment ??
            (static () => Environment.GetEnvironmentVariable("NOVA_AUTO_PORT_MAPPING"));
        this.createRuntime = createRuntime ??
            (static options => new NovaApplicationNatRuntime(new NatPortMappingRuntime(options)));
        this.createControl = createControl ??
            (static (manager, settings, saved, overridden) => new NovaApplicationPortMappingControl(
                new NovaPortMappingControl(manager, settings, saved, overridden)));
        this.createPublicRelayDriver = createPublicRelayDriver ?? (static borrowed => new NovaBorrowedPublicRelayDriver(borrowed));
    }

    public Task<NovaPublicRelayAcquireResult> AcquirePublicRelayAsync(NovaPublicRelayRequest request, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (disposing || disposed || publicRelay is null)
                return Task.FromResult(new NovaPublicRelayAcquireResult(null,
                    disposing || disposed ? NovaPublicRelayFailure.Closing : NovaPublicRelayFailure.NotInitialized));
            return publicRelay.AcquireAsync(request, cancellationToken);
        }
    }

    public Task<NovaApplicationNetworkInitializationResult> InitializeAsync()
    {
        lock (gate)
        {
            if (disposing || disposed)
                return Task.FromResult(new NovaApplicationNetworkInitializationResult(false, NovaApplicationNetworkFailure.Closing));
            if (initialization is not null)
                return initialization;

            initializing = true;
            revision++;
            initialization = Task.Run(InitializeCoreAsync);
            return initialization;
        }
    }

    public Task<NovaApplicationNetworkPersistenceResult> SetAutoPortMappingAsync(bool enabled)
    {
        lock (gate)
        {
            if (disposing || disposed || !initialized)
                return Task.FromResult(new NovaApplicationNetworkPersistenceResult(0, enabled,
                    NovaApplicationNetworkPersistenceStatus.Rejected,
                    disposing || disposed ? NovaApplicationNetworkFailure.Closing : NovaApplicationNetworkFailure.NotInitialized));

            var requestId = ++lastAcceptedRequestId;
            var completion = new TaskCompletionSource<NovaApplicationNetworkPersistenceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            saves.Add(requestId, completion.Task);
            lastPersistence = new(requestId, enabled, NovaApplicationNetworkPersistenceStatus.Pending, NovaApplicationNetworkFailure.None);
            // Serialize calls (including the synchronous desired-state portion)
            // under the admission gate. The existing control serializes writes.
            Task save;
            try { save = control!.SetEnabledAsync(enabled); }
            catch (Exception exception) { save = Task.FromException(exception); }
            revision++;
            _ = CompleteSaveAsync(save, requestId, enabled, completion);
            return completion.Task;
        }
    }

    public NovaApplicationNetworkSnapshot ReadSnapshot()
    {
        lock (gate)
        {
            var nat = runtime is null ? null : NatPortMappingStatusProjection.Create(runtime.Manager.ReadState());
            // Projection arrays must not let a snapshot consumer mutate its DTO.
            if (nat is not null)
                nat = nat with
                {
                    RegisteredInternalPorts = Array.AsReadOnly(nat.RegisteredInternalPorts.ToArray()),
                    Diagnostics = Array.AsReadOnly(nat.Diagnostics.ToArray()),
                };
            var relay = publicRelay?.ReadSnapshot();
            var availability = relay?.State switch
            {
                NovaPublicRelayState.Starting => NovaApplicationPublicListenerAvailability.Starting,
                NovaPublicRelayState.Listening => NovaApplicationPublicListenerAvailability.Listening,
                NovaPublicRelayState.Stopping => NovaApplicationPublicListenerAvailability.Stopping,
                NovaPublicRelayState.Failed => NovaApplicationPublicListenerAvailability.Failed,
                _ => NovaApplicationPublicListenerAvailability.Unavailable,
            };
            return new(initialized, initializing, disposing, disposed, revision,
                nat?.Enabled, savedEnabled, environmentEnabled.HasValue, environmentEnabled,
                environmentEnabled ?? savedEnabled, saves.Count, lastAcceptedRequestId,
                lastPersistedRequestId, lastPersistence, initializationFailure, shutdownFailure,
                shutdownOutcome, nat, availability)
            { PublicRelay = relay };
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposal is null)
            {
                // Close public lease admission before publishing app closure.
                publicRelayShutdown = publicRelay?.DisposeAsync().AsTask();
                disposing = true;
                revision++;
                disposal = Task.Run(DisposeCoreAsync);
            }
            return new ValueTask(disposal);
        }
    }

    private async Task<NovaApplicationNetworkInitializationResult> InitializeCoreAsync()
    {
        INovaApplicationNatRuntime? pendingRuntime = null;
        INovaApplicationPortMappingControl? pendingControl = null;
        var failure = NovaApplicationNetworkFailure.SettingsLoadFailed;
        try
        {
            var saved = store.Load();
            failure = NovaApplicationNetworkFailure.InvalidEnvironment;
            var environment = readEnvironment();
            var options = SystemNovaDailyRelayControllerFactory.ReadPortMappingSetting(environment, saved);
            var overridden = !string.IsNullOrWhiteSpace(environment);
            lock (gate)
            {
                if (disposing)
                {
                    failure = NovaApplicationNetworkFailure.Closing;
                    return new(false, NovaApplicationNetworkFailure.Closing);
                }
            }

            failure = NovaApplicationNetworkFailure.RuntimeInitializationFailed;
            pendingRuntime = createRuntime(options) ?? throw new InvalidOperationException("NAT runtime factory returned no owner.");
            failure = NovaApplicationNetworkFailure.ControlInitializationFailed;
            pendingControl = createControl(pendingRuntime.Manager, store, saved, overridden) ??
                throw new InvalidOperationException("NAT control factory returned no control.");
            lock (gate)
            {
                if (!disposing)
                {
                    runtime = pendingRuntime;
                    control = pendingControl;
                    publicRelay = new NovaSharedPublicRelayOwner(
                        new NovaBorrowedNatManager(runtimeOwnerId, pendingRuntime.Manager), createPublicRelayDriver);
                    savedEnabled = saved.AutoPortMapping;
                    environmentEnabled = overridden ? options.Enabled : null;
                    initialized = true;
                    pendingRuntime = null;
                    pendingControl = null;
                    return new(true, NovaApplicationNetworkFailure.None);
                }
            }
            failure = NovaApplicationNetworkFailure.Closing;
        }
        catch (Exception)
        {
            // The stage is the safe classification. Do not expose raw exceptions,
            // environment text, settings paths or backend details to consumers.
        }
        finally
        {
            try
            {
                if (pendingRuntime is not null)
                {
                    await pendingRuntime.DisposeAsync().ConfigureAwait(false);
                    lock (gate) { shutdownOutcome = pendingRuntime.ShutdownOutcome; }
                }
            }
            catch (Exception)
            {
                failure = NovaApplicationNetworkFailure.InitializationCleanupFailed;
                lock (gate) { shutdownFailure = NovaApplicationNetworkFailure.ShutdownFailed; }
            }
            finally
            {
                try { pendingControl?.Dispose(); }
                catch (Exception)
                {
                    failure = NovaApplicationNetworkFailure.InitializationCleanupFailed;
                    lock (gate) { shutdownFailure = NovaApplicationNetworkFailure.ShutdownFailed; }
                }
                lock (gate)
                {
                    initializing = false;
                    if (!initialized && failure != NovaApplicationNetworkFailure.Closing)
                        initializationFailure = failure;
                    revision++;
                }
            }
        }
        return new(false, failure);
    }

    private async Task CompleteSaveAsync(Task save, long requestId, bool enabled,
        TaskCompletionSource<NovaApplicationNetworkPersistenceResult> completion)
    {
        var result = new NovaApplicationNetworkPersistenceResult(requestId, enabled,
            NovaApplicationNetworkPersistenceStatus.Saved, NovaApplicationNetworkFailure.None);
        try { await save.ConfigureAwait(false); }
        catch (Exception)
        {
            result = result with { Status = NovaApplicationNetworkPersistenceStatus.Failed, Failure = NovaApplicationNetworkFailure.PersistenceFailed };
        }
        lock (gate)
        {
            if (result.Status == NovaApplicationNetworkPersistenceStatus.Saved && requestId > lastPersistedRequestId)
            {
                lastPersistedRequestId = requestId;
                savedEnabled = enabled;
            }
            if (lastPersistence is null || requestId >= lastPersistence.RequestId)
                lastPersistence = result;
            saves.Remove(requestId);
            revision++;
            completion.SetResult(result);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Task<NovaApplicationNetworkInitializationResult>? starting;
        lock (gate) { starting = initialization; }
        if (starting is not null)
            await starting.ConfigureAwait(false);

        var borrowerCleanupConfirmed = true;
        try
        {
            if (publicRelayShutdown is not null)
                await publicRelayShutdown.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Do not destroy the borrowed manager while a server/token may
            // still exist. Retention is explicit failure, never clean shutdown.
            borrowerCleanupConfirmed = false;
            lock (gate) { shutdownFailure = NovaApplicationNetworkFailure.ShutdownCleanupUnconfirmed; }
        }

        Task[] pending;
        lock (gate) { pending = saves.Values.Cast<Task>().ToArray(); }
        await Task.WhenAll(pending).ConfigureAwait(false);
        try
        {
            if (runtime is not null && borrowerCleanupConfirmed)
                await runtime.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            lock (gate) { shutdownFailure = NovaApplicationNetworkFailure.ShutdownFailed; }
        }
        finally
        {
            try { control?.Dispose(); }
            catch (Exception)
            {
                lock (gate) { shutdownFailure = NovaApplicationNetworkFailure.ShutdownFailed; }
            }
            lock (gate)
            {
                control = null;
                shutdownOutcome ??= runtime?.ShutdownOutcome;
                if (shutdownOutcome == NatShutdownOutcome.CleanupUnconfirmed && shutdownFailure == NovaApplicationNetworkFailure.None)
                    shutdownFailure = NovaApplicationNetworkFailure.ShutdownCleanupUnconfirmed;
                disposing = false;
                disposed = true;
                revision++;
            }
        }
        if (shutdownFailure != NovaApplicationNetworkFailure.None)
            throw new InvalidOperationException("Application NAT shutdown did not complete successfully.");
    }
}
