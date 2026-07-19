# PCN-0099: Diagnostic Decoder Configuration Validation

## Scope

Validate the managed-only conversion of explicit diagnostic H.264/AAC metadata into the existing owned codec-parameter leases.

## Required checks

- The baseline creates one H.264 and one AAC parameter owner.
- H.264 dimensions and AAC sample configuration are preserved.
- Codec metadata is supplied explicitly and is not inferred from encoded packet payloads.
- Missing configuration fails closed.
- Invalid video metadata returns the video-specific fixed result.
- Invalid audio metadata disposes the partial video owner.
- Aggregate disposal clears audio and video ownership and is idempotent.
- No native context, native transfer, codec open, packet processing, activation reference, retry, parser, or background task is added.

## Commands

```powershell
dotnet build PeerCast-Nova.sln --configuration Release --no-restore
dotnet test PeerCast-Nova.sln --configuration Release --no-build
dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore
```

## Completion boundary

This milestone prepares managed codec parameters only. Native context allocation and parameter transfer remain a later reviewed milestone.
