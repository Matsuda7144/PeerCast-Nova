# PCN-0007 TCP Transport

## Status

Implemented for Milestone 0.0.2a.

## Scope

This milestone provides byte-oriented TCP primitives only.

Included:

- outbound connections by endpoint or hostname;
- finite connection timeout;
- caller cancellation;
- loopback or public-address listening;
- cancellable accept;
- deterministic listener stop;
- finite listen backlog;
- maximum concurrently accepted connections;
- cancellable read and write;
- idempotent connection disposal;
- Windows and Linux CI coverage.

Excluded:

- HTTP channel request;
- PCP handshake state machine;
- atom stream parsing;
- relay fan-out;
- reconnect policy;
- TLS;
- UPnP or NAT traversal.

## Ownership

`TcpTransportConnector` owns a `TcpClient` until it successfully returns a
`TcpTransportConnection`.

`TcpTransportListener` owns its listening socket and pending accept operations.
Each returned connection becomes the caller's responsibility.

`TcpTransportConnection` owns its connected socket and stream.

## Cancellation semantics

- A caller-canceled connect throws `OperationCanceledException`.
- A connect that exceeds `ConnectTimeout` throws `TimeoutException`.
- Stopping a listener cancels all pending accepts.
- Canceling a read or write does not implicitly dispose the connection.
- Disposing a connection attempts a full socket shutdown and then closes it.

## Connection limit

The listener acquires a slot before accepting a connection. When all slots are
occupied, additional accept operations wait while the operating-system backlog
remains finite.

The slot is released only when the accepted `TcpTransportConnection` is disposed.

This is transport-level protection, not a substitute for later per-IP,
per-channel, or protocol-level admission policies.

## Security notes

- All timeouts and buffer sizes are finite.
- No remote bytes are parsed in this milestone.
- Hostname resolution remains delegated to the .NET socket implementation.
- No credentials or personally identifying network information are persisted.
