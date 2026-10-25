# Phase 5C-3: publishing owner local termination

Independent base: main `c150f790c59bbee707c66625d55cd0c8f1a5d172`, tree
`dc3205e050359e4c35f905cdc869dabdf70107a0`, after #322.
Provenance: only old #279's seven-file/two-commit delta, base
`68b950bd7a1f8344ba6cccdfba283640cb9fdf31` to HEAD
`b233d6fb3bc96379bd6e7c98217c7c7153cc9bc9`. No stacked history is imported.
 `ObsPublishChannelHost.RequestLocalTerminationAsync`
adds an explicit, one-generation owner boundary. Startup, screen observation, F5,
and `DisposeAsync` do not request normal termination. Application commands and GUI
wiring are unchanged; this API is not exposed by their existing playback/publishing
interfaces in this slice.

## Dependency order and one deadline

The first admitted request owns a positive timeout (at most one minute), a monotonic
start time, an absolute cancellation deadline, and the caller token. Duplicates join
that same task; pre-cancelled/invalid/unstarted/already-ending requests do not start
runtime work. Each stage receives only the remaining budget, not a fresh timeout.

1. Close the identity-bound recording registration and stop future source acquisition.
   Close the PCP server's TCP/child admission barrier. An already admitted HEAD may
   finish; it cannot reopen source registration while the owner is ending.
2. Quiesce RTMP ingress with its existing API. An already admitted message continues
   without processing cancellation. Only `Quiesced` or an independently observed OBS
   `PublisherStopped` result establishes ended upstream. Final accepted packet/byte
   counts must match the owner callbacks before proceeding.
3. Wait for the existing shared fan-out's queued and in-flight work to settle.
4. Drain the PCP server's scoped sessions with the existing Phase 5B API.
5. Complete HTTP-FLV's nested fan-out/source with Phase 5C-1 typed evidence.
6. Complete recording source EOF/fault/child/lease boundaries using Phase 5C-2.
   Consumers release their leases naturally; active readers are never disposed to
   manufacture successful cleanup.
7. Complete the shared parent fan-out with Phase 5C-1, preserving retired failures,
   discards and child cleanup evidence.
8. Await actual ingest/server/fan-out/source disposal, and the HTTP listener's
   disposal only when owned. Borrowed listeners/registries are preserved. The HTTP
   source's existing cleanup predicate is also available internally for fallback
   inspection, without changing its drain or writer behavior.

Stopping the shared parent fan-out before steps 2-6 would cancel source/HTTP work
too early. Only the failed/expired/cancelled fallback uses forced cancellation and
attempts all owned releases, including when another release fails or remains blocked.
Fallback observation is bounded by the **same** deadline. Owner lifetime cancellation
does not prevent observing cleanup within its remaining budget.

## Result truthfulness

`ObsPublishLocalTerminationResult` carries the channel/generation identity, ended
upstream/counter/admission barriers, each actually observed typed stage result,
resource-specific cleanup facts, deadline expiry and the last reached phase.
Unreached stages remain null; they are not inferred from later disposal.

`LocalCompleted` requires all local delivery/drain and owned cleanup barriers.
`TimedOut`, `Cancelled`, `DeliveryFailed`, `CleanupIncomplete` and `Failed` remain
distinct. An expired recording lease cleanup is `CleanupIncomplete` with deadline
expiry, including when the shared deadline reaches that API as token cancellation.
Late cleanup never upgrades the immutable request result or owner `Completion`.
Pending cleanup remains owned and `DisposeAsync` joins it; false cleanup evidence
causes disposal failure, rather than a clean/restartable result. Existing generation
replacement gates continue to reject unproven owner `ShutdownCompleted` results.
An individual host still cannot start a second generation.

On failed termination, accepted counters without ended-upstream proof are snapshots,
not final accepted totals. No discarded packet, failed sender or abnormal child
cleanup is erased to obtain a normal result.

`LocalTerminated` is a new owner end reason and never maps to `PublisherStopped`.
The legacy forced cancellation and normal OBS EOS paths remain unchanged when no
local request was admitted. Application handling of this new normal-local API is
deferred; it is not silently mapped into their OBS EOS/restart success path.

## Recording writer evidence

The recording source factory has no recorder-owned `RecordingSessionId`. The owner
does not own `RecordingRuntime` or its `IRecordingSessionTerminalObserver`. A
publishing `ChannelSessionId`, channel match, EOF or lease disposal cannot establish
that SessionId correlation.

Consequently this slice reports `NotRequired` when no source lease/acquisition/failure
requires recording-writer evidence, otherwise `NotObserved`. No unmatched terminal
fact is attached and no writer success is inferred. Independent writer terminal facts
remain available through the existing recording runtime API. `LocalCompleted`
proves the **owner's source/resource termination**, not successful recording file
finalization, FLV validity or History persistence. Supplying an exact generation-bound
recorder admission/terminal correlation is a later composition boundary; changing
writer behavior/format/paths/settings is outside this slice.

## Validation scope

Managed loopback RTMP/PCP fixtures cover pending mandatory/optional consumers,
buffered delivery, timeout/cancellation, shared Finish/Dispose cleanup, duplicated
requests, retained delivery/cleanup failure, active/naturally released recording
leases, writer-unobserved results, borrowed HTTP ownership and distinct generations.
Existing normal OBS-stop, cancellation, Phase 5A/5B/5C-1/5C-2 and application lifecycle
tests remain regression coverage. No real OBS, physical GUI/streaming, WAN or NAT
operation is performed. Remote receipt/playback and OBS-requested EOS are not proven.

## Independent-integration corrections

### A. Resource cleanup and forced escalation

Resource cleanup has one shared task. Each owned release also has one cached task
under the owner gate. Force admission is a separate idempotent gate transition;
it creates separately owned cancellation and recording-fault-notification tasks
even when normal resource cleanup has already started. A successful final local
commit closes force admission. A clean already-completed natural EOS is not
retroactively forced by disposal.

Releases enter on independent tasks outside owner locks, so a synchronous or
asynchronous noncooperative cancellation callback cannot prevent the remaining
owners from being asked to release. Normal release begins only after all ordered
drain stages. Forced release is allowed to cancel incomplete work; each resource
retains its actual workers, buffers and cached disposal task. HTTP source/listener
may share the underlying source disposal task safely. Borrowed listeners are not
released. Recording notification never disposes an active recording reader.

Actual cancellation success and notification success are mandatory parts of
forced cleanup evidence. Pending or failed cancellation cannot certify cleanup.
Dispose joins the actual cancellation task before disposing the owner CTS, even
when another release fails. Cancellation failures are observed and retained as
failed boolean cleanup proof; no raw exception text enters public evidence.

### B. One monotonic deadline

TimeProvider's monotonic timestamp defines remaining budget and Expired; the
same clock creates the cancellation timer. Timer notification is not required
for elapsed-clock expiry. Scoped fanout idle has its own remaining-budget timer;
if it rounds down, no delivery retry occurs: only the original residual budget
is observed before classifying the overall deadline expired. Expired fallback
starts owned cleanup without waiting beyond the original budget. No new overall
deadline is created. A test clock can advance with its timer deliberately silent.

### C. Writer evidence

NotRequired additionally requires no rejected cleanup pending/failure, no
cleanup pending/failure, no accepted terminal fault, and proven source cleanup.
Any acquired recording lease still means NotObserved; this owner has no recorder
SessionId or writer-terminal correlation. Source EOF and delivery bytes remain
independent from writer finalization and file bytes.

## Admission, final publication and current-main preservation

Ingress/relay/mandatory-child completion is checked at request acceptance under
the owner gate. Thus EOS already observed before acceptance cannot be overwritten
through the gap before RunAsync's finally. A finish accepted first may later join
PublisherStopped as upstream proof, while retaining that ingress reason. A final
gated commit rechecks force, cancellation, monotonic expiry and owner cleanup.
Only after final owner validation is the local result embedded in the owner
result and published; both share the identical immutable local result. No
application restart permission or mapping to OBS normal EOS is added.

Current-main Status revision serialization and TCP observation are retained.
HTTP first-token/legacy behavior, original Dispose exception and late fault
observation are retained. The internal HTTP cleanup predicate does not replace
SourceCleanupFailed. A bounded internal post-source-cleanup observation enables
late-fault tests; it is absent in production. Final owner snapshots keep known
recording source failures and HTTP cleanup failure separate from earlier typed
stage snapshots; they never rewrite an already published result.

Two extra files beyond old #279's seven are required:
- ObsPublishLocalTerminationSafetyTests.cs: deterministic correction/ordering tests.
- PcpRelayFanoutRecordingStreamSourceLeaseFactory.cs: recognize actual registration
  Task fault/cancel immediately in fixed failure evidence, before a concurrent
  lease Dispose has recorded its private failure flag. The OR predicates count
  each failed lease once and preserve retirement, backpressure and ownership.

Internal observers/time boundaries are not public API. Resource fault injection
is after the actual release; it does not prove a physical OS cleanup failure.
The cancellation test holds a real owner-token callback while independent release
barriers execute. No fixed sleep is used to infer success or race order.

Physical: NOT_RUN. Merge Readiness: NOT_ASSESSED. No #280 application Finish,
#281 GUI Finish, writer finalization, file/media/History acceptance, real OBS,
remote playback, WAN/NAT or publishing-wide physical graceful termination is
claimed. Noncooperative processing and callbacks have no forced finite termination.

RecordingFaultNotificationCompleted means that the idempotent fault-request task
returned successfully; it does not claim that fault defeated an earlier EOF.
The actual notification winner/counts remain in RecordingFinalEvidence and the
typed RecordingSource stage. Normal EOF may remain the winner during forced
escalation after source completion, without converting a forced owner outcome
into LocalCompleted.
