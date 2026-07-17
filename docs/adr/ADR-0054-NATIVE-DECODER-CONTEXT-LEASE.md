# ADR-0054 Native Decoder Context Lease

## Status

Accepted for milestone 0.0.6e. Codec opening, decoding, backend activation,
and license review remain pending.

## Decision

The 0.0.6d gate now exposes a successful live gate lease. It contains the five
already-loaded verified modules and their required symbol addresses while
retaining the originating `VerifiedPlaybackDecoderRuntimeLease`. It does not
enumerate, search, re-hash, reopen, or reload the runtime.

The context factory may consume that live gate directly. A convenience entry
may acquire the gate from a live runtime lease, but it performs exactly one
gate load and transfers that same acquisition without another load. Only fixed
`PlaybackDecoderFfmpegCodecId` values cross the public boundary. Undefined
values fail before gate or native access.

For a supported codec ID the factory invokes only `avcodec_find_decoder` and
`avcodec_alloc_context3`. These calls are behind a replaceable internal native
API boundary. A successful result transfers ownership of the context, gate,
and runtime lease to `PlaybackDecoderFfmpegContextLease`. Disposal is
idempotent and ordered:

1. call `avcodec_free_context` once while the avcodec module is live;
2. unload all five modules once in reverse load order;
3. dispose the verified runtime and directory guards.

If decoder lookup, allocation, or a native call fails, the gate modules are
released in reverse and no child lease is returned. The caller retains the
runtime lease on failure. If a caller prematurely disposes the gate or runtime
after successful transfer, further context use is rejected with fixed text;
child disposal remains best-effort and privacy-safe.

Non-Windows entry returns the fixed `Unsupported` result before native access.
Results expose only fixed enums and the optional ownership object. Paths,
hashes, manifest values, native pointers, DLL content, symbol names, and native
error text are not diagnostics.

## Out of scope

- `avcodec_open2`;
- codec parameters, extradata, or codec option setup;
- packet send or frame receive;
- demux, audio resampling, or video scaling;
- FFmpeg redistribution or external dependency additions.
