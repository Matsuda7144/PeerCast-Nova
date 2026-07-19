# PCN-0101: Diagnostic Codec-Open Transaction Validation

## Scope

Validate the finite diagnostic connection from the 0.0.8h parameter-configured H.264/AAC native contexts to the existing `avcodec_open2` boundary.

## Required checks

- Video opens before audio.
- Video rejection stops before audio open.
- Audio rejection disposes the complete lease in audio-to-video order.
- Unexpected failure disposes the complete lease.
- Null or disposed input fails closed before codec-open operations.
- Success retains one aggregate lease containing both opened contexts.
- Existing zero-only native success and rejected-context disposal remain delegated to the 0.0.6h boundary.
- Results remain fixed enums without paths, pointers, handles, native return values, option text, or free-text native errors.
- No packet submission, frame receive, flush, parser, retry loop, background task, production activation reference, or approval-state mutation is added.

## Commands

```powershell
dotnet build PeerCast-Nova.sln --configuration Release --no-restore
dotnet test PeerCast-Nova.sln --configuration Release --no-build
dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore
```

## Completion boundary

0.0.8i ends with two independently owned, parameter-configured, opened native decoder contexts. Packet submission and frame reception remain later milestones.
