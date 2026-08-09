# ADR-0477: Bounded ISO BMFF AVC Managed Decoder Preparation

## Status

Accepted as the AVC managed codec-parameter parity slice following ADR-0476.

## Decision

Resolve the exact selected AVC preparation input from ADR-0476 into an H.264
FFmpeg codec-parameter input using the parsed visual-entry width and height and
the owned `avcC` configuration record. Reject source-identity mismatch, empty
configuration, invalid dimensions, and pixel-count overflow before allocation.

Create an owned padded codec-parameter lease through the existing managed
factory. Retain an exact transfer plan requiring
`avcodec_parameters_alloc`, `avcodec_parameters_free`, `av_mallocz`, and
`avcodec_parameters_to_context`. Require caller-declared symbol coverage and a
caller-declared H.264 capability with bounded maximum width, height, and pixel
count. Bind that declaration without locating, loading, or invoking a runtime.
Dispose the lease through the managed preparation and through the aggregate
regular-movie preparation, including partial-failure cleanup.

## Consequences

Selected regular MP4 AVC and ordinary AAC now both reach owned managed
codec-parameter leases, exact transfer plans, required-symbol coverage, and
caller-declared decoder capability bindings. The AVC preparation retains the
exact demux composition and configuration input chain.

## Non-goals

Installed-runtime discovery or verification, ABI or symbol lookup, native
parameter transfer, decoder context allocation or open, packet submission,
scheduling, delivery, rendering, application integration, fragmented MP4, and
physical execution are not included.
