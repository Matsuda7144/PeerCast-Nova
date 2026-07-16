# PCN-0025 Downstream Relay Server

## Purpose

This boundary owns long-running downstream acceptance while preserving the
existing separation between Core state and the media data plane.

```text
caller-owned upstream PcpChannelSession
  -> PcpRelayFanout (caller owned, bounded shared payload)
     -> PcpDownstreamRelayServer (listener/session owner)
        -> TcpTransportListener (bounded accepted connections)
        -> PcpDownstreamChildSession A
        -> PcpDownstreamChildSession B
        -> ... finite transport and fan-out limits
```

## Ownership

- The server owns the listener and every accepted transport connection through
  its child session.
- Each child session owns HTTP/PCP negotiation, fan-out registration, control
  input, bounded packet writes, and connection cleanup.
- The caller owns the fan-out and upstream session; both remain outside Core.
- Core owns only immutable channel status. It owns no listener, connection,
  Pipe, queue, or payload from this path.

## Capacity boundaries

`TcpTransportOptions.MaxConcurrentConnections` bounds accepted transports.
`PcpRelayFanoutOptions.MaxChildren` separately bounds peers that can receive
packets. A TCP-accepted peer that reaches a full fan-out follows the bounded
503/OLEH/QUIT path and then releases its connection slot.

The fan-out continues to copy each upstream payload once. Every accepted child
gets an independent packet/byte-bounded queue and writes the shared payload
directly after its reusable CHAN prefix.

## Lifecycle

The server is single-use:

```text
Created -> Running -> Stopping -> Stopped
```

`RunAsync` starts listening before it returns. Caller cancellation has the
fixed result `CallerCanceled`; explicit stop has `Stopped`; an unexpected
listener failure has `AcceptFailed`. All terminal paths stop pending accepts,
cancel child work, await tracked sessions, and publish a final snapshot.

`RequestStop` is safe for callbacks that must not wait. Owners that require
completion await `StopAsync` or the `RunAsync` task. The caller disposes the
fan-out only after that completion so queued shared memory remains valid until
the last child exits.

## Observable status

The live and terminal snapshots expose only lifecycle state and aggregate
counts. Child results are folded into fixed termination categories and numeric
delivery/discard totals. Network addresses, channel/session identifiers,
agents, metadata, positions, payloads, exceptions, and free-form text are not
properties of the snapshot.

## Verification boundary

Deterministic tests cover two simultaneous children, server stop during an
incomplete HTTP request, a relay-full second peer, single-use lifecycle,
caller cancellation, aggregate delivery counts, and privacy-safe result shape.

Official Stable 4.1.0 and development 7.1.1 each connected through the server,
completed HELO/OLEH/OK, received four synthetic CHAN packets / 16 bytes with no
discard, and completed cleanup. This verifies the listener/session boundary,
not yet a continuous real-channel upstream-to-downstream relay.
