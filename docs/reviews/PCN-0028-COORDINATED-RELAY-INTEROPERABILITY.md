# PCN-0028 Coordinated Relay Interoperability

## Scope

This report records Milestone 0.0.4d validation of the owned
upstream-to-fan-out-to-downstream path. It covers deterministic reconnect and
drain behavior plus official Stable/development downstream interoperability.
It does not claim a controlled real media channel through an official upstream.

## Environment

- Date: 2026-07-14
- Platform: Windows IPv4 loopback
- .NET SDK: 10.0.301
- Official Stable downstream: PeerCastStation 4.1.0
- Official development downstream: PeerCastStation 7.1.1
- Discovery: disabled; isolated settings and dedicated ports
- Existing port 7144 process: not stopped or reconfigured

## Deterministic wire-to-wire result

A synthetic bounded upstream peer completed Nova's real HTTP/PCP client
handshake, sent two CHAN packets, and ended with QUIT. One downstream client
completed Nova's real server handshake and received both payloads. The
coordinator reached `UpstreamEnded`, completed its finite drain, stopped every
owned boundary, and reported no discard or incomplete cleanup.

A second test closed the first upstream connection after position 100. Nova
reconnected with `x-peercast-pos: 100`, accepted position 200 on the second
connection, and delivered both packets through the same downstream child.

Caller cancellation stops both boundaries with `CallerCanceled`. A truncated
upstream response becomes the fixed `UpstreamFailed` result while Core records
only `ConnectionFailed` and its fixed stage/counts.

## Official downstream results

The controlled chain was:

```text
synthetic upstream PCP wire -> PcpRelayCoordinator -> official downstream
```

Stable 4.1.0:

```text
RelayEndReason: Stopped
UpstreamPackets: 26
AcceptedHandshakes: 1
DeliveredPackets: 18
DeliveredBytes: 72
DiscardedPackets: 0
CleanupIncomplete: 0
InternalFailures: 0
ShutdownCompleted: True
AcceptanceGate: Passed
```

Development 7.1.1:

```text
RelayEndReason: Stopped
UpstreamPackets: 25
AcceptedHandshakes: 1
DeliveredPackets: 19
DeliveredBytes: 76
DiscardedPackets: 0
CleanupIncomplete: 0
InternalFailures: 0
ShutdownCompleted: True
AcceptanceGate: Passed
```

No identifier, endpoint, metadata value, packet position, exception, or
payload content was printed or persisted.

## Official-upstream exploratory boundary

A separate black-box attempt placed official Stable 4.1.0 between the
synthetic packet-only source and Nova. The source confirmed delivery of 20
packets to Stable with zero discard and complete cleanup. Nova's upstream
handshake was accepted, but Stable did not re-relay that packet-only synthetic
channel and Nova observed zero packets.

No unobserved packet-type or channel-bootstrap value was inferred from
implementation source or third-party reverse engineering. This attempt is
recorded as an unmet fixture condition, not as successful real-channel
verification and not as a Nova packet-path failure.

## Automated validation

- Release build: zero warnings, zero errors through `build.ps1`
- Foundation tests: 1 passed
- Core tests: 16 passed
- PCP Protocol tests: 43 passed
- Network tests: 72 passed
- Total: 132 passed
- Network repetition gate: 10 consecutive runs / 720 executions passed
- TCP Transport self-test: passed
- PCP Framing self-test: passed

## Decision

Milestone 0.0.4d owned relay coordinator is **Implemented**. Its controlled
wire-to-wire behavior, reconnect continuity, finite drain, and official
Stable/development downstream interoperability are **Verified**.

The complete 0.0.4 Relay milestone remains in progress until a controlled real
media channel passes from an official upstream through Nova to a downstream
peer without relying on guessed or copied protocol semantics.
