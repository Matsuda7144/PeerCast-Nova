namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Owns one consumer attachment to a shared relay coordinator. The fan-out
/// child is detached before the coordinator's consumer lease is released.
/// </summary>
public sealed class PcpRelayConsumerRegistration : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly PcpRelayChildRegistration _child;
    private readonly IAsyncDisposable? _ownerLease;
    private Task? _disposeTask;

    internal PcpRelayConsumerRegistration(
        PcpRelayChildRegistration child,
        IAsyncDisposable? ownerLease)
    {
        ArgumentNullException.ThrowIfNull(child);
        _child = child;
        _ownerLease = ownerLease;
        _ = ObserveChildCompletionAsync();
    }

    public PcpRelayChildStatusSnapshot Snapshot => _child.Snapshot;

    public Task Completion => _child.Completion;

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        List<Exception>? failures = null;
        try
        {
            await _child.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        if (_ownerLease is not null)
        {
            try
            {
                await _ownerLease.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is { Count: 1 })
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(failures[0])
                .Throw();
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException(
                "The relay consumer failed to release its attachment.",
                failures);
        }
    }

    private async Task ObserveChildCompletionAsync()
    {
        try
        {
            await _child.Completion.ConfigureAwait(false);
        }
        catch
        {
            // The owner lease must still be released after a child failure.
        }

        try
        {
            await DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // The caller observes cleanup failures when it explicitly disposes.
        }
    }
}
