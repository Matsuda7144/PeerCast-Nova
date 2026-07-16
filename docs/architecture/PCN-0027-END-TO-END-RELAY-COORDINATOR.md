# PCN-0027 End-to-End Relay Coordinator

## Purpose

This boundary joins the existing upstream and downstream implementations into
one owned, bounded relay lifecycle without moving media payload through Core.

```text
caller-owned ChannelStatusTracker
caller-owned upstream TcpTransportConnector
          |
          v
PcpRelayCoordinator
  -> PcpChannelSession
       -> one callback-scoped PcpChannelPacket
  -> PcpRelayFanout
       -> one bounded owned payload copy
       -> independent bounded child queues
  -> PcpDownstreamRelayServer
       -> accepted PcpDownstreamChildSession(s)
       -> reusable CHAN prefix + shared payload write
```

## Start and ownership

The coordinator constructs and owns the fan-out, upstream session, and
downstream server. It starts the downstream listener first, then starts the
upstream session. The Core tracker remains caller-owned and exposes immutable
status only. The connector is caller-owned because it is a stateless transport
factory that can be shared by the composition root.

The coordinator exposes the bound downstream endpoint and the downstream
server's privacy-safe live aggregate. It does not expose the upstream host,
channel/session identifiers, payloads, metadata, or exceptions.

## Packet path and reconnect

`PcpChannelSession` continues to own TCP/HTTP/PCP reconnects and passes one
packet at a time to the fan-out. The same fan-out and downstream server survive
an upstream reconnect. Therefore downstream children remain connected while
the next upstream request uses the last accepted Core position and packet
sequence continues monotonically.

The deterministic integration test proves:

- first upstream request position 0;
- one packet accepted at position 100;
- second upstream request position 100;
- another packet accepted at position 200;
- one unchanged downstream child receives both packets in order.

## Finite drain

Fan-out pending counts already include queued and in-flight packets.
`WaitForIdleAsync` observes every known child after upstream consumption has
stopped. It returns true at idle, false at its finite timeout, and propagates
caller cancellation. The coordinator records the corresponding fixed drain
outcome.

Shutdown ordering after natural upstream termination is:

1. upstream task reaches terminal state, so no new packets are accepted;
2. wait up to `DrainTimeout` for known children to become idle;
3. stop and await the downstream server and all accepted sessions;
4. dispose the fan-out and release remaining bounded payload ownership.

Explicit stop or caller cancellation skips step 2 and closes both network
boundaries immediately.

## Failure boundary

An upstream exception is reduced to `UpstreamFailed`; the Core tracker retains
only its fixed category, stage, and attempt counts. Listener or downstream
internal failure is reduced to `DownstreamFailed`. A coordinator-owned failure
is reduced to `InternalFailure`. Cleanup still runs for every branch and the
terminal result reports whether awaited shutdown completed.

## Remaining verification

Synthetic upstream wire through Nova to both official downstream build lines
is verified. Full 0.0.4 still requires a controlled real media channel from an
official upstream. Packet-type or bootstrap semantics that were not observed
from such a controlled channel are not guessed.
