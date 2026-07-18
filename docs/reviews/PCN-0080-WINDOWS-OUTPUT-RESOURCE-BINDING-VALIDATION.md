# PCN-0080 Windows Output Resource Binding Validation

## Scope

0.0.6v binds activated D3D11 and WASAPI resource identities to existing video
and audio configuration contracts. It fixes state, generation, borrowing, and
cleanup behavior without performing real output.

## Contract checks

- Activated BGRA32 video and interleaved S16/F32 audio bind successfully.
- Activation-required, unsupported-platform, format mismatch, duplicate
  configure, device-lost, cancelled, disposed, and failure outcomes are fixed.
- Undefined native results, exceptions, and missing activation resources fail
  closed.
- Audio binding failure rolls back audio then video.
- Reset updates both generations and requires explicit reconfiguration.
- Dispose waits for an in-flight binding callback and is idempotent.
- Activation retains sole ownership; binding only uses synchronized borrows.
- Production borrows are restricted to the activation-owning thread.
- Non-Windows does not access native binding or activation resources.
- Unit tests require no physical video or audio device.

## Verification

- Release build: passed with 4 environment warnings for the missing
  `c:\vcpkg\installed\x64-windows\lib` search path.
- Full test suite: 599 passed, 7 failed, 3 skipped (609 total). The same seven
  installed-runtime locator tests recorded by 0.0.6u returned
  `DirectoryUnavailable` in this Windows environment.
- Windows output resource-binding tests: 13 passed.
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed.

Status: Verified with the documented pre-existing environment failures.
