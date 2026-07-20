# PCN-0115: First Bounded Container Parser Selection Review

## Scope

0.0.8w selects the first concrete parser behind the 0.0.8v pure managed demux
output contract. It compares FLV against MP4／fragmented MP4 for the existing
PeerCast callback-chunk input shape without adding parser runtime code.

## Evidence reviewed

### Repository input boundary

- `PcpChannelPacket` exposes callback-scoped payload memory, sequence, stream
  position, and discontinuity state.
- `IPcpChannelPacketConsumer.ConsumeAsync` is the upstream backpressure
  boundary and requires prompt cancellation-aware completion.
- `PcpRelayFanout` enforces sequential consumption and does not expose a
  seekable media source.
- 0.0.8v already provides one H.264 video stream, one AAC audio stream, separate
  PTS／DTS, keyframe state, explicit EOS, finite completions, bounds, ownership,
  and one-active-completion backpressure.

### Container comparison

- FLV v1 is a sequential header and interleaved tag stream.
- Its selected AVC／AAC subset maps directly to the 0.0.8v media roles.
- FLV tag timestamps and AVC signed composition offsets preserve separate DTS
  and PTS.
- FLV frame type exposes keyframe state.
- AVC and AAC sequence headers carry the codec configuration required before
  media packet delivery.
- PeerCastStation documentation identifies H.264＋AAC FLV as a normal supported
  streaming combination.
- ISO BMFF requires a larger box, track, sample, and fragment state surface;
  fragmented MP4 separates `ftyp`＋`moov` initialization from `moof`＋`mdat`
  media segments.

## Accepted decision

The first concrete parser will be a pure managed bounded FLV v1 demux parser in
0.0.8x.

MP4, fragmented MP4, F4V, MPEG-2 TS, MKV, OGG, and avformat remain later
separately reviewed work.

## Required 0.0.8x behavior

### Input and finite progress

- Receive caller-owned `ReadOnlyMemory<byte>` chunks.
- Do not retain or dispose caller input, network packets, streams, sessions, or
  relay registrations.
- Return a fixed result, consumed-byte count, and at most one 0.0.8v completion.
- Stop before an unconsumed complete tag when packet-count or owned-byte output
  budget is reached.
- Allow the caller to dispose the completion and resubmit the unconsumed suffix.

### Bounded carry

- Retain only one bounded incomplete-record carry buffer across calls.
- Permit carry for a split FLV header, tag header, tag body, or previous-tag-size
  field.
- Never retain a complete unreported media packet, multiple complete tags,
  caller suffix, output queue, or unbounded metadata object.
- Clear carry on explicit parser reset, cancellation cleanup, failure cleanup,
  and disposal.

### Bootstrap

- Require FLV signature and version 1.
- Require both audio and video flags and the supported fixed header layout.
- Require `PreviousTagSize0 == 0`.
- Accept script-data tags only as bounded skipped records.
- Require one valid AVC sequence header and one valid AAC sequence header before
  any media tag.
- Reject media before complete stream configuration rather than buffering it.
- Create exactly one 0.0.8v FLV session after both configurations validate.

### Structural validation

- Validate tag type, data size, timestamp fields, zero stream ID, and exact
  previous-tag-size value before allocation or state commit.
- Use checked arithmetic for header, payload, carry, and observation totals.
- Bound input observation, tag size, carry size, skipped records, packet count,
  packet bytes, codec data, and timestamp magnitude.
- Skip only script-data tags in the first version.
- Reject encrypted, reserved, command-only, or undefined tag forms.
- Do not scan for a later signature or tag after malformed input.

### AVC handling

- Accept codec ID AVC only.
- Accept keyframe and inter-frame media packets only.
- Accept AVC packet types sequence header, NALU, and end of sequence.
- Retain bounded AVCDecoderConfigurationRecord bytes as codec extradata.
- Parse SPS with a bounded bit reader to derive cropped coded width and height.
- Validate PPS and configuration structure sufficiently to fail closed before
  session creation.
- Keep NALU payload length-prefixed; do not convert to Annex B.
- Walk configured NAL lengths under NAL-count and payload bounds before copying.
- Convert FLV unsigned millisecond timestamp to DTS.
- Add signed 24-bit composition offset with checked arithmetic to produce PTS.
- Leave duration unknown.
- Map FLV frame type to keyframe／non-keyframe.
- Map AVC end-of-sequence to video EOS and reject later video media.

### AAC handling

- Accept sound format AAC only.
- Accept AAC packet types sequence header and raw.
- Retain bounded AudioSpecificConfig bytes as codec extradata.
- Parse sample rate and channel count from AudioSpecificConfig.
- Support AAC Low Complexity with a supported explicit channel configuration in
  the first version.
- Reject HE-AAC extension signalling, Program Config Elements, and unsupported
  object or channel forms.
- Do not derive AAC configuration from legacy FLV sound-rate／size／type bits.
- Use FLV millisecond timestamp as both PTS and DTS.
- Leave duration unknown and keyframe state `NotApplicable`.

### Configuration repetition

- Skip an exact duplicate AVC or AAC sequence header under the skipped-record
  budget.
- Reject changed sequence headers after session creation.
- Do not mutate the 0.0.8v stream description or add a codec-change event.

### Discontinuity and final input

- Explicit input discontinuity clears carry and all bootstrap／media state.
- Require a fresh FLV header and codec sequence headers after reset.
- Do not infer byte-level resynchronization.
- On final input, reject incomplete records as `TruncatedData`.
- Reject final input before both codec descriptions as
  `InvalidStreamDescription`.
- Emit EOS for every selected stream not already ended and use the existing
  0.0.8v final-EOS rules.
- Do not create decoder flush or drain packets.

### Ownership and commit order

- Validate the entire finite event set before creating a 0.0.8v completion.
- Advance parser input offset, sequence, codec, carry, and EOS state only after
  successful completion construction.
- Release every partial allocation on cancellation, malformed input,
  limit failure, contradictory contract result, or unexpected failure.
- Do not introduce a parser-owned producer queue or resident task.

## Rejected immediate directions

- fragmented MP4 as the first parser;
- classic seekable MP4 as the first parser;
- native avformat integration;
- AMF metadata as authoritative codec configuration;
- buffering media until the missing sequence header arrives;
- copying every input chunk into an internal queue;
- PCP-to-parser composition in the same milestone;
- decoder packet submission or production activation in the same milestone.

## Safety boundaries

- 0.0.8w changes documentation only.
- No FLV parser, fixture, SPS reader, AAC reader, or runtime dependency is added.
- No PCP consumer, relay, scheduler, decoder, factory, native lease, device, or
  activation path changes.
- `PlaybackDecoderBackendFactory` remains untouched.
- FFmpeg activation, bundling, redistribution, and approval remain pending.
- Windows device activation and controlled audible／visible playback remain
  outside scope.

## Automated validation

The final branch head must pass restore, Release build, the full test suite, and
format verification on the routine Ubuntu workflow. The final diff must contain
only ADR-0107, PCN-0115, CHANGELOG, NAVIGATION, ROADMAP, and README updates and
must have no unresolved review threads.

Status: Accepted architecture review; final branch-head CI is the merge gate.
