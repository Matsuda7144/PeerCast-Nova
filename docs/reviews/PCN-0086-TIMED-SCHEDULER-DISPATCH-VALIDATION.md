# PCN-0086: Timed Scheduler Dispatch Validation

## Scope

Validation covers the 0.0.7b one-reservation timing/dispatch bridge and its
production scheduler reservation seam without requiring a Windows device.

## Covered behavior

- successful video/audio ProcessNow dispatch and consumption;
- exact bounded Wait, same-reservation redecision, decision-count, total-wait,
  and single-delay termination;
- retained late, discontinuity, generation, clock, backpressure, resize,
  device-loss, initialization, and binding outcomes;
- cancellation, concurrent dispatch rejection, reset delay interruption,
  partial-reset recovery, and idempotent disposal;
- success-only fairness advancement and exactly-once reservation completion;
- scheduler/timing/processor/frame ownership preservation;
- non-Windows fixed result before reservation or native output access;
- production scheduler reservation and device-free injected seams.

## Validation commands

- `dotnet build PeerCast-Nova.sln -c Release --no-restore`
- `dotnet test PeerCast-Nova.sln -c Release --no-build --no-restore`
- `dotnet test tests/Nova.Playback.Tests/Nova.Playback.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PlaybackTimedSchedulerDispatchBoundary`
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`
- `git diff --check`

## Remaining validation

No test assumes a Windows output device exists. Late-item drop policy, precise
timers, drop/duplication, resampling/stretch, drift correction, complete A/V
synchronization, and controlled real-media playback remain unimplemented.

## Results

- Release build: passed with 12 existing warnings for the unavailable local
  vcpkg library search path.
- Full suite: 743 passed, 7 failed, and 3 skipped (753 total). All seven
  failures are the existing installed-runtime locator tests returning
  `DirectoryUnavailable` in this workspace.
- 0.0.7b timed-dispatch tests: 26 passed.
- Format verification and `git diff --check`: passed.
