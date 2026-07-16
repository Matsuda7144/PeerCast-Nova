# PCN-0061 Verified FFmpeg Runtime Lease Validation

## Status

0.0.6c2-fix1 is verified on Windows. Ubuntu CI is configured and its
non-Windows fail-closed boundary is covered in code, but an Ubuntu runner was
not available for independent execution in this workspace. ABI and symbol
validation remain pending for 0.0.6d.

## Scope

- maximum five verified DLL handles retained until lease disposal;
- write/delete/replace denied while the lease is alive;
- all partial-failure paths release already opened handles;
- local fixed-drive explicit directories only;
- UNC, non-fixed drive, directory/file reparse points rejected;
- `OrdinalIgnoreCase` runtime names and case-duplicate rejection;
- undefined provenance and container enum values fail closed;
- no path, hash, manifest value, DLL content, or OS error disclosure;
- no native load, ABI check, symbol lookup, decoder, or media input.

## Required gates

- `build.ps1` Release build and complete solution tests;
- `dotnet format PeerCast-Nova.sln --verify-no-changes`;
- runtime locator/backend boundary Release tests repeated 20 times;
- regression coverage from 0.0.5e, 0.0.6a, 0.0.6b, and 0.0.6c1;
- clean source ZIP with forward-slash entries and no generated/native files.

## Deterministic coverage

- all five handles held and replacement/deletion prevented;
- guarded directory chain prevents parent-path rebinding;
- failure opening file 2, 3, or 5 releases all earlier handles;
- idempotent disposal and post-disposal use rejection;
- pre-cancelled caller termination;
- case-insensitive names and case-only inventory duplicate rejection;
- UNC and junction/reparse-point rejection;
- undefined Origin, Distribution, LicenseReview, and SupportedContainer values;
- fixed, privacy-safe results.

## Relay timing-test history

The pre-existing test
`PcpRelayFanoutTests.Consume_SlowChildIsStoppedWithoutBlockingFastChild`
failed once during the 0.0.6c1 final suite. Its targeted reruns passed and the
following full 276-test suite passed. The 0.0.6c2 final run and any additional
reproduction result are recorded below rather than hidden by a retry.

## Recorded results

- Release build: zero warnings and zero errors;
- Windows complete solution: 289 passed, zero failed, one explicitly skipped;
- the Windows skip is
  `Locate_NonWindowsPlatformFailsClosedBeforeFilesystemAccess`, whose reason is
  `Non-Windows fail-closed behavior is covered by Ubuntu CI.`;
- format verification: passed;
- Windows runtime locator/backend boundary group: 48 passed and one explicit
  non-Windows skip per run, repeated 20 times = 960 passed and 20 skips;
- five verified file handles and guarded directory chain held by one lease;
- file replacement, deletion, and runtime-directory rebinding denied while held;
- failures at file 2, 3, and 5 released every earlier handle;
- `Dispose`, `DisposeAsync`, and post-disposal rejection passed;
- UNC, junction/reparse point, case duplicate, and unknown enum rejection passed;
- no path, hash, manifest value, DLL content, or OS error text disclosed;
- no native library, package, executable, or external dependency added.

The prior relay timing failure did not reproduce. The named test passed in the
0.0.6c2-fix1 complete suite and in 20/20 targeted Release runs (zero failures).

## Cross-platform CI status

- GitHub Actions retains `windows-latest` and `ubuntu-latest` matrix jobs.
- Both jobs restore, Release-build, run all tests with normal console detail,
  and run `dotnet format --verify-no-changes --no-restore`.
- Windows-only locator tests use discovery-time conditional attributes with an
  explicit reason, so `cmd.exe`, `mklink /J`, fixed-drive and Win32 handle code
  are never entered on Ubuntu.
- Ubuntu executes
  `Locate_NonWindowsPlatformFailsClosedBeforeFilesystemAccess`, which expects
  the public locator to return fixed `PathRejected` before filesystem access.
- Managed policy, provenance, inventory, and artifact-verifier tests remain
  enabled on both operating systems.
- Expected Ubuntu count from the discovered test set is 277 passed and 13
  Windows-only skips. This expectation was not independently executed here:
  Docker was unavailable and WSL had no Linux distribution installed.
- Therefore Ubuntu CI success is pending, not claimed by this record.

## Artifact consistency correction

The 0.0.6c2 baseline inconsistency was reproduced before editing: the ZIP copy
of this record hashed to
`A13BB8FEE433DB859B58D867FD38E19B8A8B7FCB59A25C7D403D93B25B71C3B2`,
while the external copy hashed to
`547A540895BFBEA574ABE592356AD82C4882D9B6335F7572740C52E96A0DE0D6`.
For fix1, this source record is copied byte-for-byte to the external output,
then the ZIP, patch, review, handoff, and CI summary are frozen. The SHA-256
list is generated last and every listed digest is recomputed from the final
file. No listed artifact is changed afterward.
