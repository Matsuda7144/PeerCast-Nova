# RTMP ingress quiescence — Phase 5A

Independent base: main `4146ce863adbbec040302a824b88929d08893bd5`
(tree `d252bfd203f521fb96d35a33896ee7df889173c4`), after merged #318.
Provenance: only old #275's six-file delta, HEAD
`840e61a8e68201c6136863f5845a4950d7f2bc4f`, base
`3b0355b154af339cc8bb13e183536ee478c420bf`. No stacked history is imported.

## Boundary and guarantees

`ObsRtmpIngestHost.RequestQuiescenceAsync(timeout, cancellationToken)` closes
publisher and message admission, closes the TCP listener, interrupts blocked
accept/handshake/message reads, and waits for the ingress `Completion` barrier.
It does not start a host. The timeout must be positive and at most one minute.

Message admission occurs **after a complete RTMP message is decoded and before
its processing starts**, under the same gate used to close admission. A complete
message that wins the gate proceeds with the existing forced-stop lifetime token.
The local close token never reaches its consumer callbacks. All slices of that
admitted FLV message can therefore finish in order, once, unless a consumer fails
or the existing forced-cancellation path is used. A message that loses the gate
is not processed, including complete messages buffered on the socket. An
incomplete wire message is not admitted and is discarded when reads stop.

Existing bootstrap/configuration retention and validation remain unchanged;
quiescence does not synthesize HEAD, flush incomplete bootstrap, or guarantee
that an encoded stream was started. Counters retain their existing meaning:
only successfully completed packet-consumer calls are counted.

## Bounded waiting and terminal facts

The result distinguishes `Completed` (local `Quiesced` terminal), `AlreadyEnded`
(actual publisher-command EOS), `TimedOut`, `Cancelled`, and `Failed`.
`FinalResult` is provided only after the ingress has terminated; its packet/byte
counters cannot change. Failure or forced cancellation can retain partial
accepted counters and never become successful quiescence.

A wait deadline/cancellation does **not** force-cancel the admitted consumer or
pretend cleanup has finished. It returns with no final counters if processing
is still pending. Admission stays closed; the shared `Completion` task remains
the final barrier for eventual counters. An uncooperative consumer can remain
pending beyond the wait deadline. Existing `DisposeAsync`/Start lifetime
cancellation still provide forced cancellation, and consumers must honor that
token for cleanup to finish. A pre-cancelled request does not close admission.
Concurrent requests share the same closed ingress and Completion; each caller
has its own bounded wait. Repeated requests/disposal cannot reopen the generation.

No URLs, stream keys, exception text, or media bytes are added to public results.

## Scope

This is **not** OBS normal EOS or a publishing-wide graceful stop. It does not
drain PCP relay/HTTP-FLV, finalize recording files, or introduce application
`RequestFinishAsync` or a GUI button. Those paths are unchanged and this API is
not wired into their ownership. No real OBS or physical publishing is used.

Managed regression tests cover paused multi-slice consumer processing, listener
admission, blocked handshake/read, incomplete wire messages, admission races,
bounded waits, consumer failure, forced cancellation, exact accepted counts,
duplicate requests/disposal and preserved publisher EOS.

## Current-main integration and deterministic regressions

The existing constructor No-Bind and unbounded first-publisher wait remain:
`IoTimeout` applies to connected protocol I/O, not publisher arrival.
The #316/#317/#318 lifecycle, GUI and generation ownership are unchanged.

The old 13 cases are adapted, not reused as current PASS evidence. Additional
barriers stop complete messages immediately before/after admission and pause
actual cancelled reads while forced-stop cancellation propagates. An internal,
optional observer and borrowed stream wrapper expose those boundaries only to
managed tests; no public test control or production observer is installed.
Accept waiting is observed from an incomplete accept operation. Handshake/read
tests observe an actual pending transport read; partial-wire tests also require
the partial bytes to have been read before quiescence.

New regressions cover simultaneous quiescence/disposal, caller-independent waits,
partial-slice failure/cancellation counters and positions, incomplete bootstrap,
timeout boundaries, constructor/publisher waiting, extra-client rejection and
internal failure. Deadline tests are not presented as proof of race ordering.

A deterministic decoded-before-admission test exposed an old classification
gap: closing admission and forcing cancellation could return Quiesced. The
rejected-admission boundary now checks the existing forced-stop token before
returning Quiesced; both Start-lifetime and Dispose cancellation remain Cancelled.

Physical: NOT_RUN. Merge Readiness: NOT_ASSESSED. Managed loopback evidence is
not real OBS publishing, Windows native GUI, media playback or WAN/NAT acceptance.

## Integration validation

- Quiescence: 32 PASS (13 adapted source cases + 19 additional cases).
- Focused solution selection: 409 PASS / 0 SKIP / 0 FAIL, including Generation
  48, Lifecycle 39 and GUI Commands 18. These are subsets, not additional totals.
- One full local Managed run: 7,121 PASS / 15 SKIP / 0 FAIL.
  Six existing nonloopback Bind cases are excluded, counted as neither PASS nor
  SKIP. Hosted full evidence is separate.
- Release solution Build: Warning 0 / Error 0. Format verification, diff check
  and conflict-marker scan pass. Focused testing performed the solution restore.
- Development evidence: initial new-test compilation errors were corrected.
  One handshake/disposal assertion lacked a cancellation-propagation barrier;
  that test now waits for it. Two deterministic tests then reproduced the old
  forced-stop classification gap before the boundary fix; both pass afterward.
  No full-test or hosted-CI retry was used to obtain these results.
