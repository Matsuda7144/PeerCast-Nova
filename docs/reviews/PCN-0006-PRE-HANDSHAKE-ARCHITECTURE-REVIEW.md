# PCN-0006 Pre-Handshake Architecture Review

## Status

Accepted for implementation before Milestone 0.0.2.

## Why this review exists

The initial skeleton was intentionally small, but several early assumptions would
be expensive to reverse after streaming begins. This review freezes only the
minimum boundaries needed before implementing sockets and compatibility behavior.

## Findings

### 1. Core must not relay media payload bytes

The original event-flow diagram suggested that every event passed through Core.
That is appropriate for commands, lifecycle, status, and user-visible errors.
It is not appropriate for continuous media bytes.

Routing stream payloads through a generic event bus would:

- add avoidable allocations and dispatch overhead;
- blur ownership and backpressure;
- make slow consumers difficult to isolate;
- turn Core into a throughput bottleneck.

### 2. Separate the control plane and data plane

The control plane handles:

- application lifecycle;
- channel selection;
- connect and disconnect commands;
- status snapshots;
- faults and diagnostics.

The data plane handles:

- received stream bytes;
- framing and stream positions;
- relay fan-out;
- player input;
- recorder input;
- bounded buffering and backpressure.

Core owns orchestration, not media transport.

### 3. Player, recorder, and relay need different backpressure policies

A single broadcast abstraction is insufficient.

- A slow relay child may be disconnected to protect the upstream session.
- A player may use a bounded latency buffer and skip forward under policy.
- A recorder must never silently drop bytes. It must spool, stop with a clear
  fault, or explicitly mark a discontinuity.

These policies will be represented separately when the data plane is introduced.

### 4. Upstream reconnect must not automatically destroy local consumers

Player and recorder sessions should attach to a stable local stream session.
A brief upstream reconnect may produce an explicit discontinuity marker while
preserving the local session when compatibility allows it.

### 5. Compatibility is the MVP protocol goal

Version 0.1.0 will interoperate with existing PeerCast/PeerCastStation behavior.
It will not introduce a mandatory Nova wire protocol.

Any later extension must be:

- capability-negotiated;
- optional;
- safe for legacy peers to ignore or reject;
- documented with independent interoperability tests.

### 6. Protocol code must be independent from socket code

`Nova.Protocol.Pcp` owns deterministic parsing and serialization.

It must not depend on:

- sockets;
- UI;
- logging;
- application lifecycle;
- media decoders.

`Nova.Network` owns transport and sessions and consumes the protocol codec.

### 7. Avoid a premature catch-all shared project

`Nova.Common` was removed. Types remain with their owning module until a stable,
truly shared abstraction exists. This avoids an unbounded dependency bucket.

## Immediate decision

Proceed in three sub-milestones:

1. **0.0.2a Transport** — cancellable TCP connect, accept, timeout, and shutdown.
2. **0.0.2b PCP framing** — incremental preamble/atom parsing with fixtures.
3. **0.0.2c Compatibility** — integration test against an actual
   PeerCastStation instance.

No full handshake state machine will be declared complete before 0.0.2c.
