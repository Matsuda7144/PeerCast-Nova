namespace PeerCast.Nova.App.Tests;

public sealed class GenesisNetworkSettingsWindowLifetimeTests
{
    [Fact]
    public async Task RegistrationFailureDeletesOnlyCallerOwnedBrushOnce()
    {
        var native = new NativeClass { RegisterResult = false };
        var lifetime = native.CreateLifetime();
        Assert.False(lifetime.TryRegister());
        await lifetime.DisposeAsync();
        await lifetime.DisposeAsync();
        Assert.Equal(1, native.CallerDeletes);
        Assert.Equal(0, native.Unregisters);
        Assert.Equal(0, native.SystemDeletes);
    }

    [Fact]
    public async Task SuccessfulUnregisterDeletesClassBrushInSystemOnly()
    {
        var native = new NativeClass();
        var lifetime = native.CreateLifetime();
        Assert.True(lifetime.TryRegister());
        Assert.True(native.ClassOwnsBrush);
        Assert.Equal(0, native.CallerDeletes);
        await lifetime.DisposeAsync();
        Assert.False(native.ClassOwnsBrush);
        Assert.Equal(1, native.SystemDeletes);
        Assert.Equal(0, native.CallerDeletes);
        Assert.True(lifetime.TryRelease());
        await lifetime.DisposeAsync();
        Assert.Equal(1, native.Unregisters);
    }

    [Fact]
    public async Task UnregisterFailureRetainsOwnershipAndCachedFailure()
    {
        var native = new NativeClass { UnregisterResult = false };
        var lifetime = native.CreateLifetime();
        Assert.True(lifetime.TryRegister());
        var first = lifetime.DisposeAsync().AsTask();
        var second = lifetime.DisposeAsync().AsTask();
        Assert.Same(first, second);
        var failure = await Assert.ThrowsAsync<GenesisNetworkSettingsWindowCleanupException>(() => first);
        Assert.Same(lifetime, failure.RetainedResource);
        Assert.True(native.ClassOwnsBrush);
        Assert.Equal(1, native.Unregisters);
        Assert.Equal(0, native.CallerDeletes);
        Assert.Equal(0, native.SystemDeletes);
        Assert.False(lifetime.TryRelease());
    }

    [Fact]
    public async Task CallerBrushDeletionFailureIsNotHidden()
    {
        var native = new NativeClass { RegisterResult = false, DeleteResult = false };
        var lifetime = native.CreateLifetime();
        Assert.False(lifetime.TryRegister());
        await Assert.ThrowsAsync<GenesisNetworkSettingsWindowCleanupException>(() => lifetime.DisposeAsync().AsTask());
        Assert.Equal(1, native.CallerDeletes);
        Assert.Equal(0, native.Unregisters);
    }

    [Fact]
    public async Task WindowCreationFailureReleasesRegisteredClassWithoutCallerDelete()
    {
        var native = new NativeClass();
        var lifetime = native.CreateLifetime();
        Assert.True(lifetime.TryRegister());
        // The same class cleanup used when CreateWindowExW returns zero.
        await lifetime.DisposeAsync();
        Assert.Equal(1, native.Unregisters);
        Assert.Equal(1, native.SystemDeletes);
        Assert.Equal(0, native.CallerDeletes);
    }

    [Theory]
    [InlineData("Controls")]
    [InlineData("Render")]
    [InlineData("Timer")]
    [InlineData("ControlsThrow")]
    [InlineData("TimerThrow")]
    public void PartialInitializationCleansWindowThenClassExactlyOnce(string stage)
    {
        var native = new NativeClass();
        var lifetime = native.CreateLifetime();
        Assert.True(lifetime.TryRegister());
        var events = new List<string>();
        bool CreateControls()
        {
            events.Add("Controls");
            if (stage == "ControlsThrow") throw new IOException("private-controls");
            return stage != "Controls";
        }
        void Render()
        {
            events.Add("Render");
            if (stage == "Render") throw new IOException("private-render");
        }
        bool Timer()
        {
            events.Add("Timer");
            if (stage == "TimerThrow") throw new IOException("private-timer");
            return stage != "Timer";
        }
        void Cleanup()
        {
            events.Add("DestroyWindow");
            Assert.True(lifetime.TryRelease());
            events.Add("ClassReleased");
        }
        if (stage is "Render" or "ControlsThrow" or "TimerThrow")
            Assert.Throws<IOException>(() => GenesisNetworkSettingsWindowInitialization.TryInitialize(CreateControls, Render, Timer, Cleanup));
        else
            Assert.False(GenesisNetworkSettingsWindowInitialization.TryInitialize(CreateControls, Render, Timer, Cleanup));
        Assert.Equal(["DestroyWindow", "ClassReleased"], events.TakeLast(2));
        Assert.Equal(1, native.Unregisters);
        Assert.Equal(1, native.SystemDeletes);
        Assert.Equal(0, native.CallerDeletes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialInitializationCleanupFailureRetainsResource(bool throwDuringInitialization)
    {
        var native = new NativeClass { UnregisterResult = false };
        var lifetime = native.CreateLifetime();
        Assert.True(lifetime.TryRegister());
        var failure = Assert.Throws<GenesisNetworkSettingsWindowCleanupException>(() =>
            GenesisNetworkSettingsWindowInitialization.TryInitialize(
                () => throwDuringInitialization ? throw new IOException("private-init") : false,
                () => { }, () => true, () => Release(lifetime)));
        Assert.Same(lifetime, failure.RetainedResource);
        Assert.True(native.ClassOwnsBrush);
        Assert.Equal(0, native.CallerDeletes);
        Assert.Equal(1, native.Unregisters);
    }

    [Fact]
    public void SuccessfulInitializationDoesNotRunCleanup()
    {
        var calls = new List<string>();
        Assert.True(GenesisNetworkSettingsWindowInitialization.TryInitialize(
            () => { calls.Add("Controls"); return true; },
            () => calls.Add("Render"),
            () => { calls.Add("Timer"); return true; },
            () => throw new InvalidOperationException("unexpected-cleanup")));
        Assert.Equal(["Controls", "Render", "Timer"], calls);
    }

    [Fact]
    public async Task RegistrationCannotBeRepeatedOrAttemptedAfterRelease()
    {
        var native = new NativeClass();
        var lifetime = native.CreateLifetime();
        Assert.True(lifetime.TryRegister());
        Assert.Throws<InvalidOperationException>(() => lifetime.TryRegister());
        await lifetime.DisposeAsync();
        Assert.Throws<InvalidOperationException>(() => lifetime.TryRegister());
        Assert.Equal(1, native.Registers);
    }

    [Theory]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, true)]
    public void NativeCloseAdmissionRejectsDestroyedOrReusedHwnd(bool closed, bool sameOwner, bool exists, bool expected)
    {
        var owner = new object();
        var active = sameOwner ? owner : new object();
        var validityReads = 0;
        Assert.Equal(expected, GenesisNetworkSettingsWindowInitialization.ShouldCloseNative(
            closed, active, owner, () => { validityReads++; return exists; }));
        Assert.Equal(!closed && sameOwner ? 1 : 0, validityReads);
    }

    [Fact]
    public void NativeCloseAdmissionRejectsMissingDictionaryEntry()
    {
        Assert.False(GenesisNetworkSettingsWindowInitialization.ShouldCloseNative(
            false, null, new object(), () => throw new InvalidOperationException("must-not-read-reused-handle")));
    }

    [Fact]
    public async Task SameHostClosesOldWindowAndReopensWithFreshGenerationAndSavedOff()
    {
        var adapter = new Adapter();
        var windows = new List<Window>();
        var failures = new List<GenesisNetworkSettingsWindowFailure>();
        await using var host = Host(() =>
        {
            var window = new Window(adapter);
            windows.Add(window);
            return window;
        }, failures.Add);
        await host.ShowAsync();
        var first = Assert.Single(windows);
        Assert.True(first.Model.Refresh().Checked);
        first.Close();
        adapter.Snapshot = adapter.Snapshot with { SavedAutoPortMapping = false, RuntimeDesiredEnabled = false };
        await host.ShowAsync();
        Assert.Equal(2, windows.Count);
        var second = windows[1];
        Assert.NotSame(first, second);
        Assert.NotEqual(first.Model.Generation, second.Model.Generation);
        Assert.False(second.Model.CanAcceptCallback(first.Model.Generation));
        Assert.False(second.Model.Refresh().Checked);
        Assert.Contains("保存済み：OFF", second.Model.Refresh().Settings);
        Assert.Equal(1, first.Native.Unregisters);
        Assert.Equal(1, first.DestroyCalls);
        Assert.Equal(0, first.Native.CallerDeletes);
        Assert.Empty(failures);
        Assert.True(second.Model.Refresh().CanEdit);
        Assert.Equal(0, adapter.Writes);
    }

    [Fact]
    public async Task ReopenAlsoDisposesStillVisibleOldWindowBeforeCreation()
    {
        var adapter = new Adapter();
        var first = new Window(adapter);
        var calls = 0;
        await using var host = Host(() =>
        {
            calls++;
            if (calls == 1) return first;
            Assert.Equal(1, first.Native.Unregisters);
            Assert.False(first.Model.Refresh().CanEdit);
            return new Window(adapter);
        }, _ => throw new InvalidOperationException("unexpected-feedback"));
        await host.ShowAsync();
        await host.ShowAsync();
        Assert.Equal(2, calls);
        Assert.Equal(1, first.DestroyCalls);
    }

    [Fact]
    public async Task CleanupFailurePreventsNewCreationAndReportsSafeFeedback()
    {
        var window = new Window(new Adapter(), unregister: false);
        var calls = 0;
        var failures = new List<GenesisNetworkSettingsWindowFailure>();
        var host = Host(() => { calls++; return window; }, failures.Add);
        await host.ShowAsync();
        window.Close();
        await host.ShowAsync();
        Assert.Equal([GenesisNetworkSettingsWindowFailure.CleanupFailed], failures);
        Assert.Equal(1, calls);
        await host.ShowAsync();
        Assert.Equal(1, calls);
        Assert.Equal(1, window.Native.Unregisters);
        Assert.True(window.Native.ClassOwnsBrush);
        Assert.Equal(0, window.Native.CallerDeletes);
        var warning = GenesisGuiNextSettingsWindowFeedback.Resolve(GenesisGuiNextSettingsWindowKind.NetworkRelay);
        Assert.Equal("Network / Relay settings could not be opened.", warning.Message);
        Assert.DoesNotContain("private", warning.Message);
        await Assert.ThrowsAsync<GenesisNetworkSettingsWindowCleanupException>(() => host.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task CreationCleanupFailureRetainsResourceAndBlocksRecreation()
    {
        var native = new NativeClass { UnregisterResult = false };
        var lifetime = native.CreateLifetime();
        Assert.True(lifetime.TryRegister());
        var calls = 0;
        var failures = new List<GenesisNetworkSettingsWindowFailure>();
        var host = Host(() =>
        {
            calls++;
            Release(lifetime);
            return null;
        }, failures.Add);
        await host.ShowAsync();
        await host.ShowAsync();
        Assert.Equal(1, calls);
        Assert.Equal([GenesisNetworkSettingsWindowFailure.CleanupFailed, GenesisNetworkSettingsWindowFailure.CleanupFailed], failures);
        Assert.True(native.ClassOwnsBrush);
        await Assert.ThrowsAsync<GenesisNetworkSettingsWindowCleanupException>(() => host.DisposeAsync().AsTask());
        Assert.Equal(1, native.Unregisters);
    }

    [Fact]
    public async Task CreationExceptionUsesSafeFeedbackWithoutEscaping()
    {
        var failures = new List<GenesisNetworkSettingsWindowFailure>();
        await using var host = Host(() => throw new IOException("private-handle-path"), failures.Add);
        await host.ShowAsync();
        Assert.Equal([GenesisNetworkSettingsWindowFailure.CreationFailed], failures);
    }

    [Fact]
    public async Task MissingWindowUsesSafeFeedback()
    {
        var failures = new List<GenesisNetworkSettingsWindowFailure>();
        await using var host = Host(() => null, failures.Add);
        await host.ShowAsync();
        Assert.Equal([GenesisNetworkSettingsWindowFailure.CreationFailed], failures);
    }

    [Fact]
    public async Task BrokenFeedbackCannotFaultHostOrRecreateAfterCleanupFailure()
    {
        var window = new Window(new Adapter(), unregister: false);
        var creates = 0;
        var host = Host(() => { creates++; return window; }, _ => throw new IOException("private-feedback"));
        await host.ShowAsync();
        window.Close();
        await host.ShowAsync();
        Assert.Equal(1, creates);
        await Assert.ThrowsAsync<GenesisNetworkSettingsWindowCleanupException>(() => host.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task ShutdownAndRepeatedDisposeShareCleanupWithoutOwnerMutation()
    {
        var adapter = new Adapter();
        var window = new Window(adapter);
        var host = Host(() => window, _ => { });
        await host.ShowAsync();
        var first = host.DisposeAsync().AsTask();
        var second = host.DisposeAsync().AsTask();
        Assert.Same(first, second);
        await first;
        Assert.Equal(1, window.Native.Unregisters);
        Assert.Equal(0, adapter.Writes);
        Assert.True(adapter.ReadSnapshot().Initialized);
        Assert.False(adapter.ReadSnapshot().Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => host.ShowAsync().AsTask());
    }

    [Fact]
    public async Task ShutdownDuringCreationDisposesLateWindow()
    {
        var window = new Window(new Adapter());
        GenesisNetworkSettingsWindowHost? host = null;
        host = Host(() =>
        {
            var shutdown = host!.DisposeAsync().AsTask();
            Assert.True(shutdown.IsCompletedSuccessfully);
            return window;
        }, _ => { });
        await host.ShowAsync();
        Assert.Equal(1, window.DestroyCalls);
        Assert.Equal(1, window.Native.Unregisters);
        await host.DisposeAsync();
        Assert.Equal(1, window.Native.Unregisters);
    }

    [Fact]
    public async Task ShutdownDuringCreationCleanupFailureRemainsObservable()
    {
        var window = new Window(new Adapter(), unregister: false);
        var failures = new List<GenesisNetworkSettingsWindowFailure>();
        GenesisNetworkSettingsWindowHost? host = null;
        host = Host(() =>
        {
            var shutdown = host!.DisposeAsync().AsTask();
            Assert.True(shutdown.IsCompletedSuccessfully);
            return window;
        }, failures.Add);
        await host.ShowAsync();
        Assert.Equal([GenesisNetworkSettingsWindowFailure.CleanupFailed], failures);
        var first = host.DisposeAsync().AsTask();
        var second = host.DisposeAsync().AsTask();
        Assert.Same(first, second);
        var failure = await Assert.ThrowsAsync<GenesisNetworkSettingsWindowCleanupException>(() => first);
        Assert.Same(window, failure.RetainedResource);
        Assert.Equal(1, window.Native.Unregisters);
        Assert.True(window.Native.ClassOwnsBrush);
        Assert.Equal(0, window.Native.CallerDeletes);
    }

    [Fact]
    public async Task ReopenDoesNotCreateOnCrossThreadCleanupContinuation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new PendingWindow(entered, cleanup);
        var failures = new List<GenesisNetworkSettingsWindowFailure>();
        var creates = 0;
        var host = Host(() => { creates++; return window; }, failures.Add);
        await host.ShowAsync();
        var showReturned = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerThread = new Thread(() => showReturned.SetResult(host.ShowAsync().AsTask()));
        ownerThread.Start();
        var reopening = await showReturned.Task;
        await entered.Task;
        ownerThread.Join();
        cleanup.SetResult();
        await reopening;
        Assert.Equal(1, creates);
        Assert.Equal([GenesisNetworkSettingsWindowFailure.OwnerThreadUnavailable], failures);
        await host.DisposeAsync();
    }

    [Fact]
    public async Task SettingsMenuNetworkSelectionUsesSameHostWithoutOtherCommands()
    {
        var windows = new List<Window>();
        await using var host = Host(() =>
        {
            var window = new Window(new Adapter());
            windows.Add(window);
            return window;
        }, _ => { });
        var settings = new GenesisChannelNotificationSettings(GenesisChannelNotificationPreferences.Default, new SilentSound());
        for (var index = 0; index < 2; index++)
            await GenesisChannelNotificationSettingsMenuInteraction.ApplyAsync(settings,
                GenesisChannelNotificationSettingsCommand.NetworkRelay,
                _ => throw new InvalidOperationException("wrong-YP-route"), host.ShowAsync,
                _ => throw new InvalidOperationException("wrong-playback-route"));
        Assert.Equal(2, windows.Count);
        Assert.Equal(1, windows[0].Native.Unregisters);
        Assert.Equal(GenesisChannelNotificationPreferences.Default, settings.Current);
    }

    private static GenesisNetworkSettingsWindowHost Host(
        Func<IAsyncDisposable?> create, Action<GenesisNetworkSettingsWindowFailure> feedback) =>
        new(0, GenesisUnavailableNetworkSettingsController.Instance, createWindow: create, feedback: feedback);

    private static void Release(GenesisNetworkSettingsWindowClassLifetime lifetime)
    {
        if (!lifetime.TryRelease()) throw new GenesisNetworkSettingsWindowCleanupException(lifetime);
    }

    private sealed class NativeClass
    {
        public bool RegisterResult { get; init; } = true;
        public bool UnregisterResult { get; init; } = true;
        public bool DeleteResult { get; init; } = true;
        public bool ClassOwnsBrush { get; private set; }
        public int Registers { get; private set; }
        public int Unregisters { get; private set; }
        public int CallerDeletes { get; private set; }
        public int SystemDeletes { get; private set; }
        public GenesisNetworkSettingsWindowClassLifetime CreateLifetime() => new(
            () => { Registers++; return ClassOwnsBrush = RegisterResult; },
            () =>
            {
                Unregisters++;
                Assert.True(ClassOwnsBrush);
                if (!UnregisterResult) return false;
                ClassOwnsBrush = false;
                SystemDeletes++;
                return true;
            },
            () =>
            {
                // A fake system unregister already freed the brush. Reject both
                // class-owned deletion and deletion of the now invalid handle.
                Assert.False(ClassOwnsBrush);
                Assert.Equal(0, SystemDeletes);
                CallerDeletes++;
                Assert.Equal(1, CallerDeletes);
                return DeleteResult;
            });
    }

    private sealed class Window : IAsyncDisposable
    {
        private readonly GenesisNetworkSettingsWindowClassLifetime lifetime;
        private Task? disposal;
        private bool destroyed;
        public Window(IGuiNextNetworkSettingsAdapter adapter, bool unregister = true)
        {
            Model = new GuiNextNetworkSettingsViewModel(adapter);
            Native = new NativeClass { UnregisterResult = unregister };
            lifetime = Native.CreateLifetime();
            Assert.True(lifetime.TryRegister());
        }
        public NativeClass Native { get; }
        public GuiNextNetworkSettingsViewModel Model { get; }
        public int DestroyCalls { get; private set; }
        public void Close()
        {
            if (destroyed) return;
            destroyed = true;
            DestroyCalls++;
            Model.Close();
        }
        public ValueTask DisposeAsync()
        {
            Close();
            return new(disposal ??= lifetime.DisposeAsync().AsTask());
        }
    }

    private sealed class PendingWindow(TaskCompletionSource entered, TaskCompletionSource cleanup) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            entered.TrySetResult();
            return new(cleanup.Task);
        }
    }

    private sealed class Adapter : IGuiNextNetworkSettingsAdapter
    {
        public NovaApplicationNetworkSnapshot Snapshot { get; set; } = new(
            true, false, false, false, 0, true, false, true, null, true, 0, 0, 0, null,
            NovaApplicationNetworkFailure.None, NovaApplicationNetworkFailure.None, null, null,
            NovaApplicationPublicListenerAvailability.Unavailable);
        public int Writes { get; private set; }
        public NovaApplicationNetworkSnapshot ReadSnapshot() => Snapshot;
        public Task<NovaApplicationNetworkPersistenceResult> SetAutoPortMappingAsync(bool enabled)
        {
            Writes++;
            throw new InvalidOperationException("must-not-write");
        }
    }

    private sealed class SilentSound : IGenesisSystemNotificationSoundPlayer
    {
        public ValueTask PlayAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("must-not-play");
    }
}
