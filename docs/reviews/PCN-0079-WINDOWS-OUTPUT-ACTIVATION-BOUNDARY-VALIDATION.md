# PCN-0079 Windows Output Activation Boundary Validation

## Scope

0.0.6u adds a finite, injectable OS activation and ownership boundary for the
D3D11 and WASAPI adapters selected in 0.0.6t. It activates native device/client
objects but does not create presentation resources or initialize an audio
stream.

## Contract checks

- Non-Windows returns a fixed unsupported result without native access.
- Success, unavailable API/device, permission, invalid environment, failure,
  and disposed outcomes are fixed and contain no native values.
- Undefined results, exceptions, and unknown HRESULTs fail closed.
- COM, D3D11 resources, WASAPI resources, and event handles have one owner.
- Partial success and normal disposal release resources in reverse order.
- Repeated disposal cannot release a native resource twice.
- Tests use an injected managed native boundary and assume no real device.

## Verification

- Release build: passed with 4 invalid `LIB` search-path warnings from the
  environment's missing `c:\vcpkg\installed\x64-windows\lib` directory.
- Full test suite: 586 passed, 7 failed, 3 skipped (596 total). The seven
  pre-existing installed-runtime locator tests returned `DirectoryUnavailable`
  in this Windows environment; the focused class reproduced the same failures.
- Windows output activation and adapter-selection tests: 45 passed.
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed.

Status: Verified.
