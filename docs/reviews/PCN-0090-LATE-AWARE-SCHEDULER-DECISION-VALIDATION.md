# PCN-0090: Late-Aware Scheduler Decision Validation

## Scope

Validation covers the 0.0.7f reservation-preserving seam between one scheduler timing
decision and the 0.0.7e late reservation disposition boundary.

## Covered behavior

- the same live reservation reaches late disposition;
- explicit video-drop candidates are consumed exactly once;
- process-now, wait, retained late, audio recovery, reset, invalid, cancellation, and
  failure paths retain the reservation;
- generation mismatch and discontinuity latch reset required;
- only consumed video drop advances media preference;
- reset synchronizes timing/disposition generations and restores video-first preference;
- unsupported platforms do not reserve an item;
- no output dispatch, waiting loop, continuous pump, keyframe selection, audio recovery
  effect, timer ownership, or native resource use.

## Validation commands

- `dotnet build PeerCast-Nova.sln -c Release --no-restore`
- `dotnet test PeerCast-Nova.sln -c Release --no-build --no-restore`
- `dotnet test tests/Nova.Playback.Tests/Nova.Playback.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PlaybackLateAwareSchedulerDecisionBoundary`
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`
- `git diff --check`

## Remaining validation

A later milestone must integrate bounded wait/redecision and process-now output dispatch with
this reservation-preserving late path, then connect the combined operation to the finite
continuous pump. Controlled audible/visible real-media playback remains unimplemented.
