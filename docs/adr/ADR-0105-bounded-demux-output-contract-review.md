# ADR-0105: Bounded Demux Output Contract Review

- Status: Accepted
- Milestone: 0.0.8u
- Production FFmpeg activation: Pending and unchanged

## Context

The finite diagnostic path now reaches owned managed frames, the existing bounded
scheduler, and finite late-aware dispatch. The remaining input side still uses a
fixed diagnostic fixture containing exactly one H.264 packet and one AAC packet.
There is no general FLV or MP4 demux contract in the repository.

Production composition must not be added directly on top of the diagnostic
fixture. A container parser needs a stable, bounded, activation-neutral output
contract before any FLV, fragmented MP4, or other parser can be reviewed.

The existing decoder packet boundary accepts only encoded bytes. It does not
carry packet timestamps, stream identity, keyframe state, codec configuration
changes, or end-of-stream state. A demux contract therefore cannot be reduced to
`ReadOnlyMemory<byte>` without losing information required by later decode,
scheduling, seeking, discontinuity, and recovery decisions.

## Decision

Select a container-neutral bounded demux output contract as the next playback
foundation milestone. Implementing a concrete FLV or MP4 parser and production
activation remains later work.

### Session and stream description

One demux session must expose a finite immutable stream description before packet
delivery begins.

The first implementation target is exactly one selected video stream and one
selected audio stream:

- video codec: H.264 only;
- audio codec: AAC only;
- stable non-negative stream identifiers;
- distinct video and audio stream identifiers;
- bounded owned codec extradata;
- video width and height;
- audio sample rate and channel count;
- rational stream time bases with positive numerator and denominator;
- explicit container kind without free-text probing output.

Missing, duplicate, changing, unsupported, or contradictory stream descriptions
fail closed. Automatic multi-track selection, language preference, subtitle
selection, metadata dictionaries, and arbitrary codec support are not included.

### Packet model

Each emitted packet must contain:

- selected stream identifier and media kind;
- monotonically increasing per-stream sequence;
- owned bounded encoded payload;
- presentation timestamp and decode timestamp as independently known or unknown;
- optional known positive duration;
- keyframe flag for video;
- discontinuity kind;
- explicit normal-packet or end-of-stream event kind.

PTS and DTS must not be conflated. The parser may convert a container timestamp
using the declared stream time base, but it must not invent a missing timestamp,
duration, keyframe, discontinuity, or codec configuration.

End of stream is an explicit event and carries no payload. Flush packets, null
packet sentinels, and decoder drain loops remain outside the demux output
contract.

### Bounds

The contract must enforce caller-supplied options with hard project limits for:

- maximum input bytes observed by one finite operation;
- maximum packet payload bytes;
- maximum codec-extradata bytes;
- maximum packet count returned by one finite read;
- maximum total owned packet bytes;
- maximum skipped or unsupported container records;
- maximum timestamp conversion magnitude.

All size aggregation uses checked arithmetic. Allocation occurs only after type,
length, stream, timing, and aggregate-budget validation. No unbounded collection,
resident reader, background task, implicit retry, or unbounded metadata retention
is permitted.

### Ownership and backpressure

A successful packet result transfers ownership of its payload to the returned
completion. Disposal clears and releases all retained payload and codec data.
Rejected, cancelled, truncated, malformed, limit-exceeded, and faulted operations
must release every partially acquired resource.

The future parser receives a caller-owned readable stream or bounded byte source.
It must not dispose or retain that source unless an explicit owning adapter is
separately introduced.

One finite read returns at most the configured packet count and byte budget. The
caller explicitly invokes the next read after consuming or disposing the previous
completion. This forms the backpressure boundary; no parser-owned producer queue
is introduced.

### State and result model

The future contract must distinguish fixed results for at least:

- stream description available;
- packets available;
- end of stream;
- need more input;
- unsupported container or codec;
- invalid stream description;
- malformed or truncated data;
- timestamp conversion failure;
- limit exceeded;
- cancellation;
- disposed;
- faulted.

Undefined enum values and contradictory result/completion combinations fail
closed.

### Composition order

The approved order is:

1. define and test the pure managed bounded demux output contract;
2. add one bounded container parser or adapter behind that contract;
3. review codec-parameter and packet submission composition;
4. add bounded decode iteration, drain, and EOS handling;
5. separately review production backend activation and Windows device lifecycle;
6. perform controlled real-media validation before any playback claim.

Production composition is not selected as the immediate next milestone because
it would otherwise rely on the diagnostic fixture, omit demux timing semantics,
and combine activation, parser, decode-loop, and output ownership risks in one
change.

## Rejected alternatives

### Connect the diagnostic fixture to production activation

Rejected because the fixture is fixed to one video packet and one audio packet,
is not a real container, and cannot represent packet ordering, codec changes,
DTS, keyframes, or EOS.

### Implement FLV and MP4 parsers before defining an output contract

Rejected because parser-specific models would leak container details into decoder
composition and make bounds, ownership, and result semantics inconsistent.

### Use only packet bytes and PTS

Rejected because decode order can differ from presentation order, keyframe and
stream identity are required, and missing timing must remain distinguishable from
zero.

### Use FFmpeg avformat as the first demux implementation

Deferred. It would extend the native ABI/symbol gate, add format-context and I/O
ownership, and interact with the unresolved activation and distribution review.
The contract must remain usable by either managed parsers or a later reviewed
native adapter.

## Non-goals

No FLV parser, MP4 parser, avformat integration, network stream reader, seeking,
track switching, metadata extraction, continuous packet loop, decoder flush,
EOS drain, duration cutoff, production decoder ingress, backend activation,
Windows device activation, FFmpeg bundling, redistribution decision, or audible
or visible playback is included.

## Next

0.0.8v will implement the pure managed bounded demux output contract and focused
validation without parsing a real container or changing an activation path.
