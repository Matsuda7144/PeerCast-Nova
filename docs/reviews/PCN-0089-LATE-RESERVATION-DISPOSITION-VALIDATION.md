# PCN-0089: Late Reservation Disposition Validation

## Scope

Validation covers the 0.0.7e boundary that applies 0.0.7d late coordination outcomes
to one existing scheduler reservation.

## Covered behavior

- retained video and audio remain unconsumed;
- explicit video drop candidate is consumed exactly once;
- audio recovery remains retained;
- generation mismatch latches reset required without consumption;
- reset clears the bounded drop counter;
- invalid and failure paths fail closed;
- no keyframe selection, audio processing, output dispatch, timer, or native resource use.

## Validation commands

- `dotnet build PeerCast-Nova.sln -c Release --no-restore`
- `dotnet test PeerCast-Nova.sln -c Release --no-build --no-restore`
- `dotnet test tests/Nova.Playback.Tests/Nova.Playback.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PlaybackLateReservationDispositionBoundary`
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`
- `git diff --check`

## Remaining validation

A later milestone must integrate this ownership rule into the continuous playback pump
and define audio recovery effects. Controlled audible/visible real-media playback remains
unimplemented.
