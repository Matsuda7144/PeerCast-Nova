# PCN-0030 Fault-Resilient Relay Accounting Validation

## Scope

This report records Milestone 0.0.4e deterministic validation of coordinator
ingress accounting. The change is diagnostic and does not add or infer PCP
packet-type, media-header, or bootstrap semantics.

## Fault-path result

The controlled loopback upstream completed HTTP/PCP negotiation, sent one
complete CHAN packet with a two-byte payload, then left the next atom header
incomplete and closed. A connected downstream peer received the complete
packet before the upstream task faulted.

The terminal result was:

```text
EndReason: UpstreamFailed
DrainOutcome: Completed
UpstreamResult: absent
IngressAcceptedPackets: 1
IngressAcceptedBytes: 2
DownstreamDeliveredPackets: 1
DownstreamDeliveredBytes: 2
ShutdownCompleted: True
```

This proves the fixed ingress aggregate survives the exact path where the
upstream session cannot return its own counters. The complete packet drains
before downstream shutdown and no exception or peer content enters the
result.

## Normal and reconnect comparison

The existing continuous-wire case reports ingress 2 packets / 3 bytes, equal
to the normal upstream result and downstream delivery. The reconnect case
reports ingress 2 packets / 2 bytes across two upstream connections while one
downstream child remains connected.

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
- `dotnet format --verify-no-changes`: passed

## Decision

Milestone 0.0.4e fault-resilient relay accounting is **Implemented** and its
normal, reconnect, and upstream-failure paths are **Verified**.

The complete 0.0.4 Relay milestone remains in progress until a controlled real
media channel passes from an official upstream through Nova to a downstream
peer without guessed or copied protocol semantics.
