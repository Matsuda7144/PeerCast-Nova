# ADR-0109: Parser-to-Decoder Composition Architecture Review

- Status: Accepted
- Milestone: 0.0.8y
- Selected next implementation: 0.0.8z FLV session decoder-parameter binding
- Production FFmpeg activation: Pending and unchanged

## Context

0.0.8x added a pure managed bounded FLV v1 parser behind the 0.0.8v demux
contract. A configured parser session now exposes one H.264 video description
and one AAC audio description, while each finite media operation exposes at most
one owned packet or EOS completion with parser order, per-stream sequence, PTS,
DTS, keyframe state, discontinuity, and explicit backpressure.

The repository also contains earlier diagnostic FFmpeg boundaries for managed
codec-parameter ownership, native parameter transfer, codec open, packet send,
and one frame-receive attempt. Those boundaries were built around a fixed paired
video-then-audio fixture and must not be assumed to accept the richer parser
contract without review.

## Findings

### Session descriptions map safely to managed codec parameters

The 0.0.8x session description contains every field required by
`PlaybackDecoderFfmpegCodecParametersFactory`:

- H.264 width, height, and AVCDecoderConfigurationRecord bytes;
- AAC sample rate, channel count, and AudioSpecificConfig bytes;
- bounded codec data already validated by the parser and 0.0.8v contract.

The existing factory independently revalidates numeric and codec-specific
fields, copies extradata, adds FFmpeg input padding, and owns cleanup. A pure
managed adapter can therefore create the two parameter leases without a native
call or activation decision.

The older `PlaybackDiagnosticDecoderConfigurationAdapter` is fixture-shaped and
does not preserve identity with the parser session that produced later packet
completions. Reconstructing an unrelated diagnostic configuration would permit
accidental mixing of one session's parameters with another session's packets.

### Existing packet transaction cannot consume real parser output

Directly passing 0.0.8x packet completions to
`PlaybackDiagnosticPacketFrameTransaction` is rejected for several independent
reasons:

- the diagnostic transaction requires one video packet followed by one audio
  packet, while FLV emits one interleaved event at a time in container order;
- `PlaybackDecoderFfmpegPacketFrameBoundary.SendPacket` accepts payload bytes
  only and cannot preserve PTS, DTS, duration, keyframe state, stream time base,
  discontinuity, or sequence;
- the diagnostic transaction treats `NeedFrameDrain` as rejection and disposes
  the complete native lease, while real decoder backpressure must retain the
  still-owned parser completion for retry after bounded frame drain;
- the current packet boundary rejects empty payloads and does not expose an
  explicit null-packet flush or EOS drain operation;
- the paired transaction has no source-session identity check and no way to
  select exactly one decoder context from one parser event;
- disposing the parser completion before accepted packet submission would lose
  the encoded payload, while retaining it requires a deliberate ownership state.

Dropping parser timing or keyframe information merely to reuse the older
transaction is not acceptable because later decoded-frame timing and scheduler
composition depend on those values.

## Decision

### 0.0.8z: FLV session decoder-parameter binding

Implement one pure managed finite adapter that accepts a live
`PlaybackBoundedDemuxSession` created by the bounded FLV parser and produces an
owned decoder-parameter binding.

The adapter must:

- require a non-disposed FLV session;
- require one H.264 video stream and one AAC audio stream with distinct stable
  identifiers;
- require the current FLV `1/1000` time base for both selected streams;
- pass exact parser-owned codec configuration, dimensions, sample rate, and
  channel count through the existing codec-parameter factory;
- create video parameters before audio parameters;
- dispose the video lease if audio creation fails;
- return fixed results for invalid session, video rejection, audio rejection,
  disposal, and fault;
- own only the two new parameter leases;
- retain a non-owning reference to the exact source session for future identity
  checks;
- retain the selected stream identifiers and time bases as immutable binding
  metadata;
- dispose audio parameters before video parameters, idempotently;
- never dispose or mutate the parser or source session.

The binding must not copy encoded media packets, retain parser completions,
create native contexts, transfer native parameters, open codecs, or activate a
backend.

### Later: timestamp-aware packet submission review

After 0.0.8z, separately review and implement a packet-submission boundary that
can consume one 0.0.8v event at a time without information loss.

That later boundary must address:

- exact source-session identity against the parameter binding;
- context selection from stream identifier and media kind;
- parser-order preservation without video/audio pairing or reordering;
- AVPacket PTS, DTS, duration, time base, and keyframe flags;
- ownership retention when send reports decoder backpressure;
- bounded receive attempts and explicit retry state;
- EOS as explicit flush and bounded drain, not an empty encoded packet;
- discontinuity and stale-generation rejection;
- completion cleanup only after packet acceptance, explicit cancellation, or
  terminal failure;
- no second encoded-packet queue or resident decode loop.

Any native AVPacket field access or new FFmpeg symbol requirement must receive a
separate ABI and activation review rather than being hidden inside the managed
adapter.

## Rejected alternatives

### Connect the parser directly to the paired diagnostic transaction

Rejected because it reorders interleaved media, discards packet timing and
flags, mishandles decoder backpressure, and has no EOS or session-identity model.

### Rebuild `PlaybackDiagnosticDecoderConfiguration` from parser values

Rejected as the production-facing seam because the reconstructed object is not
bound to the exact source session. The new binding uses the existing parameter
factory directly and carries source identity explicitly.

### Convert AVCC packets to Annex B in the parameter adapter

Rejected. The bounded FLV parser deliberately preserves AVCC payload and avcC
extradata. Packet representation changes belong to a separately reviewed packet
or decoder boundary only if proven necessary.

### Dispose the parser session when parameter binding is disposed

Rejected. The parser owns its session and controls discontinuity replacement.
The binding owns only independently copied padded codec parameters.

### Add native context creation and codec open to 0.0.8z

Rejected because managed parameter binding is independently testable on Ubuntu
and does not require production activation, two runtime gates, native ownership,
or Windows-only execution.

## Non-goals

No runtime implementation is included in 0.0.8y. No PCP composition, parser
change, media-packet copy, native parameter transfer, codec open, timestamp-aware
AVPacket creation, send/receive loop, flush, drain, scheduler ingress, backend
activation, Windows device activation, FFmpeg bundling, redistribution decision,
or audible or visible playback is added.

## Next

0.0.8z will implement the pure managed FLV session decoder-parameter binding and
focused cross-platform tests. Packet submission remains blocked until the
separate timestamp-aware ownership and ABI review is complete.
