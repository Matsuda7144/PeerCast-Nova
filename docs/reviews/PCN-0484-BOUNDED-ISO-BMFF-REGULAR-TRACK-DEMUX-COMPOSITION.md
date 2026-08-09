# PCN-0484: Bounded ISO BMFF Regular Track Demux Composition

## Scope reviewed

- one finite regular-track composition over the existing exact managed chain;
- caller-bounded sample planning and admission selection with explicit
  truncation;
- owned AVC/AAC compressed payloads and exact track/movie source retention;
- caller-explicit AVC payload requirements and first-failure rejection without
  partial output.

## Verification

- focused managed tests cover AAC and AVC composition, exact source identity,
  bounded-window truncation, payload ownership after caller-buffer mutation,
  invalid configuration, missing media data, codec-policy mismatch, and missing
  track identity;
- no runtime loading, native invocation, scheduling, output, or physical
  execution is required.

## Result

Accepted as the first production-oriented regular MP4 managed composition slice
defined by ADR-0474.
