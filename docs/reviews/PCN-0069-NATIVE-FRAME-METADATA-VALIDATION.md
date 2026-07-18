# PCN-0069 Native Frame Metadata Extraction Validation

## Scope

Milestone 0.0.6k converts only callback-scoped FFmpeg 8.1 `AVFrame` metadata to
fixed managed metadata. It does not copy media planes, convert formats, create
decoded frames, invoke sinks, demux, render, play audio, redistribute FFmpeg,
or activate the backend.

## Implemented evidence

- the native pointer remains inside the 0.0.6j callback and is always freed by
  that boundary;
- Windows x64 layout reads are isolated and pinned to the official FFmpeg 8.1
  public header and verified libavutil ABI major 60;
- video/audio kind and supported formats map to fixed managed enums;
- valid frame time bases convert known timestamps and duration with checked
  arithmetic; unset time bases, FFmpeg unknown timestamps, and zero duration
  produce fixed managed unknown values;
- negative duration, malformed time base, overflow, undefined format, and
  excessive dimensions, rates, channels, samples, or duration fail closed;
- bounds match `PlaybackDecodedFrameModels`;
- consumer rejection leaves the context usable, while unexpected native
  exceptions dispose it;
- non-Windows execution returns fixed `Unsupported` without native access;
- results contain no pointer, frame content, path, hash, symbol, or native
  error value.

## Completed validation

- Windows Release build: warning 0, error 0.
- Windows Release full suite: 422 discovered, 419 passed, 3 explicitly
  Windows-skipped tests covered by Ubuntu CI, failure 0.
- Related frame metadata/native frame group: 45 passed, failure 0, skip 0.
- `dotnet format PeerCast-Nova.sln --verify-no-changes`: passed.
- `git diff --check`: passed.

## Remote validation

- Updated Draft PR head CI: pending after push.
