using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

internal enum GenesisRecordingRuleQuickAction { Enable, Disable, Delete, MoveUp, MoveDown }

internal sealed record GenesisRecordingRuleConfirmation(
    Guid RuleId, string Name, RecordingRuleAction Action, bool IsEnabled, string Message);

// One application-owned reservation covers editor drafts, confirmations and saves.
// The repository remains the sole persistence/revision authority.
internal sealed class GenesisRecordingRuleMutationController : IAsyncDisposable
{
    public const string PersistedOrderHelp = "保存順は同じ優先度のルール同士の判定順に使用されます。変更は次回のチャンネル更新時に反映されます。";
    public const string ConflictMessage = "録画ルールが外部で更新されています。最新の内容を確認してからもう一度操作してください。";
    private readonly GenesisRecordingRuleRepository repository;
    private readonly Action<GenesisRecordingRuleRepositorySnapshot, Guid?> publish;
    private readonly object gate = new();
    private readonly CancellationTokenSource shutdown = new();
    private Reservation? active;
    private bool disposed;
    private TaskCompletionSource drained = CompletedDrain();
    private Task? disposal;

    public GenesisRecordingRuleMutationController(GenesisRecordingRuleRepository repository,
        Action<GenesisRecordingRuleRepositorySnapshot, Guid?> publish)
    {
        this.repository = repository;
        this.publish = publish;
    }

    public event Action? AvailabilityChanged;
    public bool IsAvailable { get { lock (gate) { return !disposed && active is null; } } }

    internal Reservation? TryReserve()
    {
        Reservation reservation;
        lock (gate)
        {
            if (disposed || active is not null) return null;
            reservation = new(this, shutdown.Token);
            active = reservation;
            drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        AvailabilityChanged?.Invoke();
        return reservation;
    }

    public async Task<GenesisRecordingRuleEditorResult> ExecuteAsync(
        GenesisRecordingRuleRepositorySnapshot snapshot, Guid ruleId,
        GenesisRecordingRuleQuickAction action,
        Func<GenesisRecordingRuleConfirmation, CancellationToken, ValueTask<bool>> confirm)
    {
        using var reservation = TryReserve();
        if (reservation is null) return new(null, "現在は操作できません。");
        var rule = snapshot.IsAvailable ? snapshot.Rules?.Rules.FirstOrDefault(r => r.Id == ruleId) : null;
        if (snapshot.Revision is null || rule is null || !Enum.IsDefined(action))
            return new(null, "現在は操作できません。最新の内容を確認してください。");
        if ((action == GenesisRecordingRuleQuickAction.Enable && rule.IsEnabled) ||
            (action == GenesisRecordingRuleQuickAction.Disable && !rule.IsEnabled))
            return new(null, "変更はありません。");

        var moving = action is GenesisRecordingRuleQuickAction.MoveUp or GenesisRecordingRuleQuickAction.MoveDown;
        var target = moving ? GetMoveTarget(snapshot.Rules!, rule.Id, action) : null;
        if (moving && target is null) return new(null, "現在は移動できません。");
        var proposed = moving ? snapshot.Rules!.Move(rule.Id, target!.Value)
            : action == GenesisRecordingRuleQuickAction.Delete
                ? snapshot.Rules!.Remove(rule.Id)
                : snapshot.Rules!.Replace(new RecordingRule(rule.Id, rule.Name, rule.Action,
                    rule.Criteria, rule.Priority, action == GenesisRecordingRuleQuickAction.Enable));
        try
        {
            var confirmation = moving
                ? CreateReorderConfirmation(rule, snapshot.Rules!.Rules[target!.Value])
                : CreateConfirmation(rule, action);
            if (confirmation is not null && !await confirm(confirmation, reservation.Token).ConfigureAwait(false))
                return new(null, "変更を取りやめました。");
            reservation.Token.ThrowIfCancellationRequested();
            return await SaveAsync(reservation, snapshot, proposed,
                action == GenesisRecordingRuleQuickAction.Delete ? null : rule.Id,
                rule.Id, reservation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new(GenesisRecordingRuleRepositoryWriteStatus.Cancelled, Feedback(GenesisRecordingRuleRepositoryWriteStatus.Cancelled));
        }
        catch
        {
            return new(GenesisRecordingRuleRepositoryWriteStatus.PersistenceFailed, Feedback(GenesisRecordingRuleRepositoryWriteStatus.PersistenceFailed));
        }
    }

    internal async Task<GenesisRecordingRuleEditorResult> SaveAsync(
        Reservation reservation, GenesisRecordingRuleRepositorySnapshot expected,
        RecordingRuleSet proposed, Guid? appliedSelection, Guid? conflictSelection,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (disposed || !ReferenceEquals(active, reservation))
                return new(GenesisRecordingRuleRepositoryWriteStatus.Cancelled, Feedback(GenesisRecordingRuleRepositoryWriteStatus.Cancelled));
        }
        GenesisRecordingRuleRepositoryWriteResult result;
        try { result = await repository.SaveAsync(expected, proposed, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return new(GenesisRecordingRuleRepositoryWriteStatus.Cancelled, Feedback(GenesisRecordingRuleRepositoryWriteStatus.Cancelled)); }
        catch { return new(GenesisRecordingRuleRepositoryWriteStatus.PersistenceFailed, Feedback(GenesisRecordingRuleRepositoryWriteStatus.PersistenceFailed)); }
        lock (gate)
        {
            if (disposed || cancellationToken.IsCancellationRequested)
                return new(GenesisRecordingRuleRepositoryWriteStatus.Cancelled, Feedback(GenesisRecordingRuleRepositoryWriteStatus.Cancelled));
            if (result.Snapshot is { } snapshot)
            {
                if (result.Succeeded) publish(snapshot, ResolveSelection(snapshot, appliedSelection));
                else if (result.Status == GenesisRecordingRuleRepositoryWriteStatus.Conflict)
                    publish(snapshot, ResolveSelection(snapshot, conflictSelection));
                else if (result.Status == GenesisRecordingRuleRepositoryWriteStatus.Unavailable)
                    publish(snapshot.IsAvailable ? new(GenesisRecordingRuleSnapshotStatus.Unavailable,
                        null, null, result.FailureKind) : snapshot, null);
            }
        }
        return new(result.Status, Feedback(result.Status));
    }

    private static Guid? ResolveSelection(GenesisRecordingRuleRepositorySnapshot snapshot, Guid? id) =>
        id is { } selected && snapshot.Rules?.Rules.Any(r => r.Id == selected) == true ? selected : null;

    internal static int? GetMoveTarget(RecordingRuleSet rules, Guid ruleId, GenesisRecordingRuleQuickAction action)
    {
        if (action is not (GenesisRecordingRuleQuickAction.MoveUp or GenesisRecordingRuleQuickAction.MoveDown)) return null;
        for (var index = 0; index < rules.Rules.Count; index++)
        {
            if (rules.Rules[index].Id != ruleId) continue;
            var target = index + (action == GenesisRecordingRuleQuickAction.MoveUp ? -1 : 1);
            return target >= 0 && target < rules.Rules.Count ? target : null;
        }
        return null;
    }

    private static GenesisRecordingRuleConfirmation? CreateReorderConfirmation(RecordingRule rule, RecordingRule neighbor) =>
        rule.IsEnabled && neighbor.IsEnabled && rule.Priority == neighbor.Priority
            ? new(rule.Id, rule.Name, rule.Action, rule.IsEnabled,
                "同じ優先度の有効な録画ルールの保存順を変更します。次回のチャンネル更新で適用されるルールが変わる可能性があります。移動しますか？")
            : null;

    internal static GenesisRecordingRuleConfirmation? CreateConfirmation(
        RecordingRule rule, GenesisRecordingRuleQuickAction action)
    {
        var consequence = rule.Action == RecordingRuleAction.Record
            ? "次回のチャンネル更新で自動録画の対象になる可能性があります。"
            : "次回のチャンネル更新で除外が適用され、自動録画が停止する場合があります。";
        string message;
        if (action == GenesisRecordingRuleQuickAction.Delete)
            message = $"この録画ルールを削除しますか？\nルール名: {rule.Name}\n動作: {GenesisGuiNextRecordingRuleProjection.ActionLabel(rule.Action)}\n状態: {(rule.IsEnabled ? "有効" : "無効")}\n変更は次回のチャンネル更新時に反映されます。";
        else if (action != GenesisRecordingRuleQuickAction.Enable) return null;
        else if (rule.Criteria.IsMatchAll)
            message = $"このルールはすべてのチャンネルに一致します。有効化しますか？\n{consequence}";
        else if (new[] { rule.Criteria.Name, rule.Criteria.Genre, rule.Criteria.Description,
                     rule.Criteria.ContentType, rule.Criteria.StreamType, rule.Criteria.Comment }
                 .Any(p => p?.Mode == RecordingTextMatchMode.Regex))
            message = $"正規表現を含むルールです。有効化しますか？\n{consequence}";
        else return null;
        return new(rule.Id, rule.Name, rule.Action, rule.IsEnabled, message);
    }

    internal static string Feedback(GenesisRecordingRuleRepositoryWriteStatus status) => status switch
    {
        GenesisRecordingRuleRepositoryWriteStatus.Applied => "変更は次回のチャンネル更新時に反映されます。",
        GenesisRecordingRuleRepositoryWriteStatus.Conflict => ConflictMessage,
        GenesisRecordingRuleRepositoryWriteStatus.Invalid => "録画ルールの内容を保存できません。入力を確認してください。",
        GenesisRecordingRuleRepositoryWriteStatus.PersistenceFailed => "録画ルールを保存できませんでした。保存先を確認してください。",
        GenesisRecordingRuleRepositoryWriteStatus.Cancelled => "保存をキャンセルしました。",
        _ => "録画ルールを利用できません。再読み込みしてください。",
    };

    private void Release(Reservation reservation)
    {
        lock (gate)
        {
            if (!ReferenceEquals(active, reservation)) return;
            active = null;
            drained.TrySetResult();
        }
        AvailabilityChanged?.Invoke();
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposal is not null) return new(disposal);
            disposed = true;
            shutdown.Cancel();
            disposal = DisposeAfterDrainAsync(drained.Task);
            return new(disposal);
        }
    }

    private async Task DisposeAfterDrainAsync(Task pending)
    {
        await pending.ConfigureAwait(false);
        shutdown.Dispose();
    }

    private static TaskCompletionSource CompletedDrain()
    {
        var source = new TaskCompletionSource(); source.SetResult(); return source;
    }

    internal sealed class Reservation : IDisposable
    {
        private readonly GenesisRecordingRuleMutationController owner;
        private readonly CancellationTokenSource cancellation;
        private int released;
        internal Reservation(GenesisRecordingRuleMutationController owner, CancellationToken token)
        {
            this.owner = owner;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        }
        public CancellationToken Token => cancellation.Token;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) != 0) return;
            cancellation.Dispose();
            owner.Release(this);
        }
    }
}
