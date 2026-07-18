# PCN-0072 Decoded Output Adapter Validation

## Scope

0.0.6n revalidates owned managed FFmpeg frames and adapts directly compatible
layouts to the existing decoded output models without conversion or delivery.

## Verification

- `build.ps1`: passed; Release build warning 0 / error 0.
- Full suite: 475 total, 472 passed, 0 failed, 3 existing Windows skips.
- Decoded-output adapter tests: 23 passed, 0 failed, 0 skipped.
- `dotnet format PeerCast-Nova.sln --verify-no-changes`: passed.
- `git diff --check`: passed.
