# Publishing LAN PCP Relay — Phase 1

Publishing may expose its existing PCP downstream server on one explicitly selected
LAN IPv4 interface. It never creates a second server. Default LAN OFF retains the
existing loopback/ephemeral PCP listener; OBS RTMP and HTTP-FLV remain loopback-only.

## Configuration and admission

The existing Publishing editor stores ON/OFF, NIC identity, IPv4 address and an
explicit port (1–65535). Schema 1 loads with LAN OFF; successful saves write Schema
2 and retain unknown top-level properties. Corrupt/future settings are preserved
and block Start until explicit validated Save. Draft/Saved/Applied and revision
capture remain unchanged. Saving during publishing affects the following explicit
Start, never the current listener. Older executables reject Schema 2; this is not
a silent downgrade to another public bind.

Start re-enumerates NICs. Exactly one address assignment must match the saved NIC
identity, be operationally Up, use an explicit RFC1918 IPv4, and have a contiguous
IPv4 mask consistent with its prefix (1–30). Network/broadcast addresses, Any,
loopback, public/link-local/multicast/IPv6, DNS names and missing/ambiguous assignments
are rejected. Bind errors clean partial resources without changing NIC or port.
Runtime interface migration/rebinding is not implemented; another Start revalidates.
An unsafe/failed terminal generation remains subject to existing restart gates.

The server applies the captured subnet predicate to the actual TCP remote IPv4
before any HTTP/PCP reply or metadata. Missing addresses, exceptions, non-private,
off-subnet, network and broadcast sources fail closed. Binding/filtering does not
authenticate peers or prove WAN unreachability through existing forwarding/proxies.

The application root loads one GUI application state before preparing Publishing.
Its shared `DownstreamRelays` registry supplies a Broadcast registration bound to
the producer's actual ChannelId, SessionId and fanout. The existing Host server
borrows this admission. No `PrepareBroadcastAsync` or NAT/public-relay wrapper is
invoked. Missing/disposed registry, invalid connection settings or unavailable
registration block LAN Start; LAN never uses the optional legacy fail-open seam.

Connection.Startup supplies global/per-broadcast count limits: null means no policy
limit, zero denies matching new relays. Local HTTP playback, recording and OBS
ingress do not reserve relay slots. LAN OFF retains its previous optional-admission
behavior; this slice does not retrofit every loopback/private relay route.
Connection-policy edits still require application restart; Publishing LAN edits
apply at the next Publishing Start. These are distinct lifetimes.

IPv4 bandwidth numerically configured (including zero) blocks LAN Start. The existing
bandwidth policy is not complete accounting/reservation/shaping; declared metadata
bitrate is not measured throughput. With no configured IPv4 budget, the GUI states
that bandwidth limiting is not applied. TCP, fanout, queue, handshake/write deadline,
slow-child and cancellation limits remain independently enforced.

## Shutdown and display

Termination retires Broadcast admission without freeing outstanding reservations.
Existing server acceptance closure, bounded drains, child/transport cleanup and
fanout teardown remain owned by the Host. Successful terminal cleanup additionally
requires zero outstanding Broadcast reservations; late cleanup cannot retroactively
certify a failed terminal result or permit a replacement generation. GUI surface
teardown borrows the application state; only the root disposes its registry/policies
after Publishing disposal. Viewing relay activation still stops with its window.

The Publishing tab displays LAN state, actual bound endpoint and a PCP URL only
while the listener is running. URLs use the existing parser's
`pcp://IPv4:port/channel/{ChannelId:N}` contract. Stopped/unready/failed states expose
no usable URL. Diagnostics use fixed safe reasons, never raw exceptions, keys or
internal paths. LAN fields and new messages follow the existing editor theme/font
and scrolling conventions. Windows layout/glyph behavior is not physically accepted.

## Validation boundary

Managed tests use fake NIC snapshots, existing managed loopback TCP fixtures and a
deliberately unassigned IPv4 bind failure. They cover validation/source gating,
shared Broadcast policy, retirement/reservation retention, migration and next-start
settings, unavailable contexts, bandwidth rejection and disclosure. Existing
metadata-before-HEAD/DATA, late-join, Finish/EOS/disposal and generation race tests
are reused. No real LAN peer, OBS, player, router or GUI is launched in this task.

IPv6, WAN publication, YP Announce, mapping/UPnP/NAT-PMP/firewall changes, automatic
port opening, a bandwidth engine and physical PeerCastStation interoperability are
out of scope. The Publishing server is given no NAT manager. The existing validation
profile is also denied LAN registration. Local HTTP-FLV history is separate evidence.

After separate explicit authorization, use an existing LAN peer (such as
PeerCastStation) for one connection and independently observe metadata/media and
normal cleanup. Keep physical LAN interoperability and HTTP-FLV playback acceptance
separate from CI and from each other. No new observer/probe is required by this design.
