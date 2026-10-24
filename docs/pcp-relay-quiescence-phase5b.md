# PCP downstream relay quiescence — Phase 5B

Independent integration base: main `a38721145a688e619a2f356f53fba62e368ef6c7`,
after PR #319. Provenance: only PR #276's ten-file delta,
`840e61a8e68201c6136863f5845a4950d7f2bc4f` →
`acc53d824188b49f473db7df4fd2e129b9e90053`; no stacked history is imported.

## Admission and ownership

`PcpDownstreamRelayServer.StopAcceptingAsync()` closes its TCP listener and its
child-registration gate. The same gate serializes registration with closure:
an already accepted connection cannot register a child after closure. A valid
late relay HTTP request receives a 503 response and is closed. Duplicate closure
requests share the accept-loop completion barrier.

Existing child lifetime/send tokens are not cancelled by admission closure.
Existing sessions can continue receiving upstream packets until the caller ends
upstream consumption. The server owns its listener/connections; the upstream and
fan-out remain borrowed. Other owners' fan-out children and admission remain open.
Completed child registrations are pruned while retaining fixed failure evidence;
the fence does not accumulate an unbounded per-connection history.

### Existing policy admission remains authoritative

`IPcpDownstreamRelayAdmission` remains the Channel/Owner policy reservation
boundary. `PcpRelayChildAdmission` is a separate server registration fence;
it does not replace, retire or dispose the policy or a borrowed fan-out.
After request/ChannelId validation, a closed fence rejects before reservation.
Otherwise policy reservation precedes registration under the fence lock. Close
winning between those operations rejects registration but keeps the lease until
the connection and worker cleanup are proven. Policy/full responses retain the
existing PCP handshake; closed-server responses use ordinary HTTP 503.

Policy locks (Owner → Registry → Policy) are separate from fence → fan-out →
worker locks. No external TryReserve or reservation Dispose executes under the
fence. Session execution is dispatched after publishing its server entry, so
policy callbacks do not execute under the server lock. Cancellation callbacks
and listener shutdown run outside that lock as well. Routed sessions retain
their existing borrowed transport/policy contract and have no server fence.

### Reservation cleanup proof

The existing transport `CleanupCompletion`, registration `Completion`,
`ReleaseAdmissionAfterCleanupAsync` and `AdmissionCleanupCompletion` remain the
proof chain. A transport-cleanup fault retains the reservation; a returned but
faulted worker is terminal. A throwing reservation release is a cleanup failure.
The server tracks each session's admission-cleanup task before removing its
connection entry. Successful tasks are pruned, failures become fixed counters,
and unsettled tasks remain tracked. Late faults are observed even after a drain
timeout. No connection history is retained for already successful cleanup.

Zero active connections and zero pending packets are neither reservation-release
proof nor a complete cleanup barrier. Drain waits for admission cleanup within
the original overall deadline and never disposes a reservation to force success.
Snapshot admission-cleanup pending/failure counters expose that distinction.

## Drain precondition and guarantees

The caller must **prove upstream packet consumption has ended** before invoking
`DrainAfterUpstreamEndAsync(timeout, cancellationToken)`. This foundation cannot
establish that borrowed upstream boundary on its own. Calling it while upstream
continues violates the existing fan-out idle precondition.

The method closes admission, reuses the fan-out idle loop over this server's
fixed child scope, and only cancels owned session lifetimes for cleanup after
pending and in-flight sends settle. Pending counts include an active send; a
write completion is local transport evidence, not remote receipt or playback.
Delivered/discarded packet and payload-byte counters keep their existing meaning.

One positive deadline of at most one minute covers admission closure, idle waiting
and cleanup. The first non-pre-cancelled request owns the deadline/token; duplicate
requests share its task/result, with no implicit retry. A pre-cancelled request
does not close admission or consume that task.

## Results and failure boundaries

Results distinguish `Drained`, `TimedOut`, `Cancelled`, `DeliveryFailed`,
`CleanupFailed` and `Failed`. `Drained` requires settled local sends without
observed discard/send failure, a completed run, zero active connections and no
observed incomplete cleanup/internal failure. A failed accept loop is not drain
success. Server completion after successful local quiescence is `Quiesced`.

Timeout/cancellation during drain waiting never cancels a pending send. Admission
stays closed and sessions can remain alive; the existing explicit `StopAsync`,
`RequestStop`, caller lifetime cancellation and `DisposeAsync` retain their forced
cancellation semantics. A competing forced stop never becomes successful drain.
Repeated disposal shares its actual cleanup task.

`SendsDrained` and `CleanupCompleted` describe separate proofs. A timed-out or
cancelled result's snapshot is an intermediate observation, not frozen final
counters; eventual `RunAsync` completion is the server's terminal barrier.
The bounded result cannot promise termination of an uncooperative sender. An
incomplete cleanup counter is preserved as failure, not success.

Admission closure includes a settled accept-loop barrier. An accept/listener
failure settles that barrier as failed instead of leaving it pending, and a
fixed admission-close failure counter preserves the evidence. Closing the real
listener also releases its existing public-mapping demand; no new mapping or
WAN operation is introduced. Concurrent Dispose observes the shared closure
task before disposing its cancellation source.

After session and reservation cleanup, drain rechecks both its fixed child
registrations and the aggregate pruned failure evidence. This includes late
FanoutStopped/SinkFailed/SlowConsumer, discard/write failure, cleanup/internal
failure and competing forced stop. Idle-time evidence alone cannot yield Drained.
For a completed evaluation, known cleanup failure has precedence, then forced
cancellation, accept/closure failure, delivery failure, and finally Drained.
Before those barriers settle, caller cancellation or deadline expiry returns
Cancelled/TimedOut with incomplete cleanup and an intermediate snapshot; known
cleanup failure is retained. These wait outcomes do not imply discarded sends.

Public results add only enum/boolean/numeric fields. They contain no endpoints,
URL, stream key, media payload or raw exception text.

## Scope and validation

Phase 5A RTMP ingress quiescence is unchanged. HTTP-FLV, recording, application
finish and GUI termination paths are unchanged and are not wired to this API.
No OBS normal EOS, publishing-wide graceful stop or remote playback is guaranteed.
No real OBS connection, physical publishing, WAN or NAT operation is performed.

Managed tests cover existing sends across admission closure, late HTTP requests,
connection/registration races, unrelated borrowed fan-out children, blocked sends,
deadline/cancellation, historical send failure, forced-stop/disposal races,
duplicate requests and incomplete cleanup evidence. Existing PCP and RTMP tests
remain regression coverage.

The old PR's historical 16 cases / focused 57 PASS / sequential full 6,504 PASS
and 15 SKIP are provenance only. Its concurrent full run recorded 6,503 PASS,
15 SKIP and one unchanged CLI connection-failure test failure. Released-port
reuse was suspected, not established. Those old results are not validation of
this independent integration. New results are recorded separately in the PR.

Physical: NOT_RUN. Merge Readiness: NOT_ASSESSED. No physical OBS, GUI, WAN/NAT
or remote playback acceptance is claimed. Noncooperative writers can outlive
the deadline; the API bounds waiting, not their forced finite termination.
