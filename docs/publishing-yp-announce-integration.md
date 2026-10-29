# Publishing YP announce integration

Base main: `e5960fd242a3cebd529ff50e3182f4b2fdb7792a`.

## Explicit publication contract

The application root owns one broadcast identity across Publishing generations.
Construction, loading/saving settings and YP list refresh perform no announce
communication. YP defaults OFF. The runtime factory lends the owner to the
existing Publishing host. Only explicit Start with YP ON, WAN mode, an actual
ready listener, registered shared admission and an explicit public advertised
IPv4/port can start the announce session. Loopback and LAN never start it.
There is no second listener, media admission registry, automatic tracker choice,
external address lookup, NAT, firewall operation, redirect or reconnect.

Schema 4 adds YP intent, display name and a distinct `pcp://` announce endpoint.
Schema 1/2/3 always migrate YP to OFF while preserving their existing publication
mode. Unknown/corrupt/missing settings retain the fail-closed save contract.
Draft/Saved/Applied/revision and next-Start application remain unchanged.

## Connection and evidence

The production connector is separate from the IPv4 localhost fixture connector.
An explicit hostname is resolved once, with a ten-second cancellation/deadline.
Every IPv4 answer must pass the existing public-address policy; mixed private,
loopback or special-purpose answers fail closed. IPv6 answers are not used.
One validated numeric endpoint is pinned for this connection. Later DNS or
tracker data cannot change the destination. Numeric endpoints skip DNS entirely.
Incomplete DNS work is retained and cannot become cleanup success.

The existing PCP v1 preamble, HELO/OLEH codec and worker are reused. Production
uses HELO `ping` with the advertised external port, never the internal bind port.
The codec also supports exclusive `port` when a caller already has an appropriate
open contract; this initial integration never reuses connection evidence across
connections and thus always requests a fresh ping. SID is the existing relay SID;
BCID belongs to the application owner; the monotonic local generation is neither.

Reference: PeerCastStation commit
`390c4e847289c90a77dbe40cc04b7bc1cfbddf17`,
`PeerCastStation/PeerCastStation.PCP/PCPYellowPageClient.cs` (handshake and OLEH),
and `PCPOutputStream.cs` (callback SID validation). No reference source is copied.

The initial OLEH must have a nonempty tracker SID distinct from local identities,
the exact advertised external port, and a remote IPv4 equal to the explicit
advertised address. Missing/zero/mismatched ports, contradictory addresses,
disable, QUIT and invalid responses fail before BCST. Subsequent OLEH must retain
the tracker SID and endpoint report. Capability ties the report to the local
generation, channel, relay SID, BCID, listener/bind endpoint, advertised endpoint,
pinned tracker endpoint, unique connection and observation time. Each update
rechecks the same live capability; another capability cannot reuse its report.

This permits **TrackerReportedOpen**, never independently verified reachability.
PCP v1 has no cryptographic identity or nonce. Inbound ping observation uses the
existing generation's successful ping-response counter; it is uncorrelated and
does not prove which tracker initiated it. It is not an announce authorization
input. AnnounceSent is not listing confirmation; StopNoticeSent is not removal
confirmation. All independent verification/listing/removal flags remain false.

## Runtime HOST information

Every initial/state/periodic BCST reads the current generation's runtime snapshot
immediately before its serialized write. Initial BCST is allowed before HEAD, but
Receiving remains false until a publisher is connected, HEAD has been accepted
and the channel is Receiving. The listener/capability/generation must still be live.
This follows the revised acceptance contract; no initial-BCST holding mechanism
is introduced. Receiving=false does not itself prove YP removal.

Relay availability additionally requires proven shared admission room (both
global and channel limits), TCP room and fanout room. Reading capacity does not
reserve/release anything. Unknown or retired capacity is unavailable. Capacity
is advisory and may change after observation; the real connection is admitted
again by the unchanged policy. No new observer, event subscription or timer is
added. Capacity/count changes are reflected by the existing 30-second tick.
HEAD notification uses the existing worker update queue; queued changes coalesce.

numr counts only current established relay children of this server. Pending TCP/
HELO, Ping, cumulative connection totals, internal HTTP children and cleanup-only
reservations are excluded. External Direct availability/count remain false/zero.
Uptime uses the host generation's monotonic start timestamp, freezes on terminal
state/accepted Finish, resets for a new host and saturates at uint.MaxValue.
Identity/revision checks reject stale snapshots. Identical queued updates are
suppressed; periodic updates always refresh runtime state. A write already
admitted/in flight is immutable; the next queued write reads the newest state.
No network I/O is performed under the host or admission locks.

Stop notice is sent only after an initial BCST was sent and forces Receiving,
Relay and Direct OFF even if the last runtime observation was still Receiving.
Latest available counts/uptime are used; stop notice/QUIT and cleanup remain
owned by the existing writer and termination machinery.

## Ownership and shutdown

The existing worker sends initial BCST and serializes latest-metadata and
30-second updates. It sends no media. Connect/write/shutdown retain finite
bounds; production handshake allows 20 seconds for tracker callback work.
Post-handshake control reads may remain idle while the tracker keeps its connection
open; silence alone is not a failure. The read and its wrapper remain tracked.
Stop cancels the read and aborts/closes the transport with the existing finite
shutdown/cleanup bounds. Non-cooperative pending I/O cannot prove cleanup.
Tracker failure stops only announce, not OBS ingest or the relay.

Finish/EOS/Dispose, listener/network invalidation and root shutdown close
publication capability. No old connection can regain it. The session attempts
Receiving=false and QUIT through the same serialized writer, closes the actual
transport and joins tracked I/O. The existing host's cleanup evidence now
includes announce cleanup. Unproven cleanup blocks generation replacement;
expiry is never success. Stop occurs outside the host/registry locks, before
retiring shared registration and draining the existing relay. Late DNS and
connection results remain owned. No automatic WAN rebind or YP reconnect exists.

## Validation boundary

Managed tests use injected connectors/resolvers, fake tracker atoms and existing
localhost fixtures. Public-looking fake endpoints never contact the internet.
Real WAN bind/reachability, external ping, YP compatibility/listing/removal and
Windows GUI physical acceptance remain NOT_RUN. Separate-PC LAN acceptance
remains DEFERRED. Multiple YPs, IPv6, bandwidth shaping and BCID persistence are
outside this phase. A tracker with a different response contract fails closed
and requires a separately reviewed compatibility change.
