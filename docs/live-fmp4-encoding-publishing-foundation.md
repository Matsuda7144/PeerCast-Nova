# Live fMP4 encoding and publishing foundation

## Boundary

`PacedSyntheticMediaSource` and `LiveEncodingPublisher` compose the existing
H264/AAC sessions, `Fmp4Muxer`, and `PcpFmp4PublishingBoundary`. The source
is deterministic synthetic input, not camera, microphone, or desktop capture.
The public run borrows the verified FFmpeg runtime and PCP consumer until the
returned task completes. It owns and disposes one source/encoder/mux/publisher
generation and cannot be restarted.

The default source produces 192 moving 64x48 YUV420P frames at 30 fps and 300
1024-frame stereo float PCM blocks at 48 kHz, with a continuous 440 Hz phase.
Both tracks cover 6.4 seconds. Generation is bounded to 16–288 video frames
in multiples of 16, giving exactly 25 audio blocks per 16 video frames.

## Clock and timestamps

The source starts one monotonic TimeProvider epoch after initialization delivery.
Video event n is due at n*1600 ticks and audio block m at m*1024 ticks in an
exact 48 kHz source clock; tied events emit video first. Only the next due
event is allocated. Each delay targets absolute elapsed time, so rounded delays
do not accumulate drift. Downstream waits slow the single producer. Overdue
events catch up without dropping inputs or replacing media time with wall time.

Video encoder PTS is the frame index in 1/30 units. Audio encoder PTS is the
stereo sample-frame counter in 1/48000 units. Mux timing is 1/48000 for both.
Video DTS/PTS/duration multiply by 1600 exactly; audio values need no scaling.
Both DTS and PTS then shift by the native AAC initial padding. The first video
DTS equals padding; the first audio DTS is zero. Every encoder packet is retained,
including AAC priming. This reuses the finite acceptance policy: leading priming
remains in the common timeline, and gapless trimming is not implemented.

The orchestrator requires contiguous per-track DTS, strictly increasing PTS,
positive duration and nonnegative shifted timestamps. Unsupported encoder timing
fails before publishing affected media. Configuration comes from the newly
opened sessions' avcC and ASC, never retained fixture bytes.

## Incremental fragments

One initialization segment is published as PCP HEAD before raw generation.
The initial fragment interval is 38400 ticks (0.8 seconds). A cutoff is ready
only after both encoders have emitted a sample starting at or beyond it; earlier
samples are detached into an owned fragment and published before more input is
generated. Final finite completion flushes both encoders and publishes the tail.

The receiver defaults to at most 64 samples per batch. A one-second fragment
contains approximately 77 samples for this profile and is rejected with
SampleCountLimitExceeded; a regression test preserves this fact. The 0.8-second
policy fits the existing limit without changing receiver internals. Each
publication records sequence, track counts, first/last DTS/PTS, first video/audio
random-access classification, byte length, SHA256 and source progress.

The software encoder is requested to use a 24-frame keyframe interval; actual
packet flags remain authoritative. Native acceptance observed independent access
for the first fragment and dependent access for subsequent fragments. This slice
proves continuous decoder state, not arbitrary fragment joining or independent
decoding of every fragment.

## Capacity, cancellation and ownership

Pending encoded samples default to 128 samples / 1 MiB, with hard configuration
bounds of 256 samples / 4 MiB. Admission checks happen before the payload copy.
Raw event buffers, encoder packets, pending samples and serialized fragments have
separate owned storage. The publisher borrows a fragment only during awaited
publication; consumers must copy any payload retained beyond their callback.
Fragment instrumentation contains no payloads. Production retains no full stream.

The existing PCP publisher awaits each consumer callback, slicing segments into
at most 64 KiB packets. The supplied consumer must provide awaited capacity and
observe cancellation promptly. A fanout admission acknowledgement alone does not
prove every downstream child has parsed or decoded its packet.

Normal finite completion flushes and publishes all delayed packets before
disposing resources. Caller cancellation aborts without encoder flush or tail
publication, because the existing publisher's cancelled generation is terminal
and may already have delivered part of a box. Cancelled is distinct from
Completed; cancellation is not decoder EOS. Pre-cancelled startup throws without
opening native sessions and still disposes the owned source. Other encoder/mux
errors propagate; consumer errors report the publisher's canonical
ConsumerRejected result. No reconnect, retry, reconfiguration or recovery occurs.
The result's publisher snapshot is taken after publication, before disposal.

## Acceptance

`LiveEncodingPublisherTests` uses an injected immediate clock and test-only
encoder doubles. It covers source ordering/ownership, cancellation/disposal,
initialization ordering, incremental publication, native-buffer-reuse simulation,
timing failures, bounds, backpressure, flush, failure propagation and teardown.

`LiveEncodingNativeAcceptanceTests` uses the same pinned, inventory-verified
FFmpeg runtime as the encoding foundation, selected by
`NOVA_FMP4_FFMPEG_TEST_RUNTIME`. First run the real encoder / managed parser
test, then the real encoder / native decoder test. Each generates fresh media
over approximately 6.4 seconds and proves source remaining > 0 at first publish.
The tests preserve per-fragment evidence and compare every encoded packet's
payload, timing and random-access status with production-parser output.

The controlled in-process loopback serializes standard PCP CHAN / pkt atoms
through PcpAtomTreeCodec into a bounded Pipe (64 KiB pause / 32 KiB resume),
then uses the production PcpChannelStreamReader and
PlaybackPcpFmp4CompositionBoundary. Each publisher callback awaits the receiver's
completed composition callback. It is a post-handshake wire acceptance, not a
TCP handshake, relay, WAN or peer-discovery test.

Only after producer completion and all acknowledged packets does the test send
standard PCP QUIT. It requires QuitReceived, then explicitly invokes
CompleteInputAsync to drain decoder/output EOS. EOF or cancellation is never
treated as successful EOS. The native output sink copies/checks decoded data
in memory and opens no DXGI or WASAPI presentation.

Windows physical live presentation, real-device capture, WAN publishing,
late joining, long-duration stability, reconnect, third-party interoperability,
GUI and actual user broadcasts remain separate acceptance boundaries.
