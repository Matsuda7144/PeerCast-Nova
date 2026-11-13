# ADR-0492: Typed PCP QUIT Compatibility Without Identity Migration

## Status

Implemented for Smart Relay Phase 0 / A-1 on an independent branch from main
`810bc0b6265abb3fe6104fbe3af0e91901d511f2`. Managed validation is recorded in
[PCN-0502](../reviews/PCN-0502-TYPED-PCP-QUIT-COMPATIBILITY-WITHOUT-IDENTITY-MIGRATION.md).
Physical PeerCastStation interoperability is not established by this change.

## Phase 0: identities and ownership

| Identity | Generation and lifetime | Current uses |
| --- | --- | --- |
| ChannelId | Parsed from a received channel source; OBS host accepts a supplied ID or creates one when constructed | Channel routing, metadata, fanout, recording registration and YP channel identity; not a peer or owner generation |
| ChannelSessionId | Public-channel preparation and shared playback owner creation use a new Guid; OBS host creates one per host instance | Stable across upstream reconnects; Core tracker, packet/recording identity, Owner/Admission generation, and currently also relay wire SID |
| Peer Session ID (wire SID) | No separate node-runtime type/provider exists in this path | Upstream HELO takes the tracker's ChannelSessionId; Coordinator passes that same value to downstream OLEH. A diagnostic compatibility probe uses its own per-probe Guid |
| PublishingSessionId | Plain Guid field in PcpAnnounceIdentity, currently equal to the OBS host's ChannelSessionId | Binds runtime HOST snapshots to the current publishing generation and revision |
| BroadcastId / BCID | PcpAnnounceOwner generates a distinct Guid with the existing broadcast convention, retained for that owner's application lifetime across publishing generations | YP HELO/CHAN broadcast identity; must remain distinct from SID and channel identity |

Relevant existing boundaries:

- `PcpChannelSession.NegotiateAsync` passes the local tracker SessionId to HELO.
- `PcpRelayCoordinator` passes it to `PcpDownstreamRelayServer`; child OLEH
  echoes that local identity, not the remote HELO SID.
- `ObsPublishChannelHost` binds tracker, ingest, fanout/recording, HTTP and
  downstream PCP to its local ChannelSessionId. It validates incoming local
  packet identity before accepting media.
- `PublishingAnnounceIntegration` currently supplies the same Guid as
  PublishingSessionId and SenderSessionId. `PcpAnnounceOwner.Start` requires
  relay SID == publishing SID, while `PublishingAnnounceCapability.Evaluate`
  independently checks SenderSessionId == PublishingSessionId.
- `PcpAnnounceCodec` writes SenderSessionId into HELO, HOST id and BCST from;
  `PcpAnnounceWorker` checks the PublishingSessionId and revision of runtime
  HOST snapshots. These are different responsibilities despite equal values.

### Preserved invariants and future migration seams

A-1 does not change any ID generation, wire SID value, equality check, Owner
registration, fanout binding, reservation release, recording or YP lifecycle.

The existing registry and policy retire the previous Owner, retain its live
reservations until real transport/callback cleanup, and remove an Owner or lease
only on exact object identity. Public-channel removal uses ReferenceEquals, as
do recording registration tokens. The shared playback owner similarly cannot
remove a newer owner through delayed retirement. Keep these protections tied to
the local Owner generation, never a future node-wide SID.

A future PeerSessionId should be generated once per active node runtime and
injected into HELO/OLEH and HOST/BCST senders. Keep ChannelSessionId for local
generation/packet/recording checks and keep BCID separate. Before switching YP,
replace both existing SID == publishing-session checks with explicit membership
of the wire peer and publishing generation in the same current owner capability;
continue validating PublishingSessionId and revision on runtime snapshots.
A sender-only replacement would invalidate the current announce capability.
Candidate identity must remain channel-scoped when a node SID is shared.

Review the existing upstream/downstream, OBS publishing, announce/capability,
Admission and shared media compositions together before that later migration.
No process-wide SID migration or parallel owner registry is introduced here.

## Compatibility RFC: A-1 decision

1. A validated capacity refusal retains HTTP 503, reads HELO, then emits OLEH
   and a leaf QUIT with the existing little-endian code 1003
   (QUIT base 1000 + Unavailable 3). No HOST is required in this phase.
2. Decode a QUIT as exactly one four-byte leaf. Preserve the raw uint code and
   classify 0 as Unspecified, 1000 as Normal, 1003 as Unavailable, 1005 as
   NotIdentified, 1007 as BadAgent, and all other codes as Other.
   Other retains its raw code; it is not silently treated as normal termination.
3. An explicit 1003 classifies a handshake as RelayFull after HTTP 200 or 503.
   A legacy 503 + OLEH with a nonempty SID + QUIT 0 remains RelayFull.
   Bare 503, QUIT 0 without that OLEH, malformed QUIT, and explicit error codes
   do not enter the legacy capacity fallback. HTTP 401/403 remain HttpRejected.
4. An explicit rejection from an existing startup referral terminates with that
   rejection, rather than being hidden by the original capacity refusal or
   advancing the remaining referral plan. Capacity refusals and transport-only
   referral failures retain their existing bounded behavior.
5. Preserve typed received QUIT information in compatibility, channel stream,
   channel session and downstream child results. Keep existing terminal end
   reasons and post-QUIT stop behavior; no new recovery/retry policy is enabled.

These numeric meanings are observed in the primary
[PeerCastStation atom constants](https://github.com/kumaryu/peercaststation/blob/390c4e847289c90a77dbe40cc04b7bc1cfbddf17/PeerCastStation/PeerCastStation.Core/Atom.cs)
and its
[PCP output handshake](https://github.com/kumaryu/peercaststation/blob/390c4e847289c90a77dbe40cc04b7bc1cfbddf17/PeerCastStation/PeerCastStation.PCP/PCPOutputStream.cs).
Nova independently implements the existing format; it adds no atom, extension
negotiation, dependency or alternate PCP engine.

## Ownership, state and limits

The same transport/PipeReader crosses the HTTP/PCP boundary and remains open
for already-buffered post-OK CHAN bytes. The incremental stream decoder only
copies the four-byte QUIT code, not media payloads or another control tree.
Results add numeric information without peer identifiers or free-form text.
Code-0 and malformed-HELO server behavior outside the capacity response remains
unchanged; malformed HELO is never followed by the new capacity QUIT.

Existing media position, packet sequence, Core identity, bounded fanout and
shutdown/cleanup behavior are preserved. Stop/Cancel does not acquire a new
retry lifetime. Valid QUIT remains terminal after media has started, including
Unavailable; Smart Relay recovery is later work.

## Deferred

HOST sharing/referral generation, BCST routing expansion, PeerDirectory,
UpstreamSelector, automatic recovery/long retries, node SID migration, WAN bind,
NAT, bandwidth adjustment, relay GUI and manual disconnect remain outside A-1.
Phase B can build on the classified reasons after this branch's managed checks
and review. WAN product acceptance remains a separate mandatory integration gate.
