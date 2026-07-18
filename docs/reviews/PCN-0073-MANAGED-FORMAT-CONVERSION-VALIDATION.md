# PCN-0073 Managed Format Conversion Validation

## Scope

0.0.6o converts owned YUV420P/NV12 and planar S16/F32 buffers to the existing
BGRA32 and interleaved PCM decoded-output models using managed code only.

## Verification

- Release build: passed with 0 warnings and 0 errors.
- Full test suite: 493 passed, 0 failed, 3 existing Windows skips (496 total).
- Managed format conversion tests: 21 passed, 0 failed, 0 skipped.
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed.

Status: Verified.
