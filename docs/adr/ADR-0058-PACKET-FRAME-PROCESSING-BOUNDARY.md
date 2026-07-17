# ADR-0058: Packet / Frame Processing Boundary

## Status

Accepted for Milestone 0.0.6i.

## Context

0.0.6h owns an opened H.264 or AAC `AVCodecContext`, but no encoded packet may
enter it and no decoded-frame state may be observed. Direct struct access would
couple Nova to unstable native layouts and widen the unsafe boundary.

## Decision

- Accept one non-empty encoded packet no larger than 4 MiB.
- Require a live, opened decoder context before any native call.
- Allocate a temporary `AVPacket` and padded native data buffer through verified
  FFmpeg symbols.
- Copy the managed packet once, append 64 zero bytes, and transfer buffer
  ownership with `av_packet_from_data`.
- Always free the packet; free the data directly only when ownership transfer did
  not succeed.
- Map `0`, `EAGAIN`, `AVERROR_EOF`, allocation failure, and other rejection to
  fixed results without exposing native values.
- Allocate a temporary `AVFrame` only for `avcodec_receive_frame`, report its
  bounded state, and free it on every path.
- Dispose the decoder context after an unexpected managed/native exception.

## Consequences

The open context can now participate in the FFmpeg send/receive state machine
without exposing `AVPacket` or `AVFrame` layouts. Frame metadata and payload are
not extracted, retained, converted, queued, rendered, or played in this
milestone.

## Out of scope

- flush packet submission;
- frame field or payload extraction;
- timestamp normalization;
- pixel/audio conversion;
- demux;
- decoded queues, renderer, audio device, and A/V synchronization;
- FFmpeg redistribution or backend activation approval.
