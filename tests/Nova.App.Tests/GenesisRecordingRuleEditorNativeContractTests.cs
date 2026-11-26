using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRecordingRuleEditorNativeContractTests
{
    [Fact]
    public void NativeRulesButtonsDispatchThroughAppEditorCommands()
    {
        var source = RepositorySourceTestHelper.ReadAppSource("GenesisGuiNextAppShellBridge.cs");
        Assert.Contains("_tabs.CreateRecordingRule()", source, StringComparison.Ordinal);
        Assert.Contains("_tabs.EditRecordingRule(selectedId)", source, StringComparison.Ordinal);
        Assert.Contains("_tabs.CanCreateRecordingRule", source, StringComparison.Ordinal);
        Assert.Contains("_tabs.CanEditRecordingRule", source, StringComparison.Ordinal);
        Assert.Contains("GenesisRecordingRuleEditorWindow.ShowModal", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditorExposesMatchModesRequiresConfirmationForMatchAllAndPersistsOnConfirm()
    {
        Assert.Equal(["完全一致", "一部一致", "正規表現"], GenesisRecordingRuleEditorWindow.MatchModes);
        Assert.False(string.IsNullOrWhiteSpace(GenesisRecordingRuleEditor.UnsupportedMessage));

        var directory = Path.Combine(Path.GetTempPath(), "NovaRuleEditorContractTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "rules.json");
            await using var repository = new GenesisRecordingRuleRepository(path, RecordingRuleFileStore.SaveAsync);
            await using var mutations = new GenesisRecordingRuleMutationController(repository, (_, _) => { });
            await using var editor = new GenesisRecordingRuleEditor(mutations);

            var snapshot = await repository.GetSnapshotAsync();
            var draft = editor.BeginCreate(snapshot);
            Assert.NotNull(draft);
            Assert.True(editor.CanSave);

            var matchAllDraft = draft! with { Name = "全一致ルール", MatchAll = true, IsEnabled = true };
            var warningResult = await editor.SaveAsync(matchAllDraft, broadMatchConfirmed: false);
            Assert.True(warningResult.RequiresConfirmation);
            Assert.False(warningResult.Applied);
            Assert.True(editor.CanSave);

            var appliedResult = await editor.SaveAsync(matchAllDraft, broadMatchConfirmed: true);
            Assert.True(appliedResult.Applied);
            Assert.False(editor.CanSave);

            var persisted = await repository.GetSnapshotAsync();
            var rule = Assert.Single(persisted.Rules!.Rules);
            Assert.Equal("全一致ルール", rule.Name);
            Assert.True(rule.Criteria.IsMatchAll);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EditorCancelReleasesMutationReservationAndResetsCanSave()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NovaRuleEditorCancelTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "rules.json");
            await using var repository = new GenesisRecordingRuleRepository(path, RecordingRuleFileStore.SaveAsync);
            await using var mutations = new GenesisRecordingRuleMutationController(repository, (_, _) => { });
            await using var editor = new GenesisRecordingRuleEditor(mutations);

            var snapshot = await repository.GetSnapshotAsync();
            Assert.NotNull(editor.BeginCreate(snapshot));
            Assert.True(editor.CanSave);
            Assert.False(mutations.IsAvailable);

            editor.Cancel();

            Assert.False(editor.CanSave);
            Assert.Null(editor.Draft);
            Assert.True(mutations.IsAvailable);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void AppliedRuleSelectionUsesIdentityAndNeverAnOldIndex()
    {
        const nint handle = 900011;
        var id = Guid.NewGuid();
        try
        {
            GenesisChannelListRenderedChannels.AnchorRecordingRule(handle, id);
            var row = new GenesisGuiNextNativeTabRow(
                new(string.Empty, string.Empty),
                null,
                RecordingRule: new(id, 1, "edited", false, RecordingRuleAction.Record, 0, "condition"));
            Assert.Equal(
                1,
                GenesisChannelListRenderedChannels.FindAnchoredRow(
                    handle,
                    [row with { RecordingRule = row.RecordingRule! with { RuleId = Guid.NewGuid() } }, row]));
            Assert.Equal(-1, GenesisChannelListRenderedChannels.FindAnchoredRow(handle, []));
        }
        finally
        {
            GenesisChannelListRenderedChannels.Remove(handle);
        }
    }
}
