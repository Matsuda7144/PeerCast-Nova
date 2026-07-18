# PCN-0074 Awaited Decoded Sink Delivery Validation

## Scope

0.0.6p serially awaits canonical audio/video delivery from the existing 0.0.6n
adapter and 0.0.6o managed conversion boundary.

## Verification

- Release build: passed with 0 warnings and 0 errors.
- Full test suite: 504 passed, 0 failed, 3 existing Windows skips (507 total).
- Awaited sink delivery tests: 11 passed, 0 failed, 0 skipped.
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed.

The first sandboxed restore attempt could not read the user NuGet configuration;
the identical build script was rerun with normal user access and passed.

Dispose-race coverage includes disposal during an active callback, disposal with
one active and one waiting delivery, and calls admitted after disposal. No path
leaks `ObjectDisposedException`; active payload cleanup and context ownership are
preserved.

Status: Verified.
