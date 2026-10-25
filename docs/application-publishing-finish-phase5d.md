# Phase 5D: application publishing Finish

Independent base: main `7a161ce1ac914da4f799595d6313b49c51d479eb`, tree
`e3ee7ff3be794b5d0b08680b246aae3ae5f7bcc8`, after merged #323.
Provenance: only old #280's three-commit/ten-file delta, base
`b233d6fb3bc96379bd6e7c98217c7c7153cc9bc9` to HEAD
`6df332faabaf0c862f74b5022de4b5ade2b53b25`. No stacked history is imported.
The old Network deadline-assert relaxation is deliberately omitted.

`INovaObsPublishingLifecycleController.RequestFinishAsync(timeout, token)` is an
application-owned, one-generation command. The production runtime forwards to
`ObsPublishChannelHost.RequestLocalTerminationAsync`; it does not reimplement
ingress, relay, HTTP-FLV or recording drain/cleanup. The composition exposes an
application-only entry point over its current generation. No native GUI Finish
button or binding is added. Startup, status reads, navigation and F5 do not finish
or start a generation.

## Admission, deadline and ownership

An admitted Start (including synchronous Start still in progress) is required.
A Start merely queued by the existing GUI command but not yet admitted is rejected
without a Finish side effect; callers can request Finish after Start admission.
Unavailable capability, unstarted/closing generations, invalid timeouts and
pre-cancelled requests do not dispatch to the owner. Duplicates join the first
admitted task/token/deadline. A positive timeout of at most one minute begins at
application admission, including worker scheduling and Start completion waits.
Only the remaining budget is forwarded to the owner. Every additional wait is
bounded by that original deadline; no new drain timeout is introduced.

The application observes three distinct facts: the owner's typed local result,
the same owner's `Completion`, and the actual application-owned Dispose task.
ChannelId/SessionId must match the captured stable generation; the typed result
must also match the owner's attached completion evidence. A normal-local success
requires `LocalTerminated`, completed owner shutdown, matching accepted counts and
the owner-reported completed upstream/admission barriers. It is not inferred from
status text, a stopped snapshot or a Dispose return alone.

While Finish owns reconciliation, the ordinary Completion observer does not start
its former unbounded cleanup/terminal reconciliation. Stop/Dispose can still request
forced cancellation through the same cleanup task. Cleanup waits for an unsettled
synchronous Start before accessing the owner. A deadline/cancellation/failure can
start forced fallback, but the application only waits within the original deadline.
Pending cleanup remains owned, and late completion cannot upgrade the immutable
terminal result. Dispose reports incomplete cleanup instead of waiting indefinitely
again on a failed Finish result. Existing Stop-only behavior is preserved.
The outer command shutdown joins an admitted command or its lifecycle terminal,
so a blocked Start/interrupt cannot extend a bounded Finish again during composition
shutdown. The existing Stop worker retains deferred cleanup ownership.

## Outcomes and restart

`LocalCompleted` is separate from `ObsNormalEos`. Owner timeout, cancellation,
delivery failure, incomplete cleanup and other failure are retained as distinct
application kinds and bounded safe diagnostics. A matching cleanup failure cannot
be promoted to normal-local success. If an independently proven OBS EOS wins
before the owner admits the local request, its existing EOS proof/cause remains
separate; an unproven EOS still fails closed.

Generation replacement requires a terminal result, proven actual cleanup, settled
commands, matching identity and a fresh owner/SessionId. A Finish success can
permit explicit restart. A competing forced Stop can permit restart only after a
matching `HostCancelled` owner result and proven cleanup. A proven OBS EOS is also
preserved. Failed/timed-out/caller-cancelled Finish or unknown cleanup/identity does
not permit replacement, even if cleanup finishes later. Old lifecycle tasks and
status adapters remain bound to the retired owner; they never publish into the new
generation. No automatic restart or same-owner reuse is added.

## Writer evidence and exclusions

The owner's `NotObserved` / `NotRequired` writer evidence is preserved unchanged.
There is still no recorder SessionId binding/terminal-observer integration here.
Owner source EOF and cleanup do not prove writer finalization, finalized FLV,
History persistence, remote playback, or OBS-requested EOS.

Only application runtime/controller/composition/status and managed tests change.
Native GUI layout/buttons, Core/Network termination implementation, writer,
recording formats/paths/settings and existing cancellation behavior are unchanged.
Managed fixtures use fake boundaries and one loopback production owner with no
publisher. No real OBS, Windows physical streaming, WAN or NAT operation is run.

## Current-main corrections and recovery

A. Current-main CleanupOnceAsync remains a single task. Real owner disposal is
followed by actual Completion access and successful termination/ShutdownCompleted
proof. A pending/faulted/cancelled/null Completion, getter failure or unsupported
owner fails closed. The cached actual result remains owned in _cleanupResult.

B. Application admission starts one TimeProvider monotonic budget. Remaining and
Expired share that clock; each stage and final commit check elapsed expiry even
without timer notification. No stage receives a new overall deadline.

C. The lifecycle gate linearizes terminal publication against Stop/Dispose.
Caller cancellation, monotonic expiry, post-disposal identity, exact Local Result
reference and actual owner/cleanup result are checked. A matching value-copy is
not proof of the same immutable Local Result. Known final HTTP/recording failures
cannot establish LocalCompletedProven. The composition requires matching channel
and session identity and drained commands before explicit replacement.

D. Owned tasks isolate runtime Finish, cleanup/Dispose/Completion access and
borrowed status callbacks from synchronous blocking prefixes. Owner calls run
outside the lifecycle gate. Deferred Stop and Observation retain cleanup; late
task exceptions are observed. Blocking operations themselves have no forced
finite termination guarantee. Task scheduling is not cleanup evidence.

E. Cleanup failures are recorded independently from an already committed terminal.
Late failure/success never repairs or rewrites the terminal or its Read projection.

The first implementation focused run was 60 PASS / 1 FAIL. A generation change
observed after actual Dispose had left cleaned=true even though IdentityUnproven
was recorded. Recovery retains the real resource task/result but clears the
public generation-bound CleanupCompleted proof whenever identity/result matching
is unproven. Lifecycle disposal then fails closed and restart remains blocked.
The original failed assertion is retained. A recovery regression independently
checks actual cleanup true/false versus public proof false, including cleanup
failure priority. IdentityUnproven is not replaced by a generic CleanupFailed.

Additional managed loopback tests use the actual #323 Owner and the production
runtime adapter with an independent RTMP wire client. Barriers distinguish
PublisherStopped before Network Finish admission from Finish admitted first;
LocalCompleted never becomes OBS EOS. No real OBS publisher or physical run.

Supporting changes outside the old application files are a safety test file and
an App test-project link to the existing RTMP test fixture. Network engines,
Network deadline assertions, recording writers, Native GUI buttons/layout and
application startup composition are unchanged.

Physical: NOT_RUN. Merge Readiness: NOT_ASSESSED. Source EOF is not writer
finalization; file bytes/integrity/playback, History success, remote playback,
real OBS interoperability, WAN/NAT and GUI Finish remain outside acceptance.

## PR #324 independent-audit corrections

The first independent audit of c0afe71e was NOT_MERGE_READY. Its three findings
are addressed without changing #323's Network termination engine:

1. Composition Finish transfers to the already selected lifecycle under its gate
   without synchronously reading Runtime Snapshot. Duplicate Finish and shutdown
   admission do not wait for that external callback.
2. Successful Finish requires an opt-in owner generation-proof capability. The
   production adapter derives it from the actual #323 readonly channel/session IDs.
   A mutable provider must invalidate the sealed proof under Proof.Gate BEFORE
   changing identity, and must not call lifecycle operations while holding that
   gate. A provider unable to guarantee immutable identity or this ownership rule
   cannot supply the capability: Finish fails IdentityUnproven. Snapshot matching
   alone is not the capability. Commit takes lifecycle gate then proof gate;
   invalidation has no callbacks into lifecycle. Observed mismatch/Unavailable
   permanently invalidates the proof, so even a later matching stale snapshot
   cannot authorize success/restart. Terminal results themselves remain immutable.
3. Post-Dispose Completion is retained separately and receives fault observation
   even when it differs from the Start task or is rejected while still pending.
   It is never awaited without bound or promoted into successful cleanup.

The first new-focused run was 6 PASS / 1 FAIL: the blocked Snapshot was held to a
200ms deadline, but the fixture expected Cancelled instead of the final fence's
TimedOut. This history is retained, not called flaky. Recovery splits the ordering
with a manually controlled TimeProvider: deadline is fired before Dispose
admission in one case; the other observes actual Dispose entry/termination and
releases Snapshot while the clock remains before deadline. Both preserve shared
request/cleanup ownership and reject successful generation cleanup proof.
Composition's optional internal clock injection defaults to TimeProvider.System
and only forwards the same clock to each lifecycle; normal runtime behavior is
unchanged. There is no fixed sleep or acceptance of either outcome indiscriminately.

These implementation corrections require independent re-audit. Code Audit
ACCEPTED and Merge Readiness are not self-declared. Physical remains NOT_RUN.


## Concurrent status reads and admission

Composition and command admission use two phases: capture the owned generation
and internal admission state under their gate, read borrowed status without an
admission gate, then recheck generation identity, closed admission and command
tasks before publishing state or accepting Start/Interrupt. Session/channel
identity and seen-session updates remain synchronized and are applied only to
the captured current generation. Replacement publication uses the validated
candidate snapshot without reentering an external read under the composition
gate. A retired read produces unavailable status for the current generation or
disabled commands; it cannot publish retired identity into a replacement.

The status adapter still serializes projection. A noncooperative Snapshot can
therefore leave read workers blocked, but must not own composition/command
admission while it waits. CloseAdmission, Dispose admission and duplicate Finish
can proceed. This does not promise forced termination of a blocked reader or
successful cleanup when identity/owner completion is unproven. Existing Finish
identity fences, completion ownership and fail-closed restart checks remain.

`ConcurrentPublishingStatusAdmissionTests` adds 13 barrier-controlled cases:
actual adapter contention behind a Finish Snapshot (Read and ReadCommands),
command read versus close/dispose, Start/Interrupt reads at both boundaries,
retired reads versus replacement/shutdown, and prepared old commands versus a
new generation. A contention observer reports a failed adapter TryEnter; an
observation barrier pauses commands outside admission gates. Neither is active
in normal operation. A controlled TimeProvider establishes deadline expiry;
fixed sleeps do not establish ordering. Physical validation remains NOT_RUN.
