# ADR-0056 Native Parameter Transfer Boundary

## Status

Accepted for milestone 0.0.6g. Codec opening, packet/frame processing,
demux, backend activation, and license review remain pending.

## Decision

Nova transfers the managed H.264 or AAC parameter lease from 0.0.6f into the
live native decoder context from 0.0.6e through one fixed, testable native
boundary. The codec identifiers must match before native access. Non-Windows
and non-64-bit processes return a fixed unsupported result.

The existing ABI/symbol gate is extended to require:

- `avcodec_parameters_alloc`;
- `avcodec_parameters_free`;
- `avcodec_parameters_to_context`;
- `av_mallocz`;
- `av_channel_layout_default`.

The transfer boundary allocates `AVCodecParameters`, allocates one zeroed
extradata buffer including the 64-byte padding already owned by Nova, fills
only the fixed H.264 or AAC fields, and calls
`avcodec_parameters_to_context`. Temporary native parameters are always freed.
The field layout is limited to the verified FFmpeg 8.1 / libavcodec 62 Windows
x64 ABI; other platforms and pointer sizes are rejected before native access.

On success, FFmpeg owns its independent context copy and the managed parameter
lease is cleared and disposed. Native allocation failure leaves both caller
leases available for a bounded retry. A rejected transfer or native exception
makes the context unusable, so Nova disposes the context, native modules, and
verified runtime in their existing order while retaining the managed parameter
lease. Missing symbols and codec mismatch fail before native mutation.

Public results contain only fixed enums. Extradata, padded bytes, native
pointers, paths, hashes, symbol addresses, and native error text are not
diagnostics.

## Out of scope

- `avcodec_open2`;
- codec options or threading configuration;
- packet send or frame receive;
- demux, audio resampling, or video scaling;
- FFmpeg redistribution or external dependency additions.
