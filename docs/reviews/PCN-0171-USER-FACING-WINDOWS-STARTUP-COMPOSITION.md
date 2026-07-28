# PCN-0171: User-facing Windows Startup Composition

## Status

Implemented for milestone 0.0.10e.

## Boundary

`Nova.App` is the minimal user-facing application entry layer above the shared
`Nova.Windows` host. One startup composition creates one shared host, waits for
close or cancellation, then stops and disposes it in that order.

`Nova.Windows` remains the sole owner of the Win32 window, message loop, owner
thread, and bounded dispatcher. `Nova.Playback` still borrows only its
dispatcher contract and window token.

## Fail-closed behavior

- unsupported platforms return `PlatformUnsupported` before native startup;
- rejected or partial startup is stopped and disposed;
- cancellation closes the finite application run;
- native close completes the run without a resident hidden loop;
- no retry, fallback device selection, duplicate message pump, or automatic
  restart is introduced.

## Default graph

`Nova.App` references only `Nova.Windows`. It does not reference `Nova.Cli` or
`Nova.Playback`, does not distribute FFmpeg, and cannot dispatch controlled
physical, readiness, probe, diagnostic, or paired-verification commands during
default startup.

## Verification

Focused injected tests cover single-host construction, owner-host lifecycle,
startup failure cleanup, cancellation, native close, unsupported-platform
behavior, and the default project graph. No physical or audio-device operation
is used by these tests.

## Deferred evidence boundary

0.0.10d remains explicitly deferred and incomplete. Issue #129, canonical
completed audio evidence, formal acceptance, stable ordinary delivery, and
paired admission remain open. This application work grants no readiness or
physical authorization.

## Next

Project the bounded FFmpeg legal/About notice through this application entry
layer without bundling the binary or changing the shared host ownership model.
