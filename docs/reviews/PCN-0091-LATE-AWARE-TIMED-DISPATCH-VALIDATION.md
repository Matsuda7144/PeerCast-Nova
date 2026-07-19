# PCN-0091: Late-Aware Timed Dispatch Validation

## Scope

Validation covers the 0.0.7g boundary that preserves one scheduler reservation across bounded
wait/redecision, successful output dispatch, and late reservation disposition.

## Covered behavior

- wait then process uses the same reservation and consumes exactly once;
- explicit late-video drop consumes without output dispatch;
- wait-limit retains the reservation;
- generation mismatch retains and latches reset required;
- successful output advances media preference;
- all non-consumed paths complete the reservation exactly once;
- no continuous pump, keyframe selection, audio recovery effect, duplication, or drift correction.

## Validation commands

- `dotnet build PeerCast-Nova.sln -c Release --no-restore`
- `dotnet test PeerCast-Nova.sln -c Release --no-build --no-restore`
- `dotnet test tests/Nova.Playback.Tests/Nova.Playback.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PlaybackLateAwareTimedDispatchBoundary`
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`
- `git diff --check`

## Remaining validation

A later milestone must integrate this one-item operation into a finite continuous pump and
perform controlled audible/visible real-media playback verification.
