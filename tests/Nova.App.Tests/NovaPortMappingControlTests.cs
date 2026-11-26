using PeerCast.Nova.Foundation.Configuration;
using PeerCast.Nova.Network.PortMapping;
using PeerCast.Nova.Network.Tests;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaPortMappingControlTests
{
    [Fact]
    public async Task PermanentUpnpLease_ViewExplainsCrashLimitationWithoutInventingExpiry()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        var turn = test.Manager.ReconcileAsync();
        var call = await test.Router.NextAsync();
        call.Completion.TrySetResult(new(NatBackendOutcome.Success, 7144, 0,
            Backend: "UPnP IGD", LeaseKind: NatLeaseKind.Permanent));
        await turn;
        var view = NovaPortMappingView.Create(NatPortMappingStatusProjection.Create(test.Manager.ReadState()), false);
        Assert.Contains("UPnP IGD", view.Summary);
        Assert.Contains("確認 期限なし", view.Summary);
        Assert.Contains("異常終了後の解除は保証されません", view.Summary);
        Assert.Contains("推定期限: 不明", view.Summary);
        Assert.Contains("外部接続: 未確認", view.Summary);
    }

    [Fact]
    public void DefaultOff_IsSharedBySettingsAndStartup()
    {
        Assert.False(new NovaOptions().AutoPortMapping);
        Assert.False(SystemNovaDailyRelayControllerFactory.ReadPortMappingSetting(null).Enabled);
    }

    [Theory]
    [InlineData(null, true, true)]
    [InlineData("", true, true)]
    [InlineData(" ", true, true)]
    [InlineData("off", true, false)]
    [InlineData("on", false, true)]
    public void StartupPrecedence_ExplicitEnvironmentThenSaved(string? environment, bool saved, bool expected)
    {
        Assert.Equal(expected, SystemNovaDailyRelayControllerFactory.ReadPortMappingSetting(
            environment, new NovaOptions { AutoPortMapping = saved }).Enabled);
    }

    [Fact]
    public async Task SettingsOnOff_ChangesExistingListenerDemandAndSavesThroughControl()
    {
        using var directory = new SettingsDirectory();
        var store = directory.Store;
        await using var test = new NatPortMappingTestHarness(enabled: false);
        using var owner = test.Acquire();
        using var control = new NovaPortMappingControl(test.Manager, store, new(), false);
        await control.SetEnabledAsync(true);
        Assert.True(control.ReadStatus().Enabled);
        Assert.True(store.Load().AutoPortMapping);
        await test.MapAsync(46000);
        Assert.Equal(NatDisplayState.Mapped, control.ReadStatus().State);
        await control.SetEnabledAsync(false);
        Assert.False(test.Manager.Enabled);
        Assert.False(store.Load().AutoPortMapping);
        var turn = test.Manager.ReconcileAsync();
        var delete = await test.Router.NextAsync();
        Assert.Equal(NatMappingOperation.Unmap, delete.Request.Operation);
        delete.Succeed();
        await turn;
        Assert.Equal(NatDisplayState.Disabled, control.ReadStatus().State);
    }

    [Fact]
    public async Task QuickSettingsOffOn_PreservesReconcilerObservationAndFinalSavedValue()
    {
        using var directory = new SettingsDirectory();
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        using var control = new NovaPortMappingControl(test.Manager, directory.Store, new(), false);
        var turn = test.Manager.ReconcileAsync();
        var map = await test.Router.NextAsync();
        var off = control.SetEnabledAsync(false);
        var on = control.SetEnabledAsync(true);
        Assert.True(test.Manager.Enabled);
        map.Succeed(46001, 20);
        await turn;
        await Task.WhenAll(off, on);
        Assert.True(directory.Store.Load().AutoPortMapping);
        var view = NovaPortMappingView.Create(control.ReadStatus(), false);
        Assert.Contains("ルーター設定: 開放済み", view.Summary);
        Assert.Contains("外部接続: 未確認", view.Summary);
        Assert.Contains("46001", view.Summary);
        Assert.Contains("確認 20s", view.Summary);
        Assert.Contains("[LifetimeShortened]", view.Diagnostics);
        Assert.Contains("[StaleObservation]", view.Diagnostics);
        Assert.Contains("旧世代・旧ネットワーク", view.Diagnostics);
        Assert.Equal(1, test.Router.Count);
    }

    [Fact]
    public async Task SaveLoad_PreservesOtherExistingSettingsAndSurvivesNewStore()
    {
        using var directory = new SettingsDirectory();
        var original = new NovaOptions
        {
            NodeName = "saved-node",
            ListenPort = 7145,
            EnableDetailedDiagnostics = true,
        };
        await directory.Store.SaveAsync(original);
        await using var test = new NatPortMappingTestHarness(enabled: false);
        using var control = new NovaPortMappingControl(test.Manager, directory.Store, directory.Store.Load(), false);
        await control.SetEnabledAsync(true);
        var reloaded = new NovaSettingsStore(directory.Path).Load();
        Assert.Equal(original with { AutoPortMapping = true }, reloaded);
        Assert.True(SystemNovaDailyRelayControllerFactory.ReadPortMappingSetting(null, reloaded).Enabled);
        Assert.Single(Directory.GetFiles(System.IO.Path.GetDirectoryName(directory.Path)!));
    }

    [Fact]
    public async Task MappingTimeout_ViewDoesNotSayFailedOrReachable()
    {
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        var turn = test.Manager.ReconcileAsync();
        _ = await test.Router.NextAsync();
        test.Clock.Advance(TimeSpan.FromSeconds(3));
        await turn;
        var view = NovaPortMappingView.Create(NatPortMappingStatusProjection.Create(test.Manager.ReadState()), false);
        Assert.Contains("状態未確認", view.Summary);
        Assert.Contains("外部接続: 未確認", view.Summary);
        Assert.Contains("確認 未確認", view.Summary);
        Assert.Contains("要求からの推定", view.Summary);
        Assert.Contains("応答タイムアウト", view.Diagnostics);
        Assert.DoesNotContain("失敗", view.Summary);
        Assert.DoesNotContain("接続可能", view.Summary);
    }

    [Fact]
    public async Task RepeatedReadView_IsReadOnly()
    {
        using var directory = new SettingsDirectory();
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        await test.MapAsync();
        using var control = new NovaPortMappingControl(test.Manager, directory.Store, new(), true);
        var before = test.Manager.ReadState();
        var view = NovaPortMappingView.Create(control.ReadStatus(), control.StartupEnvironmentOverride);
        Assert.Contains("環境変数が優先", view.Summary);
        Assert.Equal(before.Generation, test.Manager.Generation);
        Assert.Equal(before.Mappings, test.Manager.Snapshot);
        Assert.Equal(1, test.Router.Count);
        Assert.False(File.Exists(directory.Path));
    }

    [Fact]
    public async Task SaveFailure_DoesNotUndoDesiredStateOrLeaveTemporaryFile()
    {
        using var directory = new SettingsDirectory();
        Directory.CreateDirectory(directory.Path);
        await using var test = new NatPortMappingTestHarness(enabled: false);
        using var control = new NovaPortMappingControl(test.Manager, directory.Store, new(), false);
        var failure = await Record.ExceptionAsync(() => control.SetEnabledAsync(true));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.True(test.Manager.Enabled);
        Assert.Empty(Directory.GetFiles(System.IO.Path.GetDirectoryName(directory.Path)!));
    }

    [Fact]
    public async Task RecordingOwnerSaveBeforeNatTogglePreservesLatestDocument()
    {
        using var directory = new SettingsDirectory();
        var root = System.IO.Path.GetDirectoryName(directory.Path)!;
        var original = new NovaOptions
        {
            NodeName = "original-node",
            ListenPort = 17444,
            EnableDetailedDiagnostics = true,
            RecordingOutputDirectory = System.IO.Path.Combine(root, "original-recordings"),
        };
        await directory.Store.SaveAsync(original);
        var recording = new GenesisRecordingOutputDirectorySettings(directory.Store,
            System.IO.Path.Combine(root, "default-recordings"));
        await using var test = new NatPortMappingTestHarness(enabled: false);
        using var control = new NovaPortMappingControl(test.Manager, directory.Store, original, false);
        var latestDirectory = System.IO.Path.Combine(root, "latest-recordings");
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, recording.Save(latestDirectory));
        await directory.Store.UpdateAsync(current => current with { NodeName = "latest-node", ListenPort = 17445 });

        await control.SetEnabledAsync(true);

        Assert.Equal(original with
        {
            AutoPortMapping = true,
            NodeName = "latest-node",
            ListenPort = 17445,
            RecordingOutputDirectory = latestDirectory,
        }, directory.Store.Load());
        Assert.Equal(latestDirectory, recording.CurrentRuntimeDirectory);
        Assert.Equal(latestDirectory, recording.Presentation.EffectiveDirectory);
        Assert.True(test.Manager.Enabled);
        Assert.Equal(0, test.Router.Count);
    }

    [Fact]
    public async Task NatTogglesAroundRecordingOwnerSavePreserveLatestDirectory()
    {
        using var directory = new SettingsDirectory();
        var root = System.IO.Path.GetDirectoryName(directory.Path)!;
        var original = new NovaOptions { NodeName = "retained-node", ListenPort = 18444, EnableDetailedDiagnostics = true };
        await directory.Store.SaveAsync(original);
        var recording = new GenesisRecordingOutputDirectorySettings(directory.Store,
            System.IO.Path.Combine(root, "default-recordings"));
        await using var test = new NatPortMappingTestHarness(enabled: false);
        using var control = new NovaPortMappingControl(test.Manager, directory.Store, original, false);

        await control.SetEnabledAsync(true);
        var latestDirectory = System.IO.Path.Combine(root, "latest-recordings");
        Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, recording.Save(latestDirectory));
        Assert.True(directory.Store.Load().AutoPortMapping);
        await control.SetEnabledAsync(false);

        Assert.Equal(original with { RecordingOutputDirectory = latestDirectory }, directory.Store.Load());
        Assert.False(test.Manager.Enabled);
        Assert.Equal(0, test.Router.Count);
    }

    [Fact]
    public async Task InterleavedStoreInstancesPreserveLatestFields()
    {
        using var directory = new SettingsDirectory();
        var firstStore = directory.Store;
        var secondStore = directory.Store;
        var original = new NovaOptions { NodeName = "interleaved-node", ListenPort = 19444, EnableDetailedDiagnostics = true };
        await firstStore.SaveAsync(original);
        await using var test = new NatPortMappingTestHarness(enabled: false);
        using var control = new NovaPortMappingControl(test.Manager, firstStore, original, false);
        var expected = original;

        for (var index = 0; index < 6; index++)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(directory.Path)!, $"recordings-{index}");
            await secondStore.UpdateAsync(current => current with { RecordingOutputDirectory = path });
            var enabled = index % 2 == 0;
            await control.SetEnabledAsync(enabled);
            expected = expected with { RecordingOutputDirectory = path, AutoPortMapping = enabled };
            Assert.Equal(expected, secondStore.Load());
        }

        Assert.False(test.Manager.Enabled);
        Assert.Equal(0, test.Router.Count);
    }

    [Fact]
    public async Task QueuedRapidOffOnPreservesOtherWriterAndFinalDesiredState()
    {
        using var directory = new SettingsDirectory();
        var original = new NovaOptions { NodeName = "queued-node", ListenPort = 20444, EnableDetailedDiagnostics = true };
        await directory.Store.SaveAsync(original);
        await using var test = new NatPortMappingTestHarness();
        using var owner = test.Acquire();
        using var control = new NovaPortMappingControl(test.Manager, directory.Store, original, false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(directory.Path)!, "queued-recordings");
        var otherStore = directory.Store;
        var otherWrite = Task.Run(() => otherStore.UpdateAsync(current =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            return current with { RecordingOutputDirectory = path };
        }));
        Task off = Task.CompletedTask;
        Task on = Task.CompletedTask;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            off = control.SetEnabledAsync(false);
            Assert.False(test.Manager.Enabled);
            on = control.SetEnabledAsync(true);
            Assert.True(test.Manager.Enabled);
            Assert.False(off.IsCompleted);
            Assert.False(on.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            // The application likewise drains pending saves before disposing the control.
            await Task.WhenAll(otherWrite, off, on).WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Equal(original with { AutoPortMapping = true, RecordingOutputDirectory = path }, directory.Store.Load());
        Assert.True(test.Manager.Enabled);
        Assert.Equal(0, test.Router.Count);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    public async Task CorruptLatestDocumentFailsWithoutOverwriteOrDesiredRollback(string payload)
    {
        using var directory = new SettingsDirectory();
        await using var test = new NatPortMappingTestHarness(enabled: false);
        using var control = new NovaPortMappingControl(test.Manager, directory.Store, new(), false);
        await File.WriteAllTextAsync(directory.Path, payload);

        var failure = await Record.ExceptionAsync(() => control.SetEnabledAsync(true));

        Assert.True(failure is System.Text.Json.JsonException or InvalidDataException);
        Assert.True(test.Manager.Enabled);
        Assert.Equal(payload, await File.ReadAllTextAsync(directory.Path));
        Assert.Single(Directory.GetFiles(System.IO.Path.GetDirectoryName(directory.Path)!));
        Assert.Equal(0, test.Router.Count);
    }

    [Theory]
    [InlineData("on", true, false)]
    [InlineData("off", false, true)]
    public async Task RuntimeSavePreservesStartupEnvironmentPrecedence(string environment, bool startupEnabled, bool requested)
    {
        using var directory = new SettingsDirectory();
        var original = new NovaOptions { AutoPortMapping = !startupEnabled, NodeName = "environment-node", ListenPort = 21444 };
        await directory.Store.SaveAsync(original);
        var startup = SystemNovaDailyRelayControllerFactory.ReadPortMappingSetting(environment, original);
        Assert.Equal(startupEnabled, startup.Enabled);
        await using var test = new NatPortMappingTestHarness(startup.Enabled);
        using var control = new NovaPortMappingControl(test.Manager, directory.Store, original, true);

        await control.SetEnabledAsync(requested);

        var saved = directory.Store.Load();
        Assert.Equal(original with { AutoPortMapping = requested }, saved);
        Assert.Equal(requested, test.Manager.Enabled);
        Assert.True(control.StartupEnvironmentOverride);
        Assert.Equal(startupEnabled, SystemNovaDailyRelayControllerFactory.ReadPortMappingSetting(environment, saved).Enabled);
        Assert.Equal(requested, SystemNovaDailyRelayControllerFactory.ReadPortMappingSetting(null, saved).Enabled);
        Assert.Equal(0, test.Router.Count);
    }

    [Fact]
    public async Task MissingDocumentUsesExistingDefaultsAndMutatesOnlyNatField()
    {
        using var directory = new SettingsDirectory();
        await using var test = new NatPortMappingTestHarness(enabled: false);
        using var control = new NovaPortMappingControl(test.Manager, directory.Store,
            new NovaOptions { NodeName = "stale-construction-only", ListenPort = 22444 }, false);

        await control.SetEnabledAsync(true);

        Assert.Equal(new NovaOptions { AutoPortMapping = true }, directory.Store.Load());
        Assert.True(test.Manager.Enabled);
        Assert.Single(Directory.GetFiles(System.IO.Path.GetDirectoryName(directory.Path)!));
    }

    private sealed class SettingsDirectory : IDisposable
    {
        public SettingsDirectory()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Nova-nat-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Path = System.IO.Path.Combine(root, "settings.json");
        }
        public string Path { get; }
        public NovaSettingsStore Store => new(Path);
        public void Dispose() => Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, recursive: true);
    }
}
