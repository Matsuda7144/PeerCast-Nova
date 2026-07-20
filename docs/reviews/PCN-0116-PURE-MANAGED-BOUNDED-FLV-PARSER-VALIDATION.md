# PCN-0116: Pure Managed Bounded FLV Parser Validation

## Scope

0.0.8x implements the pure managed FLV v1 parser selected by ADR-0107. It
accepts caller-pushed finite chunks, establishes one immutable H.264/AAC 0.0.8v
session, and emits at most one bounded packet or EOS completion per operation.
It does not connect network ingress, decoder submission, production activation,
or Windows output.

## Acceptance criteria

### Public operation

- One operation accepts caller-owned memory, final-input state, discontinuity,
  and cancellation.
- The parser never retains or disposes caller memory.
- Results contain a fixed state, consumed bytes, at most one 0.0.8v completion,
  byte position, carry count, skipped count, and generation.
- Unconsumed suffix remains caller-owned and can be submitted again.
- A live completion blocks the next operation without consuming input.

### FLV framing

- Signature, version 1, audio/video flags, data offset 9, and
  `PreviousTagSize0 == 0` are required.
- Only audio, video, and script tags are accepted.
- Tag data size, timestamp extension, zero stream ID, and exact previous-tag size
  are validated before semantic processing.
- Checked arithmetic and configured tag/carry limits are applied before growth
  or payload ownership.
- Malformed input is terminal; no byte-scanning resynchronization occurs.

### Carry and bounds

- At most one incomplete record is retained.
- Carry may represent a split header, tag header, body, or previous-tag-size
  field only.
- Complete unreported packets, multiple complete tags, output queues, and caller
  suffixes are never retained.
- Media tag bytes, script bytes, carry bytes, skipped bytes, skipped records,
  NAL count, parameter-set count, SPS bytes, SPS cycle offsets, input bytes,
  packet bytes, codec data, aggregate bytes, and timestamp magnitude are all
  independently bounded.

### Bootstrap and configuration

- AVC configuration is required before AAC configuration.
- Both configurations are required before media.
- Script tags may be bounded-skip but do not provide codec configuration.
- Early media is rejected rather than buffered.
- Exact duplicate sequence headers are skipped after session creation.
- Changed sequence headers fail closed.
- Session stream IDs are fixed and distinct: video 0 and audio 1.
- Both stream time bases are `1/1000` for FLV milliseconds.

### AVC

- Codec ID AVC is required.
- Sequence header requires keyframe form and zero composition offset.
- AVC configuration version, reserved bits, NAL length size, SPS/PPS counts,
  parameter-set lengths and NAL types, profile/level, and full consumption are
  validated.
- SPS parsing derives dimensions with profile-specific syntax, scaling-list
  skipping, picture-order bounds, interlaced height, and cropping.
- Dimensions and codec data pass through the existing 0.0.8v hard limits.
- Media frame type is keyframe or inter frame.
- AVCC payload remains length-prefixed and every NAL length/type is walked under
  the configured NAL-count limit.
- FLV timestamp becomes DTS; signed composition time forms PTS with checked
  conversion.
- Duration remains unknown.
- AVC end-of-sequence emits video EOS exactly once.

### AAC

- Sound format AAC is required.
- AAC Low Complexity is the only accepted object type.
- Indexed and explicit sample rates are parsed and validated by 0.0.8v limits.
- Channel configurations 1 through 7 map to explicit channel counts.
- Program Config Elements, unsupported object types, and unsupported channel
  configurations fail closed.
- Raw AAC payload excludes the FLV AAC header.
- Timestamp is both PTS and DTS; duration remains unknown.

### EOS and discontinuity

- Final incomplete input returns `TruncatedData`.
- Final input before complete configuration returns
  `InvalidStreamDescription`.
- Final configured input emits every missing stream EOS through 0.0.8v.
- Final `EndOfStream` is obtained only from the 0.0.8v session.
- Explicit discontinuity disposes the old session, clears carry/configuration,
  resets sequence/EOS/byte position, and increments generation.
- Discontinuity never bypasses an active completion.

### Ownership and cleanup

- 0.0.8v owns copied packet and EOS events.
- Parser sequence and EOS state advance only after completion creation succeeds.
- Parser disposal disposes active completion before session disposal.
- Carry and temporary AVC bytes are cleared idempotently.
- Fixed failure paths latch `Faulted` and retain no complete unreported packet.
- The parser owns no stream, PCP object, decoder, scheduler, sink, native lease,
  device, or background task.

### Safety boundaries

- No PCP-to-parser adapter is added.
- No AMF metadata parser or seek index is added.
- No MP4, fragmented MP4, or native avformat path is added.
- No decoder packet submission, decode iteration, flush, or drain is added.
- `PlaybackDecoderBackendFactory` and production activation remain unchanged.
- FFmpeg activation, bundling, redistribution, and approval remain pending.
- Windows device activation and controlled audible or visible playback remain
  outside scope.

## Focused tests

The milestone suite covers:

- bootstrap and caller-suffix preservation;
- SPS-derived dimensions and AAC sample/channel configuration;
- independent PTS and DTS with negative composition offsets;
- active-completion backpressure;
- split header and split tag carry with defensive copying;
- malformed previous-tag-size and unsupported header flags;
- media before complete configuration;
- bounded script skipping;
- exact duplicate and changed sequence headers;
- AVCC NAL length overrun rejection;
- unsupported AAC object type and invalid AVC configuration;
- explicit AVC EOS and final missing-stream EOS;
- two-stream final EOS;
- discontinuity generation and old-session disposal;
- script, input-observation, and carry option limits;
- cancellation, terminal fault latch, final truncation, and idempotent disposal.

## Automated validation

Final-head CI #331 passed restore, Release build, the full test suite, and format
verification on Ubuntu. The branch was based directly on current main during
that validation. The final merge head must retain the expected parser, test,
ADR, validation, targeted analyzer rationale, and progress-document files only,
remain behind main by zero commits, and have no unresolved review threads.

Status: Implementation, focused review, and automated validation complete.
