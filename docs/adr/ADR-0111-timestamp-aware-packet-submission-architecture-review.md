# ADR-0111: Timestamp-aware Packet Submission Architecture Review

- Status: Accepted
- Milestone: 0.0.9a
- Selected next implementation: 0.0.9b source-bound opened native decoder session
- Production FFmpeg activation: Pending and unchanged

## Context

0.0.8x added one-event-at-a-time bounded FLV parsing. Each active demux
completion preserves parser order, exact stream identity, sequence, PTS, DTS,
optional duration, keyframe state, discontinuity, encoded payload ownership, and
explicit per-stream EOS.

0.0.8z added a pure managed decoder-parameter binding. It retains a non-owning
reference to the exact source demux session and owns independent H.264 and AAC
parameter leases. It intentionally does not create native contexts or submit
packets.

The repository already contains diagnostic-only native context, parameter
transfer, codec-open, packet-send, and frame-receive boundaries. Those
boundaries were built around a fixed paired fixture and do not yet form a safe
source-bound path for real FLV completions.

## Findings

### An opened source-bound native decoder session is missing

`PlaybackBoundedFlvDecoderParameterBinding` retains the source session identity,
stream identifiers, time bases, and owned parameters, but no native decoder
contexts.

`PlaybackDiagnosticNativeParameterTransaction` and
`PlaybackDiagnosticCodecOpenTransaction` can create, configure, and open two
contexts, but their output `PlaybackDiagnosticNativeDecoderLease` does not
retain the FLV source session identity or stream metadata. Direct reuse would
allow one parser session's completion to be paired accidentally with another
session's native contexts.

The diagnostic flow also exposes separate parameter-transfer and codec-open
transactions. A production-facing source-bound lease should not expose a
partially configured or partially opened state.

### The current packet boundary loses required metadata

`PlaybackDecoderFfmpegPacketFrameBoundary.SendPacket` accepts only encoded
payload bytes. Its Windows native implementation allocates an `AVPacket`, copies
payload through `av_packet_from_data`, calls `avcodec_send_packet`, and frees the
packet.

It cannot currently set or preserve:

- PTS;
- DTS;
- duration;
- packet time base;
- keyframe flags;
- selected stream identity;
- parser sequence;
- discontinuity generation.

Dropping these fields merely to reuse the old boundary is rejected. The decoded
frame metadata and scheduler path depend on correct timing provenance.

### Decoder backpressure requires retained parser ownership

The packet boundary maps native retry pressure to `NeedFrameDrain`. The active
FLV parser completion must remain undisposed while that packet has not been
accepted. Disposing it would clear the encoded payload; copying it into a second
pending packet would create another encoded-packet queue and defeat the parser's
existing one-completion backpressure.

The native `AVPacket` itself does not need to remain allocated across a retry.
A later finite submission operation can reconstruct it from the still-owned
parser completion after bounded frame drain.

### EOS needs a distinct native operation

An EOS demux event has no encoded payload. The current packet boundary rejects
empty payloads and exposes no null-packet flush operation.

EOS must therefore select one decoder context, submit an explicit null packet,
and then permit bounded frame receive attempts until that context reports end of
stream. It must not be represented by an empty encoded packet.

### Packet layout is a separate ABI risk

The project already uses Windows x64 FFmpeg 8.1 structure offsets for selected
`AVFrame` and `AVCodecParameters` fields. The current ABI symbol gate verifies
library major versions and required symbols, but does not itself prove an exact
`AVPacket` layout.

Writing `AVPacket` PTS, DTS, duration, flags, or time base by offset without a
separate layout review would add an unverified native-memory contract. No such
write is approved by this ADR.

### Source identity is the discontinuity boundary

A parser discontinuity disposes and replaces its demux session. The later
submission path must receive the current source session explicitly and require
reference equality with the source session retained by the native decoder
session. A completion from a stale generation must be rejected without native
submission.

## Decision

### 0.0.9b: Source-bound opened native decoder session

Implement one finite composition that consumes:

- one live `PlaybackBoundedFlvDecoderParameterBinding`;
- one live video ABI gate lease;
- one live audio ABI gate lease.

The two gate leases and their verified runtime owners must be distinct because
each resulting context owns and disposes its complete gate/runtime chain.

The composition must:

- snapshot the exact source demux session, stream identifiers, and time bases;
- require a live, undisposed FLV parameter binding;
- create the H.264 context first;
- transfer the video parameter lease;
- open the video context;
- create the AAC context second;
- transfer the audio parameter lease;
- open the audio context;
- expose no partially configured or partially opened success state;
- consume and dispose the parameter binding on every result;
- consume both supplied gate leases on every result;
- dispose partial audio ownership before video ownership;
- return one source-bound opened native decoder session on success;
- retain only a non-owning source-session reference plus immutable stream IDs and
  time bases;
- own the two opened context leases and dispose audio before video;
- provide exact source-reference and stream-to-context selection checks;
- use fixed fail-closed results for invalid input, video or audio context,
  transfer or open rejection, source disposal, and fault.

The default implementation may use existing context factory, parameter transfer,
and codec-open primitives. Focused tests must use injected operations so routine
Ubuntu CI does not activate a native runtime.

0.0.9b does not submit packets or modify packet ABI.

### 0.0.9c: AVPacket metadata and flush ABI review

Before runtime packet metadata writes, perform a separate architecture and ABI
review that must determine:

- the exact approved Windows x64 FFmpeg runtime version tuple;
- the exact `AVPacket` field layout or another verified setter mechanism;
- PTS, DTS, duration, flags, stream index, and time-base representation;
- unknown timestamp and unknown duration representation;
- keyframe flag mapping;
- whether the gate must verify exact minor/micro versions or another layout
  fingerprint;
- explicit null-packet send semantics;
- privacy-safe failure behavior and cleanup.

The review must not assume that an avcodec major-version match alone proves
layout compatibility.

### Later: Native metadata boundary and finite submission state machine

After the ABI review, add a native packet boundary that accepts one immutable
managed descriptor and an explicit flush operation.

A later pure managed finite submission state machine must then:

- accept one active demux completion at a time;
- require exact source-session identity with the opened native decoder session;
- preserve parser order and monotonically increasing per-stream sequence;
- select exactly one context from stream ID and media kind;
- preserve known or unknown PTS and DTS, optional duration, time base, and
  keyframe state;
- retain the parser completion when send returns `NeedFrameDrain`;
- perform no hidden retry loop;
- dispose the completion only after packet acceptance, explicit cancellation,
  stale-session rejection, or terminal failure;
- submit EOS as null-packet flush and retain bounded per-context drain state;
- reject packets after stream flush;
- reject stale discontinuity generations;
- create no second encoded-packet queue or resident decoder loop.

Discontinuity remains an orchestration boundary: dispose the old native decoder
session and create a new source-bound session for the parser's replacement demux
session.

## Rejected alternatives

### Reuse `PlaybackDiagnosticNativeDecoderLease`

Rejected because it does not retain the exact FLV source session, stream IDs, or
time bases and therefore cannot reject cross-session packet composition.

### Reuse the payload-only packet boundary and discard timing

Rejected because PTS, DTS, duration, keyframe state, and packet time base are
required provenance, not optional diagnostics.

### Write AVPacket fields under the current major-only gate

Rejected because required field offsets have not been independently validated
against an exact approved runtime layout.

### Represent EOS as an empty packet

Rejected because empty payload is already invalid and native decoder flushing is
a separate operation.

### Copy a completion into another pending-packet owner

Rejected because it creates a second encoded-packet queue. The active parser
completion is already the bounded pending owner.

### Share one gate or runtime owner between both contexts

Rejected because each context disposes its complete parent chain. Shared parent
ownership would allow one context cleanup to invalidate the other.

### Combine context creation, packet ABI changes, and submission state in one milestone

Rejected because source identity, native ownership, layout validation, and
backpressure are independently testable risks with different platform gates.

## Non-goals

No runtime code is added in 0.0.9a. No native context is created, no parameter is
transferred, no codec is opened, no `AVPacket` field is written, no packet or
flush is submitted, no frame is drained, and no parser completion is consumed.
PCP composition, scheduler ingress, backend activation, Windows device
activation, FFmpeg bundling, redistribution approval, and audible or visible
playback remain outside scope.

## Next

0.0.9b will implement the source-bound opened native decoder session composition
with injected cross-platform tests. Packet metadata and flush remain blocked by
the separate 0.0.9c ABI review.
