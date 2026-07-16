# ADR-0032 Fault-Resilient Relay Ingress Accounting

## Status

Accepted for Milestone 0.0.4e.

## Context

`PcpChannelSession` returns packet and payload-byte totals in its terminal
result. If the session faults after delivering complete packets, its task has
no terminal result and the coordinator deliberately reduces the exception to
the fixed `UpstreamFailed` reason. The completed packets can still have been
accepted by the fan-out and delivered downstream, but diagnostics would then
incorrectly appear to have observed zero upstream packets.

The coordinator needs fault-resilient evidence without retaining payload,
packet position, channel/session identity, endpoint, metadata, exception, or
peer-controlled text. Counting must not change the existing sequential
backpressure boundary or claim a packet that the fan-out rejected.

## Decision

The coordinator inserts a private ingress consumer between
`PcpChannelSession` and `PcpRelayFanout`. It awaits the fan-out first and only
then increments checked packet and payload-byte counters. Consequently, the
aggregate means "accepted by the bounded fan-out", not merely decoded from the
wire and not necessarily delivered to every downstream child.

The aggregate is exposed as `PcpRelayIngressSnapshot` both while the
coordinator is running and in `PcpRelayCoordinatorResult`. It contains only
`AcceptedPacketCount` and `AcceptedPayloadByteCount`. The upstream session's
existing result remains unchanged and continues to provide attempt,
reconnect, HTTP, and channel-message information when it completes normally.

No packet-type, bootstrap, header, or media-container semantics are added by
this decision.

## Consequences

- Packets accepted before an upstream exception remain visible in terminal
  diagnostics.
- Normal results provide an independently comparable ingress total.
- The downstream aggregate can be compared with ingress to explain fan-out
  timing or discard without retaining content.
- The packet path still has one awaited sequential ingress callback and one
  bounded fan-out payload copy.
- Full real-media interoperability remains a separate black-box verification
  gate.
