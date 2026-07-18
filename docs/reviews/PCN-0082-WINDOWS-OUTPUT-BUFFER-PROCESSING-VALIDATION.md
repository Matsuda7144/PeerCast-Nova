# PCN-0082: Windows Output Buffer Processing Validation

## Scope

Validation covers the finite 0.0.6x one-frame/one-buffer boundary and its
injectable production seam without requiring a physical Windows output device.

## Covered behavior

- one BGRA32 frame upload/render-target/Present processing result;
- interleaved S16 and F32 WASAPI buffer processing;
- one-time Start and restart after reset;
- padding/capacity `WouldBlock` behavior;
- strict stride, byte length, audio layout, and generation validation;
- fixed format, resize, device-lost, unknown-HRESULT, undefined-result, and
  exception handling;
- synchronized activation borrow with no transferred disposal authority;
- audio-before-video cleanup, repeated reset/dispose, and in-flight disposal;
- non-Windows rejection before native access;
- injected production native-call order without a real device.

## Validation commands

- `dotnet build PeerCast-Nova.sln -c Release --no-restore`
- `dotnet test PeerCast-Nova.sln -c Release --no-build --no-restore`
- `dotnet test tests/Nova.Playback.Tests/Nova.Playback.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PlaybackWindowsOutputBuffer`
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`
- `git diff --check`

## Remaining validation

No test assumes that a Windows video or audio device exists. A controlled
Windows environment is still required to validate a real swap-chain back-buffer
upload/Present, WASAPI endpoint padding/buffer behavior, Start, occlusion,
resize, and physical device-loss transitions. Scheduler integration, continuous
playback, A/V synchronization, and real-media playback remain unimplemented.

## Results

- Release build: passed with four existing warnings for the unavailable local
  vcpkg library search path.
- Full suite: 645 passed, 7 failed, and 3 skipped (655 total). The seven failures
  are the existing installed-runtime locator tests returning
  `DirectoryUnavailable` in this workspace.
- 0.0.6x buffer-processing and production-seam tests: 23 passed.
- Format verification and `git diff --check`: passed.
