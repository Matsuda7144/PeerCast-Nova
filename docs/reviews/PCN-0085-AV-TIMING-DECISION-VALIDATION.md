# PCN-0085: A/V Timing Decision Validation

## Scope

Validation covers the 0.0.7a one-item timing-decision boundary and injected
monotonic position/reset seams without a Windows output device.

## Covered behavior

- audio/video on-time, future Wait, and media-specific late decisions;
- inclusive early/late boundaries and maximum-wait rejection;
- not-started, backward, invalid, undefined, and exceptional clock handling;
- invalid timestamp, media kind, discontinuity, and generation handling;
- discontinuity latching without item consumption or automatic lower reset;
- production relative-origin establishment, explicit reset rebase, and
  discontinuity detection without automatic rebase;
- transactional generation reset in audio-to-video order, rollback, and fixed
  partial-reset fault state;
- origin reset after both generation resets, reverse video-to-audio rollback,
  recovery from origin-reset fault, and reset-wide Decide/Dispose exclusion;
- in-flight decision serialization, repeated disposal, and lower ownership;
- platform-independent operation with no native access or device dependency.

## Validation commands

- `dotnet build PeerCast-Nova.sln -c Release --no-restore`
- `dotnet test PeerCast-Nova.sln -c Release --no-build --no-restore`
- `dotnet test tests/Nova.Playback.Tests/Nova.Playback.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PlaybackAvTimingDecisionBoundary`
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`
- `git diff --check`

## Remaining validation

No test assumes a Windows video or audio device exists. Precise timers, actual
delay, frame drop/duplication, resampling, drift correction, complete A/V
synchronization, and controlled real-media playback remain unimplemented.

## Results

- Release build: passed with one existing warning for the unavailable local
  vcpkg library search path.
- Full suite: 717 passed, 7 failed, and 3 skipped (727 total). All seven
  failures are the existing installed-runtime locator tests returning
  `DirectoryUnavailable` in this workspace.
- 0.0.7a timing-decision tests: 31 passed.
- Format verification and `git diff --check`: passed.
