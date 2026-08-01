# PCN-0183: Bounded ISO BMFF Box-header Parser

## Status

Implemented and verified as a non-physical parser prerequisite.

## Implemented boundary

- fixed 8-byte ordinary header;
- fixed 16-byte extended-size header;
- zero-size extends-to-end representation;
- 24-byte ordinary and 32-byte extended `uuid` headers;
- exact unsigned big-endian fields;
- structural size validation;
- incremental `NeedMoreData`;
- no payload read, copy, allocation, or ownership.

## Verification

Managed tests cover ordinary headers without payload, sizes greater than
32 bits, extends-to-end boxes, both `uuid` forms, every truncated base and
extended-header boundary, missing user-type bytes, and undersized declarations.

No native runtime, output device, readiness capture, diagnostic, or physical
audio/video path was exercised.

## Remaining work

Container-bounded traversal, nesting, recognized box payloads, movie/sample
metadata, fragmented runs, media data, and decoder composition remain
unimplemented.
