using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaObsPublishingLiveMetadataTests
{
    [Fact]
    public async Task SavingPreferencesLiveUpdatesRunningPublishingRuntimeMetadata()
    {
        var store = new InMemoryPublishingPreferencesStore();
        var controller = new PublishingSettingsController(store);
        await controller.LoadAsync();

        var initialRuntime = new LiveMetadataFakeRuntime();
        var activeRuntime = new LiveMetadataFakeRuntime();
        await using var composition = NovaGuiNextPublishingComposition.Create(
            new SequenceRuntimeFactory(initialRuntime, activeRuntime),
            settings: controller);

        // 未開始中は保存してもライブ更新は行われない
        var beforeStartSave = await controller.SaveAsync(new PublishingSettingsInput(
            "初期チャンネル",
            "Game",
            "初期詳細",
            "開始前コメント",
            "https://example.invalid/before",
            "1500"));
        Assert.True(beforeStartSave.Saved);
        Assert.Empty(initialRuntime.UpdatedMetadata);
        Assert.Empty(activeRuntime.UpdatedMetadata);

        // 配信開始後は設定保存と同時に実行中ランタイムのメタデータがライブ更新される
        await composition.RequestStartAsync();
        await Until(() => activeRuntime.Started);

        var liveSave = await controller.SaveAsync(new PublishingSettingsInput(
            "更新後チャンネル",
            "Music",
            "ライブ更新した詳細",
            "放送中に変更したコメント",
            "https://example.invalid/after",
            "2000"));
        Assert.True(liveSave.Saved);

        await Until(() => activeRuntime.UpdatedMetadata.Count == 1);
        var updated = Assert.Single(activeRuntime.UpdatedMetadata);
        Assert.Equal("更新後チャンネル", updated.Name);
        Assert.Equal("Music", updated.Genre);
        Assert.Equal("ライブ更新した詳細", updated.Description);
        Assert.Equal("放送中に変更したコメント", updated.Comment);
        Assert.Equal("https://example.invalid/after", updated.ContactUrl);

        await composition.RequestInterruptAsync();
    }

    [Fact]
    public async Task HostUpdatesTrackerMetadataAndCurrentMetadataDynamically()
    {
        var options = new ObsPublishChannelOptions
        {
            ChannelName = "初期枠",
            Comment = "最初のコメント",
            Description = "最初の詳細",
            Ingest = new() { Port = 0 },
            Http = new() { Port = 0 },
            RelayPort = 0,
        };
        await using var host = new ObsPublishChannelHost(options);
        Assert.Equal("最初のコメント", host.CurrentMetadata.Comment);

        var next = new ChannelMetadataSnapshot(
            name: "枠変更なし",
            genre: "雑談",
            description: "更新後の詳細",
            contactUrl: "https://example.invalid/thread",
            bitrateKbps: 1800,
            streamType: "FLV",
            comment: "最新の配信コメント",
            streamExtension: ".flv");

        // Start 前は更新不可
        Assert.False(host.TryUpdateLiveMetadata(next));

        host.Start();
        await Until(() => host.Snapshot.State == ChannelSessionState.Negotiating);

        Assert.True(host.TryUpdateLiveMetadata(next));
        Assert.Equal("最新の配信コメント", host.CurrentMetadata.Comment);
        Assert.Equal("最新の配信コメント", host.Snapshot.Metadata.Comment);
        Assert.Equal("更新後の詳細", host.Snapshot.Metadata.Description);
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not satisfied within timeout.");
            }

            await Task.Delay(15);
        }
    }

    private sealed class InMemoryPublishingPreferencesStore : IPublishingPreferencesStore
    {
        private PublishingSettingsSnapshot _snapshot = PublishingSettingsSnapshot.Default;

        public ValueTask<PublishingSettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_snapshot);

        public ValueTask SaveAsync(PublishingSettingsSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _snapshot = snapshot;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SequenceRuntimeFactory(params LiveMetadataFakeRuntime[] runtimes)
        : INovaObsPublishingRuntimeFactory
    {
        private int _index;

        public INovaObsPublishingRuntime Create()
        {
            var current = Interlocked.Increment(ref _index) - 1;
            return current < runtimes.Length ? runtimes[current] : new LiveMetadataFakeRuntime();
        }

        public INovaObsPublishingRuntime Create(PublishingSettingsSnapshot settings) => Create();
    }

    private sealed class LiveMetadataFakeRuntime : INovaObsPublishingLifecycleRuntime
    {
        private readonly TaskCompletionSource<ObsPublishChannelResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Guid _channelId = Guid.NewGuid();
        private readonly Guid _sessionId = Guid.NewGuid();
        private bool _started;

        public bool Started => Volatile.Read(ref _started);
        public List<ChannelMetadataSnapshot> UpdatedMetadata { get; } = [];

        public ObsPublishStatusSnapshot Snapshot => new(
            new ChannelId(_channelId),
            new ChannelSessionId(_sessionId),
            1,
            Started ? ChannelSessionState.Connecting : ChannelSessionState.Created,
            Started,
            false,
            false,
            "rtmp://127.0.0.1:1935/live",
            "stream",
            null,
            null,
            null,
            null);

        public Task<ObsPublishChannelResult> Completion => _completion.Task;

        public void Start(CancellationToken cancellationToken)
        {
            Volatile.Write(ref _started, true);
            _ = cancellationToken.Register(() =>
                _completion.TrySetResult(new ObsPublishChannelResult(
                    ObsPublishChannelEndReason.HostCancelled,
                    0,
                    0,
                    0,
                    0,
                    PcpRelayDrainOutcome.NotRequired,
                    false,
                    true,
                    null,
                    null)));
        }

        public bool TryUpdateLiveMetadata(ChannelMetadataSnapshot metadata)
        {
            UpdatedMetadata.Add(metadata);
            return true;
        }

        public ValueTask DisposeAsync()
        {
            _completion.TrySetResult(new ObsPublishChannelResult(
                ObsPublishChannelEndReason.HostCancelled,
                0,
                0,
                0,
                0,
                PcpRelayDrainOutcome.NotRequired,
                false,
                true,
                null,
                null));
            return ValueTask.CompletedTask;
        }
    }
}
