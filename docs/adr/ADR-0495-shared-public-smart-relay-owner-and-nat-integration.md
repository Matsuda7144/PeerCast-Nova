# ADR-0495: Shared public Smart Relay Owner and NAT integration

Status: Accepted for implementation

## Context

Smart Relay A-1/B/C preserve existing PCP identities and implement bounded HOST
referrals and upstream recovery. GUI Relay still uses a per-channel loopback
listener. The Network module already has a shared TCP listener, route registry,
routed child handler and WAN ingress policy. The application already owns one
NAT runtime, backend selection and mapping reconciliation pump.

## Decision

Phase W connects these existing components. `WanRelayEnabled` defaults false in
the existing connection preferences. The saved flag and existing nonzero
`ListenPort` apply at next startup; saving or launching Nova never starts Relay.
An explicit GUI Relay operation activates its original Coordinator/loopback
listener and, when WAN is enabled, registers that Owner on one shared IPv4
`0.0.0.0:ListenPort` listener. Recovery opt-in remains independent. WAN plus the
unimplemented IPv6 mode is rejected rather than silently ignoring IPv6.

`NovaApplicationNetworkOwner` supplies the shared host with its existing mapping
manager. It owns shared-host shutdown before NAT-runtime shutdown. Active or
cleanup-failed legacy single-channel public ownership excludes shared WAN mode;
shared WAN ownership also excludes legacy acquisition. No parallel public PCP
engine or second NAT pump is added. Existing loopback listeners remain available.

The existing shared listener has a narrow public constructor, inert route
registration, start/stop and scalar cleanup status. Routing internals, child
creation and raw server access remain internal; callers resolve only existing
Coordinators. `Nova.App` receives no friend-assembly access, preserving the
publishing/YP announce isolation contract. UI controls receive settings/snapshots,
not a manager or transport capability.

## Owner and cleanup contract

- A route binds an exact ChannelId and existing fanout. Registration runs under
  the Owner generation lock. Lookup also checks the composition's current Owner,
  exact fanout, activation and current Admission generation.
- A routed connection borrows the listener's single PipeReader and transport.
  The original Owner's `PcpDownstreamRelayServer` creates/tracks the same child
  session, shared Admission, child gate, metadata and PeerDirectory. Loopback
  and public connections reserve through the same total/per-channel policy and
  fanout child limits; no second reservation ledger is introduced.
- The listener returns from the handler before closing its borrowed transport.
  The Owner tracks actual transport/worker cleanup asynchronously, avoiding a
  listener/handler cleanup deadlock. Reservations and direct-peer leases keep
  their existing actual-cleanup proof. Route/policy leases also survive until
  transport cleanup. Repeated route/host/Owner release joins the original task.
- Retire synchronously removes only its exact route and closes generation
  Admission. Replacement registration is allowed while the old route drains;
  old completion cannot remove the replacement. Stop joins complete public
  acquisition, including a delayed provider, before disposing the stop token.
- Last route release stops the shared listener, proves cleanup, then releases
  its own NAT demand. Unconfirmed route/transport cleanup is sticky failure;
  re-acquisition is refused and successful shutdown is never reported.

## NAT and status

The shared host acquires one mapping demand only after binding the exact fixed,
nonzero port. It releases only that token; another Owner's demand on the same
port remains valid. Existing NAT-PMP/UPnP selection, renewal, serialized late-map
compensation and shutdown remain in the central manager. Auto mapping OFF still
allows manual router configuration. A port change takes effect after restart;
the old host releases its demand during shutdown.

Classic and Modern connection settings expose WAN opt-in beside existing port
and auto-mapping controls. The existing runtime status surface distinguishes TCP
preparation/listening/stopping/stopped, settings errors, cleanup failure and NAT
mapping success/failure/unconfirmed. Bind or mapping success never establishes
Internet reachability. External reachability stays Unknown; no inferred global
address is advertised in HOST.

## PCP, recovery and safety

Public OLEH/HOST use the original channel Owner SID and directory. Candidate
limits, TTL, endpoint policy, direct-peer exclusion and standard
503/HELO/OLEH/HOST(0..8)/QUIT 1003 remain unchanged. No Nova-specific atom or
PeerCastStation change is required. ChannelSessionId, PublishingSessionId, BCID,
ChannelId and publishing/YP equality contracts are not migrated.

Upstream HELO still advertises no `port`/`ping` value. Shared-listener direct
ping has its own control SID, not a claim to any channel Owner SID. Station
[PingHostAsync](https://github.com/kumaryu/peercaststation/blob/390c4e847289c90a77dbe40cc04b7bc1cfbddf17/PeerCastStation/PeerCastStation.PCP/PCPOutputStream.cs#L331)
compares ping OLEH SID with the upstream HELO SID. Therefore this phase must not
advertise the shared ping port as proof of all channel identities. Automatic
upstream port announcement/discovery remains deferred; incoming standard PCP
connections can use an explicitly known public endpoint. A broader identity
migration or an unverified external mapping is not introduced to hide this limit.

The original Coordinator now accounts for routed children during recovery and
Stop. An outage closes old public children, clears retained HEAD and waits for
actual cleanup, while keeping the same Owner, public listener and mapping demand.
Public requests during the media gap receive Unavailable; fresh HEAD starts the
new generation. C's retry budgets, selector and single upstream are unchanged.

Shared ingress applies the existing source filter, bounded pending/per-address
admission, identification/handshake deadlines and standard PCP ping handling.
WAN control atoms are limited to 4 KiB, 32 children and depth four. Missing/empty
HELO SID is refused. Invalid HTTP/ChannelId/control input cannot bypass current
Owner resolution or terminate the shared accept loop. No firewall rule is added.

## Limits

Managed real TCP tests connect loopback clients to the public Any/fixed-port
listener with an explicit test-only source allowance. This does not prove
Internet reachability, a real router mapping, PeerCastStation binary behavior,
native GUI layout or physical audio/video. IPv6 WAN, node SID migration, BCST
re-advertising, seamless switching and advanced relay GUI remain later work.
