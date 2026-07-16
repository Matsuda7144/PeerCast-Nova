# PCN-0031 Bounded Live Relay Diagnostic

## Executable path

```text
controlled upstream PeerCastStation
  -> PcpRelayCoordinator
       -> coordinator ingress accounting
       -> bounded PcpRelayFanout
       -> loopback PcpDownstreamRelayServer
  -> controlled downstream session
```

The CLI is a composition and diagnostics boundary. It does not add protocol
logic to Core, and payload remains callback-scoped inside Network. Core holds
only the existing immutable channel status and fixed fault snapshot.

## Lifecycle

1. Parse and validate all relay-specific arguments without opening a socket.
2. Construct one Core tracker and one owned relay coordinator.
3. Start the coordinator and print the fixed loopback listener and duration.
4. Wait for either coordinator termination, the finite diagnostic window, or
   Ctrl+C caller cancellation.
5. On window expiry, request prompt coordinator stop.
6. Await owned upstream, downstream, fan-out, and cleanup completion.
7. Print fixed aggregate evidence and return 0 only when the acceptance gate
   passes.

The diagnostic window is not the stream idle timeout. Both are finite and
configurable within bounded ranges. Explicit window stop skips natural-end
drain by design, matching the coordinator's prompt user-stop behavior.

## Resource limits

- downstream address: IPv4 loopback only;
- diagnostic window: 1,000 through 600,000 milliseconds;
- reconnects: 0 through 10;
- downstream TCP connections: 8;
- fan-out children: 4;
- pending queue per child: 16 packets / 4 MiB;
- handshake: 5 seconds;
- downstream write: 3 seconds;
- child stop and natural-end drain: 2 seconds;
- packet and atom limits: existing 1 MiB defaults.

## Evidence boundary

The acceptance gate establishes that complete non-empty packets crossed the
owned coordinator and at least one downstream session with no recorded
discard or incomplete cleanup. It does not inspect payload, name a media
container, classify packet types, or prove a header/bootstrap interpretation.

The same Channel ID must be supplied to the relay and downstream command, but
neither command prints it. Operators must still remove the command line from
published logs.
