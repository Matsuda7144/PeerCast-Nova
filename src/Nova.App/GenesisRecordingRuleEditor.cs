using System.Globalization;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

internal enum GenesisRecordingRuleEditorMode { Create, Edit }
internal sealed record GenesisRecordingPatternDraft(string Value = "", RecordingTextMatchMode Mode = RecordingTextMatchMode.Contains);

// The original snapshot is the immutable optimistic-write authority for this attempt.
internal sealed record GenesisRecordingRuleDraft(
    GenesisRecordingRuleEditorMode Mode,
    Guid RuleId,
    GenesisRecordingRuleRepositorySnapshot BaseSnapshot,
    int? OriginalIndex)
{
    public Guid AttemptId { get; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public bool IsEnabled { get; init; }
    public RecordingRuleAction Action { get; init; } = RecordingRuleAction.Record;
    public string Priority { get; init; } = "0";
    public bool MatchAll { get; init; }
    public string ChannelId { get; init; } = "";
    public GenesisRecordingPatternDraft NamePattern { get; init; } = new();
    public GenesisRecordingPatternDraft DescriptionPattern { get; init; } = new();
    public GenesisRecordingPatternDraft StreamTypePattern { get; init; } = new();
    public string MinimumBitrateKbps { get; init; } = "";
    public string MaximumBitrateKbps { get; init; } = "";
}

internal sealed record GenesisRecordingRuleEditorResult(
    GenesisRecordingRuleRepositoryWriteStatus? Status, string Message, bool RequiresConfirmation = false)
{
    public bool Applied => Status == GenesisRecordingRuleRepositoryWriteStatus.Applied;
}

internal sealed class GenesisRecordingRuleEditor : IAsyncDisposable
{
    public const string UnsupportedMessage = "このルールには現在の画面で編集できない条件が含まれています。";
    public const string ConflictMessage = "録画ルールが外部で更新されています。再読み込みしてからもう一度編集してください。";
    private readonly GenesisRecordingRuleMutationController mutations;
    private GenesisRecordingRuleMutationController.Reservation? reservation;
    private readonly object gate = new();
    private readonly CancellationTokenSource shutdown = new();
    private CancellationTokenSource? attemptCancellation;
    private TaskCompletionSource drained = CompletedDrain();
    private bool saving;
    private bool stale;
    private bool disposed;
    private bool cancellationDisposed;
    private GenesisRecordingRuleDraft? draft;

    public GenesisRecordingRuleEditor(GenesisRecordingRuleMutationController mutations)
    {
        this.mutations = mutations;
    }

    public GenesisRecordingRuleDraft? Draft { get { lock (gate) { return draft; } } }
    public bool CanSave { get { lock (gate) { return draft is not null && !saving && !stale && !disposed; } } }
    public static bool CanEdit(RecordingRule rule) => rule.Criteria.Genre is null &&
        rule.Criteria.ContentType is null && rule.Criteria.Comment is null;

    public GenesisRecordingRuleDraft? BeginCreate(GenesisRecordingRuleRepositorySnapshot? snapshot) =>
        Open(snapshot, null);
    public GenesisRecordingRuleDraft? BeginEdit(GenesisRecordingRuleRepositorySnapshot? snapshot, Guid id) =>
        Open(snapshot, id);

    private GenesisRecordingRuleDraft? Open(GenesisRecordingRuleRepositorySnapshot? snapshot, Guid? id)
    {
        lock (gate)
        {
            if (disposed || saving || draft is not null || snapshot is not { IsAvailable: true, Rules: not null, Revision: not null })
                return null;
            int? index = null;
            RecordingRule? rule = null;
            if (id is { } ruleId)
            {
                for (var i = 0; i < snapshot.Rules.Rules.Count; i++)
                    if (snapshot.Rules.Rules[i].Id == ruleId) { index = i; rule = snapshot.Rules.Rules[i]; break; }
                if (rule is null || !CanEdit(rule)) return null;
            }
            reservation = mutations.TryReserve();
            if (reservation is null) return null;
            draft = new(rule is null ? GenesisRecordingRuleEditorMode.Create : GenesisRecordingRuleEditorMode.Edit,
                rule?.Id ?? Guid.NewGuid(), snapshot, index);
            if (rule is not null)
            {
                draft = draft with
                {
                    Name = rule.Name,
                    IsEnabled = rule.IsEnabled,
                    Action = rule.Action,
                    Priority = rule.Priority.ToString(CultureInfo.InvariantCulture),
                    // Even legacy catch-all rules need explicit intent in an editor.
                    MatchAll = false,
                    ChannelId = rule.Criteria.ChannelId?.ToString() ?? "",
                    NamePattern = FromPattern(rule.Criteria.Name),
                    DescriptionPattern = FromPattern(rule.Criteria.Description),
                    StreamTypePattern = FromPattern(rule.Criteria.StreamType),
                    MinimumBitrateKbps = rule.Criteria.MinimumBitrateKbps?.ToString(CultureInfo.InvariantCulture) ?? "",
                    MaximumBitrateKbps = rule.Criteria.MaximumBitrateKbps?.ToString(CultureInfo.InvariantCulture) ?? "",
                };
            }
            stale = false;
            attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token, reservation.Token);
            return draft;
        }
    }

    public void Cancel()
    {
        lock (gate)
        {
            draft = null;
            attemptCancellation?.Cancel();
            if (!saving) { attemptCancellation?.Dispose(); attemptCancellation = null; reservation?.Dispose(); reservation = null; }
        }
    }

    public async Task<GenesisRecordingRuleEditorResult> SaveAsync(
        GenesisRecordingRuleDraft frozenDraft, bool broadMatchConfirmed = false)
    {
        CancellationToken token;
        lock (gate)
        {
            if (disposed || saving || stale || draft is null ||
                frozenDraft.AttemptId != draft.AttemptId ||
                frozenDraft.RuleId != draft.RuleId || frozenDraft.Mode != draft.Mode ||
                frozenDraft.OriginalIndex != draft.OriginalIndex ||
                !ReferenceEquals(frozenDraft.BaseSnapshot, draft.BaseSnapshot))
                return new(null, stale ? ConflictMessage : "現在は保存できません。");
            draft = frozenDraft;
            saving = true;
            drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            token = attemptCancellation!.Token;
        }
        try
        {
            var validation = BuildProposedRules(frozenDraft, out var proposed, out var warning);
            if (validation is not null) return new(null, validation);
            if (warning is not null && !broadMatchConfirmed) return new(null, warning, true);
            var result = await mutations.SaveAsync(reservation!, frozenDraft.BaseSnapshot, proposed!,
                frozenDraft.RuleId, null, token).ConfigureAwait(false);
            lock (gate)
            {
                if (result.Status is GenesisRecordingRuleRepositoryWriteStatus.Conflict or GenesisRecordingRuleRepositoryWriteStatus.Unavailable) stale = true;
                if (result.Applied) draft = null;
            }
            return result.Status == GenesisRecordingRuleRepositoryWriteStatus.Conflict
                ? result with { Message = ConflictMessage } : result;
        }
        finally
        {
            lock (gate)
            {
                saving = false;
                if (draft is null || disposed) { attemptCancellation?.Dispose(); attemptCancellation = null; reservation?.Dispose(); reservation = null; }
                drained.TrySetResult();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task pending;
        lock (gate)
        {
            if (!disposed)
            {
                disposed = true;
                shutdown.Cancel();
                draft = null;
            }
            pending = drained.Task;
        }
        await pending.ConfigureAwait(false);
        lock (gate)
        {
            attemptCancellation?.Dispose(); attemptCancellation = null;
            reservation?.Dispose(); reservation = null;
            if (!cancellationDisposed) { shutdown.Dispose(); cancellationDisposed = true; }
        }
    }

    internal static string? BuildProposedRules(GenesisRecordingRuleDraft draft,
        out RecordingRuleSet? proposed, out string? warning)
    {
        proposed = null; warning = null;
        if (string.IsNullOrWhiteSpace(draft.Name)) return "ルール名を入力してください。";
        if (!int.TryParse(draft.Priority, NumberStyles.Integer, CultureInfo.InvariantCulture, out var priority))
            return "優先度を整数で入力してください。";
        try
        {
            RecordingRuleCriteria criteria;
            if (draft.MatchAll)
            {
                criteria = RecordingRuleCriteria.MatchAll;
                warning = "このルールはすべてのチャンネルに一致します。保存しますか？" +
                    (draft.Action == RecordingRuleAction.Record ? "\n次回のチャンネル評価で自動録画の対象になる可能性があります。" : "");
            }
            else
            {
                ChannelId? channelId = null;
                if (draft.ChannelId.Length != 0)
                {
                    // Same canonical N-format as the existing Core schema-v1 codec;
                    // ChannelId itself remains the authoritative non-empty identity validation.
                    if (!Guid.TryParseExact(draft.ChannelId, "N", out var value)) return "ChannelIdは32桁の識別子で入力してください。";
                    channelId = new ChannelId(value);
                }
                var name = Pattern(draft.NamePattern);
                var description = Pattern(draft.DescriptionPattern);
                var stream = Pattern(draft.StreamTypePattern);
                var minimum = Bitrate(draft.MinimumBitrateKbps);
                var maximum = Bitrate(draft.MaximumBitrateKbps);
                if (channelId is null && name is null && description is null && stream is null && minimum is null && maximum is null)
                    return "条件を1つ以上指定するか、「すべてのチャンネルに一致」を選択してください。";
                criteria = new(channelId: channelId, name: name, description: description, streamType: stream,
                    minimumBitrateKbps: minimum, maximumBitrateKbps: maximum);
                if (new[] { name, description, stream }.Any(p => p?.Mode == RecordingTextMatchMode.Regex))
                    warning = "正規表現は多数のチャンネルに一致する可能性があります。内容を確認して保存しますか？";
            }
            var rule = new RecordingRule(draft.RuleId, draft.Name, draft.Action, criteria, priority, draft.IsEnabled);
            var original = draft.BaseSnapshot.Rules!;
            proposed = draft.Mode == GenesisRecordingRuleEditorMode.Create ? original.Add(rule) : original.Replace(rule);
            return null;
        }
        catch (ArgumentException) { return "条件・一致方法・ビットレート範囲・ルール件数を確認してください。"; }
        catch (FormatException) { return "ビットレートを整数（kbps）で入力してください。"; }
        catch (OverflowException) { return "ビットレートを整数（kbps）で入力してください。"; }
        catch (KeyNotFoundException) { return ConflictMessage; }
    }

    private static GenesisRecordingPatternDraft FromPattern(RecordingTextPattern? value) =>
        value is null ? new() : new(value.Value, value.Mode);
    private static RecordingTextPattern? Pattern(GenesisRecordingPatternDraft value) =>
        value.Value.Length == 0 ? null : new(value.Value, value.Mode);
    private static int? Bitrate(string value) => value.Length == 0 ? null : int.Parse(value, CultureInfo.InvariantCulture);
    private static TaskCompletionSource CompletedDrain() { var value = new TaskCompletionSource(); value.SetResult(); return value; }
}
