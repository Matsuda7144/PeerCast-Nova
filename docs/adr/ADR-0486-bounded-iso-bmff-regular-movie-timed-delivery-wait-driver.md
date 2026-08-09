# ADR-0486: Bounded ISO BMFF Regular Movie Timed Delivery Wait Driver

## Status

Accepted as the finite caller-side wait driver around ADR-0485 timing
admission.

## Decision

Own one timing-enabled regular-movie scheduler delivery and drive exactly one
retained scheduler reservation until it reaches a non-wait result. A `Wait`
decision may be resumed only after a cancellation-aware delay and only while
all caller-selected limits remain valid: a positive minimum delay, a maximum
single delay, a maximum cumulative wait, and a maximum decision count.

Defaults allow four decisions, a `1 ms` minimum delay, a `250 ms` maximum
single delay, and a `500 ms` cumulative wait. Hard limits are sixteen
decisions, one second per delay, and two seconds cumulative. Invalid options
are rejected before ownership is exposed. A wait at the final permitted
decision, an out-of-range delay, or a cumulative-limit breach returns
`WaitLimitReached`, faults the aggregate, and closes its delivery owner rather
than retaining an indefinitely re-drivable loop.

Cancellation during the delay preserves the exact scheduler reservation and
leaves the driver accepting a later call. Transfer is rejected while a drive
is active. Every non-wait delivery result remains distinct: delivered video or
audio, dropped video, empty, audio recovery required, clock not started, reset
required, and cancellation. Unexpected delivery results fail closed.

## Consequences

ADR-0485 `RetainedWait` is now actionable through one finite managed owner
without reconstructing, re-enqueuing, or duplicating a decoded frame. Snapshot
facts expose the last decision count, wait count, cumulative wait, active
state, and underlying delivery state.

## Non-goals

Timing reset, output-generation reset, audio recovery, device-loss recovery,
continuous application pumping, actual output activation, native runtime
execution, readiness capture, and physical playback are not included.
