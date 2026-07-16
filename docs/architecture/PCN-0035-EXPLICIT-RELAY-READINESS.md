# PCN-0035 Explicit Relay Readiness

## Ownership sequence

```text
caller
  -> PcpRelayCoordinator.RunAsync
       -> PcpDownstreamRelayServer.RunAsync
            -> TcpTransportListener.Start / bind
       -> readiness = Ready
       -> PcpChannelSession.RunAsync upstream

one-command gate
  -> owned relay job
       -> waits for coordinator readiness
       -> emits fixed Ready line
  -> foreground downstream probe only after Ready
  -> awaits both fixed terminal gates
  -> stops/removes only the owned relay job
```

`Ready` is published after bind and before any downstream connection is
required. It makes no claim that the upstream handshake has completed or that
any packet exists. Those facts remain part of the terminal acceptance gate.

## Terminal readiness outcomes

- `Ready`: the coordinator-owned downstream listener bound successfully;
- `Stopped`: stop/cancellation/disposal completed the contract before bind;
- `Failed`: startup or internal termination occurred before successful bind.

The result contains no endpoint, channel/session ID, exception, peer text,
metadata, position, or payload. The caller already owns any configured
endpoint and does not receive it back through readiness.

## Cancellation boundary

The readiness wait uses `Task.WaitAsync` with the observer's token. Canceling
that token does not cancel the shared readiness source or coordinator. The
coordinator continues to be owned by `RunAsync`, `RequestStop`, `StopAsync`,
caller cancellation, and `DisposeAsync` exactly as before.

The CLI intentionally waits with `CancellationToken.None`; its separate token
is already registered by `RunAsync`. This ensures Ctrl+C produces the fixed
`Stopped`/terminal summary instead of abandoning the result while readiness is
being completed.

## Orchestration boundary

The PowerShell gate observes only the exact fixed Ready line from the CLI. It
does not poll ports, scan listeners, enumerate channels, or inspect payload.
The readiness observation is bounded to 1 through 60 seconds. The downstream
session and relay window retain their existing finite limits, and relay-job
completion receives at most the configured window plus 15 seconds.
