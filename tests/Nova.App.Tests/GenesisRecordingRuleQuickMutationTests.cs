using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRecordingRuleEnableDisableTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnableDisableChangesOnlyEnabledStateAndKeepsIdentitySelection(bool enable)
    {
        var other = RuleMutationFixture.Rule("same name", true);
        var target = RuleMutationFixture.Rule("same name", !enable, RuleMutationFixture.AllCriteria());
        await using var h = await RuleMutationFixture.CreateAsync([other, target]);
        var original = h.Tabs.RecordingRulesSnapshot!;
        var result = await h.Act(target.Id, enable ? GenesisRecordingRuleQuickAction.Enable : GenesisRecordingRuleQuickAction.Disable);
        Assert.True(result.Applied);
        Assert.Equal(1, h.Writes);
        Assert.Same(original.Rules!.Rules[0], h.Rules[0]);
        var updated = h.Rules[1];
        Assert.Equal(target.Id, updated.Id);
        Assert.Equal(target.Name, updated.Name);
        Assert.Equal(target.Action, updated.Action);
        Assert.Equal(target.Priority, updated.Priority);
        Assert.Same(original.Rules!.Rules[1].Criteria, updated.Criteria);
        Assert.Equal(enable, updated.IsEnabled);
        Assert.Equal(target.Id, h.Tabs.RecordingRuleSelectionAfterSave);
        Assert.NotEqual(original.Revision, h.Tabs.RecordingRulesRevision);
        var restored = (await h.Repository.GetSnapshotAsync()).Rules!;
        Assert.Equal(updated, restored.Rules[1]);
        Assert.Equal(other, restored.Rules[0]);
        Assert.False(h.Tabs.CanEditRecordingRule(target.Id));
        Assert.True(h.Tabs.CanQuickMutateRecordingRule(target.Id));
        Assert.Equal(enable ? "無効化" : "有効化", h.Tabs.RecordingRuleToggleLabel(target.Id));
    }

    [Theory]
    [InlineData(RecordingRuleAction.Record)]
    [InlineData(RecordingRuleAction.Ignore)]
    public async Task EnableMatchAllRequiresConfirmationAndNoRejectWrite(RecordingRuleAction action)
    {
        var rule = RuleMutationFixture.Rule("broad", false, RecordingRuleCriteria.MatchAll, action);
        await using var h = await RuleMutationFixture.CreateAsync([rule]);
        var snapshot = h.Tabs.RecordingRulesSnapshot;
        h.Confirm = (confirmation, _) =>
        {
            Assert.Contains("すべてのチャンネル", confirmation.Message);
            Assert.Contains("次回", confirmation.Message);
            Assert.Contains(action == RecordingRuleAction.Record ? "自動録画の対象" : "除外", confirmation.Message);
            return ValueTask.FromResult(false);
        };
        Assert.False((await h.Act(rule.Id, GenesisRecordingRuleQuickAction.Enable)).Applied);
        Assert.Equal(1, h.Confirmations);
        Assert.Equal(0, h.Writes);
        Assert.Same(snapshot, h.Tabs.RecordingRulesSnapshot);
        h.Confirm = RuleMutationFixture.Yes;
        Assert.True((await h.Act(rule.Id, GenesisRecordingRuleQuickAction.Enable)).Applied);
    }

    [Theory]
    [InlineData(0, RecordingRuleAction.Record)]
    [InlineData(1, RecordingRuleAction.Record)]
    [InlineData(2, RecordingRuleAction.Record)]
    [InlineData(3, RecordingRuleAction.Record)]
    [InlineData(4, RecordingRuleAction.Record)]
    [InlineData(5, RecordingRuleAction.Record)]
    [InlineData(0, RecordingRuleAction.Ignore)]
    [InlineData(1, RecordingRuleAction.Ignore)]
    [InlineData(2, RecordingRuleAction.Ignore)]
    [InlineData(3, RecordingRuleAction.Ignore)]
    [InlineData(4, RecordingRuleAction.Ignore)]
    [InlineData(5, RecordingRuleAction.Ignore)]
    public async Task EveryCoreRegexRequiresConfirmationForBothActions(int field, RecordingRuleAction action)
    {
        var p = new RecordingTextPattern("^test$", RecordingTextMatchMode.Regex);
        var c = new RecordingRuleCriteria(name: field == 0 ? p : null, genre: field == 1 ? p : null,
            description: field == 2 ? p : null, contentType: field == 3 ? p : null,
            streamType: field == 4 ? p : null, comment: field == 5 ? p : null);
        var rule = RuleMutationFixture.Rule("regex", false, c, action);
        await using var h = await RuleMutationFixture.CreateAsync([rule]);
        h.Confirm = (confirmation, _) => { Assert.Contains("正規表現", confirmation.Message); return ValueTask.FromResult(false); };
        Assert.False((await h.Act(rule.Id, GenesisRecordingRuleQuickAction.Enable)).Applied);
        Assert.Equal(0, h.Writes);
        Assert.Equal(1, h.Confirmations);
        h.Confirm = RuleMutationFixture.Yes;
        Assert.True((await h.Act(rule.Id, GenesisRecordingRuleQuickAction.Enable)).Applied);
        var expected = new RecordingRule(rule.Id, rule.Name, rule.Action, c, rule.Priority, true);
        Assert.Equal(RecordingRuleJsonCodec.Serialize(new([expected])),
            RecordingRuleJsonCodec.Serialize((await h.Repository.GetSnapshotAsync()).Rules!));
    }

    [Fact]
    public async Task NarrowEnableAndBroadDisableDoNotRequireConfirmation()
    {
        var narrow = RuleMutationFixture.Rule("narrow", false);
        var broad = RuleMutationFixture.Rule("broad", true, RecordingRuleCriteria.MatchAll);
        await using var h = await RuleMutationFixture.CreateAsync([narrow, broad]);
        h.Confirm = (_, _) => throw new InvalidOperationException("confirmation unexpected");
        Assert.True((await h.Act(narrow.Id, GenesisRecordingRuleQuickAction.Enable)).Applied);
        Assert.True((await h.Act(broad.Id, GenesisRecordingRuleQuickAction.Disable)).Applied);
        Assert.Equal(0, h.Confirmations);
    }

    [Theory]
    [InlineData((int)GenesisRecordingRuleQuickAction.Enable)]
    [InlineData((int)GenesisRecordingRuleQuickAction.Disable)]
    [InlineData((int)GenesisRecordingRuleQuickAction.Delete)]
    public async Task ConflictAdoptsLatestSnapshotWithoutRetryAndResolvesSelection(int actionValue)
    {
        var action = (GenesisRecordingRuleQuickAction)actionValue;
        var rule = RuleMutationFixture.Rule("old", action == GenesisRecordingRuleQuickAction.Disable);
        await using var h = await RuleMutationFixture.CreateAsync([rule]);
        var external = new RecordingRule(rule.Id, "external", rule.Action, rule.Criteria, 900, rule.IsEnabled);
        await h.External([external]);
        var bytes = await File.ReadAllBytesAsync(h.Path);
        var result = await h.Act(rule.Id, action);
        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Conflict, result.Status);
        Assert.Equal(0, h.Writes);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(h.Path));
        Assert.Equal(external, Assert.Single(h.Rules));
        Assert.Equal(rule.Id, h.Tabs.RecordingRuleSelectionAfterSave);
        Assert.Contains("最新の内容", result.Message);
    }
}

public sealed class GenesisRecordingRuleDeleteTests
{
    [Fact]
    public async Task DeleteRemovesExactIdentityAndPreservesRemainingOrderAndUnsupportedContents()
    {
        var a = RuleMutationFixture.Rule("same", false, RuleMutationFixture.AllCriteria());
        var b = RuleMutationFixture.Rule("same", true, RuleMutationFixture.AllCriteria());
        var c = RuleMutationFixture.Rule("same", false);
        await using var h = await RuleMutationFixture.CreateAsync([a, b, c]);
        h.Confirm = (confirmation, _) =>
        {
            Assert.Equal(b.Id, confirmation.RuleId);
            Assert.Equal(b.Name, confirmation.Name);
            Assert.Equal(b.Action, confirmation.Action);
            Assert.True(confirmation.IsEnabled);
            Assert.Contains("ルール名: same", confirmation.Message);
            Assert.Contains("動作: 録画", confirmation.Message);
            Assert.Contains("状態: 有効", confirmation.Message);
            Assert.Contains("次回のチャンネル更新", confirmation.Message);
            return ValueTask.FromResult(true);
        };
        Assert.True((await h.Act(b.Id, GenesisRecordingRuleQuickAction.Delete)).Applied);
        Assert.Equal(1, h.Confirmations);
        Assert.Equal([a, c], h.Rules);
        Assert.Null(h.Tabs.RecordingRuleSelectionAfterSave);
        Assert.Equal([a, c], (await h.Repository.GetSnapshotAsync()).Rules!.Rules);
    }

    [Fact]
    public async Task RejectingDeletePreservesFileSnapshotAndSelection()
    {
        var rule = RuleMutationFixture.Rule("disabled", false);
        await using var h = await RuleMutationFixture.CreateAsync([rule]);
        Assert.True((await h.Act(rule.Id, GenesisRecordingRuleQuickAction.Enable)).Applied);
        var snapshot = h.Tabs.RecordingRulesSnapshot;
        var bytes = await File.ReadAllBytesAsync(h.Path);
        h.Confirm = (_, _) => ValueTask.FromResult(false);
        Assert.False((await h.Act(rule.Id, GenesisRecordingRuleQuickAction.Delete)).Applied);
        Assert.Equal(1, h.Writes);
        Assert.Same(snapshot, h.Tabs.RecordingRulesSnapshot);
        Assert.Equal(rule.Id, h.Tabs.RecordingRuleSelectionAfterSave);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(h.Path));
    }

    [Fact]
    public async Task DeleteLastRulePersistsValidEmptySchemaNotMissingFile()
    {
        var rule = RuleMutationFixture.Rule("last", false);
        await using var h = await RuleMutationFixture.CreateAsync([rule]);
        Assert.True((await h.Act(rule.Id, GenesisRecordingRuleQuickAction.Delete)).Applied);
        Assert.True(File.Exists(h.Path));
        var snapshot = await h.Repository.GetSnapshotAsync();
        Assert.Equal(GenesisRecordingRuleSnapshotStatus.Available, snapshot.Status);
        Assert.NotEqual(GenesisRecordingRuleRevision.Missing, snapshot.Revision);
        Assert.Empty(RecordingRuleJsonCodec.Deserialize(await File.ReadAllTextAsync(h.Path)).Rules);
        Assert.Null(h.Tabs.RecordingRuleSelectionAfterSave);
    }

    [Theory]
    [InlineData((int)GenesisRecordingRuleQuickAction.Enable)]
    [InlineData((int)GenesisRecordingRuleQuickAction.Disable)]
    [InlineData((int)GenesisRecordingRuleQuickAction.Delete)]
    public async Task ExternalRemovalConflictsAndClearsSelection(int actionValue)
    {
        var action = (GenesisRecordingRuleQuickAction)actionValue;
        var rule = RuleMutationFixture.Rule("removed", action == GenesisRecordingRuleQuickAction.Disable);
        await using var h = await RuleMutationFixture.CreateAsync([rule]);
        await h.External([]);
        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Conflict, (await h.Act(rule.Id, action)).Status);
        Assert.Empty(h.Rules);
        Assert.Null(h.Tabs.RecordingRuleSelectionAfterSave);
        Assert.Equal(0, h.Writes);
    }

    [Fact]
    public async Task DeleteMissingSelectionDoesNotSaveOrConfirm()
    {
        await using var h = await RuleMutationFixture.CreateAsync([RuleMutationFixture.Rule("other", false)]);
        Assert.False((await h.Act(Guid.NewGuid(), GenesisRecordingRuleQuickAction.Delete)).Applied);
        Assert.Equal(0, h.Writes);
        Assert.Equal(0, h.Confirmations);
    }
}

public sealed class GenesisRecordingRuleQuickMutationContractTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("malformed")]
    [InlineData("version")]
    [InlineData("io")]
    [InlineData("cancelled")]
    public async Task UnavailableSnapshotsCannotMutateOrCreateFiles(string state)
    {
        await using var h = await RuleMutationFixture.CreateAsync(null);
        if (state == "empty") await h.External([]);
        if (state == "malformed") await File.WriteAllTextAsync(h.Path, "invalid");
        if (state == "version") await File.WriteAllTextAsync(h.Path, "{\"version\":2,\"rules\":[]}");
        if (state == "io") Directory.CreateDirectory(h.Path);
        if (state == "cancelled") await h.Repository.DisposeAsync();
        await h.Tabs.ReloadRecordingRulesAsync();
        foreach (var action in Enum.GetValues<GenesisRecordingRuleQuickAction>())
        {
            Assert.False(h.Tabs.CanQuickMutateRecordingRule(null));
            Assert.False((await h.Act(null, action)).Applied);
            Assert.False((await h.Act(Guid.NewGuid(), action)).Applied);
        }
        Assert.Equal(0, h.Writes);
        Assert.Equal(0, h.Confirmations);
        if (state == "missing") Assert.False(File.Exists(h.Path));
    }

    [Fact]
    public async Task CommandsRequireRulesModeAndCurrentSelectedIdentity()
    {
        var rule = RuleMutationFixture.Rule("rule", false);
        await using var h = await RuleMutationFixture.CreateAsync([rule]);
        Assert.True(h.Tabs.CanQuickMutateRecordingRule(rule.Id));
        Assert.False(h.Tabs.CanQuickMutateRecordingRule(Guid.NewGuid()));
        await h.Tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.ActiveRecordings);
        Assert.False((await h.Act(rule.Id, GenesisRecordingRuleQuickAction.Enable)).Applied);
        h.Tabs.SelectedTab = GenesisGuiNextTab.Channels;
        Assert.False(h.Tabs.CanQuickMutateRecordingRule(rule.Id));
        Assert.Equal(0, h.Writes);
    }

    [Theory]
    [InlineData((int)GenesisRecordingRuleRepositoryWriteStatus.Invalid)]
    [InlineData((int)GenesisRecordingRuleRepositoryWriteStatus.PersistenceFailed)]
    [InlineData((int)GenesisRecordingRuleRepositoryWriteStatus.Cancelled)]
    [InlineData((int)GenesisRecordingRuleRepositoryWriteStatus.Unavailable)]
    public async Task FailedSaveNeverPublishesProposedRulesOrRawException(int statusValue)
    {
        var status = (GenesisRecordingRuleRepositoryWriteStatus)statusValue;
        var rule = RuleMutationFixture.Rule("original", false);
        await using var h = await RuleMutationFixture.CreateAsync([rule], (_, _, _) => status switch
        {
            GenesisRecordingRuleRepositoryWriteStatus.PersistenceFailed => throw new IOException("PRIVATE RAW ERROR"),
            GenesisRecordingRuleRepositoryWriteStatus.Cancelled => throw new OperationCanceledException("PRIVATE RAW ERROR"),
            _ => ValueTask.CompletedTask,
        });
        var snapshot = h.Tabs.RecordingRulesSnapshot!;
        GenesisRecordingRuleEditorResult result;
        if (status == GenesisRecordingRuleRepositoryWriteStatus.Invalid)
        {
            using var reservation = h.Tabs.RuleMutations.TryReserve()!;
            var invalid = new RecordingRule(rule.Id, new string('x', RecordingRuleFileStore.MaximumFileByteCount + 1), rule.Action, rule.Criteria);
            result = await h.Tabs.RuleMutations.SaveAsync(reservation, snapshot, new([invalid]), rule.Id, rule.Id, reservation.Token);
        }
        else
        {
            if (status == GenesisRecordingRuleRepositoryWriteStatus.Unavailable) await File.WriteAllTextAsync(h.Path, "invalid");
            result = await h.Act(rule.Id, GenesisRecordingRuleQuickAction.Enable);
        }
        Assert.Equal(status, result.Status);
        Assert.False(result.Applied);
        Assert.DoesNotContain("PRIVATE RAW ERROR", result.Message);
        if (status == GenesisRecordingRuleRepositoryWriteStatus.Unavailable)
            Assert.False(h.Tabs.CanQuickMutateRecordingRule(rule.Id));
        else Assert.Same(snapshot, h.Tabs.RecordingRulesSnapshot);
    }

    [Fact]
    public async Task SameEnabledStateIsNoChangeWithoutWrite()
    {
        var rule = RuleMutationFixture.Rule("enabled", true);
        await using var h = await RuleMutationFixture.CreateAsync([rule]);
        Assert.False((await h.Act(rule.Id, GenesisRecordingRuleQuickAction.Enable)).Applied);
        Assert.Equal(0, h.Writes);
    }
}

public sealed class GenesisRecordingRuleMutationLifecycleTests
{
    [Theory]
    [InlineData((int)GenesisRecordingRuleQuickAction.Enable)]
    [InlineData((int)GenesisRecordingRuleQuickAction.Disable)]
    [InlineData((int)GenesisRecordingRuleQuickAction.Delete)]
    public async Task ConcurrentQuickCommandsAndEditorAreExcludedUntilSaveCompletes(int actionValue)
    {
        var action = (GenesisRecordingRuleQuickAction)actionValue;
        var entered = RuleMutationFixture.Signal(); var release = RuleMutationFixture.Signal();
        var rule = RuleMutationFixture.Rule("target", action == GenesisRecordingRuleQuickAction.Disable);
        await using var h = await RuleMutationFixture.CreateAsync([rule], async (path, rules, token) =>
        {
            entered.SetResult(); await release.Task.WaitAsync(token); await RecordingRuleFileStore.SaveAsync(path, rules, token);
        });
        var first = h.Act(rule.Id, action);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False((await h.Act(rule.Id, action)).Applied);
        Assert.False((await h.Act(rule.Id, GenesisRecordingRuleQuickAction.Delete)).Applied);
        Assert.False(h.Tabs.CanCreateRecordingRule);
        Assert.Null(h.Tabs.CreateRecordingRule());
        Assert.Null(h.Tabs.EditRecordingRule(rule.Id));
        release.SetResult();
        Assert.True((await first).Applied);
        Assert.Equal(1, h.Writes);
    }

    [Fact]
    public async Task LoadingRulesDisablesQuickMutationsAndSavePublicationSupersedesOldLoad()
    {
        var entered = RuleMutationFixture.Signal(); var release = RuleMutationFixture.Signal();
        var rule = RuleMutationFixture.Rule("target", false);
        await using var h = await RuleMutationFixture.CreateAsync([rule], async (path, rules, token) =>
        {
            entered.SetResult(); await release.Task.WaitAsync(token); await RecordingRuleFileStore.SaveAsync(path, rules, token);
        });
        var save = h.Act(rule.Id, GenesisRecordingRuleQuickAction.Enable);
        await entered.Task;
        var load = h.Tabs.ReloadRecordingRulesAsync();
        Assert.True(h.Tabs.IsRecordingRulesLoading);
        Assert.False(h.Tabs.CanQuickMutateRecordingRule(rule.Id));
        Assert.False((await h.Act(rule.Id, GenesisRecordingRuleQuickAction.Delete)).Applied);
        release.SetResult();
        Assert.True((await save).Applied);
        await load;
        Assert.True(Assert.Single(h.Rules).IsEnabled);
        Assert.Equal(1, h.Writes);
    }

    [Fact]
    public async Task ConfirmationOwnsSameMutationReservationAndRejectsWithoutSave()
    {
        var entered = RuleMutationFixture.Signal(); var release = RuleMutationFixture.Signal();
        var rule = RuleMutationFixture.Rule("broad", false, RecordingRuleCriteria.MatchAll);
        await using var h = await RuleMutationFixture.CreateAsync([rule]);
        h.Confirm = async (_, token) => { entered.SetResult(); await release.Task.WaitAsync(token); return false; };
        var pending = h.Act(rule.Id, GenesisRecordingRuleQuickAction.Enable);
        await entered.Task;
        Assert.False((await h.Act(rule.Id, GenesisRecordingRuleQuickAction.Delete)).Applied);
        Assert.Null(h.Tabs.CreateRecordingRule());
        release.SetResult();
        Assert.False((await pending).Applied);
        Assert.Equal(0, h.Writes);
        Assert.True(h.Tabs.CanCreateRecordingRule);
    }

    [Fact]
    public async Task QuickMutationCannotOverlapEditorDraftConfirmationOrSave()
    {
        var entered = RuleMutationFixture.Signal(); var release = RuleMutationFixture.Signal();
        var rule = RuleMutationFixture.Rule("target", false);
        await using var h = await RuleMutationFixture.CreateAsync([rule], async (path, rules, token) =>
        {
            entered.SetResult(); await release.Task.WaitAsync(token); await RecordingRuleFileStore.SaveAsync(path, rules, token);
        });
        var draft = h.Tabs.CreateRecordingRule()! with { Name = "broad", MatchAll = true };
        Assert.False((await h.Act(rule.Id, GenesisRecordingRuleQuickAction.Enable)).Applied);
        Assert.True((await h.Tabs.RuleEditor.SaveAsync(draft)).RequiresConfirmation);
        Assert.False((await h.Act(rule.Id, GenesisRecordingRuleQuickAction.Delete)).Applied);
        var pending = h.Tabs.RuleEditor.SaveAsync(draft, true);
        await entered.Task;
        Assert.False((await h.Act(rule.Id, GenesisRecordingRuleQuickAction.Enable)).Applied);
        release.SetResult();
        Assert.True((await pending).Applied);
        Assert.Equal(1, h.Writes);
        Assert.True(h.Tabs.CanQuickMutateRecordingRule(rule.Id));
    }

    [Theory]
    [InlineData((int)GenesisRecordingRuleQuickAction.Enable)]
    [InlineData((int)GenesisRecordingRuleQuickAction.Disable)]
    [InlineData((int)GenesisRecordingRuleQuickAction.Delete)]
    public async Task ShutdownCancelsDrainsAndPreventsLatePublication(int actionValue)
    {
        var action = (GenesisRecordingRuleQuickAction)actionValue;
        var entered = RuleMutationFixture.Signal(); var cancelled = RuleMutationFixture.Signal(); var release = RuleMutationFixture.Signal();
        var rule = RuleMutationFixture.Rule("target", action == GenesisRecordingRuleQuickAction.Disable);
        await using var h = await RuleMutationFixture.CreateAsync([rule], async (_, _, token) =>
        {
            using var registration = token.Register(() => cancelled.TrySetResult());
            entered.SetResult(); await release.Task; // Deliberately completes late, even after cancellation.
        });
        var original = h.Tabs.RecordingRulesSnapshot;
        var pending = h.Act(rule.Id, action);
        await entered.Task;
        var publications = 0;
        var shutdown = h.Tabs.DisposeAsync().AsTask();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Tabs.PropertyChanged += (_, _) => publications++;
        Assert.False(shutdown.IsCompleted);
        Assert.False((await h.Act(rule.Id, action)).Applied);
        release.SetResult();
        await pending; await shutdown;
        Assert.Same(original, h.Tabs.RecordingRulesSnapshot);
        Assert.Equal(0, publications);
        // Tabs drain before the application owner disposes the shared repository.
        Assert.True((await h.Repository.GetSnapshotAsync()).IsAvailable);
    }

    [Fact]
    public async Task ShutdownDuringConfirmationDoesNotWrite()
    {
        var entered = RuleMutationFixture.Signal();
        var rule = RuleMutationFixture.Rule("broad", false, RecordingRuleCriteria.MatchAll);
        await using var h = await RuleMutationFixture.CreateAsync([rule]);
        h.Confirm = async (_, token) => { entered.SetResult(); await RuleMutationFixture.Signal().Task.WaitAsync(token); return true; };
        var pending = h.Act(rule.Id, GenesisRecordingRuleQuickAction.Enable);
        await entered.Task; await h.Tabs.DisposeAsync();
        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Cancelled, (await pending).Status);
        Assert.Equal(0, h.Writes);
    }
}

internal sealed class RuleMutationFixture : IAsyncDisposable
{
    private readonly string directory;
    private RuleMutationFixture(string directory, Func<string, RecordingRuleSet, CancellationToken, ValueTask>? save)
    {
        this.directory = directory; Path = System.IO.Path.Combine(directory, "rules.json");
        Repository = new(Path, async (path, rules, token) => { Writes++; await (save ?? RecordingRuleFileStore.SaveAsync)(path, rules, token); });
        Tabs = new(new(), Repository) { SelectedTab = GenesisGuiNextTab.Recording };
    }
    public string Path { get; }
    public int Writes { get; private set; }
    public int Confirmations { get; private set; }
    public GenesisRecordingRuleRepository Repository { get; }
    public GenesisGuiNextTabsViewModel Tabs { get; }
    public IReadOnlyList<RecordingRule> Rules => Tabs.RecordingRulesSnapshot?.Rules?.Rules ?? [];
    public Func<GenesisRecordingRuleConfirmation, CancellationToken, ValueTask<bool>> Confirm { get; set; } = Yes;
    public static ValueTask<bool> Yes(GenesisRecordingRuleConfirmation _, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); return ValueTask.FromResult(true);
    }
    public Task<GenesisRecordingRuleEditorResult> Act(Guid? id, GenesisRecordingRuleQuickAction action) =>
        Tabs.QuickMutateRecordingRuleAsync(id, action, (c, token) => { Confirmations++; return Confirm(c, token); });
    public Task External(RecordingRule[] rules) => File.WriteAllTextAsync(Path, RecordingRuleJsonCodec.Serialize(new(rules)));
    public static async Task<RuleMutationFixture> CreateAsync(RecordingRule[]? rules,
        Func<string, RecordingRuleSet, CancellationToken, ValueTask>? save = null)
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NovaQuickRuleTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var h = new RuleMutationFixture(directory, save);
        if (rules is not null) await h.External(rules);
        await h.Tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Rules);
        return h;
    }
    public static RecordingRule Rule(string name, bool enabled, RecordingRuleCriteria? criteria = null,
        RecordingRuleAction action = RecordingRuleAction.Record, int priority = 20) =>
        new(Guid.NewGuid(), name, action, criteria ?? new(name: new("test", RecordingTextMatchMode.Exact)), priority, enabled);
    public static RecordingRuleCriteria AllCriteria() => new(
        channelId: new ChannelId(Guid.NewGuid()), name: new("name"), genre: new("genre"), description: new("description"),
        contentType: new("video"), streamType: new("FLV"), comment: new("comment"), minimumBitrateKbps: 100, maximumBitrateKbps: 900);
    public static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async ValueTask DisposeAsync()
    {
        await Tabs.DisposeAsync(); await Repository.DisposeAsync(); Directory.Delete(directory, true);
    }
}
