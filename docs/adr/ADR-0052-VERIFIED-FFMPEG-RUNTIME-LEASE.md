# ADR-0052 Verified FFmpeg Runtime Lease

## Status

Accepted in milestone 0.0.6c2. License review and backend activation remain
pending.

## Decision

Nova uses a handle-retaining verified runtime lease. The locator opens no more
than the five inventory DLLs with read access and `FileShare.Read`. Write and
delete sharing are therefore denied for the lifetime of the lease. The same
open streams are passed to the existing runtime inventory verifier for size
and SHA-256 validation. A successful result transfers ownership of every open
handle to `VerifiedPlaybackDecoderRuntimeLease`; every other path disposes all
handles already opened.

Before each normal file open, Nova opens the exact directory entry with
`FILE_FLAG_OPEN_REPARSE_POINT`, rejects the reparse attribute from that handle,
and keeps the entry guarded until the read handle is established. It also
holds no-delete guard handles for every directory component from the fixed
volume root through the runtime directory. Parent renaming therefore cannot
rebind the verified path while the lease is alive.

The locator accepts only an explicit absolute directory on a local fixed
drive. UNC, network/non-fixed drives, and any reparse point in the directory
chain or file set fail closed. Runtime and inventory file-name comparison uses
`OrdinalIgnoreCase`, matching the Windows boundary, and case-only duplicates
are rejected.

The 0.0.6d ABI/Symbol Gate must accept this live lease and operate on the DLLs
owned by it. It must not search, enumerate, re-hash, or reopen the original
directory before loading. It supplies a native-load callback to the lease's
guarded operation boundary rather than receiving a path string. The lease lock
keeps all file and directory guards alive until that callback finishes.
Disposal follows unload or gate failure.

## Why handles instead of a managed copy

Windows cannot load a native library directly from an arbitrary managed file
handle. Holding the verified regular-file handles plus the guarded directory
chain is sufficient for the selected local fixed-drive boundary: their sharing
mode prevents the verified path from being written, renamed, deleted, or replaced.
It avoids creating another sensitive executable staging area and avoids a
second cleanup lifecycle. Reparse points are excluded before any candidate is
accepted.

## Cancellation and filesystem-call boundary

Cancellation is checked before filesystem access, after enumeration, between
each open, and during asynchronous hashing. Pre-cancelled calls terminate
without filesystem work, and hashing is cancellable. Windows directory
metadata queries, enumeration, and `CreateFile`-backed open calls are
synchronous OS operations and cannot be forcibly interrupted safely by a
managed cancellation token. Limiting the input to a local fixed drive removes
UNC/network waits, but an operating-system or failing-device stall remains a
platform constraint. Callers must not perform location on a UI thread.

## Privacy and ownership

Results expose only fixed enum categories, handle count, disposal state, and
lease ownership. Paths, hashes, manifest fields, DLL bytes, and OS exception
text are not normal diagnostics. `Dispose` and `DisposeAsync` are idempotent;
use after disposal fails with fixed text.

## Enum trust boundary

Undefined provenance and supported-container enum values are rejected before
manifest requirements or activation policy are evaluated. Policy evaluation
returns the fixed `InvalidProvenance` rejection. Undefined runtime result
categories cannot construct a public result.

## Out of scope

- `NativeLibrary.Load` and native-library loading;
- ABI or symbol validation;
- codec initialization, packet decode, or frame output;
- FFmpeg redistribution or new dependencies;
- license approval.
