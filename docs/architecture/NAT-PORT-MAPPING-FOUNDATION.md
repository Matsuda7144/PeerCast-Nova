# NAT Port Mapping Foundation

Status: implemented in the isolated `feature/nat-port-mapping-foundation` lane.
Managed verification is separate from real-router and external-reachability acceptance.
This slice does not change socket release, playback, or the accepted relay protocol.

## Scope and composition

The only public listener type integrated here is `PcpDownstreamRelayServer`'s
IPv4 TCP listener. After a successful bind, the server registers its actual
port with a `NatPortMappingManager`. It releases that token after stopping the
listener, before draining child sessions. Child exits and recording readers
do not independently delete the listener's mapping. Loopback and IPv6 listeners
are excluded. A failed or unbound listener supplies no public demand.

`NatPortMappingRuntime` owns the production network monitor and 250 ms pump.
It injects its manager through `PcpRelayRuntimeComposition` and the coordinator
to the server. The daily relay controller also owns one runtime across successive
relay sessions. All users of a shared listener must use the same manager; the
manager combines their owner tokens and deletes only after final release.

The daily application reads `NOVA_AUTO_PORT_MAPPING=on|off`, default `off`.
Invalid values are rejected. Hosts can instead configure `NatPortMappingOptions`
and change `Manager.SetEnabled` while the runtime remains active. The subsequent
[GUI control and diagnostics slice](NAT-PORT-MAPPING-GUI.md) adds persisted daily
settings and read-only status projection without changing operation authority.

## Authority and identity

The desired state consists of the setting, bound listener owner tokens and
current network identity. Every mutation advances a generation. A serialized
reconciler is the only authority that adopts observations or selects an operation.
Generation equality is evidence, not permission to overwrite current state.

Mapping identity includes network epoch, gateway IPv4 address, interface ID,
internal IPv4 address, protocol and internal port. Device serialization uses
gateway address plus interface ID, excluding epoch, client address and port.
Production runtimes share those device lanes. A request queued behind another
operation rechecks current demand and route after acquiring its lane.

The first route selector requires one active private IPv4 address with one
private default gateway. Ambiguous candidates remain unavailable. Network
address/availability events invalidate the epoch immediately; changed gateway,
interface or address also changes identity. Network epoch and the router's
reported epoch counter are separate observations.

## Backend and lease evidence

The first backend is NAT-PMP, implemented without additional packages. Its TCP
request/response, deletion, returned port, lifetime and counter semantics follow
[RFC 6886](https://www.rfc-editor.org/rfc/rfc6886.html). Each operation binds a
fresh UDP socket to the selected internal address and connects it to that
gateway's port 5351. Reply opcode and internal port must match. Deletion always
targets a nonzero internal port, with zero external port and lifetime; it never
deletes all mappings. Renewal suggests the router's last returned external port.

Defaults are a 120-second requested finite lease, three-second operation wait,
five-second retry interval and two-second shutdown cleanup budget. The backend
uses exponential response intervals beginning at 250 ms, capped at nine attempts
for Map/Renew and three for Unmap. The management budget can interrupt retries;
an interrupted request becomes unknown rather than proof of an unsupported router.

Lease evidence separates requested lifetime, confirmed lifetime/confirmation
time, and estimated expiry. Unknown outcomes have no confirmed lifetime.
Expiry estimates use the dispatch time conservatively and never prove deletion.
Scheduling uses a monotonic timeline, so a wall-clock adjustment cannot postpone
renewal. A confirmed shortened lifetime advances renewal to its halfway point.
Router epoch loss invalidates other active mappings using the RFC's conservative
counter comparison and a production recovery delay uniformly between zero and
five seconds; fake-router tests inject a fixed delay.

`Mapped` records matching router evidence reconciled with current demand. It
does not establish incoming Internet connectivity. `ExternalReachability` remains
`Unknown` throughout this slice. Actual returned external ports are result data,
not an assumption that the requested port was assigned.

## Late and unknown results

Cancellation and a wait timeout never discard a submitted operation. Its backend
task and device lane remain alive until all sends/retries actually end. A late
result is consumed by the same reconciler and recorded with original operation ID,
generation and identity, alongside current generation, route and owner count.

A stale Map success for the same current endpoint may satisfy replacement owners
after reevaluation; it is not reflexively deleted. A completed stale Unmap with
current demand triggers recreation. A late Renew after OFF triggers cleanup when
the route still matches. A result from an old route is retained as old evidence
and cannot become the current mapping. No old-epoch delete is sent on a new route,
even if NAT-PMP would consider their internal mapping keys identical.

NAT-PMP has no transaction token or read/query operation. Even after the backend
has ceased sending, a datagram may be applied late by a router. A mapping ACK
cannot prove that such a delete will never arrive. Periodic renewal repairs that
case; external reachability stays unknown. The fake router tests both a pending
late delete and an unobserved router-side delete after a lost response.

Shutdown first removes all desired demand, then reconciles within its own budget.
An unconfirmed cleanup returns `CleanupUnconfirmed`; it is not successful deletion.
After the deadline no new operation is dispatched, but pending results remain
observable. A late creation then remains `UnmapUnconfirmed`, with finite lease
evidence retained. Retired routes are also retained as unconfirmed; old cleanup
is not attempted through a possibly reassigned route. Mappings remain tracked for
the runtime lifetime, and structured diagnostic history retains the latest 256 events.

## Managed validation

`NatPortMappingManagerTests` uses a virtual monotonic clock and a fake router whose
physical effects and task completions are controlled separately. Explicit observation
barriers establish ordering; real-time watchdogs only fail a hung test. Coverage includes:

- OFF then ON with a stale Map success and replacement owner.
- Unmap wait timeout, then late router-side deletion before a replacement Map.
- Map timeout after router-side success, followed by confirmed retry.
- Replies from old epoch, gateway, interface and internal address.
- Shared listener partial stop and final owner release.
- Returned external-port remapping and renewal with the returned port.
- Shortened confirmed lifetime and earlier renewal.
- Renew completion during OFF.
- Shutdown cleanup timeout and retained late delete.
- Device serialization across timeout and network epochs.
- Late Map after the shutdown deadline, with no post-deadline send.
- Wall-clock reversal and router epoch recovery.
- Lost delete response plus an unobserved late delete repaired by renewal.
- 100 cheap Map/Unmap cycles.

Packet codec tests check byte order, TCP opcode, explicit router refusal,
response identity, returned port/lifetime/epoch, and exact deletion confirmation.
Listener integration tests bind an actual local TCP socket with a fake mapping
backend, verifying bound-port registration, owner release and loopback exclusion.
Application tests cover the ON/OFF setting. These tests send no NAT-PMP datagrams
to a real router and do not establish physical acceptance.

## Remaining work

GUI settings and diagnostics are implemented in the subsequent slice. A recommended
next step is an explicitly authorized real NAT-PMP router run: actual assigned port,
shortened lease/renewal, OFF/final listener stop, and deletion or bounded unconfirmed
cleanup. External reachability needs independent observation. UPnP should be a later
backend against this same manager. Multi-adapter route selection, IPv6, external
address discovery, process-crash persistence and richer historical diagnostics are
outside this foundation.
