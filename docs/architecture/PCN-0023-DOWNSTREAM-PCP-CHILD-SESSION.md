# PCN-0023 Downstream PCP Child Session

## Purpose

This boundary connects the 0.0.4a in-process relay fan-out to one legacy PCP
downstream peer without moving payload bytes through Core.

```text
caller-owned TcpTransportListener
  -> accepted TcpTransportConnection
     -> PcpDownstreamChildSession (connection owner)
        -> bounded HTTP request
        -> atomic fan-out child reservation
        -> HELO validation / OLEH + OK
        -> handshake gate opens
        -> sequential bounded CHAN writes

PcpRelayFanout (caller owned)
  -> per-child packet/byte queue
     -> one shared owned payload
        -> reusable 72-byte CHAN prefix write
        -> shared payload write
```

## Independently observed client entry

Isolated official Stable 4.1.0 and development 7.1.1 clients produced the same
content-free shape:

```text
GET /channel/{redacted} HTTP/1.0
Host: present
User-Agent: present
x-peercast-pcp: 1
x-peercast-pos: 0

helo
  agnt  24-byte leaf
  sid   16-byte leaf
  ping  2-byte leaf
  ver   4-byte leaf
```

Only the request shape, header names, Atom names, parent/leaf structure, and
lengths were retained. Identifiers, endpoints, metadata, and Atom values were
not persisted.

## Ownership

- The caller owns and stops the listener.
- The session owns exactly one accepted connection and its PipeReader.
- The caller owns the fan-out and upstream channel session.
- The session owns its fan-out child registration after a successful slot
  reservation.
- The fan-out owns queued shared-payload references until each callback really
  completes.
- Core owns no socket, Pipe, queue, or payload introduced by this path.

## Backpressure and ordering

The fan-out offers packets sequentially but each child has its own worker. A
child packet count includes the packet currently being written. A packet that
arrives during HTTP/PCP negotiation waits behind the handshake gate while still
counting against the child bounds.

The CHAN prefix is fixed-size and reused per connection. The payload write uses
the callback-scoped shared memory directly and must finish before `SendAsync`
returns. A single finite timeout covers both prefix and payload writes.

## Termination

The accepted session finishes when any of these occurs:

- remote QUIT;
- clean or failed connection end;
- malformed bounded control input;
- requested cancellation;
- fan-out shutdown;
- slow-child isolation;
- packet write timeout or failure.

The receive monitor is canceled when the child worker ends, so a fan-out stop
does not leave a session waiting indefinitely for remote input. Connection
closure follows a finite child-stop wait. A cancellation-violating writer is
not force-released from its payload; it remains in one bounded fan-out slot and
the result reports incomplete cleanup.

## Scope boundary

0.0.4b implements and verifies one downstream wire/session. A long-running
relay service still needs an owning accept-loop coordinator and a controlled
continuous upstream-to-Nova-to-downstream real-channel verification before the
whole 0.0.4 Relay milestone is declared complete.
