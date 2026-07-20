# ADR-0108: Pure Managed Bounded FLV v1 Parser

- Status: Accepted
- Milestone: 0.0.8x
- Production FFmpeg activation: Pending and unchanged

## Context

ADR-0107 selected FLV v1 as the first concrete parser behind the 0.0.8v
container-neutral demux output contract. The current PCP data plane is sequential,
callback-scoped, and non-seekable in practice. The parser therefore must preserve
upstream backpressure rather than create a stream reader, resident task, or second
packet queue.

The first parser needs enough real container semantics to establish one H.264
video stream and one AAC audio stream and to emit bounded encoded packets, while
remaining independent from PCP composition, decoder submission, FFmpeg backend
activation, and Windows output activation.

## Decision

Add `PlaybackBoundedFlvParser` as a pure managed, caller-pushed FLV v1 parser.

### Public operation

One synchronous `Parse` operation receives:

- one caller-owned `ReadOnlyMemory<byte>` chunk;
- an explicit final-input flag;
- an explicit discontinuity flag;
- a cancellation token.

It returns fixed state only:

- parser result;
- current-chunk consumed-byte count;
- at most one 0.0.8v read completion;
- stream byte position;
- current carry byte count;
- skipped-tag count for the operation;
- parser generation.

The parser never retains or disposes the caller chunk. The caller may dispose the
returned completion and submit the unconsumed suffix in another finite operation.

### Ownership and backpressure

The parser owns:

- at most one incomplete FLV record carry buffer;
- temporary AVC configuration during bootstrap;
- one established 0.0.8v demux session;
- parser sequence, EOS, generation, and byte-position state;
- a reference to the one active completion until the caller disposes it.

A second parse while a completion is active returns `ActiveCompletion` and
consumes no input. Completion payload ownership remains entirely in the 0.0.8v
session. Parser disposal disposes the active completion before the session and
clears carry and temporary codec bytes.

The parser does not own a stream, PCP packet, relay registration, decoder,
scheduler, sink, native lease, device, or background task.

### Bounded carry

Carry is permitted only for one incomplete:

- FLV header plus `PreviousTagSize0`;
- tag header;
- tag body;
- trailing `PreviousTagSize`.

The target record length is validated before carry growth. The parser does not
copy an unconsumed caller suffix, retain a complete unreported packet, or maintain
an output-event queue.

Parser options bound:

- media tag data bytes;
- script tag data bytes;
- carry bytes;
- skipped tag bytes per operation;
- NAL units per packet;
- parameter-set count;
- SPS RBSP bytes;
- SPS picture-order cycle offsets.

The nested 0.0.8v options continue to bound input observation, packet payload,
codec data, packet count, aggregate owned bytes, skipped records, time-base
components, and timestamp magnitude.

### FLV framing

The accepted header is deliberately narrow:

- signature `FLV`;
- version 1;
- both audio and video flags and no unsupported flag bits;
- data offset 9;
- `PreviousTagSize0 == 0`.

Each tag validates:

- exact audio, video, or script type;
- checked 24-bit data size;
- timestamp and timestamp extension;
- zero stream ID;
- exact trailing previous-tag size.

Undefined, encrypted, reserved, or unsupported tag forms fail closed. No byte
scanning or resynchronization is attempted after malformed input.

### Bootstrap

Bootstrap requires:

1. the supported FLV header;
2. one AVC sequence header;
3. one AAC sequence header;
4. media only after the immutable 0.0.8v session is created.

Script tags may be skipped under operation count and byte limits. They are never
parsed as authoritative metadata. Early media is not buffered. AAC configuration
before valid AVC configuration fails closed.

Exact duplicate sequence headers after session creation are skipped under the
same bounds. Changed configuration fails closed because 0.0.8v has no mutable
stream-description event.

### AVC subset

Video tags require codec ID AVC.

AVC sequence headers require keyframe form, zero composition offset, and a bounded
`AVCDecoderConfigurationRecord`. The parser validates:

- configuration version and reserved bits;
- one to the configured maximum SPS and PPS records;
- supported one-, two-, or four-byte AVCC NAL length fields;
- expected SPS and PPS NAL types;
- matching profile and level;
- consistent dimensions across SPS records;
- no unsupported trailing AVC configuration extension.

A bounded H.264 bit reader removes emulation-prevention bytes and derives coded
width and height, including interlaced height and frame cropping. Baseline,
main, and the recognized extended profile syntax are parsed without using script
metadata. Unsupported or contradictory syntax fails explicitly.

AVC NALU tags preserve length-prefixed AVCC payload. A bounded length walk
requires every NAL length and type to fit exactly before 0.0.8v copies payload
ownership. No Annex B conversion occurs.

FLV timestamp is DTS in milliseconds. The signed 24-bit composition offset is
added with checked arithmetic to form PTS. Both values use the 0.0.8v checked
rational timestamp converter. Duration remains unknown. FLV frame type maps to
keyframe or non-keyframe.

AVC end-of-sequence maps to video EOS and prevents later video media.

### AAC subset

Audio tags require sound format AAC. Legacy FLV sound-rate, sound-size, and
sound-type bits do not synthesize configuration.

AAC sequence headers contain bounded AudioSpecificConfig. The parser accepts AAC
Low Complexity, indexed or explicit supported sample rate, and explicit channel
configurations 1 through 7. Program Config Elements, HE-AAC extension signalling,
and unsupported object types fail explicitly.

AAC raw payload excludes the two-byte FLV AAC header. Tag timestamp is both PTS
and DTS. Duration remains unknown and keyframe state is `NotApplicable`.

### EOS and final input

AVC end-of-sequence emits video EOS through the 0.0.8v session. Final input:

- returns `TruncatedData` for incomplete framing;
- returns `InvalidStreamDescription` before both sequence headers;
- emits EOS for each selected stream not already ended;
- obtains final `EndOfStream` only through 0.0.8v session rules.

No decoder null packet, flush, or drain operation is produced.

### Discontinuity and failure

Explicit discontinuity is accepted only when no completion is active. It disposes
the old session, clears carry and temporary configuration, resets bootstrap,
sequence, EOS, and byte-position state, and increments generation. The supplied
chunk must begin a new supported FLV stream.

Structural, codec, timestamp, allocation, and contradictory session failures
return one fixed result and latch the parser faulted. Later calls return `Faulted`.
Cancellation before parsing, an oversized caller chunk that may be split, active
completion, and disposal remain explicit non-mutating outcomes.

## Consequences

PeerCast Nova now has a real, bounded, non-seekable H.264/AAC FLV parser that can
be exercised entirely on routine Ubuntu CI. The output is the same immutable
0.0.8v session and completion model that later MP4, fragmented MP4, or native
adapters can target.

The parser is not connected to PCP ingress or a decoder. Passing its immutable
stream description into codec-parameter ownership and its packet events into the
existing bounded packet boundary requires a separate composition review.

## Non-goals

No AMF parser, metadata extraction, seek index, FLV resynchronization, encrypted
FLV, codec configuration changes, HE-AAC, Program Config Element, Annex B
conversion, MP4 or fragmented-MP4 parser, avformat, PCP adapter, stream reader,
resident task, producer queue, decoder submission, decode loop, flush, drain,
duration cutoff, scheduler composition, backend activation, Windows device
activation, FFmpeg bundling, redistribution decision, or audible or visible
playback is included.

## Next

Review the finite parser-to-decoder composition seam. The review must preserve
0.0.8v completion backpressure, map immutable stream descriptions through the
existing managed codec-parameter owner, submit at most a bounded event set, and
keep production activation unchanged.
