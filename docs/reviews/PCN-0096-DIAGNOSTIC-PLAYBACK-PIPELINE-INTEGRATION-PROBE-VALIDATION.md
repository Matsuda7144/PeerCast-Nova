# PCN-0096: Diagnostic Playback Pipeline Integration Probe Validation

## Scope

Validate the diagnostic-only staged topology and startup gates for the playback boundaries from 0.0.6d through 0.0.8c.

Audible and visible output are outside this validation.

## Required checks

1. Build and run the full test suite on Ubuntu and Windows.
2. Verify the ordered topology contains every declared stage exactly once.
3. Verify the topology contains exactly five contiguous, non-interleaved phase groups in this order: `RuntimeDecode`, `ManagedDelivery`, `SchedulingTiming`, `OutputLifecycle`, `RecoveryOrchestration`.
4. Verify every stage belongs to one defined phase and no phase is re-entered after a later phase begins.
5. Invoke the CLI probe with nonexistent private runtime and media paths while the pinned license review is `Pending`.
6. Verify the fixed result is `LicenseReviewPending` and no path value is printed.
7. Verify no phase, runtime locator, ABI gate, or media access is entered while review is pending.
8. Verify disposal causes the fixed `Disposed` result and clears the current phase.
9. Verify no changed file is `PlaybackDecoderBackendFactory.cs`, `IPlaybackDecoderBackend.cs`, or `PlaybackDecoderBackendPolicy.cs`.
10. Verify no reference from the production decoder factory or normal startup path reaches the probe.
11. Verify no retry loop, unbounded collection, background task, or path-bearing log was added.

## Expected current result

The pinned candidate remains `Pending`. The command must stop before runtime-directory and diagnostic-media access. A full real-media transition sequence is therefore not expected to run in this revision.

The current snapshot must retain fixed numeric fields and fixed enum positions only. It must not claim a completed phase, generation transition, reservation, recovery, audible output, or visible output.

The staged-plan tests are required to pass identically on Ubuntu and Windows; installed-runtime availability must not affect the pending-review refusal path.

## Blocked integration point

The 0.0.6d through 0.0.8c public contracts begin at verified runtime/native codec operations and encoded packet processing. They do not include a public bounded demux contract for a general MP4 or FLV file. The probe must not invent or bypass that missing contract.

After approval, the first executable phase remains `RuntimeDecode`. It must fail closed before marking that phase completed unless one video packet and one audio packet are supplied by a separately approved bounded diagnostic input contract.

## Commands

```powershell
dotnet restore PeerCast-Nova.sln
dotnet build PeerCast-Nova.sln --configuration Release --no-restore
dotnet test PeerCast-Nova.sln --configuration Release --no-build
dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore
```

Diagnostic invocation after building:

```powershell
dotnet run --project src/Nova.Cli/Nova.Cli.csproj -- --playback-pipeline-integration-probe --runtime-directory C:\diagnostic-runtime --diagnostic-media-file C:\diagnostic-media\sample.bin
```

The current expected command result is the fixed enum name `LicenseReviewPending`.