namespace PeerCast.Nova.App;

internal enum GenesisNetworkSettingsWindowFailure
{
    CreationFailed,
    CleanupFailed,
    OwnerThreadUnavailable,
}

internal sealed class GenesisNetworkSettingsWindowCleanupException : InvalidOperationException
{
    public GenesisNetworkSettingsWindowCleanupException(IAsyncDisposable retainedResource)
        : base("Network settings native cleanup could not be confirmed.") =>
        RetainedResource = retainedResource;

    public IAsyncDisposable RetainedResource { get; }
}

/// <summary>Only class registration transfers the background brush to Windows.</summary>
internal sealed class GenesisNetworkSettingsWindowClassLifetime : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Func<bool> register;
    private readonly Func<bool> unregister;
    private readonly Func<bool> deleteCallerBrush;
    private bool registrationAttempted;
    private bool registered;
    private bool releaseAttempted;
    private bool released;
    private Task? disposal;

    public GenesisNetworkSettingsWindowClassLifetime(
        Func<bool> register, Func<bool> unregister, Func<bool> deleteCallerBrush)
    {
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(unregister);
        ArgumentNullException.ThrowIfNull(deleteCallerBrush);
        this.register = register;
        this.unregister = unregister;
        this.deleteCallerBrush = deleteCallerBrush;
    }

    public bool TryRegister()
    {
        lock (gate)
        {
            if (registrationAttempted || releaseAttempted)
                throw new InvalidOperationException("Network settings class registration is already closed.");
            registrationAttempted = true;
            return registered = register();
        }
    }

    public bool TryRelease()
    {
        lock (gate)
        {
            if (releaseAttempted) return released;
            releaseAttempted = true;
            // UnregisterClass deletes the class background brush itself. Failed
            // unregister leaves Windows ownership intact; never delete it here.
            return released = registered ? unregister() : deleteCallerBrush();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            disposal ??= TryRelease() ? Task.CompletedTask :
                Task.FromException(new GenesisNetworkSettingsWindowCleanupException(this));
            return new(disposal);
        }
    }
}

internal static class GenesisNetworkSettingsWindowInitialization
{
    public static bool ShouldCloseNative(bool closed, object? active, object expected, Func<bool> isWindow)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(isWindow);
        return !closed && ReferenceEquals(active, expected) && isWindow();
    }

    public static bool TryInitialize(
        Func<bool> createControls, Action render, Func<bool> startTimer, Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(createControls);
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(startTimer);
        ArgumentNullException.ThrowIfNull(cleanup);
        try
        {
            if (createControls())
            {
                render();
                if (startTimer()) return true;
            }
        }
        catch
        {
            cleanup();
            throw;
        }
        cleanup();
        return false;
    }
}
