using System.Net.NetworkInformation;
using System.Threading.Channels;

namespace PeerCast.Nova.Network.Publishing;

public interface IObsPublishInterfaceChanges { IDisposable Subscribe(Action changed); }
public sealed class SystemObsPublishInterfaceChanges : IObsPublishInterfaceChanges
{
    public static SystemObsPublishInterfaceChanges Instance { get; } = new();
    public IDisposable Subscribe(Action changed) => new Subscription(changed);
    private sealed class Subscription : IDisposable
    {
        private readonly NetworkAddressChangedEventHandler _address;
        private readonly NetworkAvailabilityChangedEventHandler _availability;
        private int _disposed;
        internal Subscription(Action changed)
        {
            _address = (_, _) => changed(); _availability = (_, _) => changed();
            NetworkChange.NetworkAddressChanged += _address;
            try { NetworkChange.NetworkAvailabilityChanged += _availability; }
            catch { NetworkChange.NetworkAddressChanged -= _address; throw; }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            NetworkChange.NetworkAddressChanged -= _address;
            NetworkChange.NetworkAvailabilityChanged -= _availability;
        }
    }
}

/// <summary>One coalesced NIC revalidation worker, not an external reachability probe.</summary>
internal sealed class ObsPublishWanNetworkLifetime : IAsyncDisposable
{
    private readonly Channel<bool> _changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly CancellationTokenSource _stop = new();
    private readonly IDisposable _subscription;
    private readonly Task _worker;
    private readonly TaskCompletionSource _invalidated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private Task? _dispose;
    internal Task Invalidated => _invalidated.Task;
    internal ObsPublishWanNetworkLifetime(ObsPublishWanBinding binding, IObsPublishLanInterfaces interfaces, IObsPublishInterfaceChanges changes)
    {
        _subscription = changes.Subscribe(() => _changes.Writer.TryWrite(true));
        _worker = Task.Run(async () =>
        {
            try
            {
                while (await _changes.Reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
                {
                    _changes.Reader.TryRead(out _);
                    _stop.Token.ThrowIfCancellationRequested();
                    if (!binding.StillMatches(interfaces)) { _invalidated.TrySetResult(); return; }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        });
        _changes.Writer.TryWrite(true); // Close the subscribe/bind race with an initial revalidation.
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_dispose ??= Task.Run(async () =>
        {
            try { _subscription.Dispose(); }
            finally { await _stop.CancelAsync().ConfigureAwait(false); }
            await _worker.ConfigureAwait(false);
            _stop.Dispose();
        }));
    }
}
