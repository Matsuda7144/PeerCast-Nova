# PCN-0100: Diagnostic Native Parameter Transaction Validation

## Scope

Validate the finite diagnostic connection from the 0.0.8g managed H.264/AAC parameter owners to the existing native context-allocation and parameter-transfer boundaries.

## Required checks

- Video context creation and parameter transfer occur before audio.
- Video and audio require distinct gate/runtime ownership chains.
- Shared-parent input fails closed before native operations.
- Video failure stops before audio acquisition.
- Audio failure releases audio then video and consumes remaining managed parameters.
- Successful disposal releases audio then video and is idempotent.
- Results remain fixed enums without paths, pointers, handles, HRESULTs, or free-text native errors.
- No codec open, packet submission, frame receive, parser, retry loop, background task, production activation reference, or approval-state mutation is added.

## Commands

```powershell
dotnet build PeerCast-Nova.sln --configuration Release --no-restore
dotnet test PeerCast-Nova.sln --configuration Release --no-build
dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore
```

## Completion boundary

0.0.8h ends with two independently owned, parameter-configured, unopened native decoder contexts. Codec open and packet submission remain later milestones.
