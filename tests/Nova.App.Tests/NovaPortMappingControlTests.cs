using PeerCast.Nova.Foundation.Configuration;
using PeerCast.Nova.Network.PortMapping;
using PeerCast.Nova.Network.Tests;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaPortMappingControlTests
{
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
