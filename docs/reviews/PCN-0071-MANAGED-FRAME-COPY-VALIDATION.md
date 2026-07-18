# PCN-0071 Managed Frame Copy Validation

## Scope

0.0.6m copies validated callback-scoped FFmpeg plane/sample data into owned
managed buffers without conversion or output delivery.

## Verification

- `build.ps1`: passed; Release build warning 0 / error 0.
- Full suite: 452 total, 449 passed, 0 failed, 3 existing Windows skips.
- Managed-copy tests: 15 passed, 0 failed, 0 skipped.
- `dotnet format PeerCast-Nova.sln --verify-no-changes`: passed.
- `git diff --check`: passed.
