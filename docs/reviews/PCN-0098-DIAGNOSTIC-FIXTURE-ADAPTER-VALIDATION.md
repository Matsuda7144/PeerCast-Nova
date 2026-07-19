# PCN-0098: Diagnostic Fixture Adapter Validation

## Scope

Validate the diagnostic-only adapter from the 0.0.8e bounded fixture into the 0.0.8d `RuntimeDecode` phase.

## Required checks

1. Build and run the full test suite on Ubuntu and Windows.
2. Verify one H.264 packet and one AAC packet preserve payload, timestamp, and duration.
3. Verify malformed fixture input returns `FixtureRejected` with no encoded input.
4. Verify cancellation before stream creation does not invoke the stream factory.
5. Verify the factory-returned stream is disposed after one finite read.
6. Verify `DiagnosticFixtureAdapter` appears exactly once after `FfmpegAbiSymbolGate` and before `NativeDecoderContextLease`.
7. Verify `Pending` license review still stops before runtime and diagnostic-media path access.
8. Verify production decoder activation contracts remain unchanged.
9. Verify no retry, seeking, packet loop, background task, native call, path log, or free-text result was added.

## Expected current result

The pinned FFmpeg candidate remains `Pending`, so the CLI probe continues to return `LicenseReviewPending` before entering `RuntimeDecode` or opening the fixture.

After a future approval, a valid fixture may be accepted by the adapter, but the probe must then stop with `RuntimeDecodeUnavailable` until native context and packet-processing connections are separately reviewed.

## Commands

```powershell
dotnet restore PeerCast-Nova.sln
dotnet build PeerCast-Nova.sln --configuration Release --no-restore
dotnet test PeerCast-Nova.sln --configuration Release --no-build
dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore
```
