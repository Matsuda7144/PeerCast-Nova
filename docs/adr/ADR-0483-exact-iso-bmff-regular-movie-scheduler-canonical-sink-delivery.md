# ADR-0483: Exact ISO BMFF Regular Movie Scheduler Canonical Sink Delivery

## Status

Accepted as the one-reservation canonical sink-delivery boundary following
ADR-0482.

## Decision

Consume one exact ADR-0482 scheduler-ingress owner and borrow one canonical
decoded-frame sink. Reserve at most one bounded scheduler item per asynchronous
call, beginning with video preference and alternating preference after each
accepted delivery. Validate the reserved frame, sequence, discontinuity, and
selected session role before delivery. The production adapter selects the
matching H.264 or AAC context from the same regular-movie decoder session and
reuses the existing awaited FFmpeg managed-frame adaptation and conversion
boundary.

Allow only one active delivery call. Consume and dispose a reservation only
after canonical sink delivery reports `Delivered`. Cancellation retains the
exact reservation for retry without incrementing the delivered count or
changing the preferred role. Treat missing context, invalid frame, conversion,
sink, or integration rejection as terminal and dispose delivery operations
before the scheduler-ingress aggregate. Continue to expose ADR-0482 transfer
through the aggregate owner so decode ingress and bounded delivery share one
lifetime.

## Consequences

Regular MP4 decoded frames can now move from exact packet/decode progress,
through the bounded scheduler, into the existing canonical audio/video sink
contract with explicit retry and cleanup semantics. No output device is
activated by this boundary.

## Non-goals

Playback clock admission, late-frame policy, D3D11 or WASAPI processing,
Windows output lifecycle, application orchestration, runtime discovery or
loading, native execution during verification, and physical execution are not
included.
