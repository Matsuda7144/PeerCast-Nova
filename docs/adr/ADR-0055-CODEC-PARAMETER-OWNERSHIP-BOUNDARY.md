# ADR-0055 Codec Parameter Ownership Boundary

## Status

Accepted for milestone 0.0.6f. Native parameter transfer, codec opening,
decoding, backend activation, and license review remain pending.

## Decision

Before any FFmpeg context mutation, Nova creates one managed ownership lease
for the codec parameters and codec-specific extradata. The boundary accepts
only the fixed H.264 and AAC codec identifiers introduced in 0.0.6e.
Undefined identifiers fail closed before allocation.

The input shape is fixed and codec-specific:

- H.264 requires bounded positive width and height and requires all audio
  fields to remain zero.
- AAC requires bounded sample rate and channel count and requires all video
  fields to remain zero.

Negative values, zero values where a positive value is required, values above
the named limits, and mixed audio/video fields fail closed. Validation is
entirely managed and therefore has identical behavior on Windows and Ubuntu.
No native module, function pointer, or decoder context is accessed.

Extradata must contain at least one byte and may contain no more than 1 MiB.
On success Nova copies the caller memory into an owner-controlled array and
appends exactly 64 zero bytes for the FFmpeg input-buffer padding contract.
The caller memory is never retained, so later caller mutation cannot affect the
owned parameters. The padded array remains callback-scoped inside the lease
and is cleared during idempotent disposal. Use after disposal is rejected with
fixed text.

Public results expose only fixed enums, bounded numeric metadata, the original
extradata length, and the optional ownership lease. Extradata content, padded
bytes, native pointers, paths, hashes, and native error text are not diagnostics.

## Fixed limits

- extradata length: 1 through 1,048,576 bytes;
- FFmpeg zero padding: exactly 64 bytes;
- video width and height: 1 through 16,384;
- video pixel count: at most 268,435,456;
- audio sample rate: 8,000 through 384,000 Hz;
- audio channel count: 1 through 32.

## Out of scope

- `avcodec_parameters_to_context`;
- direct `AVCodecContext` field writes;
- `avcodec_open2`;
- packet send or frame receive;
- demux, audio resampling, or video scaling;
- FFmpeg redistribution or external dependency additions.
