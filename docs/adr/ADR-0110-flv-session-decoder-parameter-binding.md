# ADR-0110: FLV Session Decoder-Parameter Binding

- Status: Accepted
- Milestone: 0.0.8z
- Production FFmpeg activation: Pending and unchanged

## Context

ADR-0109 confirmed that the immutable H.264/AAC session description produced by
the bounded FLV parser maps without information loss to the existing managed
FFmpeg codec-parameter ownership boundary. It also rejected direct packet
composition because the current packet boundary cannot preserve parser timing,
flags, ordering, backpressure, or EOS.

The next safe seam is therefore pure managed parameter ownership tied to the
exact parser session identity, without creating native contexts or consuming
packet completions.

## Decision

Add `PlaybackBoundedFlvDecoderParameterBindingAdapter` and an owned
`PlaybackBoundedFlvDecoderParameterBinding`.

### Accepted source session

The adapter accepts exactly one live `PlaybackBoundedDemuxSession` with:

- FLV container kind;
- H.264 video in stream 0 and video media role;
- AAC audio in stream 1 and audio media role;
- distinct stable stream identifiers;
- exact `1/1000` time bases for both streams;
- non-empty live AVCDecoderConfigurationRecord and AudioSpecificConfig bytes;
- video-only width/height fields;
- audio-only sample-rate/channel-count fields.

Null, disposed, non-FLV, alternate stream identifiers, alternate time bases,
wrong roles/codecs, empty codec data, and contradictory media fields fail closed
before parameter creation.

### Parameter creation

The adapter calls the existing
`PlaybackDecoderFfmpegCodecParametersFactory` directly:

1. create H.264 video parameters from exact session width, height, and codec data;
2. create AAC audio parameters from exact session sample rate, channel count, and
   codec data.

The existing factory independently validates values, copies codec data, appends
FFmpeg input padding, and owns cleanup.

Video creation occurs first. If audio creation rejects or throws, every created
video lease is disposed before return. Undefined factory results and
result/lease contradictions fail closed as `Faulted`.

The source session is checked before work and again before ownership transfer so
a source disposed during preparation cannot produce a successful binding.

### Binding ownership and identity

A successful binding owns exactly:

- one padded video codec-parameter lease;
- one padded audio codec-parameter lease.

It retains, but does not own:

- the exact source-session object reference;
- video and audio stream identifiers;
- video and audio time bases.

`ReferencesSourceSession` performs exact reference identity comparison for later
composition reviews. The binding does not keep a parser reference and does not
dispose or mutate the source session.

Binding disposal is idempotent and disposes audio parameters before video
parameters. Parameter ownership remains valid if the source session is disposed
after successful preparation because codec data was independently copied.

### Result model

The adapter exposes fixed results for:

- prepared;
- invalid session;
- source disposed;
- video parameters rejected;
- audio parameters rejected;
- faulted.

Successful result requires a binding. Every other result returns no binding.

## Consequences

A later packet-submission review can require exact reference identity between a
parser completion's source session and the decoder-parameter binding before any
native work. Codec setup can proceed from parser-derived configuration without
reconstructing an unrelated diagnostic fixture.

The milestone remains cross-platform and routine Ubuntu CI can validate every
branch because no native call or Windows output boundary is involved.

## Rejected alternatives

### Reuse `PlaybackDiagnosticDecoderConfigurationAdapter`

Rejected because rebuilding a fixture-shaped configuration loses exact identity
with the parser session that owns later packet completions.

### Own or dispose the parser session

Rejected because the parser controls session replacement on discontinuity. The
binding owns only independent parameter copies.

### Include native context creation or codec open

Rejected because parameter ownership is independently testable and native
lifecycle, runtime gates, and activation decisions remain separate boundaries.

### Copy encoded packet data into the binding

Rejected because packet ownership belongs to one active demux completion and
must remain governed by parser backpressure.

## Non-goals

No parser change, packet copy, parser-completion consumption, native runtime,
symbol gate, context creation, native parameter transfer, codec open,
timestamp-aware AVPacket construction, packet send, frame receive, flush, drain,
PCP composition, scheduler ingress, backend activation, Windows device
activation, FFmpeg bundling, redistribution decision, or audible/visible
playback is included.

## Next

Review the timestamp-aware packet submission and ownership boundary. That review
must preserve exact source identity, one-event parser order, PTS/DTS/time-base
metadata, keyframe flags, active-completion retention during decoder
backpressure, and explicit EOS flush/drain without adding a second encoded queue.
