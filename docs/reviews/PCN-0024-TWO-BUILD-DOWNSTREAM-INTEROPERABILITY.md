# PCN-0024 Two-Build Downstream Interoperability

## Scope

This report records black-box downstream PCP entry observation and controlled
one-hop wire validation for Milestone 0.0.4b. It does not claim continuous
real-channel upstream-to-downstream relay completion.

## Environment

- Date: 2026-07-14
- Platform: Windows IPv4 loopback
- .NET SDK: 10.0.301
- Official Stable peer: PeerCastStation 4.1.0
- Official development peer: PeerCastStation 7.1.1
- Discovery: disabled; isolated settings and dedicated ports

## Content-free entry observation

Both official builds sent the same HTTP/PCP shape:

- HTTP/1.0 GET for `/channel/{32-hex-id}`;
- `Host`, `User-Agent`, `x-peercast-pcp: 1`, and `x-peercast-pos: 0`;
- one HELO parent with `agnt`, `sid`, `ping`, and `ver` leaves;
- observed leaf lengths were 24, 16, 2, and 4 bytes respectively.

No request target identifier, session ID, IP address, metadata value, Atom
value, or raw packet payload was printed or persisted.

## Controlled downstream wire result

Each official build was instructed to receive a synthetic, process-local test
channel from Nova's loopback listener. Nova sent OLEH/OK and four packet-only
CHAN messages through the real bounded fan-out and downstream writer.

Stable 4.1.0:

```text
EndReason: FanoutStopped
HandshakeAccepted: True
CleanupCompleted: True
DeliveredPackets: 4
DeliveredBytes: 16
DiscardedPackets: 0
AcceptanceGate: Passed
```

Development 7.1.1:

```text
EndReason: FanoutStopped
HandshakeAccepted: True
CleanupCompleted: True
DeliveredPackets: 4
DeliveredBytes: 16
DiscardedPackets: 0
AcceptanceGate: Passed
```

All isolated processes and listeners were stopped after each run. The existing
port 7144 PeerCastStation process was not stopped or reconfigured.

## Automated validation

- Release build: zero warnings, zero errors
- Foundation tests: 1 passed
- Core tests: 16 passed
- PCP Protocol tests: 43 passed
- Network tests: 62 passed
- Total: 122 passed
- Network repetition gate: 10 consecutive runs / 620 executions passed
- TCP Transport self-test: passed
- PCP Framing self-test: passed

Coverage includes normal HELO/OLEH/OK, CHAN/QUIT, 503/OLEH/QUIT, wrong channel,
strict-ASCII rejection, handshake timeout, fan-out stop, deterministic write
timeout, cancellation-violating cleanup, capacity retention, and privacy-safe
result shape.

## Decision

Milestone 0.0.4b downstream PCP child wire/session is **Implemented** and its
Stable/development downstream wire interoperability is **Verified**.

The complete 0.0.4 Relay milestone remains in progress. A relay accept-loop
coordinator and continuous real-channel upstream-to-Nova-to-downstream
verification remain before declaring full Relay completion.
