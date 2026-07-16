# PCN-0013 Channel Architecture Review

## Status

Accepted for Milestone 0.0.3a Channel Control Foundation.

This review established the boundary completed across 0.0.3b through 0.0.3f.
The controlled wire-shape observation, semantic CHAN decoder, Network-to-Core
mapping, bounded live data-plane session, privacy-safe fault status, and
official two-build packet-session validation are complete. Milestone 0.0.3 is
Verified.

## Boundary decision

### Core owns the channel control plane

`Nova.Core.Channels` owns:

- immutable channel and local-session identities;
- explicit local-session lifecycle state;
- immutable metadata and status snapshots;
- monotonic snapshot revision and stream position rules.

The local session identity is stable across an upstream reconnect. It is not a
remote PCP servant session ID and it is not the channel ID.

### Protocol owns deterministic CHAN decoding

`Nova.Protocol.Pcp` owns bounded interpretation of CHAN control atoms. It
must return protocol values rather than mutate Core state. The mapping adapter
belongs at the Network boundary.

No PeerCastStation implementation code is used as a source for this mapping.
The parser will be based on controlled wire behavior and independently recorded
fixtures.

### Network owns live sessions and the data plane

`Nova.Network` owns sockets, cancellation, timeouts, reconnect attempts, and the
future bounded media path. It applies decoded protocol updates to the Core
status tracker.

The Core tracker owns no socket, Pipe, buffer, payload, cancellation source, or
other disposable resource. Stopping a real session remains a Network
responsibility. `NovaApplication` applies a finite per-module stop deadline,
passes cancellation into the module, logs a timeout, and continues best-effort
reverse-order shutdown. A Network module must still close its own sockets and
Pipes when cancellation is requested.

## State model

```text
Created -> Connecting -> Negotiating -> Receiving
              |              |             |
              +--------------+-------------+-> Reconnecting -> Negotiating
              |              |             |         |
              +--------------+-------------+---------+-> Faulted

Created / Connecting / Negotiating / Receiving / Reconnecting / Faulted
    -> Stopping -> Stopped
```

Rules:

1. Repeating the current state is idempotent and does not create a new revision.
2. `Stopped` is terminal.
3. Metadata may change only after negotiation has started.
4. Stream position advances only while receiving and cannot move backwards.
5. An explicit discontinuity object, not an implicit position reset, represents
   backward or ambiguous packet-position observations at the Network boundary.

## Snapshot model

Each immutable snapshot contains:

- channel ID;
- stable local session ID;
- lifecycle state;
- decoded metadata;
- last observed stream position when known;
- monotonically increasing revision.

Snapshots deliberately contain no payload bytes and no `Exception`. The fixed
privacy-safe fault model completed in 0.0.3f adds only category, stage, attempt,
and reconnect counts as defined by ADR-0027 and PCN-0019.

Metadata fields are user-generated and must not be logged by default. The
future CHAN adapter must apply explicit string and atom resource limits before
constructing a Core metadata snapshot.

## Backpressure and shutdown consequences

This foundation creates no queues. The bounded relay policy was added later in
0.0.4a under ADR-0028; player and recorder still require their own policies as
required by ADR-0013. Upstream cancellation must propagate from the owning
Network session; Core status publication cannot be used as a shutdown signal.

Completed in 0.0.3b:

- content-free, bounded post-handshake CHAN structure observation;
- independent field/shape documentation in PCN-0014;
- malformed, unknown, duplicate, and resource-limit synthetic tests;
- incremental payload-nonretaining CHAN control sink in `Nova.Protocol.Pcp`.

Completed in 0.0.3c:

- partial metadata and current-track mapping in `Nova.Network`;
- atomic metadata/position publication to Core;
- 32-bit packet-position unwrap and explicit backward discontinuity reporting.

Completed in 0.0.3d:

- callback-scoped packet data unit with finite pooled ownership;
- top-level-Atom yield before following buffered messages;
- awaited consumer backpressure, cancellation, QUIT, and discontinuity paths.

Completed in 0.0.3e:

- one owner for TCP, HTTP, PCP handshake, PipeReader handoff, and CHAN stream;
- finite reconnect with stable local identity, Core position, and packet
  sequence;
- controlled PeerCastStation 6.0.0.0 live packet reception and cooperative
  shutdown with privacy-safe aggregate diagnostics.

Completed in 0.0.3f:

- fixed privacy-safe fault category, stage, attempt, and reconnect counts;
- atomic state/fault publication with stop persistence and reconnect clearing;
- official Stable 4.1.0 and development 7.1.1 finite live packet-session
  verification with sanitized evidence.

## 0.0.3a validation

- clean .NET 10 Release build, zero warnings and zero errors;
- 65 total xUnit tests passed, including 12 Core tests;
- TCP Transport and PCP Framing self-tests passed;
- `dotnet format --verify-no-changes` passed.
