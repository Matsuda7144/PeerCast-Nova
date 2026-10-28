namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRecordingRuleQuickMutationNativeContractTests
{
    [Fact]
    public void NativeQuickButtonsUseRuleIdAppCommandsAndSharedAvailability()
    {
        var source = Source("GenesisGuiNextAppShellBridge.cs");
        Assert.Contains("CreateRecordingModeButton(\"有効化\"", source);
        Assert.Contains("CreateRecordingModeButton(\"削除\"", source);
        Assert.Contains("_tabs.CanQuickMutateRecordingRule(row?.RecordingRule?.RuleId)", source);
        Assert.Contains("_tabs.RecordingRuleToggleLabel(row?.RecordingRule?.RuleId)", source);
        Assert.Contains("_tabs.QuickMutateRecordingRuleAsync(selectedId, action", source);
        Assert.Contains("MessageBoxW(_owner, confirmation.Message, \"録画ルール\", 0x24) == 6", source);
        Assert.Contains("DrawRecordingModeButton(drawItem, label, false)", source);
        Assert.Contains("PresentVisible(_toggleRuleButton, isRulesPage", source);
        Assert.Contains("PresentVisible(_deleteRuleButton, isRulesPage", source);
    }

    [Fact]
    public void QuickBoundaryOwnsNoCatalogRecordingPlaybackOrCoreEditingLogic()
    {
        var source = Source("GenesisRecordingRuleMutationController.cs");
        foreach (var forbidden in new[] { "File.", "Json", "SHA256", "Refresh", "ObserveAsync", "Evaluate", "StartManual", "StopRecording", "Pcp", "Process", ".Wait(", ".Result", "GetAwaiter", "GenesisRecordingRuleDraft" })
            Assert.DoesNotContain(forbidden, source);
        Assert.Contains("await repository.SaveAsync(expected, proposed, cancellationToken)", source);
        Assert.Contains("rule.Criteria, rule.Priority", source);
        Assert.Contains("snapshot.Rules!.Remove(rule.Id)", source);
        Assert.Contains("snapshot.Rules!.Move(rule.Id, target!.Value)", source);
        var editor = Source("GenesisRecordingRuleEditor.cs");
        Assert.Contains("mutations.TryReserve()", editor);
        Assert.Contains("await mutations.SaveAsync", editor);
    }

    [Fact]
    public void NativeReorderButtonsUseSharedCommandAndExposePersistedOrderHelp()
    {
        var source = Source("GenesisGuiNextAppShellBridge.cs");
        foreach (var label in new[] { "上へ", "下へ" })
            Assert.Contains($"CreateRecordingModeButton(\"{label}\"", source);
        foreach (var direction in new[] { "Up", "Down" })
        {
            Assert.Contains($"_tabs.CanMoveRecordingRule(row?.RecordingRule?.RuleId, GenesisRecordingRuleQuickAction.Move{direction})", source);
            Assert.Contains($"PresentVisible(_moveRule{direction}Button, isRulesPage", source);
            Assert.Contains($"MoveRule{direction}ControlId => GenesisRecordingRuleQuickAction.Move{direction}", source);
            Assert.Contains($"MoveRule{direction}ControlId => \"{(direction == "Up" ? "上へ" : "下へ")}\"", source);
        }
        Assert.Contains("PresentText(_detailsIdentity, GenesisRecordingRuleMutationController.PersistedOrderHelp)", source);
        Assert.Contains("同じ優先度", GenesisRecordingRuleMutationController.PersistedOrderHelp);
        Assert.Contains("次回", GenesisRecordingRuleMutationController.PersistedOrderHelp);
        var mutation = Source("GenesisRecordingRuleMutationController.cs");
        Assert.DoesNotContain(".OrderBy", mutation);
        Assert.DoesNotContain(".Sort(", mutation);
    }

    [Fact]
    public void NativeQuickDispatchDoesNotUseSyncWaitOrDirectPersistence()
    {
        var source = Source("GenesisGuiNextAppShellBridge.cs");
        var start = source.IndexOf("private async Task ObserveQuickRuleMutationAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public void ProcessCommand", start, StringComparison.Ordinal);
        var command = source[start..end];
        foreach (var forbidden in new[] { "File.", "Json", "RecordingRuleSet", "Revision", ".Wait(", ".Result", "GetAwaiter", "StartManual", "EvaluateAutomatic" })
            Assert.DoesNotContain(forbidden, command);
        Assert.Contains("token.ThrowIfCancellationRequested()", command);
        Assert.Contains("Volatile.Read(ref _detached)", command);
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
