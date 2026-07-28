# ADR-0162: Bounded Legal/About Projection

## Status

Accepted for milestone 0.0.10e.

## Context

ADR-0161 added the user-facing `Nova.App` startup composition above the single
shared `Nova.Windows` host. The application now needs a legal/About boundary
that can present the existing FFmpeg external-runtime notice and provenance
without creating another native owner or changing the external-runtime
distribution policy.

Milestone 0.0.10d remains deferred and Issue #129 remains open. A legal
projection is not readiness or physical authorization.

## Decision

`Nova.App` embeds exactly three existing UTF-8 legal text sources:

- `NOTICE.md`;
- `PROVENANCE.md`;
- `RUNTIME-REPLACEMENT.md`.

The application reads each resource with a 16,384-character bound and rejects
the material if the combined projection exceeds 32,768 characters. The
resulting immutable model also fixes the product license, external runtime
name, and non-bundled distribution statement.

An explicit projection target is invoked through the dispatcher of the
existing `Nova.Windows` host. A successful target returns one finite
`IAsyncDisposable` lifetime. The application owns that lifetime and closes it
before host cleanup. Unsupported platforms, missing or oversized resources,
dispatcher rejection, target rejection, cancellation, and unexpected failure
all fail closed.

Default startup does not automatically open an About surface. No new HWND
owner, thread, message pump, dispatcher, hidden loop, FFmpeg binary, downloader,
installer, runtime locator, or native invocation is introduced.

## Verification

Non-native tests compare the embedded projection with the exact retained text
files, verify the immutable fixed fields, prove single-dispatch projection and
finite cleanup, cover dispatcher and target rejection, cover non-Windows
fail-closed behavior, and inspect the default project graph for binary or
invocation references.

## Consequences

The application layer now provides the bounded legal/About model and
projection contract required by a future visible view. `Nova.Windows` remains
the only application-window and dispatcher owner, while `Nova.Playback`
ownership is unchanged.

This decision does not complete or accept 0.0.10d.
