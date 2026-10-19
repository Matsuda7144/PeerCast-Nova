using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

internal interface IGenesisAutomaticRecordingCatalogObserver
{
    ValueTask<GenesisAutomaticRecordingEvaluationSummary> ObserveAsync(
        IReadOnlyList<GenesisChannelItem> channels,
        CancellationToken cancellationToken = default);
}

internal interface IGenesisRecordingRuleSetProvider
{
    ValueTask<RecordingRuleSet> GetSnapshotAsync(
        CancellationToken cancellationToken = default);
}

internal sealed class GenesisRecordingRuleFileProvider(string path) :
    IGenesisRecordingRuleSetProvider
{
    private static readonly RecordingRuleSet EmptyRuleSet = new([]);

    public ValueTask<RecordingRuleSet> GetSnapshotAsync(
        CancellationToken cancellationToken = default) =>
        LoadSnapshotAsync(path, cancellationToken);

    internal static IGenesisRecordingRuleSetProvider CreateSystem()
    {
        var path = GenesisRecordingRuleFilePath.GetSystemPath();
        return path is null
            ? new FixedGenesisRecordingRuleSetProvider(EmptyRuleSet)
            : new GenesisRecordingRuleFileProvider(path);
    }

    private static async ValueTask<RecordingRuleSet> LoadSnapshotAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var ruleSet = await RecordingRuleFileStore.LoadIfExistsAsync(
                path,
                cancellationToken)
            .ConfigureAwait(false);
        return ruleSet ?? EmptyRuleSet;
    }

    private sealed class FixedGenesisRecordingRuleSetProvider(
        RecordingRuleSet ruleSet) : IGenesisRecordingRuleSetProvider
    {
        public ValueTask<RecordingRuleSet> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ruleSet);
        }
    }
}

internal sealed record GenesisAutomaticRecordingEvaluationSummary(
    bool RuleSetAvailable,
    int CandidateCount,
    int EvaluatedCount,
    int StartedCount,
    int StoppedCount,
    int FailedCount,
    bool Cancelled);

/// <summary>
/// Evaluates each eligible channel once per successful catalog snapshot. It
/// prepares the shared PCP owner only when Recording Core has chosen Start.
/// </summary>
internal sealed partial class GenesisAutomaticRecordingEvaluationController :
    IGenesisAutomaticRecordingCatalogObserver,
    IGenesisAutomaticRecordingUserCommands,
    IAsyncDisposable
{
    private readonly RecordingRuntime _recording;
    private readonly IGenesisSharedPcpRecordingOwnerProvider _ownerProvider;
    private readonly IGenesisRecordingRuleSetProvider _ruleSetProvider;
    private readonly SemaphoreSlim _snapshotGate = new(1, 1);
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private int _disposed;
    private readonly GenesisAutomaticRecordingSuppression _suppression = new();
    private readonly Func<RecordingRuleSet, RecordingCandidate, RecordingRuleDecision> _evaluate;

    public GenesisAutomaticRecordingEvaluationController(
        RecordingRuntime recording,
        IGenesisSharedPcpRecordingOwnerProvider ownerProvider,
        IGenesisRecordingRuleSetProvider ruleSetProvider,
        Func<RecordingRuleSet, RecordingCandidate, RecordingRuleDecision>? evaluate = null)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(ownerProvider);
        ArgumentNullException.ThrowIfNull(ruleSetProvider);
        _recording = recording;
        _ownerProvider = ownerProvider;
        _ruleSetProvider = ruleSetProvider;
        _evaluate = evaluate ?? ((rules, candidate) => rules.Evaluate(candidate));
    }

    public async ValueTask<GenesisAutomaticRecordingEvaluationSummary> ObserveAsync(
        IReadOnlyList<GenesisChannelItem> channels,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return EmptySummary(cancelled: true);
        }

        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdownCancellation.Token);
        try
        {
            await _snapshotGate.WaitAsync(operationCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (operationCancellation.IsCancellationRequested)
        {
            return EmptySummary(cancelled: true);
        }

        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return EmptySummary(cancelled: true);
            }

            RecordingRuleSet ruleSet;
            try
            {
                ruleSet = await _ruleSetProvider.GetSnapshotAsync(
                        operationCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (operationCancellation.IsCancellationRequested)
            {
                return EmptySummary(cancelled: true);
            }
            catch
            {
                return EmptySummary(ruleSetAvailable: false);
            }

            _suppression.ClearUnobservedRules(ruleSet);
            var suppressionSnapshot = _suppression.Snapshot().ToDictionary(e => e.ChannelId);
            var pendingChannels = _suppression.PendingChannelsSnapshot();
            var seenChannels = new HashSet<ChannelId>();
            var candidateCount = 0;
            var evaluatedCount = 0;
            var startedCount = 0;
            var stoppedCount = 0;
            var failedCount = 0;

            foreach (var channel in channels)
            {
                if (operationCancellation.IsCancellationRequested)
                {
                    return new(
                        true,
                        candidateCount,
                        evaluatedCount,
                        startedCount,
                        stoppedCount,
                        failedCount,
                        true);
                }

                if (!GenesisRecordingCandidateAdapter.TryCreate(channel, out var adapted) ||
                    adapted is null ||
                    !seenChannels.Add(adapted.Candidate.ChannelId))
                {
                    continue;
                }

                candidateCount++;
                try
                {
                    var decision = _evaluate(ruleSet, adapted.Candidate);
                    if (pendingChannels.Contains(adapted.Candidate.ChannelId))
                    {
                        evaluatedCount++;
                        continue;
                    }
                    if (suppressionSnapshot.TryGetValue(adapted.Candidate.ChannelId, out var suppressed))
                    {
                        if (suppressed.State != GenesisAutomaticRecordingSuppressionState.Committed ||
                            decision.Kind == RecordingRuleDecisionKind.Record && decision.MatchedRule?.Id == suppressed.RuleId)
                        {
                            evaluatedCount++;
                            continue;
                        }

                        if (decision.Kind is RecordingRuleDecisionKind.Record or RecordingRuleDecisionKind.Ignore)
                        {
                            _suppression.Allow(suppressed.ChannelId, suppressed.RuleId);
                        }
                    }

                    var result = await _recording.EvaluateAutomaticDecisionAsync(
                            adapted.Candidate,
                            decision,
                            (candidate, token) => PrepareStartAsync(
                                adapted,
                                candidate,
                                token),
                            operationCancellation.Token)
                        .ConfigureAwait(false);
                    evaluatedCount++;
                    if (result.Kind == RecordingAutomationExecutionResultKind.Started)
                    {
                        startedCount++;
                    }
                    else if (result.Kind is RecordingAutomationExecutionResultKind.Stopped or
                        RecordingAutomationExecutionResultKind.StopNoDecodableVideoStart or
                        RecordingAutomationExecutionResultKind.StopAlreadyStopped)
                    {
                        stoppedCount++;
                    }
                }
                catch (OperationCanceledException)
                    when (operationCancellation.IsCancellationRequested)
                {
                    return new(
                        true,
                        candidateCount,
                        evaluatedCount,
                        startedCount,
                        stoppedCount,
                        failedCount,
                        true);
                }
                catch
                {
                    failedCount++;
                }
            }

            return new(
                true,
                candidateCount,
                evaluatedCount,
                startedCount,
                stoppedCount,
                failedCount,
                false);
        }
        finally
        {
            _snapshotGate.Release();
            PublishSuppressionChanged();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        try
        {
            _shutdownCancellation.Cancel();
        }
        catch
        {
            // A cancellation callback must not prevent in-flight evaluation
            // from reaching the controller's drain boundary.
        }

        await _snapshotGate.WaitAsync().ConfigureAwait(false);
        _snapshotGate.Release();
        await Task.WhenAll(_suppression.Observers()).ConfigureAwait(false);
        _suppression.Clear();
    }

    private async ValueTask<IAsyncDisposable?> PrepareStartAsync(
        GenesisRecordingCandidate adapted,
        RecordingCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (candidate.ChannelId != adapted.Candidate.ChannelId)
        {
            return null;
        }

        var attachment = await _ownerProvider.AttachRecordingOwnerAsync(
                adapted.Source,
                cancellationToken)
            .ConfigureAwait(false);
        if (ReferenceEquals(attachment.Recording, _recording))
        {
            return attachment;
        }

        await attachment.DisposeAsync().ConfigureAwait(false);
        throw new InvalidOperationException(
            "The shared PCP owner returned a different Recording Runtime.");
    }

    private static GenesisAutomaticRecordingEvaluationSummary EmptySummary(
        bool ruleSetAvailable = true,
        bool cancelled = false) =>
        new(ruleSetAvailable, 0, 0, 0, 0, 0, cancelled);
}
