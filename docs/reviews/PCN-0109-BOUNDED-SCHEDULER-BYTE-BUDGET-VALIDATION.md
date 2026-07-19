# PCN-0109: Bounded Scheduler Byte Budget Validation

## Scope

0.0.8q adds aggregate owned-byte accounting to `BoundedPlaybackScheduler`.
No decoder ingress or additional queue is introduced.

## Acceptance criteria

- Count capacity and byte capacity are independent.
- Plane buffer lengths use checked aggregation.
- Oversized input is disposed with `FrameTooLarge`.
- All three full policies operate under byte pressure.
- `DropOldest` may remove multiple unreserved frames.
- Reserved frames are protected from eviction.
- In-flight frames remain accounted until awaited processing ends.
- Every ownership-ending path releases byte accounting once.
- Snapshot byte usage reaches zero after ownership is fully released.
- Invalid byte options fail before scheduler operation.

## Compatibility

PCP, Channel, Relay, decoder activation, FFmpeg distribution, output devices,
and external dependencies are unchanged.

## Validation

CI #275 passed restore, Release build, the full test suite, and format
verification on Windows and Ubuntu.

Status: Verified.
