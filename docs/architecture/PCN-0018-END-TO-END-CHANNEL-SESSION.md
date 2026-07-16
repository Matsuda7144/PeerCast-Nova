# PCN-0018 End-to-End Channel Session

## Purpose

`PcpChannelSession` is the Network owner that connects the compatibility-tested
relay handshake to the bounded CHAN packet boundary. It does not add relay
fan-out, playback, recording, or UI state.

```text
stable local ChannelStatusTracker + packet consumer
  -> finite TCP connect
     -> HTTP relay request / response on one PipeReader
        -> HELO / OLEH / OK on the same PipeReader
           -> CHAN stream on the same PipeReader
              -> Core control snapshot
              -> await one callback-scoped packet consumer
  -> complete PipeReader
  -> dispose TCP connection
  -> terminal result or finite reconnect
```

## Identity and reconnect invariants

- The local `ChannelSessionId` sent in HELO is stable for every attempt.
- One `PcpChannelStateAdapter` preserves the accepted 64-bit stream position.
- The next relay request uses that Core position; the caller's initial position
  is used only until Core has accepted one.
- Packet sequence is monotonic across attempts and advances only after the
  consumer callback succeeds.
- Each reconnect owns a new TCP connection and PipeReader. Nothing from the old
  Pipe is retained.

## Finite failure policy

Connect timeout, socket/I/O failure, handshake timeout, stream idle timeout,
and truncated input may reconnect within the configured limit. The delay and
number of attempts are finite.

RelayFull, HTTP rejection, protocol rejection, malformed PCP, QUIT, and packet
consumer failure do not reconnect. A consumer exception is wrapped separately
from transport I/O so an `IOException` thrown by a recorder or player is never
mistaken for an upstream outage.

## Shutdown and backpressure

`RequestStop` only signals the owned cancellation source and is safe to call
inside `ConsumeAsync`. `StopAsync` additionally waits for the run to finish.
The session completes the PipeReader and disposes the connection on every
attempt. The packet consumer remains awaited, so no following buffered Atom is
decoded while that consumer is slow.

Consumers must return promptly on cancellation. The finite module-stop timeout
is the outer containment boundary for a broken consumer; it does not authorize
unsafe buffer reuse or forceful thread termination.

## Diagnostics and privacy

`PcpChannelSessionResult` contains only end reason, attempt/reconnect counts,
last observed HTTP status, CHAN count, packet count, and payload byte total. It
contains no channel or peer identifier, address, metadata, payload content,
position, or exception.

The live helper stops after a finite packet count or time deadline and prints
only these aggregates plus Core's final lifecycle state. A deadline returns
exit code 2 so automation does not classify a bounded timeout as packet
success.

## Verification

- Loopback coalescing writes OLEH, OK, CHAN, and QUIT together and proves that
  buffered post-OK data reaches the stream reader.
- Reconnect loopback verifies position, session ID, and packet sequence.
- Stop, RelayFull, consumer failure, initial position, idle timeout, and retry
  boundaries have deterministic tests.
- A controlled PeerCastStation 6.0.0.0 session returned HTTP 200, delivered four
  live packets through this boundary, and stopped cooperatively without logging
  identifiers, metadata, addresses, payload contents, or positions.

Official Stable 4.1.0 and current development 7.1.1 finite live packet sessions
were verified in 0.0.3f. The fixed privacy-safe observable fault model is
defined by ADR-0027 and PCN-0019; sanitized two-build evidence is recorded in
PCN-0020. These gates complete Milestone 0.0.3 Channel.
