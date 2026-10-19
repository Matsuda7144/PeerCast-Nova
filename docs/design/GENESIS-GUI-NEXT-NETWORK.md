# PeerCast Nova Genesis GUI Next - Network and Relay Settings

Project Founder: 7144

This document defines the network/relay settings workstream for Genesis GUI Next.
It complements `GENESIS-GUI-NEXT.md` and is intentionally kept independent from
the current PCP playback-integration investigation.

## Why this belongs in GUI Next

A PeerCast client that can browse and play channels but cannot show whether its
listening port is reachable, whether relay is allowed, or how network listeners
are configured leaves the user unable to diagnose an important part of normal
PeerCast operation.

GUI Next therefore includes a first-class Network / Relay settings surface.
The initial objective is not to expose every low-level backend switch. It is to
make the common operational questions answerable from the GUI:

- What port is Nova listening on?
- Is IPv4 reachable from the Internet?
- Is IPv6 enabled/reachable when supported?
- Is automatic port mapping enabled and did it discover a router?
- What external address was observed?
- Does this listener accept relay connections?
- Does this listener accept direct playback connections?
- Is access local-only or also allowed globally?
- What are the current relay/listener counts and limits when that information is
  truthfully available from the application boundary?

## PeerCastStation reference audit

PeerCastStation is used as a behavioral and architecture reference for this area.
The relevant upstream implementation includes:

- `PeerCastStation.WPF/CoreSettings/SettingControl.xaml`
  - primary listening port;
  - IPv6 enablement;
  - UPnP/NAT-PMP automatic port mapping;
  - external-address display;
  - explicit IPv4/IPv6 port-check states;
  - global/local access controls;
  - advanced listener configuration.
- `PeerCastStation.WPF/CoreSettings/SettingViewModel.cs`
  - listener projection;
  - separation of global/local Relay, Play, and Interface acceptance;
  - per-listener reachability state;
  - external endpoint projection.
- `PeerCastStation.UI/PCPPortChecker.cs`
  - finite asynchronous port-check operation;
  - IPv4/IPv6 checker targets;
  - result model containing local address, observed global address, reachable
    ports, elapsed time, and bounded failure information.

### License boundary

PeerCastStation is licensed under GNU GPL version 3. PeerCast Nova is licensed
under Apache License 2.0.

Therefore GUI Next MUST NOT copy, adapt, translate, or mechanically port GPLv3
PeerCastStation source into Nova. In particular, do not paste the WPF XAML,
`SettingViewModel`, `PCPPortChecker`, or other GPL implementation code into the
Apache-2.0 repository.

PeerCastStation may be used for:

- behavioral observation;
- identifying useful product requirements;
- understanding protocol-visible state;
- identifying robust state-machine boundaries;
- interoperability testing against documented/external behavior.

Nova implementation must use Nova-owned names, types, tests, architecture, and
code. Protocol facts and user-visible concepts are reimplemented independently.

If a future feature genuinely requires incorporating GPL code rather than
independent reimplementation, that is a separate licensing decision and is out of
scope for GUI Next.

## Nova reuse audit

GUI Next must not create a second relay stack merely to make the settings page
look like PeerCastStation. The existing Nova network layer already owns useful,
tested relay building blocks and they are the preferred implementation base.

The current reusable Nova components include:

- `Nova.Network.Transport.TcpTransportListener`
  - existing bounded TCP-listener lifecycle and accepted-connection boundary;
- `Nova.Network.Pcp.PcpRelayFanout`
  - existing bounded downstream relay fan-out;
  - exposes truthful active-child count and isolates slow/failing children;
- `Nova.Network.Pcp.PcpDownstreamChildSession`
  - existing downstream HTTP/PCP negotiation, CHAN delivery, remote control,
    bounded writes, and cleanup;
- `Nova.Network.Pcp.PcpDownstreamRelayServer`
  - existing listener/session owner;
  - already exposes accepted, active, and completed connection counts plus bounded
    relay/protocol/write outcome counters;
- `Nova.Network.Pcp.PcpRelayCoordinator`
  - existing owner for one upstream channel session, relay fan-out, and downstream
    server;
  - already exposes the downstream endpoint and server snapshot.

These components should be adapted behind a narrow application projection rather
than replaced by GUI-specific socket or PCP code.

### Shared-listener gap

The audit also identified a real architectural gap that must not be hidden by the
GUI. The current `PcpDownstreamRelayServer` is created for one fixed channel id and
one fixed fan-out. `PcpRelayCoordinator` therefore owns a per-channel downstream
server.

That is not yet equivalent to the familiar PeerCastStation model where one primary
configured port, commonly 7144, accepts incoming requests for multiple channels.
GUI Next MUST NOT present a per-channel bound endpoint as though Nova already has a
single application-wide listening port.

The intended reuse-first direction is:

1. keep `TcpTransportListener` as the socket/listener implementation;
2. add an application-owned shared downstream ingress/router that can resolve a
   requested channel id to a currently registered Nova relay target;
3. reuse the existing PCP request-validation and downstream-child semantics rather
   than writing a second protocol implementation;
4. keep `PcpRelayFanout` and its bounded child/session behavior as the per-channel
   relay delivery engine;
5. project the resulting applied listener state and existing relay counters into
   GUI Next through Nova-owned application contracts.

Any refactor needed to make the existing request parser/session usable behind a
shared ingress must preserve current protocol, capacity, timeout, cleanup, and
privacy regression tests.

### Capabilities that remain unavailable until proven

Some PeerCastStation concepts shown in the reference UI are not yet truthful Nova
capabilities and must remain hidden or disabled until a real backend exists:

- application-wide Direct Play acceptance is not inferred from PCP relay support;
- a full remote relay topology tree is not inferred from connection counters;
- UPnP/NAT-PMP mapping is not inferred from successful local binding;
- Internet reachability is not inferred from successful port mapping;
- a single global listening-port setting is not considered applied until the
  shared multi-channel ingress owns that endpoint transactionally.

This makes the reuse policy concrete: reuse existing Nova networking wherever its
semantics match, add only the missing application-level ownership/routing pieces,
and never synthesize unsupported state for the GUI.

## Default Network settings surface

The default settings page should remain compact and answer the common questions
without requiring Advanced mode.

Conceptual layout:

```text
Network / Relay

Listening
  Port                         [ 7144 ]
  IPv6                         [x] Enable when available
  Automatic port mapping       [x] UPnP / NAT-PMP

Reachability
  IPv4                         Open / Closed / Unknown / Checking / Failed
  IPv6                         Open / Closed / Unknown / Checking / Failed
  External IPv4                203.0.113.10
  External IPv6                ----
                               [ Check ports ]

Incoming connections
  From Internet
    [x] Relay
    [ ] Direct play          (enabled only when supported by backend)

  From local network
    [x] Relay
    [x] Direct play

Relay
  Current relays               2
  Current direct listeners     1
  Capacity / limit             backend-owned value when available
```

The exact controls may evolve during native-GUI implementation, but the semantic
contract should remain stable.

## Advanced listener settings

Advanced mode may expose multiple listener endpoints when the application core
supports them. Each listener definition should be modeled independently and may
contain:

- stable local identifier;
- bind address / address family;
- port;
- enabled state;
- IPv4 / IPv6 family;
- local Relay permission;
- global Relay permission;
- local Direct Play permission;
- global Direct Play permission;
- optional interface/API permission only if Nova later exposes such a service;
- authentication policy only if that service exists.

Do not expose inactive PeerCastStation concepts merely to make the Nova settings
page look complete. A checkbox must correspond to a real application capability.

## Reachability / port-check contract

Port openness cannot be truthfully inferred from successful local `Listen()` or
from router-mapping success alone. Nova needs an explicit external reachability
check.

Define an application-facing boundary conceptually equivalent to:

```text
INetworkReachabilityChecker
  CheckAsync(NetworkReachabilityRequest, CancellationToken)
      -> NetworkReachabilityResult
```

The request should carry only information required for the check, such as:

- application/session correlation token when the chosen checker protocol needs
  one;
- address family;
- local bind address where relevant;
- candidate listening ports.

The result should distinguish:

- `Open` - external checker confirmed one or more requested ports are reachable;
- `Closed` - checker completed successfully but no requested port was reachable;
- `Failed` - checker itself could not complete or returned invalid data;
- `Cancelled` - user/application shutdown cancelled the finite operation.

Useful result data:

- observed external address when supplied by the checker;
- confirmed reachable ports;
- elapsed time;
- bounded diagnostic code/message suitable for logs;
- check timestamp.

The GUI projects this result. It must not perform the network probe directly from
Win32 control event handlers.

## Port-check service safety

A public external checker creates a dependency outside Nova, so the implementation
must be replaceable and testable.

Requirements:

- checker endpoint/configuration is behind the application boundary;
- HTTPS is preferred whenever the service supports it;
- bounded timeout;
- cancellation on shutdown;
- no overlapping checks for the same listener;
- manual re-check remains possible after success or failure;
- network failure does not crash the host;
- response parsing is strict and bounded;
- never treat timeout, DNS failure, HTTP failure, or malformed response as
  `Closed`; they are `Failed`/`Unknown` because reachability was not established;
- do not send credentials, channel history, favorites, or unrelated user data to
  a port-check service.

The exact external checker service is not selected by this design slice. It must
be reviewed separately for protocol, privacy, reliability, and operational
ownership before production use.

## Automatic port mapping

Automatic port mapping is useful but separate from external verification.

Target boundary:

```text
IPortMappingService
  DiscoverAsync(...)
  MapAsync(...)
  UnmapAsync(...)
  CurrentState
```

Initial protocol targets may include UPnP IGD and NAT-PMP where a maintained,
license-compatible implementation is available. PCP (Port Control Protocol) may
be considered later; do not confuse it with PeerCast PCP.

State exposed to GUI should distinguish at least:

- Disabled;
- Discovering;
- RouterNotFound;
- Mapping;
- Mapped;
- Failed.

A successful mapping does not set external reachability to Open until the
reachability checker confirms it.

## Relay configuration and status

Relay controls are application/core concerns, not catalog concerns.

GUI Next should consume a narrow application-facing projection rather than
referencing production PCP implementation types from the native surface.

Conceptually:

```text
IRelayNetworkController / IRelayNetworkStatus
  ListenerDefinitions
  CurrentRelayConnections
  CurrentDirectListeners
  UpdateListenerSettings(...)
```

The actual interface may be split into immutable status snapshots and explicit
commands during implementation.

Required product behavior:

- the user can tell whether incoming relay is permitted;
- the user can change relay permission only through validated application
  commands;
- invalid ports (< 1, > 65535, reserved-by-policy values, duplicates that cannot
  coexist) are rejected before attempting a rebind;
- changing a live listening port is transactional: failure leaves a known valid
  listener rather than silently losing the old listener;
- GUI status reflects actual applied state, not merely edited textbox contents;
- current relay/direct counts are diagnostic status, never editable values.

## Relationship to GUI Next tabs

Network/Relay is a Settings area, not another primary channel-browser tab.

Primary tabs remain:

1. Channels;
2. New;
3. Favorites;
4. Playing;
5. future Downloads.

A compact network health indicator may later appear in the status bar, for
example:

```text
YP: updated 12s ago | Port: Open | Relay: 2
```

Clicking the network indicator may navigate to Network / Relay settings, but the
main channel list should not become a network-control dashboard.

## Implementation slices

Keep this work independent and GREEN after every slice.

1. **Network settings contract**
   - immutable listener/settings records;
   - reachability status model;
   - no sockets or native UI changes.
2. **Reachability checker boundary**
   - finite async request/result contract;
   - fake checker tests for Open/Closed/Failed/Cancelled;
   - no production external service selected yet.
3. **Relay/listener application projection**
   - expose truthful current listener and relay state from the existing Nova core;
   - do not let GUI reference PCP implementation types directly.
4. **Settings view model**
   - edit/apply/revert validation;
   - checked/applied state separated from draft state.
5. **Native settings surface**
   - basic Network / Relay page;
   - explicit Check Ports command;
   - accessibility/focus/keyboard coverage.
6. **Port mapping boundary**
   - discover/map/unmap state model;
   - select a maintained Apache-2.0-compatible implementation or implement only
     the required protocol behavior independently.
7. **Production reachability provider**
   - select/review external checker;
   - privacy and timeout policy;
   - physical verification requires local user testing.
8. **Status-bar projection**
   - optional compact Open/Closed/Unknown and relay-count display after the
     application contract is proven stable.

## Local validation boundary

Automated tests can prove state transitions, validation, cancellation, projection,
and parser behavior. They cannot prove the user's router or ISP accepts inbound
connections.

The final production reachability slice therefore requires local validation for:

- actual listen/bind success on Windows;
- UPnP/NAT-PMP router discovery where applicable;
- real external IPv4/IPv6 port-check result;
- relay acceptance from another peer;
- firewall/router behavior after port changes.

These are explicit local/physical checks and must not be claimed from CI alone.
