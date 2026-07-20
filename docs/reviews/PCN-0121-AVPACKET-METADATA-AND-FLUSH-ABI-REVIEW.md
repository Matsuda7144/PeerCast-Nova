# PCN-0121: AVPacket Metadata and Flush ABI Review

## Scope

Validate the architecture decision for one future metadata-aware native packet and null-flush boundary after the source-bound opened decoder session completed in 0.0.9b.

This milestone changes documentation only. It does not add packet allocation, metadata writes, packet submission, frame receive, flush execution, drain, PCP composition, backend activation, FFmpeg distribution, or output wiring.

## Reviewed evidence

- The existing runtime trust chain pins five FFmpeg DLL names, sizes, SHA-256 identities, import policy, ABI majors, and required symbols.
- FFmpeg documents `av_packet_alloc`／`av_packet_free` as the packet storage boundary and deprecates treating `sizeof(AVPacket)` as a durable public ABI.
- FFmpeg documents PTS and DTS in packet time-base units, duration `0` as unknown, `AV_NOPTS_VALUE` for unknown timestamps, and `AV_PKT_FLAG_KEY` for key packets.
- FFmpeg documents `avcodec_send_packet(context, NULL)` as decoder flush, with the first accepted flush entering draining and later output received until EOF.
- FFmpeg documents `AVERROR(EAGAIN)` on send as a requirement to receive output before resending the same packet; time alone does not make progress.

## Accepted boundary

- Approve direct writes only to a reviewed `AVPacket` prefix under the exact verified FFmpeg 8 runtime identity.
- Reject arbitrary FFmpeg builds and major-version-only layout assumptions.
- Allocate packet storage through FFmpeg, never managed `sizeof(AVPacket)` allocation.
- Copy one parser-owned payload into one FFmpeg-owned padded packet buffer.
- Map exact stream ID and exact snapshotted time base.
- Map known PTS／DTS directly and unknown values to `AV_NOPTS_VALUE`.
- Map known duration directly and unknown duration to `0`.
- Set only `AV_PKT_FLAG_KEY` when the parser event is a keyframe.
- Keep sequence and discontinuity as managed submission-state validation rather than undocumented packet metadata.
- Represent EOS with a null packet pointer, never an empty encoded packet.
- Keep the active parser completion and composed native packet when send returns `EAGAIN`.
- Add no retry, sleep, spin, second queue, resident pump, or hidden frame drain.

## Required implementation results

The later native boundary must return fixed explicit states for composed-and-sent, output-required, flush-accepted, decoder-EOF, invalid input, source mismatch, stream mismatch, time-base mismatch, allocation rejection, native send rejection, and fault. Undefined native or managed enum values must fail closed.

## Ownership review

The future packet owner must release native packet state before releasing the parser completion. Packet reuse must unref previous contents first. Disposal must be idempotent and scheduler-independent. The source-bound decoder session continues to own only its two opened contexts and does not own the parser or demux source session.

## Deferred

- implementation of the metadata-aware native packet／flush boundary;
- one-event submission and receive state machine;
- bounded EOS drain;
- discontinuity reset semantics and `avcodec_flush_buffers`;
- retained controlled FLV fixture validation;
- PCP-to-parser composition;
- backend activation, runtime distribution, and output.

## CI

CircleCI remains the routine Linux merge gate. Because this review is documentation-only, the final branch head must still pass restore, Release build, all tests, and format verification before merge.

Status: Complete when documentation is synchronized and the final CircleCI head passes.