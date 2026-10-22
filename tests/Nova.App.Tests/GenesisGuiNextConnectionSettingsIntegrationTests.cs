namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextConnectionSettingsIntegrationTests
{
    [Fact]
    public async Task SavingNeverInvokesRuntimeRouteAndRepeatedNavigationKeepsEditor()
    {
        await using var settings = CreateSettings();
        var window = new Window();
        var creates = 0;
        var runtime = 0;
        await using var host = new GenesisGuiNextConnectionSettingsWindowHost(0, settings,
            _ => { runtime++; return ValueTask.CompletedTask; },
            () => { creates++; return window; });
        await host.ShowAsync();
        await host.ShowAsync();
        Assert.Equal(1, creates);
        Assert.Equal(1, window.Activations);
        Assert.True((await settings.SaveAsync(GenesisGuiNextConnectionInput.From(
            settings.Startup with { ListenPort = 17144, UseIpv6 = true }))).Saved);
        Assert.Equal(0, runtime);
        Assert.Equal(7144, settings.Startup.ListenPort);
        Assert.False(settings.Startup.UseIpv6);
        await host.ShowRuntimeStatusAsync();
        Assert.Equal(1, runtime);
        Assert.Equal(17144, settings.Saved.ListenPort);
    }

    [Fact]
    public async Task ConnectionAndExistingNetworkMenuRoutesStayIndependent()
    {
        var settings = new GenesisChannelNotificationSettings(GenesisChannelNotificationPreferences.Default, new SilentSound());
        var initial = settings.Current;
        var connection = 0;
        var runtime = 0;
        ValueTask Connection(CancellationToken _) { connection++; return ValueTask.CompletedTask; }
        ValueTask Runtime(CancellationToken _) { runtime++; return ValueTask.CompletedTask; }
        await GenesisChannelNotificationSettingsMenuInteraction.ApplyAsync(settings,
            GenesisChannelNotificationSettingsCommand.Connection, null, Runtime, null, null,
            showConnectionAsync: Connection);
        Assert.Equal(1, connection);
        Assert.Equal(0, runtime);
        await GenesisChannelNotificationSettingsMenuInteraction.ApplyAsync(settings,
            GenesisChannelNotificationSettingsCommand.NetworkRelay, null, Runtime, null, null,
            showConnectionAsync: Connection);
        Assert.Equal(1, connection);
        Assert.Equal(1, runtime);
        Assert.Equal(initial, settings.Current);
    }

    [Fact]
    public async Task ClosedEditorIsCleanedBeforeReopenAndDisposalIsShared()
    {
        await using var settings = CreateSettings();
        var first = new Window();
        var second = new Window();
        var creates = 0;
        var host = new GenesisGuiNextConnectionSettingsWindowHost(0, settings,
            _ => ValueTask.CompletedTask, () => ++creates == 1 ? first : second);
        await host.ShowAsync();
        first.IsOpen = false;
        await host.ShowAsync();
        Assert.Equal(1, first.Disposals);
        Assert.Equal(2, creates);
        var close = host.DisposeAsync().AsTask();
        Assert.Same(close, host.DisposeAsync().AsTask());
        await close;
        Assert.Equal(1, second.Disposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => host.ShowAsync().AsTask());
    }

    [Fact]
    public async Task CleanupFailureRetainsResourceAndNeverRecreates()
    {
        await using var settings = CreateSettings();
        var window = new Window { FailDispose = true };
        var failures = new List<GenesisNetworkSettingsWindowFailure>();
        var creates = 0;
        var host = new GenesisGuiNextConnectionSettingsWindowHost(0, settings,
            _ => ValueTask.CompletedTask, () => { creates++; return window; }, failures.Add);
        await host.ShowAsync();
        window.IsOpen = false;
        await host.ShowAsync();
        await host.ShowAsync();
        Assert.Equal(1, creates);
        Assert.Equal(1, window.Disposals);
        Assert.All(failures, failure => Assert.Equal(GenesisNetworkSettingsWindowFailure.CleanupFailed, failure));
        var first = host.DisposeAsync().AsTask();
        Assert.Same(first, host.DisposeAsync().AsTask());
        await Assert.ThrowsAsync<GenesisNetworkSettingsWindowCleanupException>(() => first);
    }

    [Fact]
    public async Task CreationFailureRetainsNativeCleanupResource()
    {
        await using var settings = CreateSettings();
        var retained = new Window();
        var failures = new List<GenesisNetworkSettingsWindowFailure>();
        var host = new GenesisGuiNextConnectionSettingsWindowHost(0, settings,
            _ => ValueTask.CompletedTask,
            () => throw new GenesisNetworkSettingsWindowCleanupException(retained), failures.Add);
        await host.ShowAsync();
        Assert.Equal([GenesisNetworkSettingsWindowFailure.CleanupFailed], failures);
        await host.DisposeAsync();
        Assert.Equal(1, retained.Disposals);
    }

    [Fact]
    public async Task RuntimeFeedbackFailureCannotFaultSavedSettingsOrInvokeAfterShutdown()
    {
        await using var settings = CreateSettings();
        var calls = 0;
        var host = new GenesisGuiNextConnectionSettingsWindowHost(0, settings,
            _ => { calls++; throw new IOException(); }, feedback: _ => throw new IOException());
        await host.ShowRuntimeStatusAsync();
        Assert.Equal(1, calls);
        Assert.Equal(settings.Startup, settings.Saved);
        await host.DisposeAsync();
        await host.ShowRuntimeStatusAsync();
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ReentrantShutdownStillOwnsLateCreatedEditor()
    {
        await using var settings = CreateSettings();
        var window = new Window();
        GenesisGuiNextConnectionSettingsWindowHost? host = null;
        host = new(0, settings, _ => ValueTask.CompletedTask, () =>
        {
            host!.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return window;
        });
        await host.ShowAsync();
        Assert.Equal(1, window.Disposals);
        await host.DisposeAsync();
        Assert.Equal(1, window.Disposals);
    }

    private static GenesisGuiNextConnectionSettings CreateSettings() =>
        new(GenesisGuiNextConnectionPreferences.Default, DefaultGenesisGuiNextConnectionPreferencesStore.Instance);

    private sealed class Window : IGenesisGuiNextConnectionSettingsWindow
    {
        private Task? disposal;
        public bool IsOpen { get; set; } = true;
        public bool FailDispose { get; init; }
        public int Disposals { get; private set; }
        public int Activations { get; private set; }
        public void Activate() => Activations++;
        public ValueTask DisposeAsync()
        {
            if (disposal is null)
            {
                Disposals++;
                disposal = FailDispose ? Task.FromException(new GenesisNetworkSettingsWindowCleanupException(this)) : Task.CompletedTask;
                if (!FailDispose) IsOpen = false;
            }
            return new(disposal);
        }
    }

    private sealed class SilentSound : IGenesisSystemNotificationSoundPlayer
    {
        public ValueTask PlayAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
