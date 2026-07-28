# ADR-0160: Shared Windows Application Ownership Boundary

## Status

Accepted for milestone 0.0.10e.

## Context

Milestone 0.0.10d created one finite application-owned Win32 window, message
loop, owner thread, and bounded dispatcher inside `Nova.Cli` for controlled
physical validation. The next user-facing Windows application needs the same
ownership model, but it must not copy the controlled runner implementation or
move window, dispatcher, or message-pump ownership into `Nova.Playback`.

Milestone 0.0.10d is explicitly deferred, not completed. Its missing canonical
audio evidence, formal acceptance, paired admission, and Issue #129 remain open.
Application work must not reinterpret the retained physical evidence or grant a
new readiness or physical-run authorization.

## Decision

Extract the existing application-owned Windows host and its system-native Win32
implementation unchanged from `Nova.Cli` into a new `Nova.Windows` project.

`Nova.Windows` owns:

- the application window and HWND lifetime;
- the Win32 message loop;
- the single owner thread;
- bounded dispatcher admission and serialized execution;
- explicit shutdown and native cleanup.

`Nova.Playback` continues to receive only the borrowed
`IPlaybackWindowsOutputDispatcher` and nonzero window token. It does not own or
create a window, thread, queue, message loop, or retry policy.

`Nova.Cli` references `Nova.Windows` and continues to compose the existing
controlled commands through the same public runner contract. The extraction
does not add a normal CLI switch, automatic startup, physical execution, or a
second native host.

The existing public `PlaybackWindowsPhysicalRunner*` names remain temporarily
unchanged so this extraction is an ownership move rather than an API and
behavior rewrite. A later user-facing composition may place an application
facade above this host without cloning it.

## Verification

The existing cross-platform injected tests remain authoritative and continue to
prove:

- one application-owned owner thread and window token;
- bounded admission with no hidden retry;
- cancellation before admission;
- inline nested owner-thread execution;
- shutdown rejection and queued-work settlement;
- native startup failure;
- native close;
- idempotent stop and disposal.

Routine tests do not call User32, D3D11, DXGI, WASAPI, FFmpeg, readiness, or any
physical command.

## Consequences

The repository has one reusable Windows application ownership assembly and no
duplicate window／message-loop implementation. The next non-physical slice can
add the user-facing startup composition and legal／About projection above this
shared host.

This decision does not establish audio completion, formal acceptance, paired
admission, or completion of 0.0.10d.
