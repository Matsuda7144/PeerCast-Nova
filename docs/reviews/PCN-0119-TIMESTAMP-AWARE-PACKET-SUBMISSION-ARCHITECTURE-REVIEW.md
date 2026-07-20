# PCN-0119: Timestamp-aware Packet Submission Architecture Review

## Scope

0.0.9a reviews the ownership and ABI work required before one FLV demux event can enter an opened decoder without losing timing, EOS, source identity, or backpressure state. No runtime code is added.

## Reviewed boundaries

- bounded FLV parser and demux session
- demux completion ownership
- FLV decoder-parameter binding
- diagnostic native parameter and codec-open transactions
- native context lease
- packet send and frame receive boundary
- frame metadata boundary
- FFmpeg ABI symbol gate

## Findings

### Source-bound opened contexts are missing

0.0.8z owns exact H.264 and AAC parameter leases and retains the source demux session reference. The existing diagnostic native decoder lease owns two contexts but does not retain source-session identity, stream identifiers, or time bases.

A new source-bound opened native decoder session is required before real packet submission.

### The packet boundary loses metadata

The current packet boundary accepts payload bytes only. It cannot preserve PTS, DTS, duration, time base, keyframe state, stream identity, parser sequence, or discontinuity generation.

### AVPacket layout is not yet approved

The symbol gate verifies library major versions and required symbols. It does not prove an exact AVPacket layout. Any direct field write or setter mechanism requires a separate ABI review before implementation.

### The active parser completion is the pending owner

The parser already allows only one active completion. When decoder input requires frame drain, that completion must remain undisposed. Copying it into a second pending packet owner is rejected.

### EOS is explicit flush and bounded drain

EOS has no encoded payload. It must select one decoder context, submit an explicit null packet, and retain bounded drain state until end of stream. Empty encoded payload is not EOS.

## Selected sequence

### 0.0.9b — Source-bound Opened Native Decoder Session

Input:

- one live 0.0.8z parameter binding;
- distinct live video and audio ABI gate leases;
- distinct verified runtime owners behind those gates.

Operation order:

1. Snapshot exact source session, stream IDs, and time bases.
2. Create H.264 context.
3. Transfer video parameters.
4. Open video context.
5. Create AAC context.
6. Transfer audio parameters.
7. Open audio context.
8. Return one source-bound opened native decoder session.

Ownership requirements:

- consume the parameter binding and both supplied gate leases on every result;
- return no partial configured or opened success state;
- own exactly two opened contexts on success;
- retain the source session as a non-owning identity reference;
- dispose audio before video;
- distinguish invalid input, source disposal, context, transfer, open, and fault results;
- use injected operations for routine Ubuntu tests.

0.0.9b does not submit media packets or modify native packet ABI.

### 0.0.9c — AVPacket Metadata and Flush ABI Review

The later review must determine:

- exact approved runtime version identity;
- packet layout or verified setter mechanism;
- PTS, DTS, duration, time base, and keyframe mapping;
- unknown-value representation;
- stream-index handling;
- null-packet flush behavior;
- any ABI gate strengthening required.

No AVPacket field write is approved by 0.0.9a.

### Later finite submission state

After ABI approval, the submission path must:

- consume one active completion at a time;
- require exact source-session identity;
- preserve parser order and per-stream sequence;
- select one matching decoder context;
- retain completion on frame-drain pressure;
- use no hidden retry loop;
- dispose completion only after acceptance, cancellation, stale-session rejection, or terminal failure;
- flush and drain each stream explicitly at EOS;
- reject packets after flush;
- add no second encoded queue.

## Rejected approaches

- Reusing the diagnostic native lease for real FLV events.
- Sending payload while discarding timing metadata.
- Writing packet fields under a major-only version check.
- Treating empty payload as EOS.
- Copying active completion into another pending owner.
- Sharing one gate/runtime owner between both contexts.
- Combining context composition, ABI changes, and submission state in one milestone.

## Safety boundaries

- Documentation-only milestone.
- No native allocation, context creation, parameter transfer, codec open, packet send, flush, or frame receive.
- No parser completion consumption.
- No backend activation, PCP composition, scheduler change, Windows device activation, runtime distribution change, or playback claim.

## Validation

The final branch head must pass restore, Release build, the full test suite, and format verification on routine Ubuntu CI. The diff must contain only ADR-0111, PCN-0119, and synchronized progress documents, remain based on current main, and have no unresolved review threads before merge.

The first workflow attempts for the prior head failed before checkout and produced no job steps or logs. A fresh branch-head commit is used to obtain an independent merge-gate run without changing the architecture decision.

Status: Architecture review complete; final branch-head CI is the merge gate.
