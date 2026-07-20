# ADR-0106: Pure Managed Bounded Demux Output Contract

- Status: Accepted
- Milestone: 0.0.8v
- Production FFmpeg activation: Pending and unchanged

## Context

ADR-0105 selected a container-neutral output contract before any FLV, MP4, or
native avformat parser. The diagnostic fixture cannot represent real packet
ordering, independent PTS and DTS, keyframe state, discontinuity, or explicit
end of stream. Connecting it directly to production composition would combine
missing input semantics with decoder-loop, activation, and output ownership
risks.

The contract must be independently testable on routine Ubuntu CI and must not
create another resident queue or background reader.

## Decision

Add `PlaybackBoundedDemuxSession` and supporting pure managed types.

### Session description

One session owns one immutable description containing:

- one selected H.264 video stream;
- one selected AAC audio stream;
- distinct non-negative stable stream identifiers;
- fixed FLV, MP4, or fragmented-MP4 container kind;
- bounded defensively copied codec extradata;
- bounded video dimensions;
- bounded audio sample rate and channel count;
- positive rational time bases.

The existing FFmpeg codec-parameter hard limits are reused for video dimensions,
pixel count, audio configuration, and codec-data size. Unsupported codecs,
duplicate identifiers, invalid roles, contradictory media fields, invalid time
bases, and oversized codec data fail closed before ownership transfer.

The session owns codec data until disposal. Disposal clears audio codec data,
then video codec data, and is idempotent.

### Time model

`PlaybackBoundedDemuxTime` preserves known and unknown values separately. PTS
and DTS remain independent. Known PTS and DTS may be negative; known duration
must be positive.

`PlaybackBoundedDemuxTimestampConverter` converts a signed container timestamp
through a positive rational time base using checked arbitrary-precision
intermediate arithmetic and truncation toward zero. Conversion fails explicitly
for an invalid time base, invalid magnitude option, numeric overflow, or a value
outside the configured timestamp magnitude. Missing timestamps are never passed
to the converter and remain unknown.

### Event model

A finite read completion contains immutable events in parser order.

A packet event contains:

- matching stream identifier and media kind;
- monotonically increasing per-stream sequence;
- defensively copied owned encoded payload;
- independently known or unknown PTS and DTS;
- optional known positive duration;
- explicit video keyframe, non-keyframe, or unknown state;
- `NotApplicable` keyframe state for audio;
- an existing decoded-frame discontinuity kind.

An EOS event:

- belongs to exactly one selected stream;
- participates in that stream's monotonic sequence;
- has no payload;
- has unknown PTS, DTS, and duration;
- has `NotApplicable` keyframe state;
- has `None` discontinuity.

EOS is tracked independently for video and audio. Final `EndOfStream` is valid
only after both stream EOS events have been accepted. Packets after stream EOS,
repeated EOS, and contradictory terminal results fail closed. Decoder null
packets, flush, and drain loops are not represented.

### Bounds

`PlaybackBoundedDemuxOptions` supplies caller-selected limits beneath fixed hard
project limits for:

- bytes observed during one finite read;
- one packet payload;
- one stream's codec extradata;
- packet count per completion;
- aggregate owned packet bytes per completion;
- skipped container records per finite read;
- timestamp magnitude;
- rational time-base components.

The packet payload hard limit remains 4 MiB, matching the existing decoder
packet boundary. Codec-data limits remain 1 MiB, matching the existing codec-
parameter owner. Aggregate arithmetic is checked. Packet payload allocation is
performed only after the full event set, stream roles, timing, sequence, EOS,
count, and byte budgets have passed validation.

### Ownership and backpressure

A successful packet or EOS completion becomes the only active completion for
its session. The session rejects the next finite read with `ActiveCompletion`
until that completion is disposed.

Successful packet creation copies payload bytes and transfers ownership to the
completion. Completion disposal clears all packet payloads before releasing the
session read gate. Session disposal first disposes the active completion and
therefore clears packet data before clearing codec data.

Need-more-input and fixed source failures own no events. A source failure paired
with events is contradictory and returns `Faulted`. Cancellation before payload
copy transfers no ownership.

### State and result model

The contract exposes fixed results for:

- stream description available;
- packets available;
- end of stream;
- need more input;
- unsupported container or codec;
- invalid stream description;
- malformed or truncated data;
- timestamp conversion failure;
- limit exceeded;
- active prior completion;
- cancellation;
- disposed session;
- fault.

Undefined enums and contradictory result/event combinations fail closed.

## Consequences

A future managed FLV or MP4 parser can emit the same bounded session and read
model without exposing parser-specific structures to decoder composition. A
later reviewed native adapter can also target this contract without changing its
ownership or backpressure rules.

The contract does not parse bytes, own a stream, select tracks, submit decoder
packets, run a decode loop, or activate FFmpeg or Windows output.

## Non-goals

No FLV parser, MP4 parser, fragmented-MP4 parser, avformat integration, network
reader, seeking, track switching, metadata extraction, resident read loop,
parser-owned producer queue, codec-configuration change event, decoder packet
submission, flush, drain, duration cutoff, production decoder ingress, backend
activation, Windows device activation, FFmpeg bundling, redistribution decision,
or audible or visible playback is included.

## Next

Review and select the first concrete bounded container parser behind this
contract. Parser selection must not change production activation or claim
real-media playback.
