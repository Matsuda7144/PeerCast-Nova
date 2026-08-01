# PCN-0186: Bounded ISO BMFF FullBox-header Parser

## Status

Implemented and verified as a non-physical parser prerequisite.

## Implemented boundary

- fixed four-byte FullBox prefix;
- unsigned version projection;
- unsigned big-endian 24-bit flags projection;
- incremental `NeedMoreData`;
- no trailing-payload ownership or projection.

## Verification

Managed tests cover zero and maximum values, exact 24-bit byte order, every
truncated prefix length, and unchanged results in the presence of trailing
payload.

No native runtime, output device, readiness capture, diagnostic, or physical
audio/video path was exercised.

## Remaining work

Each recognized FullBox still needs an explicit type, length, supported-version,
flags, and payload policy. Movie/sample metadata, fragmented runs, media data,
and decoder composition remain unimplemented.
