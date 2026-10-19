using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRecordingRuleReorderTransformationTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task AdjacentReorderPreservesEveryFieldAndSelection(int actionValue)
    {
        var action = (GenesisRecordingRuleQuickAction)actionValue;
        var a = RuleMutationFixture.Rule("same name", true, priority: 200);
        var target = RuleMutationFixture.Rule("same name", false,
            new(genre: new("^genre$", RecordingTextMatchMode.Regex), contentType: new("video"), comment: new("comment")), RecordingRuleAction.Ignore, 100);
        var c = RuleMutationFixture.Rule("third", true, RecordingRuleCriteria.MatchAll, priority: 200);
        var d = RuleMutationFixture.Rule("unaffected", true, RuleMutationFixture.AllCriteria());
        await using var h = await RuleMutationFixture.CreateAsync([a, target, c, d]);
        var original = h.Rules.ToArray();
        var expected = action == GenesisRecordingRuleQuickAction.MoveUp ? new[] { target.Id, a.Id, c.Id, d.Id } : [a.Id, c.Id, target.Id, d.Id];
        Assert.False(h.Tabs.CanEditRecordingRule(target.Id));
        Assert.True(h.Tabs.CanMoveRecordingRule(target.Id, action));
        Assert.True((await h.Act(target.Id, action)).Applied);
        Assert.Equal(expected, h.Rules.Select(r => r.Id));
        foreach (var rule in h.Rules) Assert.Same(original.Single(r => r.Id == rule.Id), rule);
        Assert.Equal(RecordingRuleJsonCodec.Serialize(new(h.Rules)), await File.ReadAllTextAsync(h.Path));
        Assert.Equal(target.Id, h.Tabs.RecordingRuleSelectionAfterSave);
        Assert.Equal(expected, h.Tabs.RecordingRuleRows.Select(r => r.RuleId));
        Assert.Equal(Enumerable.Range(1, 4), h.Tabs.RecordingRuleRows.Select(r => r.PersistedIndex));
        var restored = (await h.Repository.GetSnapshotAsync()).Rules!;
        Assert.Equal(RecordingRuleJsonCodec.Serialize(new(h.Rules)), RecordingRuleJsonCodec.Serialize(restored));
        Assert.Equal(1, h.Writes);
        Assert.Equal(0, h.Confirmations);
    }

    [Fact]
    public async Task PositionalAndModeBoundariesRejectMovesWithoutSave()
    {
        var a = RuleMutationFixture.Rule("a", true);
        var b = RuleMutationFixture.Rule("b", true);
        var c = RuleMutationFixture.Rule("c", true);
        await using var h = await RuleMutationFixture.CreateAsync([a, b, c]);
        Assert.False(h.Tabs.CanMoveRecordingRule(a.Id, GenesisRecordingRuleQuickAction.MoveUp));
        Assert.False(h.Tabs.CanMoveRecordingRule(c.Id, GenesisRecordingRuleQuickAction.MoveDown));
        Assert.True(h.Tabs.CanMoveRecordingRule(b.Id, GenesisRecordingRuleQuickAction.MoveUp));
        Assert.True(h.Tabs.CanMoveRecordingRule(b.Id, GenesisRecordingRuleQuickAction.MoveDown));
        foreach (var (id, action) in new[] { (a.Id, GenesisRecordingRuleQuickAction.MoveUp), (c.Id, GenesisRecordingRuleQuickAction.MoveDown), (Guid.NewGuid(), GenesisRecordingRuleQuickAction.MoveUp) })
        {
            Assert.False((await h.Act(id, action)).Applied);
            Assert.False((await h.Tabs.RuleMutations.ExecuteAsync(h.Tabs.RecordingRulesSnapshot!, id, action, RuleMutationFixture.Yes)).Applied);
        }
        Assert.False((await h.Act(null, GenesisRecordingRuleQuickAction.MoveDown)).Applied);
        await h.Tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.ActiveRecordings);
        Assert.False(h.Tabs.CanMoveRecordingRule(b.Id, GenesisRecordingRuleQuickAction.MoveUp));
        Assert.False((await h.Act(b.Id, GenesisRecordingRuleQuickAction.MoveDown)).Applied);
        Assert.Equal(0, h.Writes);
        Assert.Equal(0, h.Confirmations);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("malformed")]
    [InlineData("version")]
    [InlineData("io")]
    [InlineData("cancelled")]
    public async Task UnavailableSnapshotsRejectBothReorderDirections(string state)
    {
        await using var h = await RuleMutationFixture.CreateAsync(null);
        if (state == "empty") await h.External([]);
        if (state == "malformed") await File.WriteAllTextAsync(h.Path, "invalid");
        if (state == "version") await File.WriteAllTextAsync(h.Path, "{\"version\":2,\"rules\":[]}");
        if (state == "io") Directory.CreateDirectory(h.Path);
        if (state == "cancelled") await h.Repository.DisposeAsync();
        await h.Tabs.ReloadRecordingRulesAsync();
        foreach (var action in new[] { GenesisRecordingRuleQuickAction.MoveUp, GenesisRecordingRuleQuickAction.MoveDown })
        {
            Assert.False(h.Tabs.CanMoveRecordingRule(null, action));
            Assert.False(h.Tabs.CanMoveRecordingRule(Guid.NewGuid(), action));
            Assert.False((await h.Act(Guid.NewGuid(), action)).Applied);
        }
        Assert.Equal(0, h.Writes);
        Assert.Equal(0, h.Confirmations);
    }
}

public sealed class GenesisRecordingRuleReorderConfirmationTests
{
    [Theory]
    [InlineData(3, true, true, true)]
    [InlineData(4, true, true, true)]
    [InlineData(3, true, true, false)]
    [InlineData(4, true, true, false)]
    [InlineData(3, false, true, true)]
    [InlineData(4, false, true, true)]
    [InlineData(3, true, false, true)]
    [InlineData(4, true, false, true)]
    public async Task ConfirmationDependsOnlyOnEnabledAdjacentPriority(int actionValue, bool enabled, bool neighborEnabled, bool equalPriority)
    {
        var action = (GenesisRecordingRuleQuickAction)actionValue;
        var target = RuleMutationFixture.Rule("target", enabled, RecordingRuleCriteria.MatchAll, RecordingRuleAction.Record, 200);
        var neighbor = RuleMutationFixture.Rule("neighbor", neighborEnabled, new(comment: new(".*", RecordingTextMatchMode.Regex)), RecordingRuleAction.Ignore, equalPriority ? 200 : 100);
        await using var h = await RuleMutationFixture.CreateAsync(action == GenesisRecordingRuleQuickAction.MoveUp ? [neighbor, target] : [target, neighbor]);
        var requires = enabled && neighborEnabled && equalPriority;
        var snapshot = h.Tabs.RecordingRulesSnapshot;
        var bytes = await File.ReadAllBytesAsync(h.Path);
        h.Confirm = (confirmation, _) =>
        {
            Assert.Equal(target.Id, confirmation.RuleId);
            Assert.Contains("同じ優先度の有効", confirmation.Message);
            Assert.Contains("次回", confirmation.Message);
            Assert.Contains("可能性", confirmation.Message);
            Assert.DoesNotContain("停止します", confirmation.Message);
            return ValueTask.FromResult(false);
        };
        var result = await h.Act(target.Id, action);
        Assert.Equal(requires ? 1 : 0, h.Confirmations);
        Assert.Equal(!requires, result.Applied);
        if (requires)
        {
            Assert.Same(snapshot, h.Tabs.RecordingRulesSnapshot);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(h.Path));
            Assert.Equal(0, h.Writes);
            h.Confirm = RuleMutationFixture.Yes;
            Assert.True((await h.Act(target.Id, action)).Applied);
            Assert.Equal(2, h.Confirmations);
        }
        Assert.Equal(1, h.Writes);
        Assert.Equal(target.Id, h.Tabs.RecordingRuleSelectionAfterSave);
    }
}

public sealed class GenesisRecordingRuleReorderProjectionConflictTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task DelayedSaveKeepsPersistedProjectionUntilApplied(int actionValue)
    {
        var action = (GenesisRecordingRuleQuickAction)actionValue;
        var entered = RuleMutationFixture.Signal(); var release = RuleMutationFixture.Signal();
        var a = RuleMutationFixture.Rule("a", false); var b = RuleMutationFixture.Rule("b", false);
        await using var h = await RuleMutationFixture.CreateAsync([a, b], async (path, rules, token) =>
        {
            entered.SetResult(); await release.Task.WaitAsync(token); await RecordingRuleFileStore.SaveAsync(path, rules, token);
        });
        var snapshot = h.Tabs.RecordingRulesSnapshot; var rows = h.Tabs.RecordingRuleRows;
        var target = action == GenesisRecordingRuleQuickAction.MoveUp ? b : a;
        var pending = h.Act(target.Id, action); await entered.Task;
        Assert.Same(snapshot, h.Tabs.RecordingRulesSnapshot);
        Assert.Same(rows, h.Tabs.RecordingRuleRows);
        Assert.Equal(new[] { a.Id, b.Id }, RecordingRuleJsonCodec.Deserialize(await File.ReadAllTextAsync(h.Path)).Rules.Select(r => r.Id));
        release.SetResult(); Assert.True((await pending).Applied);
        Assert.Equal(new[] { b.Id, a.Id }, h.Tabs.RecordingRuleRows.Select(r => r.RuleId));
        Assert.Equal(target.Id, h.Tabs.RecordingRuleSelectionAfterSave);
    }

    [Theory]
    [InlineData("order")]
    [InlineData("removed")]
    [InlineData("format")]
    public async Task StaleAdjacencyConflictsWithoutRebaseAndAdoptsLatestIdentity(string change)
    {
        var a = RuleMutationFixture.Rule("a", true); var b = RuleMutationFixture.Rule("b", true); var c = RuleMutationFixture.Rule("c", true);
        await using var h = await RuleMutationFixture.CreateAsync([a, b, c]);
        var old = h.Tabs.RecordingRulesSnapshot;
        byte[]? external = null;
        h.Confirm = async (_, token) =>
        {
            if (change == "format") await File.AppendAllTextAsync(h.Path, "\n ", token);
            else await h.External(change == "removed" ? [c, a] : [c, a, b]);
            external = await File.ReadAllBytesAsync(h.Path, token);
            return true;
        };
        var result = await h.Act(b.Id, GenesisRecordingRuleQuickAction.MoveDown);
        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Conflict, result.Status);
        Assert.False(result.Applied);
        Assert.Equal(0, h.Writes);
        Assert.Equal(external, await File.ReadAllBytesAsync(h.Path));
        Assert.NotEqual(old!.Revision, h.Tabs.RecordingRulesRevision);
        Assert.Equal(change == "format" ? new[] { a.Id, b.Id, c.Id } : change == "removed" ? [c.Id, a.Id] : [c.Id, a.Id, b.Id], h.Rules.Select(r => r.Id));
        Assert.Equal(change == "removed" ? (Guid?)null : b.Id, h.Tabs.RecordingRuleSelectionAfterSave);
        Assert.Equal(GenesisRecordingRuleMutationController.ConflictMessage, result.Message);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task FailedReorderNeverPublishesProposedOrder(int statusValue)
    {
        var status = (GenesisRecordingRuleRepositoryWriteStatus)statusValue;
        var a = RuleMutationFixture.Rule("a", false); var b = RuleMutationFixture.Rule("b", false);
        await using var h = await RuleMutationFixture.CreateAsync([a, b], (_, _, _) => status switch
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
            var invalid = new RecordingRule(b.Id, new string('x', RecordingRuleFileStore.MaximumFileByteCount + 1), b.Action, b.Criteria);
            result = await h.Tabs.RuleMutations.SaveAsync(reservation, snapshot, new([invalid, a]), b.Id, b.Id, reservation.Token);
        }
        else
        {
            if (status == GenesisRecordingRuleRepositoryWriteStatus.Unavailable) await File.WriteAllTextAsync(h.Path, "invalid");
            result = await h.Act(b.Id, GenesisRecordingRuleQuickAction.MoveUp);
        }
        Assert.Equal(status, result.Status);
        Assert.False(result.Applied);
        Assert.DoesNotContain("PRIVATE RAW ERROR", result.Message);
        if (status == GenesisRecordingRuleRepositoryWriteStatus.Unavailable)
            Assert.False(h.Tabs.CanMoveRecordingRule(b.Id, GenesisRecordingRuleQuickAction.MoveUp));
        else Assert.Same(snapshot, h.Tabs.RecordingRulesSnapshot);
    }
}

public sealed class GenesisRecordingRuleReorderSingleFlightTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EnableDisableDeleteReservationExcludesBothReorderDirections(int actionValue)
    {
        var action = (GenesisRecordingRuleQuickAction)actionValue;
        var entered = RuleMutationFixture.Signal(); var release = RuleMutationFixture.Signal();
        var a = RuleMutationFixture.Rule("a", false);
        var b = RuleMutationFixture.Rule("b", action == GenesisRecordingRuleQuickAction.Disable);
        var c = RuleMutationFixture.Rule("c", false);
        await using var h = await RuleMutationFixture.CreateAsync([a, b, c], async (path, rules, token) =>
        {
            entered.SetResult(); await release.Task.WaitAsync(token); await RecordingRuleFileStore.SaveAsync(path, rules, token);
        });
        Assert.True(h.Tabs.CanMoveRecordingRule(b.Id, GenesisRecordingRuleQuickAction.MoveUp));
        Assert.True(h.Tabs.CanMoveRecordingRule(b.Id, GenesisRecordingRuleQuickAction.MoveDown));
        var pending = h.Act(b.Id, action); await entered.Task;
        Assert.False((await h.Act(b.Id, GenesisRecordingRuleQuickAction.MoveUp)).Applied);
        Assert.False((await h.Act(b.Id, GenesisRecordingRuleQuickAction.MoveDown)).Applied);
        Assert.False(h.Tabs.CanMoveRecordingRule(b.Id, GenesisRecordingRuleQuickAction.MoveUp));
        Assert.False(h.Tabs.CanMoveRecordingRule(b.Id, GenesisRecordingRuleQuickAction.MoveDown));
        release.SetResult(); Assert.True((await pending).Applied); Assert.Equal(1, h.Writes);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ReorderSaveExcludesAllQuickCommandsAndCreateEdit(int actionValue)
    {
        var action = (GenesisRecordingRuleQuickAction)actionValue;
        var entered = RuleMutationFixture.Signal(); var release = RuleMutationFixture.Signal();
        var a = RuleMutationFixture.Rule("a", false); var b = RuleMutationFixture.Rule("b", false); var c = RuleMutationFixture.Rule("c", false);
        await using var h = await RuleMutationFixture.CreateAsync([a, b, c], async (path, rules, token) =>
        {
            entered.SetResult(); await release.Task.WaitAsync(token); await RecordingRuleFileStore.SaveAsync(path, rules, token);
        });
        var pending = h.Act(b.Id, action); await entered.Task;
        foreach (var other in Enum.GetValues<GenesisRecordingRuleQuickAction>())
        {
            Assert.False((await h.Act(b.Id, other)).Applied);
            Assert.False(h.Tabs.CanMoveRecordingRule(b.Id, GenesisRecordingRuleQuickAction.MoveUp));
            Assert.False(h.Tabs.CanMoveRecordingRule(b.Id, GenesisRecordingRuleQuickAction.MoveDown));
        }
        Assert.Null(h.Tabs.CreateRecordingRule()); Assert.Null(h.Tabs.EditRecordingRule(b.Id));
        release.SetResult(); Assert.True((await pending).Applied); Assert.Equal(1, h.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditorDraftConfirmationAndSaveExcludeReorder(bool edit)
    {
        var entered = RuleMutationFixture.Signal(); var release = RuleMutationFixture.Signal();
        var a = RuleMutationFixture.Rule("a", false); var b = RuleMutationFixture.Rule("b", false);
        await using var h = await RuleMutationFixture.CreateAsync([a, b], async (path, rules, token) =>
        {
            entered.SetResult(); await release.Task.WaitAsync(token); await RecordingRuleFileStore.SaveAsync(path, rules, token);
        });
        var draft = (edit ? h.Tabs.EditRecordingRule(b.Id) : h.Tabs.CreateRecordingRule())! with { Name = "broad", MatchAll = true, NamePattern = new() };
        Assert.False(h.Tabs.CanMoveRecordingRule(b.Id, GenesisRecordingRuleQuickAction.MoveUp));
        Assert.False((await h.Act(b.Id, GenesisRecordingRuleQuickAction.MoveUp)).Applied);
        Assert.True((await h.Tabs.RuleEditor.SaveAsync(draft)).RequiresConfirmation);
        Assert.False((await h.Act(a.Id, GenesisRecordingRuleQuickAction.MoveDown)).Applied);
        var pending = h.Tabs.RuleEditor.SaveAsync(draft, true); await entered.Task;
        Assert.False((await h.Act(b.Id, GenesisRecordingRuleQuickAction.MoveUp)).Applied);
        release.SetResult(); Assert.True((await pending).Applied); Assert.Equal(1, h.Writes);
    }

    [Fact]
    public async Task LoadingDisablesReorderWhileSavePublicationSupersedesOldLoad()
    {
        var entered = RuleMutationFixture.Signal(); var release = RuleMutationFixture.Signal();
        var a = RuleMutationFixture.Rule("a", false); var b = RuleMutationFixture.Rule("b", false);
        await using var h = await RuleMutationFixture.CreateAsync([a, b], async (path, rules, token) =>
        {
            entered.SetResult(); await release.Task.WaitAsync(token); await RecordingRuleFileStore.SaveAsync(path, rules, token);
        });
        var pending = h.Act(b.Id, GenesisRecordingRuleQuickAction.MoveUp); await entered.Task;
        var reload = h.Tabs.ReloadRecordingRulesAsync();
        Assert.True(h.Tabs.IsRecordingRulesLoading);
        Assert.False(h.Tabs.CanMoveRecordingRule(b.Id, GenesisRecordingRuleQuickAction.MoveUp));
        Assert.False((await h.Act(a.Id, GenesisRecordingRuleQuickAction.MoveDown)).Applied);
        release.SetResult(); Assert.True((await pending).Applied); await reload;
        Assert.Equal(new[] { b.Id, a.Id }, h.Rules.Select(r => r.Id)); Assert.Equal(1, h.Writes);
    }
}

public sealed class GenesisRecordingRuleReorderLifecycleTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ReorderConfirmationReservationRejectsOtherMutationsAndShutdownWritesNothing(int actionValue)
    {
        var action = (GenesisRecordingRuleQuickAction)actionValue;
        var entered = RuleMutationFixture.Signal();
        var a = RuleMutationFixture.Rule("a", true); var b = RuleMutationFixture.Rule("b", true); var c = RuleMutationFixture.Rule("c", true);
        await using var h = await RuleMutationFixture.CreateAsync([a, b, c]);
        var snapshot = h.Tabs.RecordingRulesSnapshot;
        h.Confirm = async (_, token) => { entered.SetResult(); await RuleMutationFixture.Signal().Task.WaitAsync(token); return true; };
        var pending = h.Act(b.Id, action); await entered.Task;
        foreach (var other in Enum.GetValues<GenesisRecordingRuleQuickAction>()) Assert.False((await h.Act(b.Id, other)).Applied);
        Assert.Null(h.Tabs.CreateRecordingRule()); Assert.Null(h.Tabs.EditRecordingRule(b.Id));
        await h.Tabs.DisposeAsync();
        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Cancelled, (await pending).Status);
        Assert.Same(snapshot, h.Tabs.RecordingRulesSnapshot); Assert.Equal(0, h.Writes);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ShutdownDrainsDelayedReorderWithoutLatePublication(int actionValue)
    {
        var action = (GenesisRecordingRuleQuickAction)actionValue;
        var entered = RuleMutationFixture.Signal(); var cancelled = RuleMutationFixture.Signal(); var release = RuleMutationFixture.Signal();
        var a = RuleMutationFixture.Rule("a", false); var b = RuleMutationFixture.Rule("b", false); var c = RuleMutationFixture.Rule("c", false);
        await using var h = await RuleMutationFixture.CreateAsync([a, b, c], async (_, _, token) =>
        {
            using var registration = token.Register(() => cancelled.TrySetResult()); entered.SetResult(); await release.Task;
        });
        var snapshot = h.Tabs.RecordingRulesSnapshot;
        var pending = h.Act(b.Id, action); await entered.Task;
        var shutdown = h.Tabs.DisposeAsync().AsTask(); await cancelled.Task;
        var publications = 0; h.Tabs.PropertyChanged += (_, _) => publications++;
        Assert.False(shutdown.IsCompleted);
        Assert.False(h.Tabs.CanMoveRecordingRule(b.Id, action));
        Assert.False((await h.Act(b.Id, action)).Applied);
        release.SetResult(); await pending; await shutdown;
        Assert.Same(snapshot, h.Tabs.RecordingRulesSnapshot); Assert.Equal(0, publications);
        Assert.True((await h.Repository.GetSnapshotAsync()).IsAvailable);
    }
}
