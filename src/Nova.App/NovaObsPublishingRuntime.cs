using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.App;

internal interface INovaObsPublishingStatusSource
{
    ObsPublishStatusSnapshot Snapshot { get; }
}

internal interface INovaObsPublishingRuntime : INovaObsPublishingStatusSource, IAsyncDisposable
{
    void Start(CancellationToken cancellationToken);
}

internal interface INovaObsPublishingLifecycleRuntime : INovaObsPublishingRuntime
{
    Task<ObsPublishChannelResult> Completion { get; }
}

internal interface INovaObsPublishingRuntimeFactory
{
    INovaObsPublishingRuntime Create();
}

/// <summary>Application-owned resources, outside the observational projection/window.</summary>
internal sealed class SystemNovaObsPublishingRuntimeFactory : INovaObsPublishingRuntimeFactory
{
    public static SystemNovaObsPublishingRuntimeFactory Instance { get; } = new();

    private SystemNovaObsPublishingRuntimeFactory() { }

    public INovaObsPublishingRuntime Create() => new NovaObsPublishingRuntime(new ObsPublishChannelOptions());
}

internal sealed class NovaObsPublishingRuntime : INovaObsPublishingLifecycleRuntime
{
    private readonly ObsPublishChannelHost _host;

    public NovaObsPublishingRuntime(ObsPublishChannelOptions options) => _host = new(options);

    public ObsPublishStatusSnapshot Snapshot => _host.PublishingStatus;

    public void Start(CancellationToken cancellationToken) => _host.Start(cancellationToken);

    public Task<ObsPublishChannelResult> Completion => _host.Completion;

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}
