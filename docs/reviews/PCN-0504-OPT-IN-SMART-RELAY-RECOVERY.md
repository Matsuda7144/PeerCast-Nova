# PCN-0504: Opt In Smart Relay Recovery

## Scope

Phase C based on `2d6d45edb563a56c25241157dc02e313f76c1d9a`.
[ADR-0494](../adr/ADR-0494-opt-in-smart-relay-recovery.md) records the opt-in
policy, existing-session integration, retry budgets and media/Owner boundaries.

## Managed checks

- Real Coordinator EOF and QUIT 1003 recovery using the same Owner SID, fanout
  and listener port; resume request uses accepted DATA end, old child receives
  QUIT 1003, old HEAD disappears and a replacement child receives the new HEAD.
- Real 503/OLEH/HOST/QUIT chain, alternate target success and eight-attempt
  budget across new HOST introductions. Permanent identity/normal QUIT stops.
- Fake clock: five-minute limit versus persistent mode, one low-frequency
  attempt, 60/120/300 seconds, +/-20% jitter, 30-second wave deadline, Stop and
  retired-directory cancellation without late retries. Eight Owners share four
  connect/negotiation slots and waiting cancellation owns no transport.
- Accepted DATA-end cursor, 32-bit wrap, identical versus conflicting duplicate,
  gap/backward rejection, changed HEAD/new generation, first HEAD after raw DATA, fresh-HEAD requirement
  and thirty seconds of advancing DATA before stable-reception reset.
- Explicit continuous-media stall profile sees Degraded/recovery even with
  intervening control traffic; its monitor cancels at Stop.
- Actual cleanup callback held outstanding: Owner faults once at the cleanup
  deadline, reports unconfirmed cleanup and never retries. Partial downstream
  media write closes with only its prefix, with no QUIT bytes in the payload.
- GUI explicit mode/default OFF, actual GUI Relay Owner retained after parent
  loss, Stop releases reservations, and retired Owner shutdown cannot remove
  its replacement. Normal activation and public-channel tests remain included.

## Local results

- Release solution build: zero warnings/errors (.NET SDK 10.0.301, Windows).
- New recovery network tests: 33 passed. Related existing session/coordinator/
  fanout/directory/QUIT set: 174 passed before the final partial-write case.
- GUI activation/public Owner tests: 56 passed.
- Final full Release solution tests: 8,229 passed, 15 existing platform/native
  acceptance skips, zero failures (8,244 total). Network 1,183; App 3,039;
  Playback 3,104 passed/15 skipped; Core 459; Protocol 66; CLI 374; Foundation 4.
- Solution-wide Format verification passed. After the final status-wording
  correction and final HEAD-boundary case, changed-file Format checks passed.
- Exact-head GitHub Actions evidence is recorded in the Draft PR/final report.

The first full run exposed unsupported characters in the new GUI status labels;
the labels now use existing covered characters, and the final font coverage
checks pass. That run also observed the unchanged 50 ms quiescence race test
`Quiescence_forced_stop_during_reservation_wait_never_succeeds` (TimedOut versus
Cancelled). The final full run passed it without modifying that test or its
quiescence implementation. Its intermittent stability is not claimed fixed.

## Acceptance limits

New network tests use managed loopback/ephemeral fixtures. Candidate global
addresses are synthetic and mapped by the existing fake connector; no external
candidate probing is performed. Existing PCP/QUIT/referral, OBS/YP and full
Playback/Recording regression tests are run, not physical native acceptance.

No actual PeerCastStation binary, GUI desktop, OBS feed, router/public listener,
human video/audio or recording playback was operated. Platform/native tests
retain their explicit skip gates. Pre-existing intermittent failures are not
claimed fixed. Recovery deliberately disconnects old downstreams; seamless media
and WAN completion are not claimed. Native GUI mode selection/persistence and
automatic content-profile selection remain future UI work; the explicit process
setting and public options API are the initial activation surface.
