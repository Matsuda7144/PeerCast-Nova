# PCN-0191: Bounded ISO BMFF Sample-description Entry List

## Status

Implemented and verified as a non-physical parser slice.

## Implemented boundary

- exact complete `stsd` box validation;
- FullBox version 0 with zero flags;
- default 16-entry and hard 256-entry bounds;
- exact bounded box traversal for each sample entry;
- read-only complete-box-relative entry locations;
- declared-count and payload-end agreement;
- no entry-payload access or ownership.

## Verification

Managed tests cover zero and multiple entries, entry types and offsets, extended
and extends-to-end headers, both invalid limits, entry saturation, fewer and
more entries than declared, truncated and undersized entries, unsupported
version, nonzero flags, wrong type, declared-size shortage, trailing input, and
truncated fixed payload.

No native runtime, output device, readiness capture, diagnostic, or physical
audio/video path was exercised.

## Remaining work

Visual/audio sample-entry fields, AVC/AAC configuration, remaining sample
tables, fragmented runs, media data, streaming accumulation, nesting policy,
and decoder composition remain unimplemented.
