using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRecordingRuleEditorTests
{
    [Fact]
    public async Task CreateRuleFromMissingSnapshotAppendsNewRuleAndPersists()
    {
        await using var h = await Harness.CreateAsync();
        var draft = Valid(h.Tabs.CreateRecordingRule()!);
        Assert.NotEqual(Guid.Empty, draft.RuleId);
        Assert.Equal(GenesisRecordingRuleRevision.Missing, draft.BaseSnapshot.Revision);
        var result = await h.Editor.SaveAsync(draft);
        Assert.True(result.Applied);
        Assert.True(File.Exists(h.Path));
        var persisted = await h.Repository.GetSnapshotAsync();
        Assert.Equal(draft.RuleId, Assert.Single(persisted.Rules!.Rules).Id);
        Assert.Equal(persisted.Revision, h.Tabs.RecordingRulesRevision);
    }
    [Fact]
    public async Task CreateRuleAppendsAfterExistingRulesWithoutSorting()
    {
        var a = Rule("Z", -3); var b = Rule("B", 2);
        await using var h = await Harness.CreateAsync([a, b]);
        var d = Valid(h.Tabs.CreateRecordingRule()!) with { Name = "A", Priority = "99" };
        Assert.True((await h.Editor.SaveAsync(d)).Applied);
        Assert.Equal([a.Id, b.Id, d.RuleId], h.Rules.Select(r => r.Id));
        Assert.Equal(a, h.Rules[0]); Assert.Equal(b, h.Rules[1]);
    }
    [Fact]
    public async Task CreateRuleDefaultsDisabled()
    {
        await using var h = await Harness.CreateAsync();
        var d = h.Tabs.CreateRecordingRule()!;
        Assert.False(d.IsEnabled);
        Assert.False(d.MatchAll);
        Assert.Equal(RecordingRuleAction.Record, d.Action);
    }
    [Fact]
    public async Task CreateRulePreservesEnteredActionPriorityAndCriteria()
    {
        await using var h = await Harness.CreateAsync();
        var id = new ChannelId(Guid.NewGuid());
        var d = Valid(h.Tabs.CreateRecordingRule()!) with
        {
            Name = "  Rule  ",
            Action = RecordingRuleAction.Ignore,
            Priority = int.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            IsEnabled = true,
            ChannelId = id.ToString(),
            NamePattern = new("foo", RecordingTextMatchMode.Exact),
            DescriptionPattern = new("bar"),
            StreamTypePattern = new("FLV", RecordingTextMatchMode.Exact),
            MinimumBitrateKbps = "0",
            MaximumBitrateKbps = "500",
        };
        Assert.True((await h.Editor.SaveAsync(d)).Applied);
        var rule = Assert.Single(h.Rules);
        Assert.Equal("Rule", rule.Name); Assert.True(rule.IsEnabled);
        Assert.Equal(RecordingRuleAction.Ignore, rule.Action); Assert.Equal(int.MinValue, rule.Priority);
        Assert.Equal(id, rule.Criteria.ChannelId); Assert.Equal(RecordingTextMatchMode.Exact, rule.Criteria.Name!.Mode);
        Assert.Equal("bar", rule.Criteria.Description!.Value); Assert.Equal("FLV", rule.Criteria.StreamType!.Value);
        Assert.Equal(0, rule.Criteria.MinimumBitrateKbps); Assert.Equal(500, rule.Criteria.MaximumBitrateKbps);
    }
    [Fact] public Task CreateRuleRequiresExplicitCriteriaOrMatchAll() => RejectAsync(d => d with { NamePattern = new() });
    [Fact] public Task CreateInvalidChannelIdDoesNotSave() => RejectAsync(d => d with { ChannelId = "invalid" });
    [Fact] public Task CreateInvalidRegexDoesNotSave() => RejectAsync(d => d with { NamePattern = new("[", RecordingTextMatchMode.Regex) });
    [Fact] public Task CreateInvalidPriorityDoesNotSave() => RejectAsync(d => d with { Priority = "1.5" });
    [Fact] public Task CreateInvalidBitrateRangeDoesNotSave() => RejectAsync(d => d with { MinimumBitrateKbps = "5", MaximumBitrateKbps = "4" });
    [Theory]
    [InlineData(" ")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("not-id")]
    public Task InvalidChannelIdentityCannotBecomeMatchAll(string value) => RejectAsync(d => d with { ChannelId = value });
    [Theory]
    [InlineData("-1", "")]
    [InlineData("", "-1")]
    [InlineData(" ", "")]
    [InlineData("2147483648", "")]
    public Task InvalidBitrateDoesNotSave(string minimum, string maximum) => RejectAsync(d => d with { MinimumBitrateKbps = minimum, MaximumBitrateKbps = maximum });
    [Fact] public Task BlankNameDoesNotSave() => RejectAsync(d => d with { Name = " " });
    [Fact] public Task PriorityOverflowDoesNotSave() => RejectAsync(d => d with { Priority = "2147483648" });
    [Fact] public Task WhitespacePatternIsNotImplicitMatchAll() => RejectAsync(d => d with { NamePattern = new(" ") });
    [Fact] public Task InvalidActionDoesNotSave() => RejectAsync(d => d with { Action = (RecordingRuleAction)99 });
    [Fact] public Task InvalidMatchModeDoesNotSave() => RejectAsync(d => d with { NamePattern = new("foo", (RecordingTextMatchMode)99) });
    [Fact]
    public async Task CreateMatchAllRequiresConfirmation()
    {
        await using var h = await Harness.CreateAsync();
        var d = Valid(h.Tabs.CreateRecordingRule()!) with { MatchAll = true, ChannelId = "hidden invalid ignored", MinimumBitrateKbps = "bad" };
        var result = await h.Editor.SaveAsync(d);
        Assert.True(result.RequiresConfirmation); Assert.Equal(0, h.Writes);
        Assert.Contains("すべてのチャンネル", result.Message); Assert.Contains("次回", result.Message);
        Assert.True((await h.Editor.SaveAsync(d, true)).Applied);
        Assert.True(Assert.Single(h.Rules).Criteria.IsMatchAll);
        Assert.Null(h.Rules[0].Criteria.Name);
    }
    [Fact]
    public async Task CreateRegexRuleRequiresBroadMatchConfirmation()
    {
        await using var h = await Harness.CreateAsync();
        var d = Valid(h.Tabs.CreateRecordingRule()!) with { NamePattern = new(".*", RecordingTextMatchMode.Regex) };
        var result = await h.Editor.SaveAsync(d);
        Assert.True(result.RequiresConfirmation); Assert.Contains("正規表現", result.Message); Assert.Equal(0, h.Writes);
        Assert.True((await h.Editor.SaveAsync(d, true)).Applied);
        Assert.Equal(RecordingTextMatchMode.Regex, Assert.Single(h.Rules).Criteria.Name!.Mode);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EachRegexFieldRequiresConfirmation(int field)
    {
        await using var h = await Harness.CreateAsync();
        var d = Valid(h.Tabs.CreateRecordingRule()!);
        var regex = new GenesisRecordingPatternDraft(".*", RecordingTextMatchMode.Regex);
        d = field switch { 0 => d with { NamePattern = regex }, 1 => d with { DescriptionPattern = regex }, _ => d with { StreamTypePattern = regex } };
        Assert.True((await h.Editor.SaveAsync(d)).RequiresConfirmation);
        Assert.Equal(0, h.Writes);
    }
    [Fact]
    public async Task CancelledBroadMatchConfirmationDoesNotSave()
    {
        await using var h = await Harness.CreateAsync();
        var d = Valid(h.Tabs.CreateRecordingRule()!) with { MatchAll = true };
        Assert.True((await h.Editor.SaveAsync(d)).RequiresConfirmation);
        // Native No does not issue the confirmed command; draft remains editable.
        Assert.True(h.Editor.CanSave); Assert.Equal(d, h.Editor.Draft); Assert.False(File.Exists(h.Path)); Assert.Equal(0, h.Writes);
    }
    [Fact] public Task CreateCancelDoesNotCreateMissingRuleFile() => CancelAsync(false);
    [Fact] public Task CancelledEditDoesNotPersist() => CancelAsync(true);
    [Fact] public Task EditorCancelDispatchesNoRepositoryWrite() => CancelAsync(false);
    [Fact]
    public async Task ConcurrentExternalCreateReturnsConflictWithoutOverwrite() => await ConflictAsync(false, false);
    [Fact]
    public async Task ExternalFileChangeDuringEditReturnsConflictWithoutOverwrite() => await ConflictAsync(true, false);
    [Fact]
    public async Task ExternalRuleDeletionDuringEditReturnsConflict() => await ConflictAsync(true, true);
    [Fact]
    public async Task ConflictDoesNotPublishDraftAsApplied() => await ConflictAsync(true, false);
    [Fact]
    public async Task ConflictRequiresFreshEditSnapshotBeforeRetry()
    {
        var a = Rule("A"); await using var h = await Harness.CreateAsync([a]);
        var stale = h.Tabs.EditRecordingRule(a.Id)! with { Name = "stale" };
        await h.ExternalAsync([new(a.Id, "external", a.Action, a.Criteria)]);
        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Conflict, (await h.Editor.SaveAsync(stale)).Status);
        Assert.False(h.Editor.CanSave); Assert.Null((await h.Editor.SaveAsync(stale)).Status); Assert.Equal(0, h.Writes);
        Assert.Equal(stale, h.Editor.Draft);
        h.Editor.Cancel();
        var fresh = h.Tabs.EditRecordingRule(a.Id)! with { Name = "fresh" };
        Assert.NotEqual(stale.BaseSnapshot.Revision, fresh.BaseSnapshot.Revision);
        Assert.True((await h.Editor.SaveAsync(fresh)).Applied);
    }
    [Fact] public Task EditRulePreservesRuleId() => EditAsync();
    [Fact] public Task EditRulePreservesPersistedPosition() => EditAsync();
    [Fact] public Task EditRuleDoesNotModifyOtherRules() => EditAsync();
    [Fact] public Task EditRuleCanChangeNameActionPriorityEnabledAndSupportedCriteria() => EditAsync();
    [Fact] public Task EditRuleDoesNotSortAfterPriorityChange() => EditAsync();
    [Fact] public Task EditRuleUsesExpectedRepositoryRevision() => EditAsync();
    [Fact] public Task EditSelectionRemainsAnchoredByRuleIdAfterAppliedSave() => EditAsync();
    [Fact]
    public async Task LegacyMatchAllEditRequiresExplicitIntent()
    {
        var rule = new RecordingRule(Guid.NewGuid(), "legacy", RecordingRuleAction.Record, RecordingRuleCriteria.MatchAll);
        await using var h = await Harness.CreateAsync([rule]);
        var d = h.Tabs.EditRecordingRule(rule.Id)!;
        Assert.False(d.MatchAll); Assert.Null((await h.Editor.SaveAsync(d)).Status); Assert.Equal(0, h.Writes);
        Assert.True((await h.Editor.SaveAsync(d with { MatchAll = true })).RequiresConfirmation);
    }
    [Fact]
    public async Task PersistenceFailureKeepsPersistedProjectionUnchanged()
    {
        var a = Rule("A"); await using var h = await Harness.CreateAsync([a], (_, _, _) => throw new IOException("private details"));
        var before = h.Tabs.RecordingRulesSnapshot;
        var d = h.Tabs.EditRecordingRule(a.Id)! with { Name = "changed" };
        var r = await h.Editor.SaveAsync(d);
        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.PersistenceFailed, r.Status);
        Assert.Same(before, h.Tabs.RecordingRulesSnapshot); Assert.Equal(d, h.Editor.Draft); Assert.True(h.Editor.CanSave);
        Assert.DoesNotContain("private details", r.Message);
    }
    [Fact]
    public async Task RepositoryInvalidResultDoesNotPublishDraft()
    {
        await using var h = await Harness.CreateAsync();
        var before = h.Tabs.RecordingRulesSnapshot;
        var d = Valid(h.Tabs.CreateRecordingRule()!) with { Name = new string('a', RecordingRuleFileStore.MaximumFileByteCount) };
        var result = await h.Editor.SaveAsync(d);
        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Invalid, result.Status);
        Assert.Same(before, h.Tabs.RecordingRulesSnapshot); Assert.False(File.Exists(h.Path)); Assert.True(h.Editor.CanSave);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RuleWithUnsupportedEditorCriterionRemainsVisible(int field)
    {
        await using var h = await Harness.CreateAsync([Unsupported(field)]);
        Assert.Single(h.Tabs.RecordingRuleRows);
        Assert.Contains(field switch { 0 => "Genre", 1 => "ContentType", _ => "Comment" }, h.Tabs.RecordingRuleRows[0].CriteriaSummary);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RuleWithUnsupportedEditorCriterionCannotBeEdited(int field)
    {
        var a = Unsupported(field); await using var h = await Harness.CreateAsync([a]);
        Assert.False(h.Tabs.CanEditRecordingRule(a.Id)); Assert.Null(h.Tabs.EditRecordingRule(a.Id)); Assert.Equal(0, h.Writes);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task UnsupportedEditorRuleIsNotSilentlyRewritten(int field)
    {
        var a = Unsupported(field); await using var h = await Harness.CreateAsync([a]);
        Assert.Null(h.Editor.BeginEdit(h.Tabs.RecordingRulesSnapshot, a.Id));
        Assert.Equal(RecordingRuleJsonCodec.Serialize(new([a])), await File.ReadAllTextAsync(h.Path));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CreatingOrEditingOtherRulePreservesUnsupportedRuleExactly(int field)
    {
        var a = Unsupported(field); var b = Rule("B"); await using var h = await Harness.CreateAsync([a, b]);
        Assert.True((await h.Editor.SaveAsync(Valid(h.Tabs.CreateRecordingRule()!))).Applied);
        Assert.Equal(a, h.Rules[0]); Assert.Equal(b, h.Rules[1]);
        Assert.True((await h.Editor.SaveAsync(h.Tabs.EditRecordingRule(b.Id)! with { Name = "B edited" })).Applied);
        Assert.Equal(a, h.Rules[0]); Assert.Equal([a.Id, b.Id], h.Rules.Take(2).Select(r => r.Id));
        Assert.Equal(a, (await h.Repository.GetSnapshotAsync()).Rules!.Rules[0]);
    }
    [Fact] public Task MalformedRuleFileDisablesCreateAndEdit() => UnavailableAsync("not json");
    [Fact] public Task UnsupportedSchemaDisablesCreateAndEdit() => UnavailableAsync("{\"version\":2,\"rules\":[]}");
    [Fact]
    public async Task UnavailableRepositoryDisablesCreateAndEdit()
    {
        await using var repo = new GenesisRecordingRuleRepository(null);
        await using var tabs = new GenesisGuiNextTabsViewModel(new(), repo) { SelectedTab = GenesisGuiNextTab.Recording };
        await tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Rules);
        Assert.False(tabs.CanCreateRecordingRule); Assert.Null(tabs.CreateRecordingRule()); Assert.Null(tabs.EditRecordingRule(Guid.NewGuid()));
    }
    [Fact]
    public async Task MissingRuleFileAllowsCreate()
    {
        await using var h = await Harness.CreateAsync(); Assert.True(h.Tabs.CanCreateRecordingRule); Assert.NotNull(h.Tabs.CreateRecordingRule());
    }
    [Fact]
    public async Task RulesReloadUpdatesMutationAvailability()
    {
        await using var h = await Harness.CreateAsync();
        await File.WriteAllTextAsync(h.Path, "bad"); await h.Tabs.ReloadRecordingRulesAsync();
        Assert.False(h.Tabs.CanCreateRecordingRule);
        await h.ExternalAsync([Rule("repaired")]); await h.Tabs.ReloadRecordingRulesAsync();
        Assert.True(h.Tabs.CanCreateRecordingRule); Assert.NotNull(h.Tabs.CreateRecordingRule());
    }
    [Fact]
    public async Task RepositoryBecomingUnavailableDoesNotOverwriteOrCloseEditor()
    {
        await using var h = await Harness.CreateAsync(); var d = Valid(h.Tabs.CreateRecordingRule()!);
        await File.WriteAllTextAsync(h.Path, "bad");
        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Unavailable, (await h.Editor.SaveAsync(d)).Status);
        Assert.Equal("bad", await File.ReadAllTextAsync(h.Path)); Assert.Equal(d, h.Editor.Draft); Assert.False(h.Tabs.CanCreateRecordingRule); Assert.False(h.Editor.CanSave);
    }
    [Fact]
    public async Task RulesCreateCommandAvailableOnlyInRulesMode()
    {
        await using var h = await Harness.CreateAsync();
        h.Tabs.SelectedTab = GenesisGuiNextTab.Channels; Assert.False(h.Tabs.CanCreateRecordingRule); Assert.Null(h.Tabs.CreateRecordingRule());
        h.Tabs.SelectedTab = GenesisGuiNextTab.Recording; Assert.True(h.Tabs.CanCreateRecordingRule);
        await h.Tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.ActiveRecordings);
        Assert.False(h.Tabs.CanCreateRecordingRule); Assert.Null(h.Tabs.CreateRecordingRule());
    }
    [Fact]
    public async Task RulesEditCommandRequiresEditableSelectedRule()
    {
        var a = Rule("A"); await using var h = await Harness.CreateAsync([a]);
        Assert.False(h.Tabs.CanEditRecordingRule(null)); Assert.False(h.Tabs.CanEditRecordingRule(Guid.NewGuid()));
        Assert.True(h.Tabs.CanEditRecordingRule(a.Id)); Assert.NotNull(h.Tabs.EditRecordingRule(a.Id));
    }
    [Fact] public async Task RulesEditCommandDisabledForUnsupportedCriteriaRule() => await UnsupportedCommandAsync();
    [Fact]
    public async Task RulesMutationCommandsUnavailableInActiveRecordingsMode()
    {
        var a = Rule("A"); await using var h = await Harness.CreateAsync([a]);
        await h.Tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.ActiveRecordings);
        Assert.Null(h.Tabs.CreateRecordingRule()); Assert.Null(h.Tabs.EditRecordingRule(a.Id));
    }
    [Fact] public Task CreateEditDialogSaveDispatchIsSingleFlight() => DoubleSaveAsync();
    [Fact] public Task DoubleSaveDoesNotCreateDuplicateRules() => DoubleSaveAsync();
    [Fact] public Task ApplicationShutdownDrainsInFlightRuleSave() => ShutdownAsync();
    [Fact] public Task ApplicationShutdownRejectsNewRuleMutation() => ShutdownAsync();
    [Fact]
    public async Task ClosingEditorDuringSaveDoesNotDisposeRepositoryEarly()
    {
        var entered = Gate(); var release = Gate();
        await using var h = await Harness.CreateAsync(null, async (_, _, _) => { entered.SetResult(); await release.Task; });
        var save = h.Editor.SaveAsync(Valid(h.Tabs.CreateRecordingRule()!)); await entered.Task;
        h.Editor.Cancel(); Assert.Null(h.Tabs.CreateRecordingRule());
        var pendingRead = h.Repository.GetSnapshotAsync().AsTask(); Assert.False(pendingRead.IsCompleted);
        release.SetResult(); await save;
        Assert.True((await pendingRead).IsAvailable); Assert.NotNull(h.Tabs.CreateRecordingRule());
    }
    [Fact]
    public async Task CancellationIsBoundedAndDoesNotPublishDraft()
    {
        var entered = Gate();
        await using var h = await Harness.CreateAsync(null, async (_, _, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); });
        var before = h.Tabs.RecordingRulesSnapshot;
        var save = h.Editor.SaveAsync(Valid(h.Tabs.CreateRecordingRule()!)); await entered.Task; h.Editor.Cancel();
        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Cancelled, (await save).Status);
        Assert.Same(before, h.Tabs.RecordingRulesSnapshot); Assert.False(File.Exists(h.Path));
    }
    [Fact]
    public async Task EditorCannotChangeSnapshotIdentityOrRuleId()
    {
        await using var h = await Harness.CreateAsync(); var d = Valid(h.Tabs.CreateRecordingRule()!);
        Assert.Null((await h.Editor.SaveAsync(d with { RuleId = Guid.NewGuid() })).Status);
        Assert.Null((await h.Editor.SaveAsync(d with { BaseSnapshot = d.BaseSnapshot with { } })).Status);
        Assert.Equal(0, h.Writes);
    }
    [Fact]
    public async Task MaximumRuleCountIsCoreOwnedAndRejectsAppend()
    {
        var rules = Enumerable.Range(0, RecordingRuleSet.MaximumRuleCount).Select(i => Rule($"R{i}")).ToArray();
        await using var h = await Harness.CreateAsync(rules);
        Assert.Null((await h.Editor.SaveAsync(Valid(h.Tabs.CreateRecordingRule()!))).Status); Assert.Equal(0, h.Writes);
        Assert.Equal(RecordingRuleSet.MaximumRuleCount, h.Rules.Count);
    }
    [Fact]
    public void RuleRowStillCannotPlayRecordStopOrFavorite()
    {
        var row = new GenesisGuiNextNativeTabRow(new("", ""), null, RecordingRule: GenesisGuiNextRecordingRuleProjection.CreateRows(new([Rule("R")]))[0]);
        Assert.False(row.IsInteractive); Assert.Null(row.InteractiveChannel); Assert.Null(row.ActiveRecording);
        Assert.Equal(GenesisGuiNextFavoriteToggleResult.Unavailable, GenesisGuiNextFavoriteToggle.Apply(new(new()), row));
    }

    private static async Task RejectAsync(Func<GenesisRecordingRuleDraft, GenesisRecordingRuleDraft> change)
    {
        await using var h = await Harness.CreateAsync();
        var result = await h.Editor.SaveAsync(change(Valid(h.Tabs.CreateRecordingRule()!)));
        Assert.False(result.Applied); Assert.False(result.RequiresConfirmation); Assert.NotEmpty(result.Message);
        Assert.Equal(0, h.Writes); Assert.False(File.Exists(h.Path)); Assert.Empty(h.Rules);
    }

    [Fact]
    public async Task CancelledEditDraftCannotSaveAfterReopeningTheSameRuleAndRevision()
    {
        var rule = Rule("unchanged");
        await using var h = await Harness.CreateAsync([rule]);
        var cancelled = h.Tabs.EditRecordingRule(rule.Id)! with { Name = "cancelled draft" };
        h.Editor.Cancel();
        var reopened = h.Tabs.EditRecordingRule(rule.Id)!;
        Assert.Same(cancelled.BaseSnapshot, reopened.BaseSnapshot);
        Assert.NotEqual(cancelled.AttemptId, reopened.AttemptId);
        Assert.Null((await h.Editor.SaveAsync(cancelled)).Status);
        Assert.Equal(0, h.Writes);
        Assert.Equal("unchanged", Assert.Single(h.Rules).Name);
    }
    private static async Task CancelAsync(bool edit)
    {
        var rule = Rule("R"); await using var h = await Harness.CreateAsync(edit ? [rule] : null);
        var before = h.Tabs.RecordingRulesRevision;
        var d = edit ? h.Tabs.EditRecordingRule(rule.Id) : h.Tabs.CreateRecordingRule(); Assert.NotNull(d);
        h.Editor.Cancel(); Assert.False(h.Editor.CanSave); Assert.Null((await h.Editor.SaveAsync(Valid(d!))).Status);
        Assert.Equal(before, h.Tabs.RecordingRulesRevision); Assert.Equal(0, h.Writes); Assert.Equal(edit, File.Exists(h.Path));
    }
    private static async Task ConflictAsync(bool edit, bool remove)
    {
        var rule = Rule("R"); await using var h = await Harness.CreateAsync(edit ? [rule] : null);
        var d = Valid(edit ? h.Tabs.EditRecordingRule(rule.Id)! : h.Tabs.CreateRecordingRule()!) with { Name = "draft only" };
        var external = remove ? Array.Empty<RecordingRule>() : [Rule("external")];
        await h.ExternalAsync(external); var bytes = await File.ReadAllBytesAsync(h.Path);
        var result = await h.Editor.SaveAsync(d);
        Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Conflict, result.Status); Assert.False(result.Applied);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(h.Path)); Assert.Equal(0, h.Writes);
        Assert.Equal(external.Select(r => r.Id), h.Rules.Select(r => r.Id)); Assert.False(h.Editor.CanSave);
        Assert.Equal(d, h.Editor.Draft); Assert.Contains("外部で更新", result.Message);
    }
    private static async Task EditAsync()
    {
        var a = Rule("A", 4); var b = Rule("B", 1); var c = Rule("C", 9);
        await using var h = await Harness.CreateAsync([a, b, c]); var before = h.Tabs.RecordingRulesRevision;
        var d = h.Tabs.EditRecordingRule(b.Id)! with { Name = "changed", IsEnabled = true, Action = RecordingRuleAction.Ignore, Priority = "999", NamePattern = new("x", RecordingTextMatchMode.Exact), DescriptionPattern = new("y"), StreamTypePattern = new("FLV"), MinimumBitrateKbps = "1", MaximumBitrateKbps = "2" };
        Assert.Equal(before, d.BaseSnapshot.Revision); Assert.Equal(1, d.OriginalIndex);
        Assert.True((await h.Editor.SaveAsync(d)).Applied);
        Assert.Equal([a.Id, b.Id, c.Id], h.Rules.Select(r => r.Id)); Assert.Equal(a, h.Rules[0]); Assert.Equal(c, h.Rules[2]);
        var changed = h.Rules[1]; Assert.Equal("changed", changed.Name); Assert.True(changed.IsEnabled); Assert.Equal(999, changed.Priority);
        Assert.Equal(RecordingRuleAction.Ignore, changed.Action); Assert.Equal("x", changed.Criteria.Name!.Value); Assert.Equal("y", changed.Criteria.Description!.Value);
        Assert.Equal("FLV", changed.Criteria.StreamType!.Value); Assert.Equal(1, changed.Criteria.MinimumBitrateKbps); Assert.Equal(2, changed.Criteria.MaximumBitrateKbps);
        Assert.Equal(b.Id, h.Tabs.RecordingRuleSelectionAfterSave); Assert.NotEqual(before, h.Tabs.RecordingRulesRevision);
    }
    private static async Task UnavailableAsync(string text)
    {
        await using var h = await Harness.CreateAsync(); await File.WriteAllTextAsync(h.Path, text); await h.Tabs.ReloadRecordingRulesAsync();
        Assert.False(h.Tabs.CanCreateRecordingRule); Assert.Null(h.Tabs.CreateRecordingRule()); Assert.Null(h.Tabs.EditRecordingRule(Guid.NewGuid()));
        Assert.Equal(text, await File.ReadAllTextAsync(h.Path)); Assert.Equal(0, h.Writes);
    }
    private static async Task UnsupportedCommandAsync()
    {
        var a = Unsupported(0); await using var h = await Harness.CreateAsync([a]);
        Assert.False(h.Tabs.CanEditRecordingRule(a.Id)); Assert.Null(h.Tabs.EditRecordingRule(a.Id));
    }
    private static async Task DoubleSaveAsync()
    {
        var entered = Gate(); var release = Gate();
        await using var h = await Harness.CreateAsync(null, async (path, rules, token) => { entered.SetResult(); await release.Task; await RecordingRuleFileStore.SaveAsync(path, rules, token); });
        var d = Valid(h.Tabs.CreateRecordingRule()!); var first = h.Editor.SaveAsync(d); await entered.Task;
        Assert.False(h.Editor.CanSave); Assert.Null((await h.Editor.SaveAsync(d)).Status); Assert.Null(h.Tabs.CreateRecordingRule());
        release.SetResult(); Assert.True((await first).Applied); Assert.Equal(1, h.Writes); Assert.Single(h.Rules);
        Assert.Null((await h.Editor.SaveAsync(d)).Status); Assert.Equal(1, h.Writes);
    }
    private static async Task ShutdownAsync()
    {
        var entered = Gate(); var cancelled = Gate(); var release = Gate();
        await using var h = await Harness.CreateAsync(null, async (_, _, token) =>
        {
            entered.SetResult(); using var registration = token.Register(() => cancelled.TrySetResult());
            await release.Task; token.ThrowIfCancellationRequested();
        });
        var save = h.Editor.SaveAsync(Valid(h.Tabs.CreateRecordingRule()!)); await entered.Task;
        var shutdown = h.Tabs.DisposeAsync().AsTask(); await cancelled.Task;
        Assert.False(shutdown.IsCompleted); Assert.Null(h.Tabs.CreateRecordingRule()); Assert.Null(h.Editor.BeginCreate(h.Tabs.RecordingRulesSnapshot));
        release.SetResult(); Assert.Equal(GenesisRecordingRuleRepositoryWriteStatus.Cancelled, (await save).Status); await shutdown;
        Assert.False(File.Exists(h.Path)); Assert.Empty(h.Rules);
    }
    private static GenesisRecordingRuleDraft Valid(GenesisRecordingRuleDraft d) => d with { Name = "New rule", NamePattern = new("specific") };
    private static RecordingRule Rule(string name, int priority = 0) => new(Guid.NewGuid(), name, RecordingRuleAction.Record, new(name: new("specific")), priority, false);
    private static RecordingRule Unsupported(int field) => new(Guid.NewGuid(), "unsupported", RecordingRuleAction.Ignore,
        field switch { 0 => new(genre: new("g")), 1 => new(contentType: new("c")), _ => new(comment: new("note")) }, -8, false);
    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class Harness : IAsyncDisposable
    {
        private readonly string directory;
        private Harness(string directory, Func<string, RecordingRuleSet, CancellationToken, ValueTask>? write)
        {
            this.directory = directory; Path = System.IO.Path.Combine(directory, "rules.json");
            Repository = new(Path, async (path, rules, token) => { Writes++; await (write ?? RecordingRuleFileStore.SaveAsync)(path, rules, token); });
            Tabs = new(new(), Repository) { SelectedTab = GenesisGuiNextTab.Recording };
        }
        public string Path { get; }
        public int Writes { get; private set; }
        public GenesisRecordingRuleRepository Repository { get; }
        public GenesisGuiNextTabsViewModel Tabs { get; }
        public GenesisRecordingRuleEditor Editor => Tabs.RuleEditor;
        public IReadOnlyList<RecordingRule> Rules => Tabs.RecordingRulesSnapshot?.Rules?.Rules ?? [];
        public static async Task<Harness> CreateAsync(RecordingRule[]? rules = null, Func<string, RecordingRuleSet, CancellationToken, ValueTask>? write = null)
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NovaRuleEditorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory); var h = new Harness(directory, write);
            if (rules is not null) await h.ExternalAsync(rules);
            await h.Tabs.SelectRecordingPageModeAsync(GenesisGuiNextRecordingPageMode.Rules); return h;
        }
        public Task ExternalAsync(RecordingRule[] rules) => File.WriteAllTextAsync(Path, RecordingRuleJsonCodec.Serialize(new(rules)));
        public async ValueTask DisposeAsync() { await Tabs.DisposeAsync(); await Repository.DisposeAsync(); Directory.Delete(directory, true); }
    }
}
