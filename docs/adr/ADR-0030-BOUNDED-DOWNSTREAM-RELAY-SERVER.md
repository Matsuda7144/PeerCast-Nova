# ADR-0030 Bounded Downstream Relay Server

## Status

Accepted for Milestone 0.0.4c.

## Context

Milestone 0.0.4b established one bounded downstream PCP child session. The
remaining listener owner must accept multiple peers without transferring
socket, Pipe, queue, or payload ownership into Core. It must also stop pending
accepts and incomplete handshakes in finite time, isolate a failed child, and
avoid exposing peer-controlled or identifying values through diagnostics.

The listener must not own the upstream channel session or relay fan-out. Those
objects have a wider lifetime and must remain available until every accepted
child has completed cleanup.

## Decision

`PcpDownstreamRelayServer` owns one single-use `TcpTransportListener` and every
`PcpDownstreamChildSession` created from its accepted connections. The caller
owns the `PcpRelayFanout`, upstream channel session, channel identity, and
server lifetime. The fan-out must outlive the server.

`RunAsync` starts the listener synchronously, records the bound endpoint, and
runs one cancellable accept loop. Transport connection concurrency remains
bounded by `TcpTransportOptions`; PCP child capacity remains independently
bounded by `PcpRelayFanoutOptions`. A peer accepted by TCP can therefore
receive the established 503/OLEH/QUIT relay-full response without acquiring a
fan-out slot.

Each accepted connection is immediately assigned to an independently owned
child session. One child's protocol rejection, timeout, disconnect, slow
consumer, or write failure is recorded and cannot stop the accept loop or
another child. An unexpected internal child failure closes that connection
and increments a fixed counter without retaining the exception.

Server stop cancels the accept operation and all child operations, stops the
listener, and awaits every tracked child before completing. `RequestStop`
provides a nonblocking cancellation signal; `StopAsync`, the task returned by
`RunAsync`, and `DisposeAsync` provide awaited completion. The server is
single-use and has the explicit states Created, Running, Stopping, and Stopped.

The final result aggregates fixed enums and numeric counts only: accepted,
active and completed connections; accepted handshakes; fixed child end
reasons; cleanup failures; delivered and discarded packet/byte totals; and
internal failures. It contains no endpoint, identifier, agent, metadata,
payload, position, exception, or peer-controlled text.

The server does not dispose the caller-owned fan-out. Shutdown ordering is:

1. stop the downstream server and await all accepted children;
2. stop or dispose the fan-out;
3. stop the upstream channel session according to its owner.

## Consequences

- Pending accepts and incomplete handshakes cannot survive an awaited server
  stop.
- Multiple children share the existing bounded payload owner without moving
  payload through Core.
- TCP connection capacity and fan-out child capacity remain separate finite
  controls.
- Child failures remain isolated and externally observable only as fixed
  aggregate categories.
- Listener and session ownership is complete for 0.0.4c.
- Continuous real-channel upstream-to-Nova-to-downstream integration remains
  required before declaring the complete 0.0.4 Relay milestone verified.
