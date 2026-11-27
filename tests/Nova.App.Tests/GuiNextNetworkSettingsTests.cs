using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Foundation.Configuration;
using PeerCast.Nova.Network.PortMapping;
using PeerCast.Nova.Network.Tests;

namespace PeerCast.Nova.App.Tests;

public sealed class GuiNextNetworkSettingsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialCheckboxUsesRuntimeDesiredAndShowsSavedSeparately(bool enabled)
    {
        var adapter = new Adapter { Snapshot = Snapshot() with { RuntimeDesiredEnabled = enabled, SavedAutoPortMapping = !enabled } };
        var view = new GuiNextNetworkSettingsViewModel(adapter).Refresh();
        Assert.Equal(enabled, view.Checked);
        Assert.True(view.CanEdit);
        Assert.Contains("runtime desired：" + (enabled ? "ON" : "OFF"), view.Settings);
        Assert.Contains("保存済み：" + (enabled ? "OFF" : "ON"), view.Settings);
        Assert.DoesNotContain("NOVA_AUTO_PORT_MAPPING", view.IntegratedSettings);
        Assert.Empty(adapter.Calls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void EnvironmentOverrideAndNextStartRemainExplicit(bool environment, bool saved)
    {
        var view = GuiNextNetworkSettingsPresentation.Create(Snapshot() with
        {
            RuntimeDesiredEnabled = saved,
            SavedAutoPortMapping = saved,
            StartupEnvironmentOverride = true,
            StartupEnvironmentEnabled = environment,
            NextStartEffectiveEnabled = environment,
        });
        Assert.Equal(saved, view.Checked);
        Assert.Contains("override：" + (environment ? "ON" : "OFF"), view.Settings);
        Assert.Contains("次回起動時：" + (environment ? "ON" : "OFF"), view.Settings);
        Assert.Contains("環境変数 NOVA_AUTO_PORT_MAPPING が優先", view.Settings);
        Assert.Contains($"自動開放: 実行中 {(saved ? "ON" : "OFF")} / 保存済み {(saved ? "ON" : "OFF")}", view.IntegratedSettings);
        Assert.Contains($"NOVA_AUTO_PORT_MAPPING={(environment ? "ON" : "OFF")}（優先） / 次回起動 {(environment ? "ON" : "OFF")}",
            view.IntegratedSettings);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckboxDispatchesOnlyTypedWriteAndDisplaysPendingThenSaved(bool enabled)
    {
        var adapter = new Adapter();
        var callbacks = new List<long>();
        var model = new GuiNextNetworkSettingsViewModel(adapter, callbacks.Add);
        var task = model.SetEnabledAsync(enabled);
        Assert.Equal(enabled, Assert.Single(adapter.Calls).Enabled);
        var pending = model.Refresh();
        Assert.Equal(enabled, pending.Checked);
        Assert.Contains("保存待機：1件", pending.Settings);
        Assert.Contains("保存中", pending.Settings);
        Assert.False(task.IsCompleted);
        adapter.Complete(0);
        Assert.Equal(NovaApplicationNetworkPersistenceStatus.Saved, (await task).Status);
        var saved = model.Refresh();
        Assert.Contains("保存待機：0件", saved.Settings);
        Assert.Contains("保存成功", saved.Settings);
        Assert.Equal(GuiNextNetworkTone.Green, saved.SettingsTone);
        Assert.Equal([model.Generation], callbacks);
    }

    [Fact]
    public async Task PersistenceFailureDoesNotRollbackDesiredOrClaimMappingFailure()
    {
        var adapter = new Adapter();
        var model = new GuiNextNetworkSettingsViewModel(adapter);
        var task = model.SetEnabledAsync(true);
        adapter.Complete(0, NovaApplicationNetworkFailure.PersistenceFailed);
        await task;
        var view = model.Refresh();
        Assert.True(view.Checked);
        Assert.Contains("保存済み：OFF", view.Settings);
        Assert.Contains("保存失敗", view.Settings);
        Assert.Contains("runtime設定は戻しません", view.Settings);
        Assert.Contains("PersistenceFailed", view.Notice);
        Assert.Equal(GuiNextNetworkTone.Orange, view.SettingsTone);
        Assert.DoesNotContain("PersistenceFailed", view.Mapping);
    }

    [Fact]
    public async Task RapidOnOffRejectsOldFailureAfterNewSuccess()
    {
        var adapter = new Adapter();
        var model = new GuiNextNetworkSettingsViewModel(adapter);
        var first = model.SetEnabledAsync(true);
        var second = model.SetEnabledAsync(false);
        Assert.Contains("保存待機：2件", model.Refresh().Settings);
        adapter.Complete(1);
        await second;
        adapter.Complete(0, NovaApplicationNetworkFailure.PersistenceFailed);
        await first;
        var final = model.Refresh();
        Assert.False(final.Checked);
        Assert.Contains("保存成功", final.Settings);
        Assert.DoesNotContain("保存失敗", final.Settings);
        Assert.Equal(NovaApplicationNetworkFailure.None, final.Failure);
        Assert.Equal([true, false], adapter.Calls.Select(call => call.Enabled));
    }

    [Fact]
    public async Task OldSuccessCannotOverwriteNewFailure()
    {
        var adapter = new Adapter();
        var model = new GuiNextNetworkSettingsViewModel(adapter);
        var first = model.SetEnabledAsync(true);
        var second = model.SetEnabledAsync(false);
        adapter.Complete(1, NovaApplicationNetworkFailure.PersistenceFailed);
        await second;
        adapter.Complete(0);
        await first;
        Assert.Contains("保存失敗", model.Refresh().Settings);
        Assert.False(model.Refresh().Checked);
    }

    [Fact]
    public async Task CompletionAfterCloseCannotRefreshOrReadDestroyedGeneration()
    {
        var adapter = new Adapter();
        var callbacks = new List<long>();
        var model = new GuiNextNetworkSettingsViewModel(adapter, callbacks.Add);
        var task = model.SetEnabledAsync(true);
        var reads = adapter.Reads;
        model.Close();
        model.Close();
        adapter.Complete(0);
        await task;
        Assert.Empty(callbacks);
        Assert.False(model.CanAcceptCallback(model.Generation));
        Assert.False(model.Refresh().CanEdit);
        Assert.Equal(reads, adapter.Reads);
        Assert.Equal(NovaApplicationNetworkFailure.Closing, (await model.SetEnabledAsync(false)).Failure);
        Assert.Single(adapter.Calls);
    }

    [Fact]
    public async Task ReopenReadsFreshSnapshotAndRejectsPreviousGeneration()
    {
        var adapter = new Adapter();
        var old = new GuiNextNetworkSettingsViewModel(adapter);
        var task = old.SetEnabledAsync(true);
        old.Close();
        var current = new GuiNextNetworkSettingsViewModel(adapter);
        Assert.NotEqual(old.Generation, current.Generation);
        Assert.False(current.CanAcceptCallback(old.Generation));
        Assert.True(current.CanAcceptCallback(current.Generation));
        Assert.True(current.Refresh().Checked);
        adapter.Complete(0);
        await task;
        Assert.Contains("保存成功", current.Refresh().Settings);
    }

    [Theory]
    [InlineData(true, "保存成功")]
    [InlineData(false, "保存失敗")]
    public async Task IntegratedPaneShowsSaveCompletedAfterReturningToConnection(bool succeeds, string expected)
    {
        var adapter = new Adapter();
        var callbacks = new List<long>();
        var pane = new GuiNextNetworkSettingsViewModel(adapter, callbacks.Add);
        var save = pane.SetEnabledAsync(true);
        Assert.Empty(callbacks);
        // Re-enter while the App-owned write is still pending.
        var pending = pane.Refresh();
        Assert.Contains("即時反映・自動保存（保存・キャンセルとは別）", pending.IntegratedSettings);
        Assert.Contains("実行中 ON / 保存済み OFF / 保存中", pending.IntegratedSettings);
        adapter.Complete(0, succeeds ? NovaApplicationNetworkFailure.None : NovaApplicationNetworkFailure.PersistenceFailed);
        await save;
        Assert.True(pane.CanAcceptCallback(Assert.Single(callbacks)));
        var complete = pane.Refresh();
        Assert.Contains(expected, complete.IntegratedSettings);
        Assert.Contains($"実行中 ON / 保存済み {(succeeds ? "ON" : "OFF")}", complete.IntegratedSettings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownDisablesCommands(bool disposed)
    {
        var adapter = new Adapter { Snapshot = Snapshot() with { Disposing = !disposed, Disposed = disposed } };
        var model = new GuiNextNetworkSettingsViewModel(adapter);
        Assert.False(model.Refresh().CanEdit);
        Assert.Equal(NovaApplicationNetworkFailure.Closing, (await model.SetEnabledAsync(true)).Failure);
        Assert.Empty(adapter.Calls);
    }

    [Fact]
    public async Task TypedClosingRejectionDisablesWithoutPretendingToSave()
    {
        var adapter = new Adapter();
        var model = new GuiNextNetworkSettingsViewModel(adapter);
        var task = model.SetEnabledAsync(true);
        adapter.Complete(0, NovaApplicationNetworkFailure.Closing);
        await task;
        Assert.False(model.Refresh().CanEdit);
        Assert.Contains("保存要求を受理できません", model.Refresh().Settings);
    }

    [Theory]
    [InlineData("Unavailable", "未起動")]
    [InlineData("Starting", "開始中")]
    [InlineData("Listening", "待受中")]
    [InlineData("Stopping", "停止中")]
    [InlineData("Failed", "失敗")]
    public void ListenerStateShowsOnlyActualListeningEndpoint(string state, string text)
    {
        var availability = Enum.Parse<NovaApplicationPublicListenerAvailability>(state);
        var relay = Relay() with { State = state == "Listening" ? NovaPublicRelayState.Listening : NovaPublicRelayState.Stopped };
        var view = GuiNextNetworkSettingsPresentation.Create(Snapshot() with
        {
            PublicListenerAvailability = availability,
            PublicRelay = relay,
        });
        Assert.Contains("listener：" + text, view.Listener);
        Assert.Contains(state == "Listening" ? "実bound port：8123" : "実bound port：なし", view.Listener);
        Assert.Contains("共有参照（要求含む）：2", view.Listener);
        Assert.Contains(relay.ChannelId!.ToString()!, view.Listener);
        if (state == "Unavailable") Assert.Contains("公開listener待機中", view.Notice);
    }

    [Fact]
    public void StaleEndpointCannotBecomeActualPort()
    {
        var view = GuiNextNetworkSettingsPresentation.Create(Snapshot() with
        {
            PublicListenerAvailability = NovaApplicationPublicListenerAvailability.Listening,
            PublicRelay = Relay() with { State = NovaPublicRelayState.Stopping },
        });
        Assert.Contains("実bound port：なし", view.Listener);
    }

    [Theory]
    [InlineData("Mapping", "開放要求中")]
    [InlineData("Renewing", "更新要求中")]
    [InlineData("Unmapping", "閉鎖要求中")]
    [InlineData("UnmapUnconfirmed", "閉鎖未確認")]
    [InlineData("ResultUnknown", "状態未確認")]
    [InlineData("Rejected", "要求が拒否")]
    public void MappingStatesReuseExistingProjection(string state, string text)
    {
        var view = GuiNextNetworkSettingsPresentation.Create(Snapshot() with { Nat = Nat() with { State = Enum.Parse<NatDisplayState>(state) } });
        Assert.Contains(text, view.Mapping);
        Assert.Contains("外部到達性：未確認", view.Mapping);
        Assert.Equal(GuiNextNetworkTone.Orange, view.MappingTone);
    }

    [Fact]
    public void RouterAcknowledgementIsNotWanReachability()
    {
        var view = GuiNextNetworkSettingsPresentation.Create(Snapshot() with
        {
            Nat = Nat() with { State = NatDisplayState.Mapped, LeaseEvidence = NatLeaseEvidenceKind.RouterConfirmedWithEstimatedExpiry },
        });
        Assert.Contains("Router ACK：応答確認済み（期限は推定）", view.Mapping);
        Assert.Contains("外部到達性：未確認", view.Mapping);
        Assert.DoesNotContain("到達確認済み", view.Mapping);
    }

    [Fact]
    public void IndependentNatAndPublicRevisionsRefreshWithoutApplicationRevisionChange()
    {
        var adapter = new Adapter { Snapshot = Snapshot() with { Nat = Nat() } };
        var model = new GuiNextNetworkSettingsViewModel(adapter);
        var before = model.Refresh();
        adapter.Snapshot = adapter.Snapshot with
        {
            Nat = Nat() with { State = NatDisplayState.UnmapUnconfirmed },
            PublicListenerAvailability = NovaApplicationPublicListenerAvailability.Listening,
            PublicRelay = Relay() with { Revision = 99 },
        };
        var after = model.Refresh();
        Assert.Equal(0, adapter.Snapshot.Revision);
        Assert.NotEqual(before.Mapping, after.Mapping);
        Assert.Contains("閉鎖未確認", after.Mapping);
        Assert.Contains("実bound port：8123", after.Listener);
    }

    [Fact]
    public void HistoricalDiagnosticsStayHistoricalAndDisplayIsBounded()
    {
        var diagnostics = Enumerable.Range(0, 50).Select(index => new NatDisplayDiagnostic(
            DateTimeOffset.UnixEpoch, "StaleObservation", true)).ToArray();
        var view = GuiNextNetworkSettingsPresentation.Create(Snapshot() with { Nat = Nat() with { Diagnostics = diagnostics } });
        Assert.Equal(32, view.Diagnostics.Split("\r\n").Length);
        Assert.All(view.Diagnostics.Split("\r\n"), line => Assert.Contains("旧世代・旧ネットワーク", line));
        Assert.DoesNotContain("開放済み", view.Mapping);
    }

    [Theory]
    [InlineData("SettingsLoadFailed")]
    [InlineData("InvalidEnvironment")]
    [InlineData("RuntimeInitializationFailed")]
    [InlineData("ControlInitializationFailed")]
    public async Task InitializationFailureIsSafeAndCannotBeRecoveredByGui(string failure)
    {
        var adapter = new Adapter { Snapshot = Snapshot() with { Initialized = false, InitializationFailure = Enum.Parse<NovaApplicationNetworkFailure>(failure) } };
        var model = new GuiNextNetworkSettingsViewModel(adapter);
        Assert.False(model.Refresh().CanEdit);
        Assert.Contains(failure, model.Refresh().Notice);
        Assert.Equal(failure, (await model.SetEnabledAsync(true)).Failure.ToString());
        Assert.Empty(adapter.Calls);
    }

    [Fact]
    public void SnapshotExceptionIsRedactedAndFailClosed()
    {
        var adapter = new Adapter { ThrowRead = true };
        var view = new GuiNextNetworkSettingsViewModel(adapter).Refresh();
        Assert.False(view.CanEdit);
        Assert.DoesNotContain("private", view.ToString());
        Assert.Contains("取得できません", view.Notice);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SyncAndAsyncSaveExceptionsAreSafeAndRetainRequestedIdentity(bool sync)
    {
        var adapter = new Adapter { ThrowSave = sync };
        var model = new GuiNextNetworkSettingsViewModel(adapter);
        var task = model.SetEnabledAsync(true);
        if (!sync) adapter.Calls[0].Completion.SetException(new IOException("private-source"));
        var result = await task;
        Assert.True(result.RequestedEnabled);
        Assert.Equal(NovaApplicationNetworkFailure.PersistenceFailed, result.Failure);
        Assert.DoesNotContain("private", model.Refresh().ToString());
    }

    [Fact]
    public async Task DeadRefreshSinkCannotFaultAcceptedSave()
    {
        var adapter = new Adapter();
        var model = new GuiNextNetworkSettingsViewModel(adapter, _ => throw new InvalidOperationException("closed"));
        var task = model.SetEnabledAsync(true);
        adapter.Complete(0);
        Assert.Equal(NovaApplicationNetworkPersistenceStatus.Saved, (await task).Status);
    }

    [Theory]
    [InlineData(0, false, true, 1)]
    [InlineData(2, false, true, 0)]
    [InlineData(0, true, true, 2)]
    [InlineData(2, false, false, 1)]
    [InlineData(1, true, false, 2)]
    public void KeyboardFocusCyclesAndSkipsDisabledCheckbox(int current, bool reverse, bool editable, int expected) =>
        Assert.Equal(expected, GenesisNetworkSettingsWindow.NextNatFocus(current, reverse, [editable, true, true]));

    [Fact]
    public void NoEnabledFocusTargetReturnsUnavailable() =>
        Assert.Equal(-1, GenesisNetworkSettingsWindow.NextNatFocus(0, false, [false, false]));

    [Fact]
    public async Task RealAdapterPersistsOnlyNatFieldAndDoesNotAcquireOrDuplicateRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "Nova-network-gui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new NovaSettingsStore(Path.Combine(root, "settings.json"));
        var router = new FakeNatRouter { AutoComplete = true };
        var clock = new ManualNatTimeProvider();
        var creates = 0;
        var driverCreates = 0;
        var owner = new NovaApplicationNetworkOwner(store, static () => "off", options =>
        {
            creates++;
            return new NovaApplicationNatRuntime(new NatPortMappingRuntime(options, router, new Network(), clock));
        }, createPublicRelayDriver: _ => { driverCreates++; throw new InvalidOperationException("must-not-acquire"); });
        try
        {
            await store.SaveAsync(new NovaOptions { NodeName = "original", ListenPort = 8123 });
            Assert.True((await owner.InitializeAsync()).Initialized);
            var adapter = new GuiNextNetworkSettingsAdapter(owner);
            var first = new GuiNextNetworkSettingsViewModel(adapter);
            var second = new GuiNextNetworkSettingsViewModel(adapter);
            await store.UpdateAsync(options => options with
            {
                NodeName = "latest",
                RecordingOutputDirectory = root,
                EnableDetailedDiagnostics = true,
            });
            Assert.Equal(NovaApplicationNetworkPersistenceStatus.Saved, (await first.SetEnabledAsync(true)).Status);
            Assert.True(second.Refresh().Checked);
            Assert.Contains("次回起動時：OFF", second.Refresh().Settings);
            first.Close();
            Assert.True(second.Refresh().CanEdit);
            Assert.Equal(NovaApplicationNetworkPersistenceStatus.Saved, (await second.SetEnabledAsync(false)).Status);
            second.Close();
            var saved = store.Load();
            Assert.False(saved.AutoPortMapping);
            Assert.Equal("latest", saved.NodeName);
            Assert.Equal(8123, saved.ListenPort);
            Assert.Equal(root, saved.RecordingOutputDirectory);
            Assert.True(saved.EnableDetailedDiagnostics);
            Assert.Equal(1, creates);
            Assert.Equal(0, driverCreates);
            Assert.Equal(0, router.Count);
            Assert.Equal(NovaPublicRelayState.Stopped, owner.ReadSnapshot().PublicRelay!.State);
            Assert.False(owner.ReadSnapshot().Disposed);
        }
        finally
        {
            await owner.DisposeAsync();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ValidationProfileAdapterSupportsGuiSaveAndReopenWithoutNetworkDemand()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Nova-network-profile-gui-" + Guid.NewGuid().ToString("N")));
        if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid test cleanup root.");
        var production = Path.Combine(root, "fake-production");
        Directory.CreateDirectory(root);
        try
        {
            await using var profile = await NovaValidationProfile.CreateAsync(
                Path.Combine(root, "profile"), [production], static _ => null);
            await using var owner = profile.CreateNetworkOwner();
            Assert.True((await owner.InitializeAsync()).Initialized);
            var adapter = new GuiNextNetworkSettingsAdapter(owner);
            var first = new GuiNextNetworkSettingsViewModel(adapter);
            Assert.False(first.Refresh().Checked);
            Assert.True(first.Refresh().CanEdit);
            var runtimeOwner = adapter.ReadSnapshot().PublicRelay!.RuntimeOwnerId;

            Assert.Equal(NovaApplicationNetworkPersistenceStatus.Saved,
                (await first.SetEnabledAsync(true)).Status);
            Assert.True(profile.Settings.Load().AutoPortMapping);
            Assert.Contains("保存成功", first.Refresh().Settings);
            first.Close();

            var reopened = new GuiNextNetworkSettingsViewModel(adapter);
            Assert.NotEqual(first.Generation, reopened.Generation);
            Assert.False(reopened.CanAcceptCallback(first.Generation));
            Assert.True(reopened.Refresh().Checked);
            Assert.Contains("外部到達性：未確認", reopened.Refresh().Mapping);
            Assert.DoesNotContain("応答確認済み", reopened.Refresh().Mapping);
            Assert.Equal(NovaApplicationNetworkPersistenceStatus.Saved,
                (await reopened.SetEnabledAsync(false)).Status);
            Assert.False(profile.Settings.Load().AutoPortMapping);
            reopened.Close();

            var snapshot = adapter.ReadSnapshot();
            Assert.False(snapshot.Disposed);
            Assert.Null(snapshot.Nat!.Network);
            Assert.Empty(snapshot.Nat.RegisteredInternalPorts);
            Assert.Equal(NatLeaseEvidenceKind.None, snapshot.Nat.LeaseEvidence);
            Assert.Equal(NovaApplicationPublicListenerAvailability.Unavailable, snapshot.PublicListenerAvailability);
            Assert.Equal(runtimeOwner, snapshot.PublicRelay!.RuntimeOwnerId);
            Assert.Null(snapshot.PublicRelay.Endpoint);
            Assert.Equal(0, snapshot.PublicRelay.ReferenceCount);
            Assert.Empty(Directory.GetFiles(profile.Paths.Recordings));
            Assert.False(Directory.Exists(production));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingSettingsMenuDispatchesNetworkRouteOnly()
    {
        var settings = new GenesisChannelNotificationSettings(GenesisChannelNotificationPreferences.Default, new SilentSound());
        var calls = 0;
        await GenesisChannelNotificationSettingsMenuInteraction.ApplyAsync(settings,
            GenesisChannelNotificationSettingsCommand.NetworkRelay,
            _ => throw new InvalidOperationException("wrong YP route"),
            _ => { calls++; return ValueTask.CompletedTask; },
            _ => throw new InvalidOperationException("wrong playback route"));
        Assert.Equal(1, calls);
    }

    private sealed class SilentSound : IGenesisSystemNotificationSoundPlayer
    {
        public ValueTask PlayAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private static NovaApplicationNetworkSnapshot Snapshot() => new(
        true, false, false, false, 0, true, false, false, null, false, 0, 0, 0, null,
        NovaApplicationNetworkFailure.None, NovaApplicationNetworkFailure.None, null, null,
        NovaApplicationPublicListenerAvailability.Unavailable);

    [Theory]
    [InlineData("Failed", "設定エラー")]
    [InlineData("Listening", "TCP待受中")]
    [InlineData("Off", "公開OFF")]
    public void IntegratedStatusKeepsListenerMappingAndReachabilitySeparate(string state, string text)
    {
        var nat = new NatPortMappingReadState(true, 1, null, 120, DateTimeOffset.UnixEpoch, [], [], []);
        var view = GuiNextNetworkSettingsPresentation.Create(Snapshot() with
        {
            SharedPublicRelay = new(Enum.Parse<GenesisSharedPublicRelayState>(state), 7144, 0, nat, false, null),
        });
        Assert.Contains($"WAN Relay: {text} / Port 7144\r\nNAT: 未作成 / 外部到達: 未確認", view.IntegratedSettings);
        Assert.Contains("保存操作なし", view.IntegratedSettings);
        Assert.DoesNotContain("到達不可", view.IntegratedSettings);
        Assert.DoesNotContain("変更: なし", view.IntegratedSettings);
    }

    private static NatPortMappingDisplay Nat() => new(
        true, 1, "NAT-PMP", null, NatMappingProtocol.Tcp, [], NatDisplayState.Mapping,
        NatDisplayReachability.NotChecked, null, null, 120, null, NatLeaseEvidenceKind.None,
        DateTimeOffset.UnixEpoch, null, []);

    private static NovaPublicRelaySnapshot Relay() => new(
        Guid.NewGuid(), 1, 1, NovaPublicRelayState.Listening, 2,
        new ChannelId(Guid.Parse("C0700662C84565C65125E0F927A501D0")), null,
        new NovaPublicRelayEndpoint("0.0.0.0", 8123), NovaPublicRelayFailure.None);

    private sealed class Network : INatNetworkSource
    {
        public NatNetworkIdentity GetCurrentNetwork() => NatPortMappingTestHarness.Home;
    }

    private sealed class Call(long id, bool enabled)
    {
        public long Id { get; } = id;
        public bool Enabled { get; } = enabled;
        public TaskCompletionSource<NovaApplicationNetworkPersistenceResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Adapter : IGuiNextNetworkSettingsAdapter
    {
        public NovaApplicationNetworkSnapshot Snapshot { get; set; } = GuiNextNetworkSettingsTests.Snapshot();
        public List<Call> Calls { get; } = [];
        public int Reads { get; private set; }
        public bool ThrowRead { get; init; }
        public bool ThrowSave { get; init; }
        public NovaApplicationNetworkSnapshot ReadSnapshot()
        {
            Reads++;
            if (ThrowRead) throw new IOException("private-source");
            return Snapshot;
        }
        public Task<NovaApplicationNetworkPersistenceResult> SetAutoPortMappingAsync(bool enabled)
        {
            if (ThrowSave) throw new IOException("private-save");
            var call = new Call(Calls.Count + 1, enabled);
            Calls.Add(call);
            Snapshot = Snapshot with
            {
                RuntimeDesiredEnabled = enabled,
                PendingSaveCount = Snapshot.PendingSaveCount + 1,
                LastAcceptedRequestId = call.Id,
                LastPersistenceResult = new(call.Id, enabled, NovaApplicationNetworkPersistenceStatus.Pending, NovaApplicationNetworkFailure.None),
            };
            return call.Completion.Task;
        }
        public void Complete(int index, NovaApplicationNetworkFailure failure = NovaApplicationNetworkFailure.None)
        {
            var call = Calls[index];
            var status = failure == NovaApplicationNetworkFailure.None ? NovaApplicationNetworkPersistenceStatus.Saved :
                failure == NovaApplicationNetworkFailure.Closing ? NovaApplicationNetworkPersistenceStatus.Rejected : NovaApplicationNetworkPersistenceStatus.Failed;
            var result = new NovaApplicationNetworkPersistenceResult(call.Id, call.Enabled, status, failure);
            Snapshot = Snapshot with { PendingSaveCount = Snapshot.PendingSaveCount - 1 };
            if (call.Id == Snapshot.LastAcceptedRequestId) Snapshot = Snapshot with { LastPersistenceResult = result };
            if (failure == NovaApplicationNetworkFailure.None && call.Id > Snapshot.LastPersistedRequestId)
                Snapshot = Snapshot with { SavedAutoPortMapping = call.Enabled, LastPersistedRequestId = call.Id };
            call.Completion.SetResult(result);
        }
    }
}
