# PCN-0118: FLV Session Decoder-Parameter Binding Validation

## Scope

0.0.8z implements the pure managed parameter-binding seam selected by ADR-0109.
It maps one live bounded FLV demux session into independently owned H.264/AAC
codec-parameter leases while preserving exact source-session identity.

It does not consume packet completions or perform native work.

## Acceptance criteria

### Source validation

- A source session is required.
- The source must not be disposed when preparation starts or before ownership
  transfer completes.
- Container kind is exactly FLV.
- Video is stream 0, video media, and H.264.
- Audio is stream 1, audio media, and AAC.
- Stream identifiers are distinct.
- Both time bases are exactly `1/1000`.
- Video has positive width/height and zero audio fields.
- Audio has positive sample rate/channel count and zero video fields.
- Both codec-data views are live and non-empty.
- Undefined or contradictory state fails closed before successful binding.

### Parameter creation

- Video parameters are created before audio parameters.
- Exact parser-owned width, height, sample rate, channel count, and codec data are
  passed to the existing factory.
- The factory independently validates and creates padded copies.
- Video rejection stops before audio creation.
- Audio rejection disposes the created video lease.
- Exceptions after partial creation dispose every created lease.
- Undefined factory results dispose unexpected leases and return `Faulted`.
- Source disposal during creation disposes both parameter leases and returns
  `SourceDisposed`.

### Binding identity and ownership

- Success owns exactly one video parameter lease and one audio parameter lease.
- The exact source-session object reference is retained non-owningly.
- Video/audio stream identifiers and time bases are snapshotted immutably.
- `ReferencesSourceSession` uses exact reference identity.
- Source-session disposal after successful preparation does not invalidate the
  independent parameter copies.
- Binding disposal does not dispose or mutate the source session.
- Binding disposal is idempotent and releases audio before video.
- Async disposal has the same ownership result.

### Result model

- `Prepared` always includes a live binding.
- `InvalidSession`, `SourceDisposed`, `VideoParametersRejected`,
  `AudioParametersRejected`, and `Faulted` include no binding.
- Null and incompatible sessions are `InvalidSession`.
- Disposed or concurrently disposed sources are `SourceDisposed`.

### Safety boundaries

- No parser state or behavior changes.
- No encoded packet copy or parser-completion consumption.
- No native runtime, ABI symbol, context, parameter transfer, or codec open.
- No packet submission, frame receive, flush, or drain.
- No PCP composition, scheduler ingress, backend activation, or Windows output.
- No AVCC-to-Annex-B conversion.
- FFmpeg activation, bundling, redistribution, and approval remain pending.

## Focused tests

The milestone suite covers:

- successful exact H.264/AAC parameter mapping and zero padding;
- exact source identity, stream identifiers, and FLV time bases;
- independent parameter ownership after source-session disposal;
- binding cleanup without source-session ownership;
- null and disposed source handling;
- non-FLV, alternate stream-ID, and alternate time-base rejection;
- video rejection before audio creation;
- audio rejection with partial-video cleanup;
- exception cleanup after video creation;
- undefined factory-result cleanup;
- source disposal during audio creation;
- async and idempotent binding disposal.

## Automated validation

The final branch head must pass restore, Release build, the full test suite, and
format verification on routine Ubuntu CI. The branch must remain based on current
main, contain only the expected implementation, tests, ADR, validation, and
progress-document updates, and have no unresolved review threads before merge.

Status: Implementation and focused review complete; final branch-head CI is the
merge gate.
