# PCN-0083: Windows Output Scheduler Adapter Validation

## Scope

Validation covers the finite 0.0.6y scheduler reservation/acknowledgement and
one-item dispatch adapter without requiring a physical Windows output device.

## Covered behavior

- one video and one S16/F32 audio item dispatch;
- deterministic video/audio preference alternation;
- fixed empty result;
- success-only acknowledgement and disposal;
- item retention for WouldBlock, resize, device loss, initialization and
  unknown failure;
- strict format, layout, timestamp, sequence, discontinuity, and generation
  validation;
- undefined source/output values, exceptions, and cancellation;
- reset generation updates and repeated reset/dispose;
- audio-success/video-failure, cancellation, and disposal rollback to the prior
  audio generation;
- fixed partial-reset fault snapshot, dispatch rejection, retained queued item,
  and explicit-reset recovery when rollback fails;
- disposal waiting for an in-flight pump;
- non-Windows rejection before dispatcher access;
- production bounded-scheduler reservation seam without scheduler ownership
  transfer.

## Validation commands

- `dotnet build PeerCast-Nova.sln -c Release --no-restore`
- `dotnet test PeerCast-Nova.sln -c Release --no-build --no-restore`
- `dotnet test tests/Nova.Playback.Tests/Nova.Playback.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PlaybackWindowsOutputSchedulerAdapter`
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`
- `git diff --check`

## Remaining validation

No test assumes a Windows video or audio device exists. Controlled Windows
validation still needs a real scheduler item dispatched through actual D3D11
Present and WASAPI buffer/Start processing. Automatic continuous pumping,
timer drive, A/V synchronization, drift correction, and real-media playback
remain unimplemented.

## Results

- Release build: passed with six existing warnings for the unavailable local
  vcpkg library search path.
- Full suite: 663 passed, 7 failed, and 3 skipped (673 total). The seven failures
  are the existing installed-runtime locator tests returning
  `DirectoryUnavailable` in this workspace.
- 0.0.6y scheduler-adapter tests: 18 passed.
- Format verification and `git diff --check`: passed.
