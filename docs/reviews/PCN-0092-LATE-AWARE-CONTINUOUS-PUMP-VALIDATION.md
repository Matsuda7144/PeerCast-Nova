# PCN-0092: Late-Aware Continuous Pump Validation

## Scope

Validation covers the finite continuous owner around the 0.0.7g late-aware timed dispatch boundary.

## Covered behavior

- processed video/audio advance without idle delay;
- consumed video drops count as progress and increment a bounded numeric counter;
- retained late, wait-limit, empty, would-block, and clock-not-started use bounded idle delay;
- audio recovery stops and latches reset required;
- pump count, run duration, consecutive idle count, and idle delay are mandatory and bounded;
- reset delegates generations and clears counters only on success;
- no keyframe selection, audio recovery effect, duplication, resampling, drift correction, or native resource ownership.

## Validation commands

- `dotnet build PeerCast-Nova.sln -c Release --no-restore`
- `dotnet test PeerCast-Nova.sln -c Release --no-build --no-restore`
- `dotnet test tests/Nova.Playback.Tests/Nova.Playback.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PlaybackLateAwareContinuousPumpBoundary`
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`
- `git diff --check`

## Remaining validation

A later milestone must define an audio recovery effect and precise scheduling behavior. Controlled audible/visible real-media playback remains unimplemented.