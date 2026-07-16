# PCN-0026 Two-Build Relay Server Interoperability

## Scope

This report records bounded accept-coordinator validation for Milestone
0.0.4c. It verifies the listener, accepted-session, fan-out, wire-write, and
cleanup path. It does not claim continuous real-channel upstream-to-downstream
relay completion.

## Environment

- Date: 2026-07-14
- Platform: Windows IPv4 loopback
- .NET SDK: 10.0.301
- Official Stable peer: PeerCastStation 4.1.0
- Official development peer: PeerCastStation 7.1.1
- Discovery: disabled; isolated settings and dedicated ports
- Existing port 7144 process: not stopped or reconfigured

## Controlled coordinator result

Each official build was instructed to receive a synthetic, process-local test
channel from Nova's bounded downstream relay server. Nova accepted the
connection, completed the independently implemented HTTP/PCP child handshake,
and sent four packet-only CHAN messages through the real fan-out and writer.

Stable 4.1.0:

```text
ServerEndReason: Stopped
AcceptedConnections: 1
AcceptedHandshakes: 1
CompletedConnections: 1
DeliveredPackets: 4
DeliveredBytes: 16
DiscardedPackets: 0
CleanupIncomplete: 0
InternalFailures: 0
AcceptanceGate: Passed
```

Development 7.1.1 produced the identical fixed-field result.

No channel/session identifier, request target, address, agent, metadata,
position, exception, Atom value, or payload content was printed or persisted.
All isolated listeners and processes were stopped after each run.

## Automated validation

- Release build: zero warnings, zero errors through `build.ps1`
- Foundation tests: 1 passed
- Core tests: 16 passed
- PCP Protocol tests: 43 passed
- Network tests: 67 passed
- Total: 127 passed
- Network repetition gate: 10 consecutive runs / 670 executions passed
- TCP Transport self-test: passed
- PCP Framing self-test: passed

Coordinator-specific coverage includes two concurrent accepted children,
server stop while a child is waiting for HTTP, independent TCP/fan-out capacity,
503 relay-full completion, caller cancellation, single-use lifecycle, aggregate
delivery/discard counts, cleanup accounting, and privacy-safe result shape.

## Decision

Milestone 0.0.4c bounded downstream relay server is **Implemented** and its
Stable/development interoperability is **Verified**.

The complete 0.0.4 Relay milestone remains in progress. Continuous
real-channel upstream-to-Nova-to-downstream integration and its position and
shutdown evidence remain before declaring full Relay completion.
