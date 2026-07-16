# ADR-0029 Bounded Downstream PCP Child

## Status

Accepted for Milestone 0.0.4b.

## Context

Milestone 0.0.4a established a bounded in-process fan-out, but it deliberately
did not own accepted TCP connections or serialize packets for legacy peers. A
downstream peer can be slow before or after PCP negotiation, can send malformed
HTTP or Atom input, can disconnect during a write, or can ignore cancellation.
Those cases must not block the upstream channel session or retain unbounded
payload memory.

The downstream request and HELO shape were observed as black-box behavior from
isolated official Stable 4.1.0 and development 7.1.1 binaries. No
PeerCastStation implementation source was used as an implementation basis.

## Decision

`PcpDownstreamChildSession` becomes the sole owner of one accepted
`TcpTransportConnection`. The caller continues to own the listener and
`PcpRelayFanout`.

The session reads one strict-ASCII, size-limited HTTP request from the same
`PipeReader` later used for PCP control input. It accepts the independently
observed `GET /channel/{32-hex-id}` form, PCP version 1, and a finite numeric
position. A wrong channel receives 404 and malformed input receives 400.

For a valid request, the session atomically reserves one fan-out child slot
before sending HTTP 200. Packet delivery is gated until HELO validation and the
OLEH/OK reply complete. Packets offered during negotiation remain subject to
the existing per-child packet and byte bounds. If no child slot is available,
the session sends HTTP 503, validates HELO, then sends OLEH and QUIT without
creating a child queue.

HELO requires one leaf session ID. Agent, servant version, open port, and ping
port are optional. Duplicate known fields, parent fields, and simultaneous
`port` and `ping` are rejected; unknown fields are ignored. OLEH contains only
Nova's agent, local session ID, and servant version. OK uses code 1. The
relay-full QUIT uses a generic zero code without assigning it a legacy semantic
meaning; relay-full classification is the HTTP 503 plus QUIT sequence.

Each CHAN packet uses a reusable 72-byte prefix containing `chan/id/pkt` and
`type/pos/data` headers. The payload remains in the fan-out's shared bounded
memory and is written as a second operation, so serialization does not create a
second payload-sized copy. One child worker preserves packet order. The total
prefix-plus-payload write has a finite deadline.

The session concurrently monitors bounded inbound control Atoms and the child
worker's real completion. Remote QUIT, connection end, fan-out stop, slow-child
isolation, write timeout, and write failure produce fixed end reasons. The
terminal result contains only fixed enums, booleans, HTTP status, and numeric
counters. It contains no endpoint, channel/session identifier, agent,
metadata, payload, position, exception, or free-form peer text.

Cleanup first requests cooperative child stop, then closes the owned connection
and makes one more finite wait. If a writer violates cancellation, the session
returns with `CleanupCompleted=false`; the fan-out retains that child's bounded
capacity slot and payload ownership until the callback really returns.

`x-peercast-pos` is validated and echoed as legacy negotiation metadata in
0.0.4b. There is no historical replay buffer yet; each CHAN packet's `pos` is
the authoritative wire position.

## Consequences

- Negotiation cannot race an unbounded or unreserved child queue.
- A slow or broken downstream connection cannot backpressure the upstream
  session indefinitely.
- Packet payload bytes are copied once at fan-out ingress, not again for CHAN
  framing.
- Listener accept-loop policy and the fan-out remain separate from one
  connection's protocol ownership.
- Historical replay and continuous real-channel relay integration remain later
  work; 0.0.4b proves the downstream wire/session boundary.
