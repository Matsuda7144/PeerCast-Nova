namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRecordingRuleEditorNativeContractTests
{
    [Fact]
    public void NativeRulesButtonsDispatchOnlyThroughAppEditorCommands()
    {
        var source = Source("GenesisGuiNextAppShellBridge.cs");
        Assert.Contains("CreateRecordingModeButton(\"新規\"", source);
        Assert.Contains("CreateRecordingModeButton(\"編集\"", source);
        Assert.Contains("_tabs.CreateRecordingRule()", source);
        Assert.Contains("_tabs.EditRecordingRule(selectedId)", source);
        Assert.Contains("_tabs.CanCreateRecordingRule", source);
        Assert.Contains("_tabs.CanEditRecordingRule(row?.RecordingRule?.RuleId)", source);
        Assert.Contains("GenesisRecordingRuleEditor.UnsupportedMessage", source);
        Assert.Contains("GenesisRecordingRuleEditorWindow.ShowModal", source);
    }
    [Fact]
    public void NativeEditorOwnsNoJsonRevisionRecordingOrCatalogOperation()
    {
        var source = Source("GenesisRecordingRuleEditorWindow.cs");
        foreach (var forbidden in new[] { "File.", "Json", "RecordingRuleSet", "SHA256", "Process.Start", "EvaluateAutomatic", "StartManual", "StopRecording", "RefreshCurrent", ".Wait(", ".Result", "GetAwaiter" })
            Assert.DoesNotContain(forbidden, source);
        Assert.Contains("await editor.SaveAsync(frozen, confirmed)", source);
        Assert.Contains("PostMessageW(window, CompletedMessage", source);
        Assert.Contains("if (Open)", source);
    }
    [Fact]
    public void NativeEditorOffersSupportedFieldsAndNoDestructiveCommands()
    {
        var source = Source("GenesisRecordingRuleEditorWindow.cs");
        Assert.Equal(["完全一致", "一部一致", "正規表現"], GenesisRecordingRuleEditorWindow.MatchModes);
        foreach (var text in new[] { "ルール名", "有効", "録画", "除外", "優先度", "すべてのチャンネルに一致", "ChannelId", "名前", "説明", "StreamType", "最小 kbps", "最大 kbps", "保存", "キャンセル" })
            Assert.Contains(text, source);
        foreach (var text in new[] { "Genre", "ContentType", "Comment", "削除", "複製", "上へ", "下へ" })
            Assert.DoesNotContain(text, source);
    }
    [Fact]
    public void NativeSaveIsSingleFlightAndWarningNoDoesNotDispatchConfirmedSave()
    {
        var source = Source("GenesisRecordingRuleEditorWindow.cs");
        Assert.Contains("saveOperation is { IsCompleted: false }", source);
        Assert.Contains("!editor.CanSave", source);
        Assert.Contains("EnableWindow(pair.Value, false)", source);
        Assert.Contains("MessageBoxW(window, result.Message, Title, 0x24) == 6", source);
        Assert.Contains("BeginSave(true); return;", source);
        Assert.Contains("else if (result.Applied)", source);
    }
    [Fact]
    public void NativeCloseCancelsAttemptWithoutDisposingRepositoryAndPumpSupportsKeyboard()
    {
        var source = Source("GenesisRecordingRuleEditorWindow.cs");
        Assert.Contains("editor.Cancel()", source);
        Assert.DoesNotContain("editor.Dispose", source);
        Assert.Contains("EnableWindow(owner, false)", source);
        Assert.Contains("EnableWindow(owner, true)", source);
        Assert.Contains("IsDialogMessageW(window, ref message)", source);
        Assert.Contains("PostQuitMessage", source);
    }
    [Fact]
    public void AppliedRuleSelectionUsesIdentityAndNeverAnOldIndex()
    {
        var source = Source("SystemGenesisMainWindowInteractionBridgeFactory.cs");
        Assert.Contains("GenesisChannelListRenderedChannels.AnchorRecordingRule(_channelList, ruleId)", source);
        Assert.Contains("_guiNextTabs.ClearRecordingRuleSelectionAfterSave()", source);
        const nint handle = 900011;
        var id = Guid.NewGuid();
        try
        {
            GenesisChannelListRenderedChannels.AnchorRecordingRule(handle, id);
            var row = new GenesisGuiNextNativeTabRow(new("", ""), null,
                RecordingRule: new(id, 1, "edited", false, Core.Recording.RecordingRuleAction.Record, 0, "condition"));
            Assert.Equal(1, GenesisChannelListRenderedChannels.FindAnchoredRow(handle, [row with { RecordingRule = row.RecordingRule! with { RuleId = Guid.NewGuid() } }, row]));
            Assert.Equal(-1, GenesisChannelListRenderedChannels.FindAnchoredRow(handle, []));
        }
        finally { GenesisChannelListRenderedChannels.Remove(handle); }
    }
    private static string Source(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "src", "Nova.App", name);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(name);
    }
}
