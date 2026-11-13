# ADR-0493: Owner Scoped PCP HOST Directory and Referrals

## Status and scope

Smart Relay Phase B, based on main
`1143340e36ac26ddd55553e3e4db439552f4a6ce` after PR #365.
The main-push CI run 43339004955 succeeded. Phase A-1's ID/Owner contracts and
typed QUIT handling remain in place. Managed validation:
[PCN-0503](../reviews/PCN-0503-OWNER-SCOPED-PCP-HOST-DIRECTORY-AND-REFERRALS.md).

## Decision

Each existing PcpDownstreamRelayServer owns one PcpPeerDirectory for its exact
channel/local session lifetime. PcpRelayCoordinator lends that same directory
to its existing upstream PcpChannelSession. There is no process-wide directory,
new relay engine, listener, timer, connection probe or Owner registry.

The directory retains at most 256 records. HOST observations expire after
120 seconds of monotonic time, pruned on use. Endpoint and known SID aliases
merge; new SID-less observations cannot erase an identified peer. Each record
keeps its most recent source kind/transport ConnectionId, observation timestamp,
a saturated failure count (maximum 64), known local negotiation success and
last failure time. Remote refresh does not clear that local failure history.
History expires with the bounded record. No arbitrary history list is retained.

State-changing observations are limited to 64/channel/second. Direct downstream
knowledge is also bounded to 256 leases; overflow suppresses all referrals
until the overflow leases end rather than dropping a loop exclusion.

## HOST parsing and intake

Reuse PcpRelayHostCandidateParser and the existing wire-value codec. Preserve
the original two endpoint-ordinal rules, including malformed slot reservation.
Add optional cid, id, flg1, numr and upstream endpoint metadata; missing fields
are Unknown. Reject malformed/duplicate identity or known scalar metadata.
Limit a HOST to 32 fields and 1 KiB of flat control data.

Sources are:
- Upstream handshake HOST atoms, bounded by the existing handshake atom/candidate
  budgets (eight retained candidates), observed on the existing PipeReader.
- Top-level HOST atoms during upstream media reception, observed by a bounded
  callback sink on the existing incremental decoder.
- Top-level HOST atoms read by an established downstream's existing control
  monitor, bounded by the existing control-atom reader and HOST parser.

The streaming sink copies only recognized small HOST fields, never CHAN media.
Malformed/oversized semantic HOST data is ignored without changing packet
sequence, position, media ownership or reader advancement. Existing hard PCP
framing/content/depth limits still reject unrecoverably invalid wire framing;
this is not a new arbitrary resynchronization mechanism.

BCST is not newly traversed, forwarded or re-advertised. Existing BCST handling
and the YP announce codec/lifetimes are untouched. Hops remains Unknown unless
provided internally; no invented HOST hops field is read or emitted.

## Safety and selection

Directory registration requires an explicit matching ChannelId and eligible
global IPv4 endpoint. Reuse PcpRelayRedirectPolicy, including its address list,
and add explicit port-range validation to that shared policy. Keep any local
endpoint metadata separate; Phase B never advertises it.

Exclude known local SID/listener endpoint, known direct downstream SID or
endpoint, a peer declaring our known endpoint as its upstream, and the requester's
SID/transport/listening endpoint. A standalone server's actual bound endpoint
and current channel SID are the available local identities. A full Internet
ancestry graph or an unknown external NAT endpoint cannot be inferred from PCP;
global acyclicity is not certified here.

ReferralPlanner applies eligibility/exclusions first, then Receiving,
RelayAvailable, local successful negotiation, observation freshness and Hops.
Explicit Firewalled peers and failures within a 30-second cooldown are omitted.
SID-less records can be retained/promoted, but cannot be advertised: Station
requires HOST id, so Nova never fabricates a peer SID. Select at most eight
distinct endpoints. Relay availability is an untrusted claim, not a reservation.

The existing startup referral flow also ignores known mismatched cid, our SID
and known direct loops. Legacy HOSTs lacking cid/id keep their original bounded
startup behavior; they do not qualify for directory registration/advertisement.
The existing upstream plan is reused, not replaced by an UpstreamSelector.

## Compatibility RFC and runtime integration

For a valid capacity refusal:

```text
HTTP 503 -> receive/validate HELO -> OLEH -> HOST (0..8) -> QUIT 1003
```

Encode the complete OLEH/HOST/QUIT sequence once and write it through the
existing bounded control writer, before any media-worker handshake gate is
released. This preserves atom ordering and avoids competing writes/readers.
HOST uses standard id/cid/IP/port and optional flags/count fields, existing
GUID/legacy IPv4/little-endian numeric codecs. The Relay bit remains 0x02
(available); Receiving is 0x10, Firewalled 0x08. Missing flags/count stay omitted.

Reference behavior was checked at PeerCastStation master
`390c4e847289c90a77dbe40cc04b7bc1cfbddf17`:
[PCPOutputStream.SendHost](https://github.com/kumaryu/peercaststation/blob/390c4e847289c90a77dbe40cc04b7bc1cfbddf17/PeerCastStation/PeerCastStation.PCP/PCPOutputStream.cs#L526)
and
[PCPSourceStream HOST intake](https://github.com/kumaryu/peercaststation/blob/390c4e847289c90a77dbe40cc04b7bc1cfbddf17/PeerCastStation/PeerCastStation.PCP/PCPSourceStream.cs#L683).
Its receiver accepts one global endpoint pair and identifies hosts by id.
No custom atom, required new field, peer modification or copied engine is used.

Only a failed capacity registration reaches this sequence; invalid HTTP,
channel mismatch and malformed HELO retain their prior rejection paths.
Phase A-1's explicit errors and 503/OLEH/QUIT-0 compatibility remain unchanged.
Zero eligible candidates still emits OLEH/QUIT 1003.

Where Admission is injected, referrals require the existing optional
IPcpDownstreamRelayCapacity.IsCurrent observation. Unknown admission provenance
fails closed to zero HOST, never guessed current. The GUI channel Owner now
forwards that existing observation from its exact registry registration.
No new Owner contract or ID equality rule is introduced.

Accepted downstreams acquire identity-bound exclusion leases after HELO.
They are released only after actual transport/previous admission cleanup;
failed cleanup conservatively retains the exclusion until directory retirement.
Server StopAccepting/Stop/Dispose closes and clears the directory; new plans
are suppressed after the existing Owner-current/cancellation/child-admission
checks. Already-started bounded writes use the existing deadlines/cancellation.
Late callbacks reference only the old directory, never its replacement.

## Limits and deferred work

The normal Coordinator/standalone downstream server path is integrated.
The shared routed handler without an owner-bound directory keeps its existing
zero-HOST behavior; Phase W must explicitly bind that ownership if used for
future public multiplexing. No independent directory registry is guessed there.

Node-wide PeerSessionId migration, BCST routing, recovery/long retries, quality
switching, WAN binds, NAT, bandwidth, relay-tree UI and manual disconnect are
not implemented. The GUI Relay's loopback boundary is unchanged. Physical
PeerCastStation and WAN acceptance remain separate requirements.
