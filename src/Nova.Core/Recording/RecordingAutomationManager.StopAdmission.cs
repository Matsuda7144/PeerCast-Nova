using System.Runtime.CompilerServices;
using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Core.Recording;

public sealed partial class RecordingAutomationManager
{
    private int admissionOperations;
    private TaskCompletionSource? admissionsDrained;
    private readonly CancellationTokenSource admissionClosing = new();
    // Weak canonical identities retain recovery after idle eviction without
    // creating persistent channel history or another capacity policy.
    private readonly ConditionalWeakTable<RecordingSessionId, RecordingAutomaticStopAdmission>
        stopReceipts = new();

    public async ValueTask<RecordingAutomaticStopAdmissionResult> RequestAutomaticStopAsync(
        ChannelId channelId,
        RecordingSessionId expectedSessionId,
        CancellationToken admissionToken = default)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(expectedSessionId);
        CoordinatorEntry? entry;
        lock (gate)
        {
            ThrowIfDisposed();
            if (!acceptsEvaluations)
            {
                return RecordingAutomaticStopAdmissionResult.NotAdmitted(
                    RecordingAutomaticStopNotAdmittedReason.Unavailable);
            }

            if (admissionToken.IsCancellationRequested)
            {
                return RecordingAutomaticStopAdmissionResult.NotAdmitted(
                    RecordingAutomaticStopNotAdmittedReason.CancelledBeforeAdmission);
            }

            entry = TryAcquireCoordinator(channelId);
            if (entry is null)
            {
                return RecordingAutomaticStopAdmissionResult.NotAdmitted(
                    RecordingAutomaticStopNotAdmittedReason.NoActiveSession);
            }

            // Provisional canonical entry pin, before the sink can cross effect.
            if (admissionOperations++ == 0)
            {
                admissionsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        var transferredPin = false;
        using var linkedAdmission = CancellationTokenSource.CreateLinkedTokenSource(
            admissionToken, admissionClosing.Token);
        try
        {
            var result = await entry.Coordinator.RequestAutomaticStopAsync(
                expectedSessionId, linkedAdmission.Token).ConfigureAwait(false);
            if (result.Admission is { } canonical)
            {
                lock (gate)
                {
                    _ = stopReceipts.GetValue(expectedSessionId, _ => canonical);
                }
            }
            if (result is { Admission: { } receipt, IsNew: true })
            {
                // Transfer this operation pin to completion; shutdown joins it.
                entry.StopOwner = ReleaseAfterCompletionAsync(channelId, entry, receipt);
                transferredPin = true;
            }

            return result;
        }
        finally
        {
            if (!transferredPin)
            {
                ReleaseCoordinator(channelId, entry);
            }

            lock (gate)
            {
                if (--admissionOperations == 0)
                {
                    admissionsDrained!.TrySetResult();
                }
            }
        }
    }

    public bool TryGetAutomaticStopAdmission(
        ChannelId channelId,
        RecordingSessionId expectedSessionId,
        out RecordingAutomaticStopAdmission? admission)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(expectedSessionId);
        lock (gate)
        {
            ThrowIfDisposed();
            if (coordinators.TryGetValue(channelId, out var entry))
            {
                if (entry.Coordinator.TryGetAutomaticStopAdmission(expectedSessionId, out admission))
                {
                    return true;
                }
            }

            foreach (var pair in stopReceipts)
            {
                if (pair.Key == expectedSessionId && pair.Value.ChannelId == channelId)
                {
                    admission = pair.Value;
                    return true;
                }
            }

            admission = null;
            return false;
        }
    }

    private async Task ReleaseAfterCompletionAsync(
        ChannelId channelId,
        CoordinatorEntry entry,
        RecordingAutomaticStopAdmission receipt)
    {
        await receipt.Completion.ConfigureAwait(false);
        ReleaseCoordinator(channelId, entry);
    }

    private async Task DrainAutomaticAdmissionsAsync()
    {
        await admissionClosing.CancelAsync().ConfigureAwait(false);
        Task admissionDrain;
        lock (gate)
        {
            admissionDrain = admissionsDrained?.Task ?? Task.CompletedTask;
        }

        await admissionDrain.ConfigureAwait(false);
        Task[] owners;
        lock (gate)
        {
            owners = coordinators.Values.Select(static entry => entry.StopOwner)
                .OfType<Task>().ToArray();
        }

        foreach (var owner in owners)
        {
            await owner.ConfigureAwait(false);
        }
    }
}
