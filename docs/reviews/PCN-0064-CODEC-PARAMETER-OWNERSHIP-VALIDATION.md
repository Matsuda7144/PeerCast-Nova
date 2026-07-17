# PCN-0064 Codec Parameter Ownership Validation

## Scope

Milestone 0.0.6f verifies managed codec-parameter validation, defensive
extradata ownership, fixed FFmpeg padding, and finite disposal only. It does
not claim native parameter transfer, codec open, packet/frame processing,
demux, conversion, redistribution, activation, or license approval.

## Implemented evidence

- only fixed H.264 and AAC codec identifiers are accepted;
- H.264 and AAC numeric fields have named finite bounds;
- mixed video/audio fields, negative values, zero required values, oversized
  values, and undefined enums fail closed;
- extradata is required, bounded to 1 MiB, and copied before ownership succeeds;
- the owned buffer contains exactly 64 trailing zero bytes;
- caller mutation after creation cannot alter the owned bytes;
- disposal is idempotent, clears the complete owned buffer, and rejects later
  use;
- validation is managed-only and has identical Windows and non-Windows behavior;
- public results and fixed failures expose no extradata, padding, native pointer,
  path, hash, or native error text;
- no native API, FFmpeg binary, or new dependency was added.

## Completed validation

- Release builds completed with zero warnings and zero errors on Windows and Ubuntu.
- Windows full suite: 340 total, 337 passed, 3 OS-specific skips, 0 failed.
- Ubuntu full suite: 338 total, 327 passed, 11 OS-specific skips, 0 failed.
- Related Release tests completed successfully for 20 of 20 Windows runs.
- `dotnet format PeerCast-Nova.sln --verify-no-changes` passed on Windows and Ubuntu.
- `git diff --check` passed for the pull-request change set.

## External CI

Windows and Ubuntu GitHub Actions completed successfully for the final pull-request
head before review readiness.
