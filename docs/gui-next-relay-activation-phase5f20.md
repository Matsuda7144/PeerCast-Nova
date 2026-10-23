# GUI Next explicit local Relay activation — Phase 5F-20

## Scope and authority

Follow-up to #293 at `e08fb4756241ba9b0de531e5f9a06a4c3b64ecd9`.
Independent integration of source #294 at `5524b57824c938956edfda731a919dcaa1cd7d4a` (three commits) onto main `67f66fe1d67436d5848c3f20d54a99f8a5f7a621`, after #310; exact-main CI 39834767350 succeeded at preflight. Only the 16-file source delta is transplanted, preserving current main's font coverage, connection settings, Logs and prior lifecycle regressions. Source #291-#293 are closed without merge; source #294 and downstream #295 are preserved.
Managed integration only. This is **local IPv4 loopback/port-0 Viewing Relay**,
not WAN publication. No saved ListenPort, IPv6, NAT or bandwidth enforcement.
Broadcast producer handoff remains available to existing owners, but there is no
GUI Broadcast start command and no fabricated producer.

## Production path

Normal GUI Next startup creates one `GenesisGuiNextRelayActivation` beside the
existing `PublicChannels`, registry and startup Relay policy. The interactive
surface binds it to `GenesisMainViewModel` before constructing the native bridge.
Construction does not prepare an owner or open a socket.

Native `Relay開始` / `Relay停止` are separate from Play/Stop, Recording and
Publishing. Commands use `GenesisGuiNextRelayCommandDispatch`. Start accepts only
an interactive row's actual PCP source, parsed by the existing
`DailyPcpPlaybackSourceParser`. Optional ChannelId metadata must match that URI;
display text, YP names and free-form playback input never invent an identity.
Viewing classification comes from this explicit receive operation, not YP data.

The existing composition creates a fresh session, exact fan-out registration and
real `PcpRelayCoordinator`. Only its prepared owner's `RunAsync` activates it.
The activation controller authorizes only `127.0.0.1:0`, then reports the actual
runtime endpoint. It does not own or duplicate sockets or media consumers.

## Readiness and presentation

`Stopped → Preparing → Starting → Running → Stopping → Stopped/Failed`.

Three observations remain distinct:

1. `WaitForReadinessAsync`: coordinator listener bound and upstream task started.
2. `UpstreamNegotiation`: existing PCP session handshake accepted.
3. `FirstPacketAccepted`: real upstream PCP packet accepted into existing fan-out.

Only (3), with a current, uncanceled, unretired operation, allows Running.
No packet means a bounded receive readiness timeout, even after a good handshake.
Each handshake/first-packet wait is bounded at 30 seconds; no startup reconnect
attempts are added. Existing runtime connection/handshake/shutdown bounds remain.
Endpoint text is cleared after confirmed cleanup. GUI notifications post to the
native GUI thread; network continuations never draw native controls.

The existing dark-blue details pane reserves 76 additional logical pixels for
the two tab-stop owner-drawn buttons and a wrapping status/error label. Existing
Japanese font application, drawing-DC font selection, focus and DPI paths remain.
The existing glyph-coverage sample and its regression source list include the
new Relay labels and fixed Japanese errors; font selection itself is unchanged.
Native appearance, clipping, keyboard and DPI acceptance require a later physical
phase; managed layout/source checks do not prove pixels.

## Stop, ownership and failure

Stop retires admission synchronously, then cancels the operation and awaits its
long-lived run/disposal. The GUI command wait is bounded at 15 seconds. An
unfinished shutdown remains Failed, blocks another Start and retains the owner;
it is never described as a clean Stopped operation. Repeated Stop shares the same
task. Closing blocks Start immediately, retires admission, then asynchronously
stops before application registry/policy shutdown.

Cleanup requires successful runtime disposal, successful coordinator completion
with `ShutdownCompleted=true`, and zero reservations for the exact registered
owner/session. TCP and callback cleanup remain in the existing Network owners;
only those owners release real connection leases. The registry is not reset.
Bounded incomplete shutdown does not free slots or permit a new generation.
If the completed cleanup result is unconfirmed, the retained operation blocks retry even if a reservation is later released. A GUI wait timeout alone leaves cleanup running; only its later successful runtime/transport/reservation checks can establish clean completion. Pending cleanup never grants capacity or permits retry.

Preparing/starting Stop, concurrent commands, external owner replacement,
cancellation, late disposal and shutdown are guarded by operation identity and
fresh generations. External retirement closes the old GUI runtime without
disposing a newer owner. Retry after successful cleanup prepares a new session.

Japanese fixed messages distinguish missing/unresolved selection, already
starting/running, stopping/closing, bind, upstream connection, negotiation,
readiness timeout, cancellation, supersession and shutdown failure. Internal
exceptions/peer text are not displayed. Bind classification has an injected
managed failure test; no fixed/public port is occupied to manufacture that test.

## Policy and tests

Uses the same `Connection.Startup → NetworkPolicies.Relays → DownstreamRelays`
instances as #293. Null is unlimited, zero rejects new relays, positive limits
enforce both total and Viewing channel limits. Real PCP integration checks
concurrent admission, unchanged HTTP 503/RelayFull, disconnect/re-admission,
cross-channel shared total capacity and TCP cleanup. Saved changes leave the
active policy unchanged. Existing real playback/recording consumer exclusion,
late TCP cleanup and generation tests remain applicable and are rerun.

`GenesisGuiNextRelayActivationTests`, `GenesisGuiNextRelayRuntimeTests` and the
startup/window lifetime tests cover the command-to-production-coordinator path,
readiness, failures, cancellation, repeated commands, late preparation, cleanup
timeout and reservation protection. Only fake stores and synthetic loopback
ephemeral PCP peers are used; no external PCP/YP, physical GUI or media run.

The six existing non-loopback temporary-bind tests are excluded locally for this integration; the previous #308 exception is not inherited. Filtered local results are separate from the unchanged hosted GitHub Actions unfiltered result. Native pixels, keyboard, layout and DPI remain PHYSICAL ACCEPTANCE PENDING. A future separately authorized run needs Windows Sandbox isolation with ClipboardRedirection=Enable, isolated stores, a synthetic loopback PCP upstream, startup-no-listener observations, actual ephemeral endpoint/readiness/stop evidence and normal GUI exit. Existing Evidence and #306 Physical NOT_ACCEPTED remain unchanged.

## Next boundary

Perform separately authorized Windows physical acceptance of the new local Relay
controls with an isolated settings environment and synthetic local PCP upstream.
Do not interpret managed integration as physical, WAN, fixed-port, IPv6, NAT,
Broadcast activation or bandwidth acceptance.
