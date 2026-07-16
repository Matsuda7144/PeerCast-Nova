# PCN-0019 Privacy-Safe Channel Faults

## Purpose

This document defines the observable fault boundary between the owning Network
channel session, Core state, diagnostics, and a future UI. It extends the
payload-free Core snapshot model without moving exception or remote data into
Core.

## Model

```text
private Network outcome or exception
  -> fixed category + fixed stage + finite attempt counts
     -> ChannelStatusTracker.RecordFault
        -> one atomic Core revision: Faulted + ChannelFaultSnapshot
           -> localized UI or fixed-field diagnostic output
```

`ChannelFaultSnapshot` contains exactly category, stage, attempt count, and
reconnect count. Counts must describe a real attempt: attempt is at least one,
and reconnects are non-negative and less than attempts.

## Fixed taxonomy

| Category | Meaning at the public boundary |
| --- | --- |
| `ConnectionFailed` | The finite upstream connection or input ended before a usable terminal result. |
| `TimedOut` | A configured connect, negotiation, or receive deadline expired. |
| `RemoteUnavailable` | The peer reported a temporary unavailable condition such as relay full. |
| `RemoteRejected` | The peer rejected HTTP or PCP negotiation. |
| `RemoteEnded` | An established peer ended or quit the channel session. |
| `ProtocolViolation` | Received data violated the bounded HTTP/PCP model. |
| `ConsumerFailed` | The awaited local packet consumer failed. |
| `ResourceLimitExceeded` | A configured size, depth, or count limit was exceeded. |
| `InternalFailure` | A local failure did not match a more specific fixed category. |

Stages are `Connecting`, `Negotiating`, `Receiving`, and `Consuming`. They
describe only the local operation stage and never include an endpoint or Atom
name.

## Lifecycle

- `RecordFault` is the only path into `Faulted`.
- State and fault are published together in one immutable revision.
- Repeating the same fault is idempotent.
- A different terminal fault cannot replace an existing one in the same
  attempt lifecycle.
- `Stopping` and `Stopped` retain the last fault for observation.
- `Reconnecting` clears the prior fault before a new attempt.
- User-requested and consumer-requested cooperative stops remain fault-free.

## Network mapping

`PcpChannelSession` performs mapping at the boundary that owns the private
exception and protocol result. Terminal handshake outcomes map to negotiating;
receive QUIT or clean end maps to receiving; consumer exceptions map to
consuming. Timeout uses the active stage. Bounded PCP size/depth/count failures
map to resource limits, malformed PCP/HTTP maps to protocol violation, and
socket or I/O failure maps to connection failure.

Retryable failures are kept private while another finite attempt remains. When
the retry budget is exhausted, the final fixed fault and total attempt counts
are published before the original exception is rethrown to the owner.

## Privacy and UI rules

The observable fault must never contain or derive display text from:

- channel, peer, or session identifiers;
- IP addresses, host names, ports, or URLs;
- channel or track metadata;
- payload bytes or payload-derived text;
- stream positions or packet types;
- HTTP/PCP numeric codes or Atom names;
- exception type names, messages, stack traces, or inner exceptions.

CLI output uses fixed labels and invariant integer formatting. A future UI maps
the enums to local resource strings. Unknown enum values are rejected at the
Core constructor rather than rendered as external text.

## Verification

The Core suite reflects over the snapshot to prevent string or exception
properties and checks all lifecycle invariants. The Network suite verifies
remote QUIT, relay full, handshake timeout, malformed channel data, consumer
failure, retry exhaustion, explicit stop, and callback-requested stop. The
official two-build packet sessions complete normally and therefore publish no
fault.

