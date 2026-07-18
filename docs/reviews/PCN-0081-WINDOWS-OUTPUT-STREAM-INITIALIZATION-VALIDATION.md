# PCN-0081 Windows Output Stream Initialization Validation

## Scope

0.0.6w adds finite injectable initialization state for future Windows video
targets and WASAPI shared/event-driven streams. It performs no rendering,
presentation, audio buffer processing, or playback start.

## Contract checks

- Bound BGRA32 video and interleaved S16/F32 audio initialize with independent generations.
- Activation/binding prerequisites, platform, configuration, format, resize,
  device-loss, cancellation, disposal, and failure outcomes are fixed.
- Window identity is an opaque token; no OS handle crosses the public contract.
- Audio buffer duration is bounded from 1 ms through 2 seconds.
- Audio partial failure rolls back audio then video.
- Resize and independent reset/generation transitions are explicit.
- Undefined values, exceptions, missing resources, and unknown results fail closed.
- Dispose waits for in-flight initialization and is idempotent.
- Non-Windows does not access the native initializer.
- Tests require no physical video or audio device.
- Production seam tests cover typed-resource/window resolution order, audio
  event/client recreation order, HRESULT normalization, unknown HRESULT,
  and activation-owned reset delegation without requiring a device.

## Verification

- Release build: passed with 6 environment warnings for the missing
  `c:\vcpkg\installed\x64-windows\lib` search path.
- Full test suite: 622 passed, 7 failed, 3 skipped (632 total). The same seven
  installed-runtime locator tests documented by 0.0.6u and 0.0.6v returned
  `DirectoryUnavailable` in this Windows environment.
- Windows output stream-initialization and production-seam tests: 23 passed.
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed.

Status: Verified with the documented pre-existing environment failures.
