# GUI Next publishing lifecycle Phase 2

## Dependency and ownership

Independent integration from main
`51c0de5e2576c1e776fecc1a1c7b53554675ad7a`, adapting only the seven-file
delta of old PR #272 (`e91182d0d47de27ffc68662eba506cbfcbc658d7`)
against its old base #271 (`f0ae19170c7d7cb36dbcea9b253549571c6a695d`).
Current #313 publishing hosts, #314 read-only status and #315 GUI composition
are retained. No stacked history or old polling/startup helpers are imported.
Old PRs and branches remain unchanged.

`NovaGuiNextPublishingComposition` now owns
`INovaObsPublishingLifecycleController`. The controller retains the same one
runtime and borrows the existing bounded status adapter. The GUI still receives
only `INovaObsPublishingReadOnlyStatus`. Normal startup, screen navigation,
status reads, F5 and Refresh never request Start.

The existing runtime forwards its host's Completion through the optional
`INovaObsPublishingLifecycleRuntime` extension. A runtime without that boundary
cannot start through the controller. Its disposal is still attempted, but an
unsupported runtime cannot certify cleanup without completion evidence. A missing
owner has no resources to release and retains the existing Unavailable behavior.

## Command semantics

- `RequestStart`: admits Start once. Accepted means the request reached the
  existing runtime, not listener readiness, OBS admission or accepted media.
  Duplicate concurrent requests return AlreadyStarted. Caller cancellation is
  checked before admission; it cannot later cancel the owned runtime.
- `RequestStopAsync`: requests cancellation cleanup using existing DisposeAsync.
  It is **not** a graceful stop or normal-EOS request. A request racing synchronous
  Start waits for Start to settle before invoking disposal. Duplicate requests
  await the same terminal result and share one cleanup task.
- Completion is observed after an accepted Start; source termination also
  triggers one cleanup. Start/property-access/completion/cleanup failures are
  classified without exposing exception text. Late results cannot repair a
  previously reported incomplete cleanup.
- Current production host disposal joins its Completion. If a runtime reports
  successful disposal while Completion remains pending, Stop fails closed with
  CompletionFailed and CleanupCompleted=false; its pending observation retires.
  Late completion cannot upgrade this result. No arbitrary publisher-arrival
  deadline is introduced. Synchronous Start and owned Dispose must still settle
  cooperatively: this layer cannot forcibly terminate a hung borrowed runtime
  or non-cooperative I/O and does not claim a universal finite Stop deadline.
- Even without Start, Completion is obtained only after owner Dispose completes
  and its actual ShutdownCompleted result is required. A false shutdown result,
  pending/faulted/cancelled/null task or result, or getter failure cannot certify
  cleanup. Dispose exceptions retain CleanupFailed. Duplicate Stop/Dispose share
  the same cleanup and terminal result; late completion cannot repair a failure.
- Application disposal awaits the same terminal cleanup and reports incomplete
  cleanup as a fixed exception to the existing startup result boundary. It does
  not falsely report successful shutdown. Failed cleanup may leave owned
  resources outstanding; there is no force-cleanup/retry mechanism here.
- Stop before Start disposes the unstarted generation. Start after closure or
  terminal completion is rejected. Owner regeneration/restart is deferred.

The snapshot retains the existing channel/session identity and safe projection,
adding typed lifecycle state, completion kind and failure classification.
NotStarted, Starting, Running, Stopping, Stopped and Failed are distinct. Running
means an accepted runtime lifetime; the existing source status still distinguishes
listener preparation, OBS waiting and accepted HEAD. Terminal controller facts
dominate a late Receiving status. Connection URLs, publish routes/keys and raw
exceptions remain excluded.

Before command admission, the existing adapter's Observed/Stopped/Unavailable
availability is preserved; the new controller does not overwrite those observed
facts with its own initial NotStarted state.

## Normal EOS evidence and remaining foundation

The current host recognizes normal OBS unpublish and then drains its fanout and
HTTP source before cleanup. A normal-EOS result is recognized only when all
existing result facts agree:

- host and ingest both report PublisherStopped;
- one HEAD and positive accepted packet/byte counts exist and match ingest counts;
- relay drain is Completed and HttpDrained is true;
- host ShutdownCompleted is true and application-owned disposal succeeds.

PublisherStopped alone, disconnect, timeout, cancellation, incomplete drain or
cleanup failure cannot satisfy normal EOS. The typed result distinguishes OBS
normal EOS, cancellation, disconnect, incomplete EOS and failure. These are
managed lifecycle facts, not physical audio/video or WAN evidence.

The foundation still has **no application-requested normal stop API**. A later
production boundary must close admission, arrange bounded draining and report
incomplete cleanup truthfully before normal-stop GUI wiring. This slice neither
implements nor claims that capability. No YP final-inactive behavior is composed.

## Validation scope

Deterministic fake-runtime tests cover explicit start, duplicate start/stop,
Start/Stop ordering, caller cancellation, synchronous and asynchronous failure,
cleanup, strict EOS evidence, terminal dominance, privacy, no implicit startup
and rejected restart. Existing managed foundation tests retain their bounded
loopback fixtures. Release build, solution tests, format and matching-head GHA
are required.

No GUI command buttons, native/physical run, real OBS, broadcast, WAN or NAT
operations are performed. Existing PRs and Phase 3C scoped closeout are unchanged.
