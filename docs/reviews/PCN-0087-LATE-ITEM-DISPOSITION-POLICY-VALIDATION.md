# PCN-0087: Late Item Disposition Policy Validation

## Scope

Validation covers the 0.0.7c pure late-item disposition boundary without a
scheduler reservation, Windows output device, native runtime, wall clock, or
recalculation of presentation timing.

## Covered behavior

- default retention of video and audio late results;
- explicitly configured video drop-candidate and audio recovery recommendations;
- direct use of existing `VideoLate` and `AudioLate` timing results;
- rejection of mismatched media/result pairs and non-late timing results;
- invalid and undefined media, timing-result, discontinuity, and policy values;
- generation mismatch and discontinuity latching `ResetRequired`;
- independent video/audio generation reset and recovery from fault;
- idempotent disposal and lower-resource ownership preservation;
- no scheduler consumption, frame disposal, drop, resampling, or stretch.

## Validation commands

- `dotnet build PeerCast-Nova.sln -c Release --no-restore`
- `dotnet test PeerCast-Nova.sln -c Release --no-build --no-restore`
- `dotnet test tests/Nova.Playback.Tests/Nova.Playback.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PlaybackLateItemDispositionPolicyBoundary`
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`
- `git diff --check`

## Remaining validation

No effect is executed by this milestone. Scheduler acknowledgement, actual
video dropping, keyframe policy, audio resampling/stretch, drift correction,
precise timers, complete A/V synchronization, and controlled real-media
playback remain unimplemented.
