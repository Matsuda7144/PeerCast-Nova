# PCN-0021 Bounded Relay Fan-Out

## Purpose

This document defines the first 0.0.4 Relay boundary. It connects the existing
single-consumer upstream session to multiple independently bounded child sinks
without introducing downstream wire protocol behavior yet.

```text
PcpChannelSession / PcpChannelStreamReader
  -> await PcpRelayFanout.ConsumeAsync
     -> one pooled payload copy
        -> child A queue (packet + byte bound) -> sequential SendAsync
        -> child B queue (packet + byte bound) -> sequential SendAsync
        -> child C queue (packet + byte bound) -> sequential SendAsync
     -> release caller reference and return to upstream
```

Every accepted queue entry adds one reference to the shared payload. The caller
reference is released after all currently active children have been offered the
packet. A child releases its reference after `SendAsync` completes or when its
pending entry is discarded during shutdown.

## Queue and backpressure policy

- Upstream `ConsumeAsync` calls must remain sequential, matching the existing
  stream-reader contract.
- Child packet and byte limits are both positive, finite, and hard-capped.
- Pending counts include one in-flight callback, not only queued entries.
- A packet exceeding the remaining packet or byte capacity stops that child as
  `SlowConsumer` before enqueue.
- Other children still receive the same upstream packet.
- The fan-out never waits for ordinary child delivery; it waits only for the
  bounded shared copy and queue offers.
- There is no unbounded `Channel<T>`, generic event bus, or Core payload path.

## Child lifecycle

```text
Accepting
  -> Stopping (OwnerRemoved | FanoutStopped | SlowConsumer | SinkFailed)
     -> Stopped
```

The first stop reason wins. Queue overflow and sink failure remove the child
from active fan-out immediately. One asynchronous cancellation request is sent
to the child worker. `StopAsync` waits only for the configured finite duration
and reports whether cleanup completed.

A child ignoring cancellation stays in `Stopping`, retains any callback-scoped
payload safely, and continues to occupy one of the finite child slots. Once the
callback returns, pending owners are released, state becomes `Stopped`, and the
slot becomes available again.

## Observable status and privacy

`PcpRelayChildStatusSnapshot` exposes:

- fixed lifecycle state;
- fixed terminal reason;
- pending packet and byte counts;
- delivered packet and byte totals;
- discarded packet and byte totals.

Pending includes an in-flight send. Discarded counts include the packet that
caused a capacity stop and any accepted packets that did not complete delivery.
The snapshot contains no string, exception, address, channel/peer/session ID,
metadata, payload content, or protocol-derived display text.

## Failure isolation

A child exception is consumed by that child's worker and becomes
`SinkFailed`. It is never rethrown from the upstream fan-out callback. Allocation
or invariant failure while making the single shared copy is different: it is a
local fan-out failure and may terminate the owning upstream consumer path.

## Reconnect and discontinuity

The same fan-out instance can remain attached while `PcpChannelSession`
performs its finite upstream reconnects. Monotonic packet sequence continues as
defined by the session. An explicit `PcpStreamDiscontinuity` is copied into each
relay packet without changing or hiding it.

0.0.4a does not synthesize a discontinuity merely because a reconnect attempt
occurred. The downstream PCP writer must define whether and how a reconnect is
represented on the legacy wire before live relay verification.

## Remaining 0.0.4 work

- accept a bounded downstream TCP child;
- parse and answer the legacy HTTP/PCP child handshake;
- serialize bounded CHAN metadata and packet atoms;
- propagate downstream cancellation and QUIT;
- verify one-hop relay against controlled official PeerCastStation builds.

