# ADR-0172: Opened-decoder Discontinuity Flush Boundary

## Status

Accepted as a post-0.0.10e non-physical native-boundary prerequisite.

## Context

The current production discontinuity behavior stops and disposes the old
decoder generation before creating a replacement. Earlier decisions deferred
`avcodec_flush_buffers` until its symbol ownership, context state, and failure
behavior could be reviewed independently.

FFmpeg exposes `avcodec_flush_buffers` for resetting an opened decoder's
internal buffered state. It is not an end-of-stream signal and does not replace
packet draining or generation lifecycle ownership.

## Decision

Add `avcodec_flush_buffers` to the exact required `avcodec-62.dll` ABI symbol
inventory.

Add a synchronous boundary that:

- accepts one owned `PlaybackDecoderFfmpegContextLease`;
- permits native access only while the context and ABI-symbol leases are held;
- requires the context to be opened;
- invokes the reviewed `avcodec_flush_buffers` function pointer exactly once;
- retains the opened context after a successful flush;
- returns fixed, privacy-safe result categories;
- rejects unsupported, disposed, unopened, or symbol-unavailable states before
  native access; and
- disposes the context after a native exception because its decoder state is
  no longer trusted.

The boundary is a capability only. It is not connected to production
discontinuity handling in this slice.

## Consequences

The native reset call now has an explicit ownership and failure boundary that
can be composed in a later lifecycle decision. Verified runtimes that omit
`avcodec_flush_buffers` fail ABI admission.

The existing stop-old-before-create-new production discontinuity behavior is
unchanged.

## Non-goals

No EOS behavior, packet drain, parser reset, generation reuse, multi-context
transaction, production composition change, physical run, A/V synchronization,
or output-device reset is included.
