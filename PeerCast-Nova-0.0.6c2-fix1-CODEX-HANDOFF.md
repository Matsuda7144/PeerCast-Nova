# PeerCast Nova 0.0.6c2-fix1 Codex Handoff

## Current state

0.0.6c2-fix1 preserves the verified runtime lease trust boundary and fixes
cross-platform test discovery and release-artifact hash consistency. Windows
Release validation passed. Ubuntu CI is configured but was not independently
run in this workspace because no Docker or installed WSL Linux distribution
was available. Do not describe Ubuntu as verified until its matrix job passes.

FFmpeg remains user/host supplied, dynamically linked, and never bundled or
redistributed by Nova. License review remains Pending.

## Runtime lease boundary

- At most five inventory DLLs are opened and verified.
- File and directory guard handles remain alive until lease disposal.
- Write, delete, replacement, and parent-directory rebinding are denied.
- UNC, non-fixed drives, and reparse points fail closed.
- Runtime file names use Windows `OrdinalIgnoreCase` rules.
- Size and SHA-256 remain owned by the existing inventory verifier.
- Paths, hashes, manifest values, DLL bytes, and OS exception details are not
  normal diagnostics.

## Mandatory 0.0.6d handoff rule

0.0.6d MUST receive the live `VerifiedPlaybackDecoderRuntimeLease`. It MUST NOT
re-enumerate or rediscover the source directory, re-hash as a substitute for
ownership, or reopen DLLs independently from the caller-supplied original
path. Native loading, ABI/symbol inspection, failure cleanup, and unload must
finish while the lease and directory guards remain alive.

0.0.6d must treat dependent-DLL resolution as a new trust boundary:

- do not call `NativeLibrary.Load(path)` directly;
- do not permit current-directory or ordinary `PATH` dependency search;
- evaluate `LoadLibraryExW` with narrowly scoped search flags;
- include `LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR` for verified sibling DLLs;
- include `LOAD_LIBRARY_SEARCH_SYSTEM32` for Windows system DLLs;
- permit only the five verified FFmpeg DLLs and Windows system DLLs;
- if practical, inspect PE import tables before loading and reject imports
  outside that allow-list;
- release all native modules in reverse load order;
- keep the lease and directory guards alive throughout every load callback;
- do not run the synchronous native loader on a UI thread because it cannot be
  forcibly cancelled safely;
- if strict finite termination becomes mandatory, consider moving native load
  and ABI probing into a future helper process.

Do not add packet decode, codec initialization, frame output, FFmpeg bundling,
or a new external dependency in the ABI/Symbol Gate milestone.

## Validation state

- Windows Release build: 0 warnings / 0 errors.
- Windows tests: 289 passed, 0 failed, 1 explicit non-Windows skip.
- Format verification: passed.
- Runtime/backend group: 48 passed + 1 explicit skip per run, 20 runs.
- Relay timing test: full-suite pass and 20/20 targeted passes.
- Ubuntu expected: 277 passed and 13 explicitly reasoned Windows-only skips.
- Ubuntu actual execution: pending; not independently reproduced here.
