# ADR-0027 Fixed Privacy-Safe Channel Faults

## Status

Accepted for Milestone 0.0.3f.

## Context

Channel sessions need an observable terminal fault for a future UI and for
bounded diagnostics. Passing exception text or remote values through Core
would make privacy, localization, compatibility, and stable API behavior depend
on untrusted external input.

The result must distinguish useful failure classes without exposing a channel
ID, local or remote session ID, endpoint, metadata, payload, stream position,
protocol code, exception, or free-form message.

## Decision

Core exposes one immutable `ChannelFaultSnapshot` containing only:

- a fixed `ChannelFaultCategory` enum;
- a fixed `ChannelFaultStage` enum;
- the one-based attempt count;
- the completed reconnect count.

The snapshot contains no `string`, `Exception`, identifier, endpoint,
metadata, payload, stream position, protocol code, or extensible property bag.
Network converts private operational exceptions and terminal protocol outcomes
to the fixed Core taxonomy. A UI localizes the enum values locally; it does not
render remote or exception text from this boundary.

`ChannelStatusTracker.RecordFault` publishes `Faulted` and the fault snapshot
in one revision. A direct transition to `Faulted` is prohibited so an observer
cannot receive a faulted state without its classification. The final fault is
retained through `Stopping` and `Stopped` for post-stop observation. An explicit
transition to `Reconnecting` clears the previous attempt's fault.

Requested or cooperative stop is not a fault. Repeated publication of the same
fault is idempotent; replacing a terminal fault without an explicit reconnect
is rejected.

## Consequences

- Observers receive a deterministic and localizable terminal classification.
- Logs and UI do not need exception text to explain the broad failure class.
- Detailed exceptions remain private to the owning Network boundary and may be
  handled by a separately governed developer diagnostic facility in the future.
- New observable categories require an intentional Core API change and tests.
- Fixed categories are less detailed than raw exceptions by design.

## Verification

Core tests enforce enum and count validity, atomic publication, idempotence,
stop persistence, reconnect clearing, and the absence of string or exception
properties. Network tests cover connection, timeout, remote outcome, protocol,
consumer, resource-limit, retry-exhaustion, and requested-stop behavior.

