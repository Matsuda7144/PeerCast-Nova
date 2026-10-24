# GUI Next publishing lifecycle — Phase 4

## Dependency

This independent slice starts at main
`dd4438c5034c93f0b26115c5ac90f0d1b57dd2ad`, after #313–#317. It adapts only
the seven-file generation change from old #274
(`3b0355b154af339cc8bb13e183536ee478c420bf`, base
`6c37b98d82988b5f5f543454544047ed7634e229`). No stacked history is imported.
The current #316 lifecycle controller remains unchanged, including fail-closed
completion handling. Old PRs and their branches remain unchanged.

## Application ownership and restart

`NovaGuiNextPublishingComposition` provides stable borrowed status/command
interfaces to the existing GUI binding. It owns the runtime factory and active
generation, which contains its own lifecycle controller, read-only adapter and
asynchronous commands. GUI code does not receive a runtime or socket.

The Start button becomes available after a completed, successfully cleaned
terminal generation and drained commands. No new owner is created by reads,
navigation, F5, or interruption itself. Only another explicit Start request:

1. Closes and drains the retired command boundary and confirms its disposal.
2. Creates a new owner outside the GUI thread and composition lock.
3. Verifies a previously unused owner, valid not-started snapshot, and a new
   SessionId. The existing factory supplies identity; the GUI does not generate it.
   Source-idle facts are derived from the existing validated snapshot; a fresh
   lifecycle's NotStarted state alone is insufficient.
4. Atomically publishes the new generation and admits Start unless shutdown won.

Successful cancellation cleanup or foundation-proven normal EOS is eligible.
Cleanup failure, unknown completion, lifecycle failure, reused identity, invalid
candidate or factory failure keeps restart unavailable with a fixed safe reason.
Failure during replacement does not trigger automatic retry. Start failure in
the new generation remains associated with its new SessionId and cannot cause
another implicit replacement.

The status includes the application generation number. Each retired lifecycle
keeps its own completion and projection; callbacks cannot select a new active
generation. Reads and delayed GUI continuations always resolve through the stable
application boundary. Retired SessionIds are remembered as identity-only metadata;
owner reuse tracking uses weak references, not an accumulating runtime history.

## Ordering and shutdown

- One replacement task is admitted at a time; duplicate clicks are ignored.
- Pending old Start/Interrupt commands and terminal cleanup disable restart.
- Interrupt during admitted new Start uses existing ordered cancellation cleanup.
- Interrupt during factory preparation is unavailable/no-op: the retired command
  boundary is closed, and a new Start has not yet been admitted.
- Shutdown closes admission immediately, waits for replacement, then disposes the
  active generation. If shutdown wins during factory creation, the candidate is
  cleaned without Start or publication.
- Application-only synchronous, idempotent `CloseAdmission()` closes both outer
  replacement admission and current commands under the composition gate. The GUI
  borrowed interface is unchanged. Startup calls it at the existing early shutdown
  point before Network/Playback cleanup; publishing disposal is joined after host
  teardown. Closing itself performs no Start, factory call, disposal or wait.
- Factory preparation is admitted under that same gate, but factory execution and
  candidate snapshot reads occur outside it. Close before preparation admission
  prevents creation; Close after admission joins creation and cleans its result.
  Candidate publication and Start admission share one final gate with Close.
- A rejected candidate whose cleanup fails stays owned for truthful shutdown
  failure reporting. Already-owned runtimes are never disposed a second time.
- Late old snapshots/results remain tied to retired objects; they cannot overwrite
  active identity, state, error or button availability.
- Dispose returning with pending/faulted/cancelled/null/missing completion or
  `ShutdownCompleted=false` permanently prevents restart. A late successful result
  cannot repair a terminal failure. Only genuinely ongoing disposal may be awaited
  for its eventual proven result; no timeout extension substitutes for proof.

## UI and validation scope

Existing dark-themed buttons/status remain. 配信中断 continues to mean cancellation
cleanup; it is not a graceful-stop/normal-EOS API. Start acceptance, RTMP listening,
OBS connection and media reception retain their distinct existing projections.
No URL, stream key or exception body is added.

Managed tests cover three GUI-bound cycles, fresh and reused identities, terminal
and cleanup gates, duplicate restart, old-generation isolation, factory/candidate
failure, cancellation during new Start, and application shutdown races. Core and
Network are unchanged. No OBS app launch/connection, physical broadcasting,
WAN/NAT operation or Windows physical GUI acceptance is performed. Native GUI
acceptance and a production graceful-stop API remain separate tasks.

Deterministic barriers additionally cover Close before factory admission, during
factory execution, at factory return and during the last candidate read before
publication; new Start/Interrupt ordering; pending/faulted/invalid candidate
cleanup; unsupported runtimes; invalid identities; late completion; and disposed
tabs. Fixed sleeps are not used as race evidence. Current command, native bridge,
font, status, lifecycle and HTTP-FLV tests are retained rather than replaced by
older stacked versions.

Non-cooperative factory/Start/Dispose/I/O can still delay shutdown: this slice
does not forcibly abort code or claim bounded cleanup without evidence. Session
identity history grows by one GUID per accepted generation for the application
lifetime so that old SessionIds cannot be reused. It contains no connection
settings and is not persisted. Physical validation remains NOT_RUN; merge
readiness is NOT_ASSESSED until a separate audit.
