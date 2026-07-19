# PCN-0097: Bounded Diagnostic Playback Fixture Validation

## Scope

Validate the diagnostic-only finite input contract introduced for milestone 0.0.8e.

## Required checks

1. Build and run the full test suite on Ubuntu and Windows.
2. Verify a round trip preserves exactly one H.264 packet and one AAC packet, including timestamps, durations, and payload bytes.
3. Verify the writer rejects unsupported codec roles, non-positive durations, negative timestamps, empty payloads, and payloads above the fixed limits.
4. Verify the reader rejects invalid magic, unsupported version, nonzero reserved data, unsupported codec roles, invalid timing, invalid lengths, truncation, and trailing data with fixed enum results.
5. Verify payload allocation occurs only after header, codec, timing, and length validation.
6. Verify the source stream is caller-owned and is not retained or disposed by the fixture reader.
7. Verify no retry loop, packet loop, seeking, container probe, path-bearing log, metadata string, native call, or background task is added.
8. Verify no changed file is `PlaybackDecoderBackendFactory.cs`, `IPlaybackDecoderBackend.cs`, or `PlaybackDecoderBackendPolicy.cs`.
9. Verify production activation and the 0.0.8d integration probe execution path remain unchanged.

## Fixed limits

- Video packet count: 1
- Audio packet count: 1
- Video codec: H.264
- Audio codec: AAC
- Maximum video payload: 1 MiB
- Maximum audio payload: 256 KiB
- Fixture version: 1

## Expected result

The fixture contract can be tested entirely in managed memory while FFmpeg license review remains `Pending`. Passing these checks does not claim MP4/FLV demux, native decode, start-recover-stop execution, audible output, or visible output.

## Commands

```powershell
dotnet restore PeerCast-Nova.sln
dotnet build PeerCast-Nova.sln --configuration Release --no-restore
dotnet test PeerCast-Nova.sln --configuration Release --no-build
dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore
```
