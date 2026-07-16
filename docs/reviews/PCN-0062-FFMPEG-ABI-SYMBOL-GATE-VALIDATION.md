# PCN-0062 FFmpeg ABI and Symbol Gate Validation

## Scope

Milestone 0.0.6d validates the trust boundary between the live 0.0.6c2
verified runtime lease and native FFmpeg ABI inspection. It does not claim
codec initialization, decoding, frame output, redistribution, activation, or
license approval.

## Implemented evidence

- the same live five-file lease is consumed under its ownership lock;
- no directory rediscovery, enumeration, re-hash, or managed file re-open;
- bounded PE import inspection uses the five already-open streams;
- dependencies are limited to the verified five names, fixed system names, and
  Windows API-set names;
- `LoadLibraryExW` uses only `DLL_LOAD_DIR` and `SYSTEM32` search flags;
- five fixed DLL names, ABI majors, version functions, and required symbol
  lists are checked;
- success and every partial failure release loaded modules once in reverse;
- fixed result categories disclose no path, hash, manifest value, symbol, DLL
  content, or OS error text;
- non-Windows returns fixed `Unsupported` before lease or loader access.

## Local Windows verification

- Release build: passed with 0 warnings and 0 errors.
- Full tests: 305 total, 303 passed, 2 explicitly skipped because their
  non-Windows assertions execute in Ubuntu CI.
- New gate tests: normal five-DLL flow, runtime mismatch, rejected dependency,
  missing symbol, ABI mismatch, load failures at positions 2/3/5, reverse
  release, exactly-once release, disposed lease, CWD/PATH exclusion, privacy,
  and non-Windows fail-closed coverage.
- Related Release repetition: 20/20 passed. Each Windows run executed 33 tests
  successfully and explicitly skipped the 2 non-Windows assertions; 660
  Windows-executable test invocations passed with zero failures.
- `dotnet format PeerCast-Nova.sln --verify-no-changes`: passed.
- `git diff --check`: passed.

## External CI

Windows and Ubuntu GitHub Actions results are recorded on the pull request.
They must not be described as successful until the remote jobs complete.

## Remaining work

The installed FFmpeg runtime was not activated as a decoder. Real runtime
independent reproduction, decoder construction, media input, and LGPL display
obligations remain pending.
