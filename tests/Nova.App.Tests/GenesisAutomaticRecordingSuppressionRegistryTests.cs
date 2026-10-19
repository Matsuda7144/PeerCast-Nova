using System.Reflection;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisAutomaticRecordingSuppressionRegistryTests
{
    private static RecordingAutomaticStopAdmission Receipt(ChannelId channel, RecordingSessionId session, Guid rule) =>
        (RecordingAutomaticStopAdmission)Activator.CreateInstance(typeof(RecordingAutomaticStopAdmission),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            [channel, session, rule, new TaskCompletionSource<RecordingAutomationStopResult>().Task], null)!;

    [Fact]
    public async Task Capacity4096Rejects4097WithoutEvictionAndRollbackAllowFreeSlots()
    {
        var registry = new GenesisAutomaticRecordingSuppression();
        var rule = Guid.NewGuid(); var entries = new List<GenesisAutomaticRecordingSuppression.Entry>();
        for (var i = 0; i < GenesisAutomaticRecordingSuppression.Capacity; i++)
        {
            var channel = new ChannelId(Guid.NewGuid()); var session = new RecordingSessionId(Guid.NewGuid());
            var entry = Assert.IsType<GenesisAutomaticRecordingSuppression.Entry>(registry.Reserve(channel, session));
            registry.Bind(entry, Receipt(channel, session, rule), () => Task.CompletedTask);
            if ((i & 1) == 0) registry.Commit(entry, RecordingAutomationStopResultKind.Stopped);
            entries.Add(entry);
        }
        await Task.WhenAll(registry.Observers());
        Assert.Equal(4096, registry.Snapshot().Count);
        Assert.Null(registry.Reserve(new ChannelId(Guid.NewGuid()), new RecordingSessionId(Guid.NewGuid())));
        Assert.Null(registry.Reserve(entries[0].ChannelId, entries[0].SessionId));
        Assert.False(registry.Allow(entries[1].ChannelId, rule)); // pending cannot be resumed
        Assert.True(registry.Allow(entries[0].ChannelId, rule));
        var replacement = Assert.IsType<GenesisAutomaticRecordingSuppression.Entry>(
            registry.Reserve(new ChannelId(Guid.NewGuid()), new RecordingSessionId(Guid.NewGuid())));
        registry.Rollback(replacement);
        Assert.NotNull(registry.Reserve(new ChannelId(Guid.NewGuid()), new RecordingSessionId(Guid.NewGuid())));
        Assert.Equal(4095, registry.Snapshot().Count);
        var retained = registry.Snapshot().Select(e => e.ChannelId).ToHashSet();
        Assert.All(entries.Skip(1), e => Assert.Contains(e.ChannelId, retained));
    }

    [Fact]
    public async Task UnresolvedReceiptRetainsPendingFenceAndHidesRawResolutionFault()
    {
        var registry = new GenesisAutomaticRecordingSuppression();
        var channel = new ChannelId(Guid.NewGuid());
        var session = new RecordingSessionId(Guid.NewGuid());
        var rule = Guid.NewGuid();
        var receipt = Receipt(channel, session, rule);
        typeof(RecordingAutomaticStopAdmission).GetMethod("RecordResolutionFault",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(receipt, [new IOException("C:\\private\\pcp://secret exception")]);
        var entry = registry.Reserve(channel, session)!;
        registry.Bind(entry, receipt, () => Task.CompletedTask);
        await Task.WhenAll(registry.Observers());
        var row = Assert.Single(registry.Snapshot());
        Assert.Equal(GenesisAutomaticRecordingSuppressionState.AdmittedPending, row.State);
        Assert.Equal("停止結果を確認中です。", row.StatusText);
        Assert.True(registry.IsPending(channel));
        Assert.Contains(channel, registry.PendingChannelsSnapshot());
        Assert.False(registry.Allow(channel, rule));
        Assert.False(registry.IsStoppedGeneration(channel, session));
        Assert.DoesNotContain("private", row.StatusText);
        Assert.DoesNotContain("pcp:", row.StatusText);
    }

    [Theory]
    [InlineData(RecordingAutomationStopResultKind.Stopped, "停止")]
    [InlineData(RecordingAutomationStopResultKind.NoDecodableVideoStart, "作成されません")]
    [InlineData(RecordingAutomationStopResultKind.Faulted, "保存に失敗")]
    public async Task TypedTerminalCommitsExactPairAndBoundedJapaneseFeedback(RecordingAutomationStopResultKind kind, string notice)
    {
        var registry = new GenesisAutomaticRecordingSuppression();
        var c = new ChannelId(Guid.NewGuid()); var s = new RecordingSessionId(Guid.NewGuid()); var r = Guid.NewGuid();
        var entry = registry.Reserve(c, s)!; registry.Bind(entry, Receipt(c, s, r), () => Task.CompletedTask);
        await Task.WhenAll(registry.Observers()); registry.Commit(entry, kind);
        var row = Assert.Single(registry.Snapshot());
        Assert.Equal(c, row.ChannelId); Assert.Equal(r, row.RuleId);
        Assert.Equal(GenesisAutomaticRecordingSuppressionState.Committed, row.State);
        Assert.Contains(notice, row.StatusText); Assert.True(row.StatusText.Length < 120);
        Assert.DoesNotContain("pcp:", row.StatusText); Assert.DoesNotContain("C:\\", row.StatusText);
        Assert.DoesNotContain("Exception", row.StatusText);
        Assert.Null(registry.Reserve(c, s));
        registry.Rollback(entry); Assert.Single(registry.Snapshot()); // cannot undo admission
        registry.Clear(); Assert.Empty(registry.Snapshot());
        Assert.Empty(new GenesisAutomaticRecordingSuppression().Snapshot());
    }

    [Fact]
    public async Task AllowRemovesExactPairOnlyAndObservedDisableDeleteClearAbsentChannels()
    {
        var registry = new GenesisAutomaticRecordingSuppression(); var r = Guid.NewGuid(); var other = Guid.NewGuid();
        var c = new ChannelId(Guid.NewGuid()); var d = new ChannelId(Guid.NewGuid()); var e = new ChannelId(Guid.NewGuid());
        foreach (var (channel, rule) in new[] { (c, r), (d, r), (e, other) })
        {
            var s = new RecordingSessionId(Guid.NewGuid()); var entry = registry.Reserve(channel, s)!;
            registry.Bind(entry, Receipt(channel, s, rule), () => Task.CompletedTask); registry.Commit(entry, RecordingAutomationStopResultKind.Stopped);
        }
        await Task.WhenAll(registry.Observers());
        Assert.False(registry.Allow(c, other)); Assert.True(registry.Allow(c, r)); Assert.False(registry.Allow(c, r));
        Assert.Equal(2, registry.Snapshot().Count);
        registry.ClearUnobservedRules(new([new RecordingRule(other, "kept", RecordingRuleAction.Record, RecordingRuleCriteria.MatchAll)]));
        Assert.Equal(e, Assert.Single(registry.Snapshot()).ChannelId);
        registry.ClearUnobservedRules(new([new RecordingRule(other, "disabled", RecordingRuleAction.Record, RecordingRuleCriteria.MatchAll, isEnabled: false)]));
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public async Task SuppressedProjectionHasNoSessionAndCannotInheritActiveCommands()
    {
        var channel = new ChannelId(Guid.NewGuid()); var rule = Guid.NewGuid();
        var suppressed = new GenesisAutomaticRecordingSuppressionRow(channel, rule,
            GenesisAutomaticRecordingSuppressionState.Committed, "自動録画を停止しました。");
        var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        tabs.SelectedTab = GenesisGuiNextTab.Recording; tabs.UpdateSuppression([suppressed]);
        var native = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.Null(native.ActiveRecording); Assert.Null(native.ActiveManualRecording);
        Assert.Null(native.InteractiveChannel); Assert.Null(native.RecordingRule); Assert.False(native.IsInteractive);
        Assert.Same(suppressed, native.Suppression);
        Assert.Contains(channel.Value.ToString("N").ToUpperInvariant(), native.DisplayChannel.Name);
        Assert.Contains(rule.ToString("D"), native.DisplayChannel.Description);
        var vm = new GenesisMainViewModel(new FakePlaybackSessionController());
        Assert.False(await GenesisGuiNextRecordingCommandDispatch.StopRecordingRowAsync(vm, native));
        Assert.False(GenesisGuiNextRecordingCommandPresentation.Resolve(vm, GenesisGuiNextTab.Recording, native).IsRecordEnabled);
        tabs.UpdateSuppression([]); Assert.Empty(GenesisGuiNextNativeTabProjection.Create(tabs));
    }
}
