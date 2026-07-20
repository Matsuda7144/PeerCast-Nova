# PCN-0113: Bounded Demux Output Contract Review

## Scope

0.0.8u reviews the completed finite diagnostic path through late-aware dispatch
and selects the next safe input-side milestone before bounded container parsing or
production composition.

## Findings

- The repository has no general FLV or MP4 demux contract or parser.
- The 0.0.8e fixture is diagnostic-only and contains exactly one H.264 packet and
  one AAC packet.
- The current decoder packet boundary accepts encoded bytes but does not carry
  stream identity, DTS, keyframe state, codec configuration changes, or EOS.
- Direct production composition would combine missing demux semantics with
  activation, decode-loop, and output ownership risks.
- A container-neutral bounded demux output contract can be implemented and tested
  without FFmpeg, D3D11, WASAPI, decoder-factory activation, or real media.

## Accepted decision

The next milestone is 0.0.8v, a pure managed bounded demux output contract.
Concrete FLV／MP4 parsing and production composition remain later milestones.

## Required contract coverage

### Stream description

- Exactly one selected H.264 video stream and one selected AAC audio stream for
  the first contract version.
- Stable, distinct, non-negative stream identifiers.
- Bounded owned codec extradata.
- Video dimensions and audio sample configuration.
- Positive rational time bases.
- Fixed container and codec enums only.
- Missing, duplicate, changing, unsupported, or contradictory descriptions fail
  closed.

### Packet and event model

- Stream identifier and media kind.
- Monotonic per-stream sequence.
- Owned bounded encoded payload.
- Independently known or unknown PTS and DTS.
- Optional known positive duration.
- Video keyframe flag.
- Discontinuity kind.
- Explicit packet or EOS event.
- No timestamp, duration, keyframe, discontinuity, or codec-data synthesis.

### Bounds

- Bounded input bytes per operation.
- Bounded packet payload and codec extradata.
- Bounded packet count and aggregate owned packet bytes.
- Bounded skipped container records.
- Bounded timestamp conversion magnitude.
- Checked arithmetic before allocation.
- No resident reader, background task, implicit retry, or parser-owned producer
  queue.

### Ownership and backpressure

- Successful results own returned payload and codec data.
- Completion disposal is idempotent and releases all retained data.
- Rejected, cancelled, truncated, malformed, limit-exceeded, and faulted paths
  release partial ownership.
- Caller-owned sources are not retained or disposed.
- The caller requests the next finite read only after consuming or disposing the
  prior completion.

### Result model

The future contract distinguishes stream description, packets, EOS, need-more-
input, unsupported container／codec, invalid description, malformed／truncated,
timestamp conversion failure, limit exceeded, cancellation, disposed, and faulted
states. Undefined or contradictory states fail closed.

## Rejected immediate directions

- Production activation using the diagnostic fixture.
- FLV and MP4 parser implementations before a shared contract.
- A packet model containing only bytes and PTS.
- Immediate avformat integration before native ownership and activation review.

## Safety boundaries

- No production code is added by 0.0.8u.
- No parser, network reader, seeking, continuous packet loop, flush, or EOS drain.
- No change to `PlaybackDecoderBackendFactory` or another activation path.
- FFmpeg activation, bundling, redistribution, and approval remain pending.
- Windows device activation and controlled audible／visible playback remain
  outside scope.

## Automated validation

The final branch head must pass restore, Release build, the full test suite, and
format verification on the routine Ubuntu workflow before merge.

Status: Accepted architecture review; final branch-head CI is the merge gate.
