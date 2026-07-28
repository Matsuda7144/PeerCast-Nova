# ADR-0161: User-facing Windows Startup Composition

## Status

Accepted for milestone 0.0.10e.

## Context

ADR-0160 moved the one application-owned Win32 window, message loop, owner
thread, and bounded dispatcher into `Nova.Windows`. A normal application entry
boundary is now required above that shared host. It must not copy the native
pump, make `Nova.Playback` own application resources, or make controlled
physical commands part of default startup.

Milestone 0.0.10d remains deferred and Issue #129 remains open. Application
startup does not reinterpret retained evidence or grant readiness or physical
authorization.

## Decision

Add `Nova.App` as the user-facing application layer. Its startup composition:

1. creates exactly one adapter over `PlaybackWindowsPhysicalRunnerHost`;
2. starts the shared host and fails closed on unsupported platforms or rejected
   startup;
3. waits for native window close or caller cancellation;
4. closes dispatcher admission and stops the shared host;
5. disposes the shared host and its native resources.

`Nova.App` references only `Nova.Windows`. It does not reference `Nova.Cli` or
`Nova.Playback`, does not bundle FFmpeg, and has no route to controlled
physical, readiness, probe, diagnostic, or paired-verification commands.

The shared host gains only a finite `WaitForExitAsync` observation contract.
Window creation, message pumping, dispatcher admission, owner-thread behavior,
native cleanup, and the controlled CLI composition remain single and unchanged.

## Verification

Injected non-native tests prove one host creation, ordered
start/wait/stop/dispose lifecycle, partial-start cleanup, cancellation, native
close completion, unsupported-platform rejection, and the absence of CLI,
Playback, FFmpeg, and controlled-command references from the default
application graph.

Routine tests do not call User32, D3D11, DXGI, WASAPI, FFmpeg, readiness, or any
physical command.

## Consequences

The application has a minimal entry boundary for the next legal/About
projection without introducing a second window, dispatcher, owner thread, or
message loop. Resident hidden retry, fallback device selection, and automatic
physical execution remain absent.

This decision does not complete or accept 0.0.10d.
