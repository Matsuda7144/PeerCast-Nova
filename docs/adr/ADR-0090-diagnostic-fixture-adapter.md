# ADR-0090: Diagnostic Fixture Adapter

## Status

Accepted for diagnostic-only use.

## Decision

The 0.0.8e bounded fixture is adapted into exactly one H.264 encoded video packet and one AAC encoded audio packet after the 0.0.8d license and ABI gates.

The adapter owns only the stream returned by its caller-supplied factory. It copies no additional payload, retains no stream, performs no native call, and has no retry, seek, packet loop, background task, activation reference, or free-text result. The returned encoded input owns the managed payload arrays created by the bounded fixture reader.

The 0.0.8d phase plan records `DiagnosticFixtureAdapter` immediately after `FfmpegAbiSymbolGate` and before `NativeDecoderContextLease`.

## Fail-closed behavior

- `Pending` license review stops before runtime, media path, stream factory, or adapter access.
- Invalid or rejected fixtures return a fixed diagnostic-media result.
- Cancellation before opening the stream does not invoke the factory.
- Accepted encoded input stops at `RuntimeDecodeUnavailable` until native context, parameter transfer, codec open, and packet submission are connected in a later reviewed milestone.

## Non-goals

This decision does not approve FFmpeg activation, add MP4/FLV demux, connect native decode, produce decoded frames, activate Windows output, or claim start-recover-stop playback.
