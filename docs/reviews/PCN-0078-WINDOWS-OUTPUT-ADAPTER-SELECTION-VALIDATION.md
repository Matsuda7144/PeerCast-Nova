# PCN-0078 Windows Output Adapter Selection Validation

## Scope

0.0.6t selects Direct3D 11 and WASAPI shared/event-driven output adapters and
adds a non-activating availability probe boundary. It checks required System32
DLL exports but performs no device enumeration, rendering, audio playback,
device creation, or COM activation.

## Contract checks

- Windows 11 first-target and non-Windows fixed unsupported result.
- Deterministic D3D11/WASAPI selection.
- BGRA32 video and S16/F32 interleaved audio format decisions.
- Fixed platform, API, future device, format, permission, environment, disposal, and
  failure results.
- Known HRESULT normalization and unknown HRESULT fail-closed behavior.
- Undefined platform/API enums and source exceptions fail closed.
- No temporary native-library handle survives a source callback.
- Windows Actions run 29656388691 reached and passed the Windows output probe
  tests; its job failed separately in
  `PcpRelayFanoutTests.Consume_CopiesPayloadBeforeIngressCallbackReturns` with a
  two-second `TimeoutException`.

## Verification

- Release build: passed with 0 warnings and 0 errors.
- Full test suite: 578 passed, 0 failed, 3 existing Windows skips (581 total).
- Windows output selection/probe tests: 30 passed, 0 failed, 0 skipped.
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed.

Status: Verified.
