# PCN-0485: Bounded ISO BMFF Regular Movie Demux Composition

## Scope reviewed

- one finite movie composition over caller-ordered regular-track requests and a
  common file span;
- exact same-movie sequence-set projection and caller-explicit AVC/AAC stream
  selection;
- bounded cross-track merge traversal with caller-selected equal-time
  tie-break, stable stream indexes, and explicit exhaustion or truncation;
- owned compressed packets and first-failure rejection without partial output.

## Verification

- focused managed tests cover exact video/audio source binding, deterministic
  equal-time ordering, owned payloads after caller-buffer mutation, bounded
  truncation with exact final cursor state, track limits, track-composition
  failure, movie-identity mismatch, and invalid media-kind selection;
- no runtime loading, native invocation, scheduling, output, or physical
  execution is required.

## Result

Accepted as the regular MP4 movie-level demux ownership slice defined by
ADR-0475.
