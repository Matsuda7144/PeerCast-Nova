# PCN-0063 Native Decoder Context Lease Validation

## Scope

Milestone 0.0.6e verifies decoder lookup, context allocation, and ordered native
ownership only. It does not claim codec open, parameter setup, packet/frame
processing, demux, conversion, redistribution, activation, or license approval.

## Implemented evidence

- a successful live 0.0.6d gate is transferred without rediscovery, re-hash,
  managed reopen, or native module reload;
- only fixed H.264 and AAC codec IDs are accepted;
- decoder lookup and context allocation use a testable native-call boundary;
- unsupported decoder, allocation failure, and native exceptions fail closed;
- successful ownership retains context, gate, and runtime through disposal;
- disposal frees context first, modules in reverse second, and runtime last;
- double disposal is harmless and use after child, gate, or parent disposal is
  rejected;
- non-Windows returns fixed `Unsupported`;
- results and exceptions contain no path, hash, manifest value, native pointer,
  DLL content, symbol, or native error text.

## Local Windows verification

- Release build: passed with 0 warnings and 0 errors.
- Full tests: 318 total, 315 passed, 3 explicitly skipped because their
  non-Windows assertions execute in Ubuntu CI.
- Related Release repetition: 20/20 passed. Each Windows run executed 39 tests
  successfully and explicitly skipped 3 non-Windows assertions; 780
  Windows-executable test invocations passed with zero failures.
- `dotnet format PeerCast-Nova.sln --verify-no-changes`: passed.
- `git diff --check`: passed.

## External CI

Windows and Ubuntu GitHub Actions results are recorded on the pull request and
must not be described as successful until both jobs complete.
