# ADR-0028 Bounded Shared Relay Fan-Out

## Status

Accepted for Milestone 0.0.4a.

## Context

The 0.0.3 ingress boundary owns one packet only until
`IPcpChannelPacketConsumer.ConsumeAsync` returns. Relay children must continue
sending independently after that callback, but a slow child must not stall the
upstream session or an unrelated child. A generic broadcast queue would hide
ownership, byte growth, failure, and shutdown behavior.

## Decision

`PcpRelayFanout` is a Network data-plane consumer. For each sequential upstream
packet it makes one finite pooled copy, then shares that owned payload through
reference-counted child queue entries. Payload bytes never pass through Core.

Each child has independent finite limits for pending packet count and pending
payload bytes. Pending includes the packet currently inside the child's
`SendAsync` callback. Reaching either limit stops that child with the fixed
reason `SlowConsumer`; the triggering packet is counted as discarded. The
fan-out continues offering the packet to other children and does not fault the
upstream session.

Each child has one sequential worker. A sink exception stops only that child
with `SinkFailed`. Owner removal and fan-out shutdown use separate fixed end
reasons. Status contains only fixed state/end enums and numeric pending,
delivered, and discarded counters. It contains no exception, text, endpoint,
identifier, metadata, position text, or payload content.

Child cancellation is requested away from the upstream callback. Waiting for a
child to stop has a finite deadline. A child that violates its cancellation
contract remains counted against the finite child limit until its callback
actually returns, so repeated broken children cannot accumulate without bound.
Payload memory held by such a child remains valid and bounded; it is never
forcibly reused.

The public relay packet is valid only during the child callback. It carries
sequence, numeric packet type, wire and optional monotonic position, payload,
and explicit discontinuity. It deliberately omits channel, peer, and session
identifiers.

## Consequences

- A slow or failed child cannot apply indefinite upstream backpressure.
- Default worst-case retained payload is bounded by child count multiplied by
  the per-child byte bound, plus one in-progress upstream copy. Shared payloads
  normally make actual ownership lower than that sum.
- Fan-out work is proportional to the finite child count, but payload bytes are
  copied once rather than once per child.
- Child completion order may differ, while packet order within each child is
  preserved.
- A cancellation-violating sink can occupy one bounded slot after the stop wait
  expires; forceful disposal would violate payload lifetime safety.
- The fan-out object is not the owner of the upstream session. Their common
  Network module must stop the session and dispose the fan-out.

## Scope

This ADR defines the 0.0.4a in-process relay data-plane boundary. It does not
implement downstream TCP acceptance, HTTP/PCP negotiation, CHAN serialization,
QUIT handling, or live one-hop interoperability. Those remain required before
Milestone 0.0.4 Relay is complete.

