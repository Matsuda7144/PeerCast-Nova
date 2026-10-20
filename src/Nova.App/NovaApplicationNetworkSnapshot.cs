using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.App;

internal enum NovaApplicationNetworkFailure
{
    None,
    SettingsLoadFailed,
    InvalidEnvironment,
    RuntimeInitializationFailed,
    ControlInitializationFailed,
    InitializationCleanupFailed,
    NotInitialized,
    Closing,
    PersistenceFailed,
    ShutdownFailed,
    ShutdownCleanupUnconfirmed,
}

internal enum NovaApplicationNetworkPersistenceStatus
{
    Pending,
    Saved,
    Failed,
    Rejected,
}

internal enum NovaApplicationPublicListenerAvailability
{
    Unavailable,
}

internal sealed record NovaApplicationNetworkInitializationResult(
    bool Initialized,
    NovaApplicationNetworkFailure Failure);

internal sealed record NovaApplicationNetworkPersistenceResult(
    long RequestId,
    bool RequestedEnabled,
    NovaApplicationNetworkPersistenceStatus Status,
    NovaApplicationNetworkFailure Failure);

/// <summary>
/// Revision orders owner transitions, not the independent NAT pump. CapturedAt
/// in the NAT projection records a read, not proof of runtime health or WAN access.
/// Saved values describe successful owner loads/writes, not external file changes.
/// </summary>
internal sealed record NovaApplicationNetworkSnapshot(
    bool Initialized,
    bool Initializing,
    bool Disposing,
    bool Disposed,
    long Revision,
    bool? RuntimeDesiredEnabled,
    bool? SavedAutoPortMapping,
    bool StartupEnvironmentOverride,
    bool? StartupEnvironmentEnabled,
    bool? NextStartEffectiveEnabled,
    int PendingSaveCount,
    long LastAcceptedRequestId,
    long LastPersistedRequestId,
    NovaApplicationNetworkPersistenceResult? LastPersistenceResult,
    NovaApplicationNetworkFailure InitializationFailure,
    NovaApplicationNetworkFailure ShutdownFailure,
    NatShutdownOutcome? ShutdownOutcome,
    NatPortMappingDisplay? Nat,
    NovaApplicationPublicListenerAvailability PublicListenerAvailability)
{
    public NovaApplicationNetworkFailure SafeFailure =>
        ShutdownFailure != NovaApplicationNetworkFailure.None ? ShutdownFailure :
        InitializationFailure != NovaApplicationNetworkFailure.None ? InitializationFailure :
        LastPersistenceResult?.Failure ?? NovaApplicationNetworkFailure.None;
}
