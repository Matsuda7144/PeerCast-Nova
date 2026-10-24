# Phase 5C-2: recording source completion evidence

Independent integration on main `5ea3d6c2931331d2328dfc68287d64d7004adc2b`
(tree `a4caf054e529dab4c77859bdc8b1421520f02cba`), after #321.
Provenance is only old #278's five-file delta from
`c7d16435d0eacbbdc8148cd057eac41c408efe11` to
`68b950bd7a1f8344ba6cccdfba283640cb9fdf31`; stacked history is not imported.
This slice changes the bounded
`PcpRelayFanoutRecordingStreamSourceLeaseFactory` source boundary. The publishing
owner, application commands, GUI, writer, file format, paths and rules are unchanged.

## Completion contract

The owner must stop upstream publication before calling
`CompleteWithEvidenceAsync(timeout, terminalFault, cancellationToken)`.
The positive timeout is capped at one minute and applies to the whole operation:

1. Close source acquisition and join already admitted acquisition/owner-lease work.
2. For normal completion, use the existing scoped fan-out idle wait to settle only
   this factory's children. Pending counts include in-flight delivery.
3. Join the existing `CompleteAsync` EOF/fault notification. First notification wins;
   a later normal request cannot erase an earlier fault.
4. Stop these children and await their actual completion tasks, including cleanup.
5. Wait for recording consumers to release their source leases naturally, including
   their optional borrowed owner lease cleanup.

An explicit terminal fault retains the existing forced-fault path instead of
pretending to drain undeliverable packets. Other borrowed fan-out children and the
global fan-out owner are not stopped by this source operation.

Concurrent typed completion calls share the first admitted task, deadline and
cancellation token. They are not retries. A pre-cancelled call does not close
admission. Existing callers of `CompleteAsync` keep its notification-only contract.

## Typed evidence and outcomes

`Evidence` contains fixed-size aggregate counters and flags for admission,
in-progress acquisitions, acquired/retired/active leases, pending packets,
delivered/discarded packets and bytes, EOF/fault notifications, source/delivery
failures, aborts, actual child termination and successful/failed/pending cleanup.
No URL, key, exception text, file path or per-session history is collected.
Failed retired leases remain represented by aggregates after removal. Disposal
request, task completion and successful cleanup are distinct facts.

Outcomes distinguish `NormalEof`, `SourceFault`, `DeliveryFailed`, `TimedOut`,
`Cancelled`, `CleanupIncomplete` and unexpected `Failed`. The result also records
the phase where waiting ended and whether the deadline expired. A cleanup deadline
returns `CleanupIncomplete` with `DeadlineExpired`, not cleanup success. Concurrent
conditions remain available in the evidence even when one outcome takes precedence.
Acquisition failure conservatively prevents a clean result.

Timeout/cancellation bounds observation; it does not make an uncompleted task
disappear or dispose an active reader to manufacture evidence. Pending work remains
visible. The caller still owns the existing forced-cleanup policy. A completed result
is immutable: late cleanup can update live `Evidence`, but never upgrade that
earlier timeout/incomplete/cancelled result into success.

## EOF is not writer finalization

`NormalEof` proves notification at the Pipe writer boundary, settled child delivery,
actual child termination and source lease cleanup. It does **not** prove that a
reader consumed all buffered bytes, that a media file is valid, or that the recording
file writer finalized successfully. Source delivery byte counts are not file bytes.
`WriterFinalizationObserved` is always false at this boundary and cannot be set by
consumers of the result.

Independent writer evidence already exists through
`IRecordingSessionTerminalObserver.ObserveTerminal(RecordingSessionTerminalFact)`.
A `Completed` terminal fact requires a writer-proven `RecordingFileWriteResult`
(including file bytes and completion kind) and carries its `RecordingSessionId`.
The source factory receives only the publishing `ChannelSessionId`, not that
recorder-owned SessionId or observer. Correlation/owner-wide aggregation is a later
slice; no writer result is inferred from EOF or lease disposal here.

## Validation scope

Managed fixtures cover buffered and blocked readers, blocked delivery, deadlines,
cancellation, duplicate completion/disposal, active/blocked/failing owner leases,
pending acquisition, retained historical faults/discards, unrelated borrowed
children, and an independent existing runtime/writer terminal observer.
Opaque `.bin` bytes validate the existing writer finalization contract without
making an FLV/media claim. No real OBS, physical playback, WAN or NAT operation
is performed, and this is not publishing-wide graceful stop or OBS normal EOS proof.

## Independent-integration design corrections

### A. Rejected lease cleanup is independent of acquisition cancellation

Pending acquisitions include cleanup of rejected leases. Rejected cleanup has
separate pending and failure counts, including owner Dispose OCE regardless of
the acquisition token. These counters do not increase acquired/retired lease
counts or duplicate AcquisitionFailureCount. Ordinary acquisition failures are
recorded at acquisition boundaries; rejected cleanup failures are recorded only
at their cleanup boundary. Both original acquisition and cleanup exceptions are
preserved if both fail. No unbounded rejected-lease history is retained.

### B. Accepted fault request is not notification evidence

AcceptedTerminalFault is fixed when the first valid typed call is accepted.
OwnerFaultRequested describes the fault choice of the first actual legacy
notification, not the typed request. TerminalNotificationSelected and
TerminalNotificationCompleted distinguish selection from successful execution.
A typed fault request can survive timeout/cancellation before notification.
An earlier legacy EOF remains the actual EOF winner even when a typed fault was
accepted; the typed outcome is conservatively SourceFault, never NormalEof.
No exception text is added to public evidence. Pre-cancelled requests do not set
these facts or close admission. Duplicate typed requests cannot replace them.

### C. Registration failure cannot skip bridge/owner cleanup

Stop-request failure is recorded, then actual registration Completion is still
joined. After actual termination, registration/terminal failure cannot skip
Bridge Dispose or borrowed Owner Lease Dispose. Multiple exceptions remain on
the original shared Dispose task (aggregated when needed); fixed registration,
bridge and owner cleanup failure counts preserve stage distinctions after
retirement. CleanupFailedLeaseCount counts the lease once, not each exception.
Worker Snapshot.CleanupFailed independently prevents clean source completion.

Two minimal supporting files add an internal child-completion observation
boundary (PcpRelayChildRegistration.cs and PcpRelayChildWorker.cs). It runs after
real worker cleanup outside locks; the optional recording observer can gate and
fault the actual Worker Completion task in deterministic tests. No public test
API is added. With no observer the boundary is a completed no-op. Bridge-stage
fault injection occurs after real bridge disposal and is not evidence of a
physical Pipe cleanup failure. Worker-token tests inject real cancellation
callback failure and verify #321 cleanup evidence is retained.

## Outcome precedence and preserved limits

On an otherwise finished operation: unproven/failed cleanup takes precedence,
then delivery failure, accepted/winning/source fault, aborted lease, proven EOF,
otherwise Failed. Waiting timeout/cancellation records its phase and deadline
status with all then-known fixed evidence. Late cleanup changes live Evidence,
not an already returned result. A cleanup deadline is CleanupIncomplete plus
DeadlineExpired. Owner cleanup and worker callbacks can remain noncooperative;
no finite forced termination guarantee is added.

Physical: NOT_RUN. Merge Readiness: NOT_ASSESSED. Managed source evidence does
not prove writer finalization, file bytes/format/playability, History success,
real OBS or WAN/NAT. #279 publishing-owner termination, #280 application Finish
and #281 GUI Finish are outside this slice. Existing #319/#320/#321 contracts,
borrowed fanout ownership, retained HEAD and recording backpressure remain.
