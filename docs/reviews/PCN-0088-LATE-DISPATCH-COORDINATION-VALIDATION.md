# PCN-0088: Late Dispatch Coordination Validation

## Scope

Validation covers the 0.0.7d pure coordination boundary between the existing
0.0.7b timed-dispatch late results and the 0.0.7c late-item disposition policy.
No scheduler reservation, Windows device, native runtime, wall clock, or media
payload is required.

## Covered behavior

- default retention of video and audio late dispatch results;
- configured video drop-candidate and audio recovery recommendations;
- exact dispatch-result/media pairing;
- rejection of processed, wait, retry, mismatched, and undefined results;
- generation mismatch and discontinuity latching `ResetRequired`;
- explicit generation reset across coordination and policy boundaries;
- idempotent disposal without disposing the injected policy;
- no acknowledgement, consumption, release, frame disposal, drop, resampling,
  stretch, delay, or retry effect.

## Validation commands

- `dotnet build PeerCast-Nova.sln -c Release --no-restore`
- `dotnet test PeerCast-Nova.sln -c Release --no-build --no-restore`
- `dotnet test tests/Nova.Playback.Tests/Nova.Playback.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PlaybackLateDispatchCoordinationBoundary`
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`
- `git diff --check`

## Remaining validation

A later boundary must hold an actual scheduler reservation while applying this
coordination result. Actual video drop acknowledgement, keyframe policy, audio
recovery effects, resampling/stretch, drift correction, precise timers,
complete A/V synchronization, and controlled real-media playback remain
unimplemented.
