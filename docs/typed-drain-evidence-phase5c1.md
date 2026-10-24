# Phase 5C-1: typed local drain evidence

Independent base: main `efc8c1455499a4a94237a0a6c1b8570caea93df4`, after #320.
Provenance is only old #277's nine-file delta, base
`acc53d824188b49f473db7df4fd2e129b9e90053` to HEAD
`c7d16435d0eacbbdc8148cd057eac41c408efe11`; no stacked commits are imported.
This slice changes only PCP fan-out and the
HTTP-FLV source/client completion boundary. It does not integrate publishing
owner termination, recording finalization, application commands or GUI.

## Evidence and guarantee

`PcpRelayFanout.Evidence` is an immutable, fixed-field snapshot. Current child
snapshots and one aggregate for retired children provide packet/byte delivery
and discard totals, end-reason counts, initial-send failures and cleanup
callback failures. Removed children leave no retained child, sink, payload,
exception, endpoint or history collection. Counters are checked 64-bit totals;
queue and concurrent child limits remain unchanged. Counts are per child:
multicast and retained HEAD replay are separate sends, not unique source bytes.

These barriers are deliberately separate:

- `QueuesEmpty`: no queued live packet; an active send can still exist.
- `SendsSettled`: no queued/in-flight packet and no pending HTTP initial send.
- `SendsCompleted`: settled and no historical delivery failure/discard.
- `ChildrenStopped`: all captured worker tasks actually reached completion.
- `CleanupCompleted`: child completion waits succeeded, retained fan-out memory
  was released, and no historical cleanup callback failure was observed.

HTTP response-header/bootstrap writes join the existing child lifetime through
an optional internal startup barrier. Successful activation settles that
barrier; failure is signalled only after initial writes have returned. Abort
cancels those actual writes and still waits for their terminal barrier. Empty
live queues cannot hide a pending or failed initial HTTP write. Initial header
and bootstrap bytes are not included in live packet/byte counters.

`PcpRelayFanout.CompleteAfterUpstreamEndAsync` closes admission, reuses the
existing bounded idle wait, then joins the existing bounded child cleanup.
Caller must prove upstream consumption has ended before requesting completion;
this API does not quiesce an upstream producer. The first admitted request owns
one overall deadline/token and immutable result. Duplicate requests share its
task, including a failed result; they are not retries. Invalid or pre-cancelled
requests do not mutate admission. Timeout/cancellation during idle wait does not
abort an admitted send. Explicit `DisposeAsync` retains the forced-cancel path;
a concurrent forced disposal cannot produce successful drain evidence.

`CleanupCompletion` exposes the typed result of that existing disposal path.
An expired child wait is `Incomplete`, even if a worker finishes later. A
throwing child cancellation callback remains isolated from ingress, but its
failure is preserved and prevents successful cleanup. Legacy `DisposeAsync`
retains bounded return on incomplete waits and throws a fixed error on proven
cleanup failure. Its return alone is not cleanup evidence.

`PcpHttpFlvPlaybackSession.CompleteWithEvidenceAsync` adds a typed result:
`Completed`, `TimedOut`, `Cancelled`, `DeliveryFailed`, `CleanupIncomplete` or
`Failed`, plus separate fan-out evidence and source-removal/cleanup flags.
One overall deadline covers drain and source cleanup. HTTP preserves its
existing forced cleanup after an unsuccessful wait, but an expired wait does
not block indefinitely or claim cleanup. A synchronous source-removal callback
cannot extend the caller's bounded wait. The existing `CompleteAsync` boolean
route returns true only for `Completed`; cancellation still throws
`OperationCanceledException`. The publishing owner has not been switched to the
new result or a new finish command.

## Limits

Source removal alone is not child cleanup proof. A typed result records what
was proven at that boundary and does not upgrade after its deadline. Consult
live evidence or actual worker completion separately when later cleanup matters.
Forced cancellation can settle tasks without delivering their packets; discard
and failure totals remain visible after those children retire.

Send completion proves only that the local sink returned successfully. It does
not prove remote reception, decoding, playback or remote media completion.
HTTP source cleanup proves its local fan-out/bootstrap/removal boundary, not
completion of the host's listener, HTTP client monitor, socket disposal or
entire publishing owner. These continue to have their own host lifetime.
Recording source EOF is not recording-writer finalization. Neither OBS normal
EOS nor overall publishing graceful stop is implemented or certified here.

## Validation

Managed tests use deterministic gates and fake sinks for queue versus send
state, successful drain, slot reuse with retired failure totals, slow-consumer
discard, timeout, cancellation, duplicate completion, concurrent forced
disposal, blocked/throwing cleanup callbacks, initial HTTP send barriers and
failed/blocked source removal. Existing network/publishing tests exercise the
normal and forced-cancel routes. No real OBS, GUI, WAN or NAT operation is used.

## Independent-integration design corrections

### A: startup CTS ownership

One internal startup-lifetime owner holds the CTS. It counts actual header and
bootstrap operations; Activate/EndStartup cannot settle their barrier while a
write remains active. Cancellation request is a nonblocking request with a
separate owned task for actual cancellation and callbacks. The worker unregisters
its request callback and joins that actual task before its own Completion.
The client lifecycle ends initial writes, performs bounded registration stop,
closes the connection, then retains asynchronous ownership until the real worker,
startup and cancellation tasks end. Only then is the CTS disposed. A bounded stop
timeout never authorizes early CTS disposal or termination of a callback.

No external callback runs under the startup-owner lock. Duplicate cleanup shares
the actual task. Failures are observed by the worker and client lifecycle. A
narrow CA1001 annotation documents this custom asynchronous disposal contract;
an arbitrary IDisposable entry point would bypass the required worker proof.
Noncooperative writes/callbacks can keep that asynchronous cleanup pending.

### B: scoped cleanup failure evidence

The only extra production file beyond the old nine is
PcpDownstreamChildSession.cs. Its cleanup result now consumes the worker's
CleanupFailed flag, which feeds #320's existing fixed CleanupIncompleteCount and
CleanupFailed drain classification. No server-drain implementation is replaced.
Actual worker termination, transport cleanup, policy reservation release and
absence of callback failure remain separate facts. Callback failure does not
retain a lease forever once actual transport and worker termination are proven.
An internal observer at the existing test packet-writer seam registers a real
worker-token failure, outside registration/server/fan-out locks, so this bridge
is exercised through an actual PCP session and policy reservation. Production
writers do not implement the observation interface.

Scoped WaitForIdle remains server-local and does not call global Completion.
Retired registrations remain readable and #320's final failure recheck remains.
Global CompleteAfterUpstreamEndAsync may be used only by the whole fan-out owner;
a borrower must never use it to finish another owner's PCP/HTTP/recording child.
The HTTP session creates and owns its own fan-out, so its global completion has
that authority. Policy admission, reservation proof and close linearization stay
unchanged. The additional observer tests unrelated-owner failure isolation.

### C: late source cleanup fault observation

The session keeps its original shared disposal task and explicitly observes a
late Task.Exception after an outer bounded wait ends. A fixed SourceCleanupFailed
flag retains the failure without retaining exception details in public evidence.
Dispose callers still receive the original task exception. Typed results remain
immutable deadline snapshots: a later live failure/success does not rewrite an
earlier TimedOut/Cancelled result. Source removal remains single execution.
Legacy OCE uses the first admitted completion request's cancellation token.

New deterministic tests cover both cancellation/end orders, actual cancellation
callback retention across a bounded stop, header/bootstrap gates and injected
failure/timeout signals through the real loopback host, concurrent host disposal,
retained HEAD ownership release, scoped callback failure with successful lease
release, unrelated-owner isolation, and blocked removal that faults after timeout
or cancellation. No actual OS socket-write timeout or remote playback is inferred
from an injected boundary failure. Old #277's 24 PASS and old hosted totals are
history, not new validation; current results are recorded separately in the PR.

Physical: NOT_RUN. Merge Readiness: NOT_ASSESSED. HTTP source completion does not
prove the host listener/client-monitor/socket shutdown or publishing-wide Finish.
