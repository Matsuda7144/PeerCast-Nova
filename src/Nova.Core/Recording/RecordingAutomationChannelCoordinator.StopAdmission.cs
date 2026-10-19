namespace PeerCast.Nova.Core.Recording;

public sealed partial class RecordingAutomationChannelCoordinator
{
    private RecordingAutomaticStopAdmission? automaticStop;
    private Task? automaticStopOwner;

    internal bool HasPendingAutomaticStop =>
        Volatile.Read(ref automaticStop) is { Completion.IsCompletedSuccessfully: false };

    internal async Task DrainAutomaticStopAsync()
    {
        var receipt = Volatile.Read(ref automaticStop);
        if (receipt is not null)
        {
            await receipt.Completion.ConfigureAwait(false);
            if (automaticStopOwner is { } owner)
            {
                await owner.ConfigureAwait(false);
            }
        }
    }

    public bool TryGetAutomaticStopAdmission(
        RecordingSessionId expectedSessionId,
        out RecordingAutomaticStopAdmission? admission)
    {
        ArgumentNullException.ThrowIfNull(expectedSessionId);
        var current = Volatile.Read(ref automaticStop);
        admission = current?.SessionId == expectedSessionId ? current : null;
        return admission is not null;
    }

    public async ValueTask<RecordingAutomaticStopAdmissionResult> RequestAutomaticStopAsync(
        RecordingSessionId expectedSessionId,
        CancellationToken admissionToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedSessionId);
        try
        {
            await gate.WaitAsync(admissionToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (admissionToken.IsCancellationRequested)
        {
            return NotAdmitted(RecordingAutomaticStopNotAdmittedReason.CancelledBeforeAdmission);
        }

        try
        {
            ThrowIfDisposed();
            var current = state;
            var session = current.Session;
            if (session is null)
            {
                return NotAdmitted(RecordingAutomaticStopNotAdmittedReason.NoActiveSession);
            }

            if (session.SessionId != expectedSessionId)
            {
                return NotAdmitted(RecordingAutomaticStopNotAdmittedReason.StaleGeneration);
            }

            if (session.Origin != RecordingSessionOrigin.AutomaticRule)
            {
                return NotAdmitted(RecordingAutomaticStopNotAdmittedReason.ManualMismatch);
            }

            if (automaticStop is { } existing && existing.SessionId == expectedSessionId)
            {
                return RecordingAutomaticStopAdmissionResult.Admitted(existing, isNew: false);
            }

            if (!current.AutomationState.IsRecording ||
                current.AutomationState.ActiveRuleId is not { } winner)
            {
                return NotAdmitted(RecordingAutomaticStopNotAdmittedReason.ProvenanceUnavailable);
            }

            if (sink is not IRecordingStopAdmissionSink capable)
            {
                return NotAdmitted(RecordingAutomaticStopNotAdmittedReason.Unavailable);
            }

            // The manager already holds a provisional entry pin. Capture the
            // committed winner before sink terminal cleanup can erase it.
            RecordingSinkStopAdmission? sinkAdmission;
            try
            {
                var result = capable.RequestStopAdmission(
                    new RecordingSinkStopRequest(session), admissionToken);
                sinkAdmission = result.Admission;
                if (sinkAdmission is null)
                {
                    return NotAdmitted(MapReason(result.Reason!.Value));
                }
            }
            catch (Exception exception)
            {
                if (!capable.TryGetStopAdmission(session, out sinkAdmission) ||
                    sinkAdmission is null)
                {
                    return RecordingAutomaticStopAdmissionResult.NotAdmitted(
                        RecordingAutomaticStopNotAdmittedReason.FailureBeforeAdmission,
                        RecordingFaultDescriptor.FromException(exception));
                }
            }

            var completion = new TaskCompletionSource<RecordingAutomationStopResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var receipt = new RecordingAutomaticStopAdmission(
                ChannelId, expectedSessionId, winner, completion.Task);
            Volatile.Write(ref automaticStop, receipt);
            // The worker owns no locks. Long completion waits never retain the
            // channel gate. Its errors are observed without inventing terminal.
            automaticStopOwner = Task.Run(() =>
                CompleteAutomaticStopAsync(receipt, sinkAdmission, completion), CancellationToken.None);
            return RecordingAutomaticStopAdmissionResult.Admitted(receipt, isNew: true);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task CompleteAutomaticStopAsync(
        RecordingAutomaticStopAdmission receipt,
        RecordingSinkStopAdmission sinkReceipt,
        TaskCompletionSource<RecordingAutomationStopResult> completion)
    {
        try
        {
            var terminal = await sinkReceipt.Completion.ConfigureAwait(false);
            var kind = terminal.Kind switch
            {
                RecordingSinkStopResultKind.Stopped => RecordingAutomationStopResultKind.Stopped,
                RecordingSinkStopResultKind.NoDecodableVideoStart =>
                    RecordingAutomationStopResultKind.NoDecodableVideoStart,
                RecordingSinkStopResultKind.Faulted => RecordingAutomationStopResultKind.Faulted,
                _ => throw new InvalidOperationException("An admitted sink must resolve exact terminal cleanup."),
            };

            await gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (state.Session?.SessionId == receipt.SessionId)
                {
                    Volatile.Write(ref state, RecordingAutomationExecutionState.Idle);
                }

                completion.TrySetResult(new RecordingAutomationStopResult(
                    ChannelId, kind, state, terminal.Fault));
            }
            finally
            {
                gate.Release();
            }
        }
        catch (Exception exception)
        {
            receipt.RecordResolutionFault(exception);
            // The receipt stays pending/pinned. Shutdown cannot claim success.
        }
    }

    private async ValueTask EnterMutationAsync(CancellationToken token)
    {
        while (true)
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            var pending = automaticStop;
            if (pending is null || pending.Completion.IsCompletedSuccessfully)
            {
                return;
            }

            gate.Release();
            await pending.Completion.WaitAsync(token).ConfigureAwait(false);
        }
    }

    private static RecordingAutomaticStopAdmissionResult NotAdmitted(
        RecordingAutomaticStopNotAdmittedReason reason) =>
        RecordingAutomaticStopAdmissionResult.NotAdmitted(reason);

    private static RecordingAutomaticStopNotAdmittedReason MapReason(
        RecordingSinkStopNotAdmittedReason reason) => reason switch
        {
            RecordingSinkStopNotAdmittedReason.CancelledBeforeAdmission =>
                RecordingAutomaticStopNotAdmittedReason.CancelledBeforeAdmission,
            RecordingSinkStopNotAdmittedReason.AlreadyTerminal =>
                RecordingAutomaticStopNotAdmittedReason.AlreadyTerminal,
            RecordingSinkStopNotAdmittedReason.AlreadyStoppingByOtherOperation =>
                RecordingAutomaticStopNotAdmittedReason.AlreadyStoppingByOtherOperation,
            RecordingSinkStopNotAdmittedReason.Unavailable =>
                RecordingAutomaticStopNotAdmittedReason.Unavailable,
            RecordingSinkStopNotAdmittedReason.Rejected =>
                RecordingAutomaticStopNotAdmittedReason.Rejected,
            _ => RecordingAutomaticStopNotAdmittedReason.FailureBeforeAdmission,
        };
}
