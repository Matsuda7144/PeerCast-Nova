# ADR-0107: First Bounded Container Parser Selection

- Status: Accepted
- Milestone: 0.0.8w
- Selected next implementation: bounded FLV v1 demux parser
- Production FFmpeg activation: Pending and unchanged

## Context

0.0.8v added a parser-neutral pure managed demux output contract with one H.264
video stream, one AAC audio stream, finite packet and EOS completions, separate
PTS and DTS, count and byte bounds, explicit ownership, and caller-driven
backpressure.

The next step must select one real container without combining parser work with
codec submission, decode iteration, production backend activation, or Windows
output activation.

The existing PCP ingress is sequential and non-seekable in practice:

- `PcpChannelPacket` exposes callback-scoped payload memory, a sequence, stream
  position, and explicit discontinuity information;
- `IPcpChannelPacketConsumer.ConsumeAsync` is the upstream backpressure boundary;
- `PcpRelayFanout` consumes packets sequentially and does not expose a seekable
  media file.

PeerCastStation documentation describes H.264 plus AAC FLV as a normal PeerCast
streaming case and provides FLV-to-MPEG-2-TS remuxing for that exact combination.
FLV therefore matches the current network shape and the 0.0.8v H.264/AAC contract
more directly than a seek-oriented MP4 parser.

## Compared options

### FLV v1

FLV is a sequential header followed by interleaved tags and previous-tag-size
records. Its first-version model permits at most one audio and one video stream,
which matches the first 0.0.8v session description.

For the selected codec subset:

- AVC video tags expose a decode timestamp in the FLV tag timestamp;
- AVC NALU tags expose a signed composition-time offset, allowing PTS and DTS to
  remain separate;
- video frame type exposes keyframe state;
- AVC sequence headers carry an AVCDecoderConfigurationRecord and SPS/PPS data;
- AAC sequence headers carry AudioSpecificConfig;
- AAC raw tags carry encoded access-unit payloads;
- tags can be parsed incrementally without seeking or a complete-file index.

### MP4 and fragmented MP4

ISO BMFF requires a box hierarchy and track/sample metadata. The streaming
fragmented form separates an initialization segment (`ftyp` plus `moov`) from
media segments (`moof` plus one or more `mdat` boxes). A bounded implementation
must resolve track defaults, sample descriptions, fragment runs, data offsets,
decode-time bases, composition offsets, random-access state, and box nesting.

That work is valid later, but it is a larger first parser cut and would not
exercise the PeerCast callback-chunk boundary as directly as FLV.

## Decision

Select a pure managed bounded FLV v1 parser as 0.0.8x.

MP4, fragmented MP4, F4V, MPEG-2 TS, MKV, OGG, and native avformat remain later
separately reviewed parsers or adapters.

## 0.0.8x parser shape

### Caller-pushed finite chunks

The parser receives one caller-owned `ReadOnlyMemory<byte>` chunk per public
operation. It must not retain or dispose the caller's memory, stream, network
packet, relay registration, or session.

One operation returns:

- a fixed parser result;
- the number of input bytes consumed;
- at most one 0.0.8v read completion;
- bounded parser-state counters only.

The caller may consume and dispose the returned completion, then submit the
unconsumed suffix in another operation. This permits the caller to remain inside
the original PCP callback when several finite parser operations are needed.

### Bounded carry, not a producer queue

The parser may own one bounded carry buffer only for an incomplete FLV header,
tag header, tag body, or previous-tag-size field split across caller chunks.

It must not retain:

- a complete unreported media packet;
- multiple complete tags;
- a queue of output events;
- a caller input suffix that could instead be reported as unconsumed;
- an unbounded script-data or metadata object.

When output count or owned-byte budget is reached, parsing stops before consuming
the next complete output tag and reports the consumed byte count. This preserves
0.0.8v completion backpressure without introducing a second queue.

### Bootstrap before media

The first parser version requires, in order:

1. FLV signature, version 1, audio and video flags, and the supported fixed
   header layout;
2. `PreviousTagSize0 == 0`;
3. one AVC sequence header;
4. one AAC sequence header;
5. media tags only after both codec configurations are valid.

Script-data tags may appear during bootstrap or media parsing, but are skipped
only under record-count and byte-observation limits. AMF metadata is not parsed
or used to synthesize stream configuration.

A media tag before both codec sequence headers is complete fails closed. The
parser does not buffer early media while waiting for the other stream's
configuration.

### FLV structural validation

The parser validates before allocation or state commit:

- FLV signature and version;
- required audio and video flags;
- supported data offset;
- tag type, data size, timestamp, timestamp extension, and zero stream ID;
- exact previous-tag-size values;
- all checked size additions;
- configured input, tag, carry, skip, packet-count, and packet-byte limits.

Script-data tags are the only skipped tag class in the first version. Encrypted,
reserved, command-only, or undefined tag forms fail closed.

No byte-scanning resynchronization is attempted after malformed data. Explicit
input discontinuity resets parser bootstrap state and clears carry; parsing then
requires a new FLV header and codec sequence headers.

### AVC subset

The first parser accepts video tags only when:

- codec ID is AVC;
- frame type is keyframe or inter frame for media packets;
- AVC packet type is sequence header, NALU, or end of sequence;
- the signed 24-bit composition-time field is structurally valid;
- sequence-header codec data and NALU payloads remain within 0.0.8v limits.

The AVC sequence header is retained as bounded codec extradata. A bounded SPS
bit reader derives coded width and height, including frame cropping, without
using script metadata. Unsupported or contradictory SPS/PPS/configuration data
returns `UnsupportedCodec` or `InvalidStreamDescription`.

AVC NALU payload remains length-prefixed in container form; the parser does not
convert it to Annex B. It performs a bounded length walk using the configured
NAL length size so malformed or overrun NAL units fail before payload ownership
transfer.

FLV tag timestamp is converted from unsigned milliseconds to DTS. PTS is the
checked sum of DTS and the signed composition-time offset. Duration remains
unknown. Keyframe state comes from FLV frame type.

AVC end-of-sequence maps to video EOS. Later video media in the same parser
session fails closed.

### AAC subset

The first parser accepts audio tags only when:

- sound format is AAC;
- AAC packet type is sequence header or raw;
- sequence-header and raw payload lengths remain bounded.

The AAC sequence header is retained as bounded codec extradata. A bounded
AudioSpecificConfig bit reader derives the actual sample rate and channel count.
The first version supports AAC Low Complexity with an explicit supported channel
configuration; HE-AAC extension signalling, Program Config Elements, and codec
configuration changes remain later work.

Legacy FLV sound-rate, sound-size, and sound-type bits are not used to synthesize
the AAC session description. AAC raw-tag timestamp is both PTS and DTS. Duration
remains unknown and keyframe state is `NotApplicable`.

### Codec-configuration repetition

After a session description is established:

- an exact byte-for-byte duplicate AVC or AAC sequence header may be skipped
  under the skipped-record budget;
- a changed sequence header fails closed;
- no mutable stream-description or codec-change event is introduced.

A future discontinuity-aware restart may create a new parser and 0.0.8v session.
It must not mutate the existing session description.

### EOS and final input

A caller marks a chunk as final explicitly.

- final input with an incomplete header, tag, or previous-tag-size field returns
  `TruncatedData`;
- final input before both valid sequence headers returns
  `InvalidStreamDescription`;
- otherwise the parser emits EOS for every selected stream not already ended and
  returns final `EndOfStream` only through the existing 0.0.8v rules;
- no decoder flush or drain packet is produced.

### Ownership and state commit

The parser validates a complete finite event set before asking the 0.0.8v
session to create an owned completion. Parser sequence, carry, configuration,
input-offset, and EOS state advance only after completion construction succeeds.

Cancellation, allocation failure, contradictory 0.0.8v results, and undefined
enums leave no partially committed parser state or retained packet payload.

## Rejected immediate alternatives

### Implement fragmented MP4 first

Rejected for the first parser because initialization/media segment separation,
nested boxes, sample defaults, fragment runs, and data offsets create a larger
state and validation surface before the callback-chunk path has been proven.

### Implement ordinary seekable MP4 first

Rejected because a classic sample-table parser naturally assumes complete-file
metadata and random access, while the current PCP ingress exposes sequential
callback-scoped chunks.

### Use avformat as the parser

Deferred because this would add native format-context and I/O ownership, expand
the ABI/symbol gate, and interact with unresolved FFmpeg activation and
distribution decisions.

### Parse script metadata for dimensions and audio configuration

Rejected because metadata may be missing, stale, contradictory, or attacker
controlled. Codec sequence headers are the authoritative source for the selected
subset.

### Copy every caller chunk into an internal parser queue

Rejected because it duplicates existing network backpressure and violates the
no-second-queue decision. Only one incomplete-record carry buffer is permitted.

## Non-goals

No parser implementation, real FLV fixture, AMF parser, metadata extraction,
seek index, MP4/fMP4 parser, F4V extensions, MPEG-2 TS, MKV, OGG, encrypted FLV,
RTMP handshake, PCP-to-parser composition, decoder codec-parameter composition,
packet submission, decode loop, flush, drain, duration cutoff, backend
activation, Windows device activation, FFmpeg bundling, redistribution decision,
or audible or visible playback is included in 0.0.8w.

## References

- PeerCast Nova callback-scoped packet and backpressure contracts:
  `src/Nova.Network/Pcp/PcpChannelPacket.cs`,
  `src/Nova.Network/Pcp/IPcpChannelPacketConsumer.cs`, and
  `src/Nova.Network/Pcp/PcpRelayFanout.cs`.
- Adobe Flash Video File Format Specification, version 10.1:
  http://download.macromedia.com/f4v/video_file_format_spec_v10_1.pdf
- PeerCastStation FLV-to-MPEG-2-TS feature note:
  https://www.pecastation.org/blog/2017/04/16/flvtots.html
- W3C ISO BMFF Byte Stream Format:
  https://www.w3.org/TR/mse-byte-stream-format-isobmff/

## Next

0.0.8x will implement the pure managed bounded FLV v1 parser and focused
cross-platform tests behind the 0.0.8v contract. It will not connect PCP ingress,
FFmpeg packet submission, production activation, or Windows output.
