# ADR-0053 FFmpeg ABI and Symbol Gate

## Status

Accepted for milestone 0.0.6d. Decoder activation and license review remain
pending.

## Decision

The ABI and symbol gate accepts only a live
`VerifiedPlaybackDecoderRuntimeLease` created by the 0.0.6c2 locator. The gate
runs inside the lease's guarded callback, so all five verified file handles and
the guarded directory chain remain alive for the complete native operation. It
does not enumerate, search, re-hash, or open the original runtime through a
managed file API.

Before native loading, a bounded PE import-table inspector reads each image
from the stream already owned by the lease. Every import must be either one of
the verified five FFmpeg DLL names, a fixed Windows system DLL, or a Windows
API-set name. Unknown imports fail closed before any module is loaded.

On Windows, the native boundary calls `LoadLibraryExW` with only
`LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR` and `LOAD_LIBRARY_SEARCH_SYSTEM32`. It never
uses `NativeLibrary.Load`, the current directory, or normal `PATH` search. The
gate has fixed load order, expected ABI major, version function, and required
symbol lists for all five DLLs. Version majors are read from FFmpeg's numeric
version functions.

Every successfully loaded module is recorded immediately. On success and on
all partial failures, modules are released exactly once in reverse load order.
The gate returns only fixed enum categories. Paths, hashes, manifest values,
symbol names, DLL bytes, and native error text are not diagnostic output.

On non-Windows systems the public gate returns the fixed `Unsupported` result
before inspecting the lease or touching a native loader.

## Test boundary

Native loading, symbol lookup, version invocation, release, dependency
inspection, and platform support are separated behind internal interfaces.
Deterministic tests cover the complete policy without loading third-party
binaries. Windows and Ubuntu CI retain the same managed policy tests; Ubuntu
also proves the public fail-closed unsupported result.

## Remaining constraints

Windows native loading is synchronous and cannot be forcibly cancelled. The
gate must not run on a UI thread. A future requirement for strict interruption
would require process isolation. The verified lease and directory guards must
outlive every native-load callback.

## Out of scope

- codec or demuxer initialization;
- packet decode or decoded frame output;
- a decoder instance or long-lived native module owner;
- FFmpeg redistribution or new external dependencies;
- backend activation or license approval.
