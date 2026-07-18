# PCN-0084: Finite Continuous Playback Pump Policy Validation

## Scope

Validation covers the 0.0.6z bounded continuous-pump policy and production
clock/delay/adapter seams without requiring a physical Windows output device.

## Covered behavior

- finite pump-count, duration, and consecutive-idle termination;
- Empty/WouldBlock cancellable delay with no busy spin;
- explicit Stop interruption and idempotency;
- caller cancellation and concurrent-run rejection;
- device-loss, resize, initialization/binding, and reset-required termination;
- invalid/undefined lower results and adapter exceptions;
- backward, invalid, and exceptional clock handling;
- successful transactional reset and partial-reset fault mapping;
- reset-wide `Resetting` exclusion for concurrent Run, Reset, and Stop;
- reset cancellation, exception, and Dispose waiting without final-state
  overwrite or lower ownership transfer;
- reset/dispose stopping an in-flight run;
- repeated reset/dispose without owning or disposing lower boundaries;
- non-Windows and invalid options rejected before lower pump access.

## Validation commands

- `dotnet build PeerCast-Nova.sln -c Release --no-restore`
- `dotnet test PeerCast-Nova.sln -c Release --no-build --no-restore`
- `dotnet test tests/Nova.Playback.Tests/Nova.Playback.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~FiniteContinuousPlaybackPumpPolicy`
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`
- `git diff --check`

## Remaining validation

No test assumes a Windows video or audio device exists. Controlled Windows
validation still needs a bounded run reaching actual D3D11 Present and WASAPI
buffer/Start processing. A/V synchronization, drift correction, precise timer
deadlines, and real-media playback remain unimplemented.

## Results

- Release build: passed with seven existing warnings for the unavailable local
  vcpkg library search path.
- Full suite: 685 passed, 8 failed, and 3 skipped (696 total). Seven failures
  are the existing installed-runtime locator tests returning
  `DirectoryUnavailable`; one unrelated network fan-out timing assertion also
  failed in this run.
- 0.0.6z finite-pump-policy tests: 23 passed.
- Format verification and `git diff --check`: passed.
