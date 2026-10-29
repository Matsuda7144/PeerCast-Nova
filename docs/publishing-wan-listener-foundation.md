# Publishing WAN Listener Foundation

Base: `bd968ad7a945fda369d33a0666a24cb644d5164b` (PR #331).
This slice adds explicit WAN ingress to the existing Publishing relay server.
It does not enable YP announcement or prove physical LAN/WAN reachability.

## Configuration and endpoint identity

Loopback (default), LAN and WAN are mutually exclusive. Existing LAN settings
retain their RFC1918/subnet gate; WAN has a separate address policy and required
Broadcast registration in the same root-owned admission registry. No second
listener/server/registry is created. RTMP and HTTP-FLV remain loopback-only.

WAN Start resolves exactly one up NIC assignment matching the selected ID/IP,
with a consistent IPv4 mask. RFC1918 and ordinary global unicast bind addresses
are allowed. Any/loopback/multicast and special-purpose ranges are rejected.
The conservative special-purpose table follows IANA's IPv4 registry snapshot
dated 2025-10-09; global special services are not user broadcast endpoints.
There is no online lookup or external-address discovery.

The configured internal address/port is never replaced by another NIC or an
ephemeral port. Public IPv4/port is an independent optional pair (both empty or
both valid). A private advertised endpoint is rejected. Different internal and
external ports support manually configured translation without performing NAT.
Tracker-observed/verified endpoints remain null, and ReachabilityVerified is
always false. Runtime readiness also checks the actual bound endpoint; a test
bind substitution cannot masquerade as the configured WAN listener.

Saving settings does not bind, rebind, subscribe network events or announce.
Explicit Start is required. A coalesced, generation-owned NIC-change worker
invalidates this generation if its assignment/up/mask/prefix changes; it never
selects a replacement. Stop joins/unsubscribes that worker with finite evidence.
IPv4 numeric bandwidth limits (including zero) reject WAN Start through the
existing policy gate. Connection caps are not bandwidth shaping.

## One listener and one PipeReader

Only WAN mode opts into the new dispatch. It peeks a bounded prefix on the
existing child's PipeReader. `GET ` stays untouched for the existing HTTP relay
reader; `pcp\n` consumes exactly a validated 12-byte PCP v1 preamble. Buffered
HELO/other bytes remain on that same reader. Malformed direct PCP never falls
back to HTTP. Loopback/LAN retain the existing HTTP-only behavior.

The HTTP reservation/HELO/OLEH/OK/metadata/HEAD/DATA path and late join are reused.
The original accept timestamp is retained: protocol identification is at most
3 seconds and the HTTP handshake at most 10 seconds (or the smaller existing
child option), not a new ten seconds after dispatch.

## Ping protocol

Reference behavior was independently implemented from PeerCastStation commit
`390c4e847289c90a77dbe40cc04b7bc1cfbddf17`, specifically
`PeerCastStation.PCP/PCPPongOutputStream.cs` and `PCPOutputStream.cs`.
No reference code was copied.

Direct PCP accepts a minimal HELO with a unique nonempty SID; agent/version are
optional for the ping request. OLEH returns the fixed current Publishing relay
SID, then closes. The same captured SID is already used by HTTP relay OLEH.
QUIT is handled without an OLEH. Invalid version, SID, shape, incomplete input,
EOF and cancellation close the connection safely. Ping sends no CHAN, HEAD,
DATA or OK; it neither registers a fanout child nor reserves a media relay slot.
It never initiates a reverse ping or follows peer-supplied endpoints.

## Admission and resource accounting

WAN requires a nonnull, identity-matched registered Broadcast admission. Unknown
registrations cannot use the legacy optional-admission path. WAN also explicitly
rejects an injected NAT mapping manager. Publishing never supplies one.

WAN control defaults (Nova safety values, not PCP protocol requirements):

| Resource | Default |
| --- | --- |
| Pending handshakes | 16 |
| Pending handshakes per source IP | 2 |
| Concurrent pings | 4 |
| Ping attempt/response budget | 8 per second, per listener |
| Protocol identification | 3 seconds since accept |
| Ping | 3 seconds since accept |
| HTTP handshake | 10 seconds since accept |
| Incoming PCP control | 4 KiB, 32 children, depth 4 |

Per-IP limits apply only to unfinished handshakes, not all active viewers behind
one NAT. RFC1918 sources (e.g. translated/hairpin paths) and ordinary global IPv4
sources are allowed in WAN mode; other special sources are rejected. Counters
are bounded and the per-IP table contains only active pending leases.

Existing TCP cap 128, fanout cap 32, bounded queues, write deadlines and slow
consumer termination remain in force. TCP slots include ping and malformed
connections. Ping/control accounting is distinct from shared media reservation
accounting. Rate budget is charged before response work, including failed attempts.

## Lifecycle and cleanup

Finish/Dispose/network invalidation close the WAN source/admission gates before
new protocol work. StopAccepting closes control and child admission before
stopping the socket. Existing host quiescence/drain ordering is retained.
All accepted ping/relay tasks belong to the same server session set.

Counter leases are released only after actual transport cleanup and owned I/O
termination. A cancellation-resistant ping write is retained in the existing
cleanup task ledger. Deadline expiration returns incomplete evidence, never a
false success; late writes retain the original SID and cannot retarget a newer
generation. Pending/incomplete cleanup blocks dangerous generation replacement
through the existing host/composition evidence contract.

EOS and explicit Finish require the existing successful cleanup evidence. Forced
active Dispose is not normal EOS/drain: the pre-existing aggregate shutdown
snapshot can be conservative. Tests separately await Dispose's joined cleanup
and check WAN session/lease/subscription cleanup; they do not relabel that snapshot
as clean EOS or change recording lifecycle code.

## Settings and UI

Schema 3 stores an explicit mode plus validated LAN/WAN configuration; flags and
mode must agree. Schema 1 migrates to Loopback; Schema 2 preserves LAN values
without enabling WAN. Missing/unknown/inconsistent/corrupt documents remain on
disk and block Start until explicit validated save. File limits and atomic saving
are unchanged. Draft/Saved/Applied/SettingsRevision remain separate, with changes
applied at the next Start only. Older binaries reject the newer schema safely.

The existing themed/scrollable editor gains a mode selector and public address/
port fields. Status separates actual bind, declared public endpoint and unknown
reachability. It exposes no unverified connectable WAN URL. No YP ON control is
added. PR #331's owner/worker/access/transport and production isolation remain
unmodified and unconnected.

## Validation scope and deferred work

Managed tests use fake NICs, injected source conditions, the existing localhost
TCP/RTMP fixtures and a small extension of the existing manual test clock. The
internal host test constructor substitutes only loopback binding; public entry
points cannot supply that hook. No actual WAN listener, external PCP ping, YP
connection, external-IP service, firewall/router/NAT operation or physical app
launch is part of this task. LAN physical acceptance remains DEFERRED.

Real NIC binding, external routing/forwarding, PCP interoperability and A/V are
not physically accepted. Tracker verification handshake, callback correlation,
public capability and YP GUI/production announce wiring remain for a separately
approved phase. A received ping alone never verifies a particular tracker or
external endpoint.
