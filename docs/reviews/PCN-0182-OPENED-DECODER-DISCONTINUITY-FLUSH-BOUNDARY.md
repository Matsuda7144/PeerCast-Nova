# PCN-0182: Opened-decoder Discontinuity Flush Boundary

## Status

Implemented and verified as a non-physical native-boundary prerequisite.

## Implemented boundary

- exact `avcodec_flush_buffers` ABI-symbol admission;
- opened-context requirement before native access;
- context and symbol lease ownership held across the call;
- one native flush per successful request;
- retained context after success;
- fail-closed context disposal after a native exception;
- fixed result categories without pointers or native error text.

## Verification

Managed tests cover H.264 and AAC success, unopened and disposed contexts,
unsupported platforms, missing symbols, native exceptions, ownership, cleanup,
and privacy-safe results. ABI-gate tests verify that the flush symbol is
mandatory and missing-symbol cleanup remains complete.

No FFmpeg runtime, output device, readiness capture, diagnostic, or physical
audio/video path was exercised.

## Remaining work

Production discontinuity still replaces the entire decoder generation. A later
decision must define whether and when one or both opened contexts may be
flushed and reused, how parser and queued-frame state participate, and how a
partial multi-context failure is handled. EOS remains separate.
