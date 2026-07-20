# PCN-0114: Pure Managed Bounded Demux Output Validation

## Scope

0.0.8v implements the container-neutral pure managed output contract selected by
ADR-0105. It defines bounded session descriptions, finite packet and EOS
completions, timestamp conversion, ownership, sequence state, and backpressure
without parsing a real container.

## Acceptance criteria

### Session description

- Exactly one H.264 video stream and one AAC audio stream are accepted.
- Stream identifiers are stable, distinct, and non-negative.
- Video and audio roles and codec-specific fields are exact and contradictory
  fields fail closed.
- Video dimensions, pixel count, audio sample rate, and channel count reuse the
  existing codec-parameter hard limits.
- Rational time-base numerator and denominator are positive and bounded.
- Codec extradata is non-empty, bounded, defensively copied, session-owned, and
  cleared on disposal.
- Unsupported container and codec values are fixed results.

### Timestamp model

- PTS and DTS are independently known or unknown.
- Negative known PTS and DTS remain valid within the configured magnitude.
- Known duration is positive; unknown duration remains valid.
- Rational conversion uses checked arbitrary-precision intermediate arithmetic.
- Invalid time bases, invalid limits, overflow, and excessive magnitude fail
  explicitly.
- Missing timing is never synthesized.

### Packet and EOS events

- Events preserve parser order.
- Every event has the selected stream identifier, exact media kind, and a
  monotonically increasing per-stream sequence.
- Packet payload is non-empty, individually bounded, aggregate bounded, copied,
  and owned by the returned completion.
- Video keyframe state is keyframe, non-keyframe, or unknown.
- Audio keyframe state is `NotApplicable`.
- Discontinuity uses the existing fixed decoded-frame enum.
- EOS carries no payload or timing, uses `NotApplicable` keyframe state and
  `None` discontinuity, and occurs at most once per stream.
- Final `EndOfStream` requires both video and audio EOS.
- Packets after EOS, repeated EOS, sequence regression, media mismatch, and
  contradictory terminal results fail closed.

### Bounds

- Input bytes observed per operation are bounded.
- Packet payload is bounded by the existing 4 MiB decoder packet hard limit.
- Codec extradata is bounded by the existing 1 MiB codec-parameter hard limit.
- Packet count and aggregate owned packet bytes are independently bounded.
- Skipped container records are bounded.
- Timestamp magnitude and time-base components are bounded.
- Option combinations that cannot satisfy one packet or aggregate ownership are
  rejected.
- Aggregate arithmetic is checked before payload allocation.

### Ownership and backpressure

- At most one successful completion is active per session.
- A second read while ownership remains returns `ActiveCompletion`.
- Completion disposal is idempotent, clears payloads, and releases the session
  gate.
- Session disposal disposes active packet ownership before clearing audio then
  video codec data.
- Need-more-input and fixed source failures own no events.
- Source failures paired with events return `Faulted`.
- Cancellation before copy owns no payload.
- The contract owns no stream, parser, decoder, scheduler, sink, native lease,
  device, or activation resource.

### Safety boundaries

- No FLV, MP4, or fragmented-MP4 parser is added.
- No avformat symbol, format context, or native I/O ownership is added.
- No resident reader, background task, retry loop, or parser-owned queue is
  added.
- No decoder packet submission, decode iteration, flush, or drain is added.
- `PlaybackDecoderBackendFactory` and all production activation paths remain
  unchanged.
- FFmpeg activation, bundling, redistribution, and approval remain pending.
- Windows device activation and controlled audible or visible playback remain
  outside scope.

## Focused tests

The milestone suite covers:

- defensive stream-description ownership and disposal clearing;
- duplicate stream identifiers, wrong codec, invalid time base, and option
  rejection;
- positive and negative rational timestamp conversion;
- invalid time-base and magnitude conversion failures;
- packet payload ownership and independent PTS／DTS;
- active-completion backpressure;
- payload clearing and gate release;
- monotonic sequence state across reads;
- packet-count, aggregate-byte, and input-byte limits;
- need-more-input contradictions;
- independent stream EOS and final EOS;
- invalid EOS payload and packets after EOS;
- unknown timing and audio/video keyframe roles;
- fixed source failure contradictions;
- cancellation and undefined result rejection;
- session-first active-payload cleanup before codec-data cleanup.

## Automated validation

The final branch head must pass restore, Release build, the full test suite, and
format verification on the routine Ubuntu workflow. The branch must be based on
current main, contain only the expected implementation, tests, ADR, validation,
and progress-document updates, and have no unresolved review threads before
merge.

Status: Implementation and focused review complete; final branch-head CI is the
merge gate.
