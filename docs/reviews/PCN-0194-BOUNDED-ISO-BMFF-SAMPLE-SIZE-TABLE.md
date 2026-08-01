# PCN-0194: Bounded ISO BMFF Sample-size Table

## Status

Implemented and verified as a non-physical parser slice.

## Implemented boundary

- exact complete `stsz` box validation;
- FullBox version 0 with zero flags;
- allocation-free uniform-size projection for the full 32-bit sample count;
- default 4,096-entry and hard 65,536-entry individual-size bounds;
- exact uniform and individual payload validation;
- read-only individual-size projection; and
- exact total sample-byte projection.

## Verification

Managed tests cover individual and uniform forms, retained zero individual
sizes, maximum uniform size/count, empty tables, extended and extends-to-end
headers, both invalid limits, individual entry saturation, short and extra
individual bytes, unexpected uniform entry bytes, unsupported versions,
nonzero flags, wrong type, declared-size shortage, and trailing input.

No native runtime, output device, readiness capture, diagnostic, or physical
audio/video path was exercised.

## Remaining work

Sample-to-chunk mapping, chunk offsets, sample lookup, fragmented runs, media
data, and decoder composition remain unimplemented.
