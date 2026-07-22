# ADR-0141: Application-Owned Windows Physical Runner Harness

## Status

Accepted for milestone 0.0.10d Phase 3.

## Context

ADR-0138 requires a real application-owned window, message loop, and owner-thread dispatcher before controlled D3D11／DXGI and WASAPI validation can run. ADR-0139 and ADR-0140 already define the evidence gate and connect sanitized native counters, but neither creates the application surface that owns the HWND and dispatcher thread.

The first physical runner must remain finite and explicit. It must not turn `Nova.Playback` into the owner of a window, message pump, dispatcher thread, command queue, timer, retry loop, or device-recovery policy. It must also be testable on routine Linux CI without claiming that Win32, D3D11, or WASAPI executed.

## Decision

Add `PlaybackWindowsPhysicalRunnerHost` and `SystemPlaybackWindowsPhysicalRunnerNative` to `Nova.Cli`.

`Nova.Cli` is the application boundary and owns:

- one dedicated controlled-run thread;
- one visible Win32 top-level window;
- the window class and HWND lifetime;
- the Win32 message loop;
- bounded dispatcher admission;
- explicit stop and disposal;
- rejection of new operations after shutdown.

`Nova.Playback` receives only the borrowed `IPlaybackWindowsOutputDispatcher` implementation and the current nonzero window token. It does not create, destroy, retain, or replace the thread or window.

### Platform and configuration admission

The production constructor accepts only Windows 11 or later. Unsupported platforms fail before the thread or window is created.

Client width and height, title, and maximum pending operation count are validated before startup. Invalid values fail closed.

### Finite dispatcher

The application dispatcher has a fixed maximum pending-operation count. Work beyond that limit returns `Faulted` immediately; it is not queued, retried, delayed, or resubmitted.

Cancellation already requested before admission returns `Cancelled` without retaining or executing the operation.

The Win32 owner thread drains accepted work when the application posts one private message. Operations execute serially. Nested invocation from the owner thread executes inline to avoid self-deadlock.

The dispatcher records only sanitized counters and state. It retains no packet, frame, runtime path, device identifier, native pointer, or output evidence artifact.

### Window and message-loop lifetime

The system native implementation registers a unique window class, adjusts the outer rectangle for the requested client size, creates and shows one top-level window, and enters a standard `GetMessage` loop.

A private `WM_APP` message drains accepted dispatcher work. `WM_CLOSE` destroys the window and `WM_DESTROY` posts quit. Explicit application stop posts `WM_CLOSE` and closes dispatcher admission.

Window creation or message-loop failure settles startup as `Faulted`. Native close settles the runner as stopped and later dispatcher calls return `Shutdown`.

### Cleanup

Stop performs these application-level actions:

1. close dispatcher admission;
2. settle queued but unstarted operations as `Shutdown`;
3. request window close;
4. allow the owner thread to finish its current operation;
5. leave the message loop;
6. clear the borrowed window token and owner-thread identity.

Playback production and output resources must still be stopped and disposed through the existing production-first lifecycle before the runner itself is stopped. This harness does not reorder or automate playback cleanup.

### Cross-platform verification

The native pump is injected behind an internal application interface. Linux tests use a fake owner-thread pump and prove host behavior only. They do not exercise User32, D3D11, DXGI, WASAPI, FFmpeg, or physical output.

## Consequences

The repository now has an explicit application-owned window and dispatcher harness suitable for a later Windows 11 physical run. The host is not automatically started by normal CLI execution and does not constitute physical video or audio validation.

The next phase must compose the approved runtime lifecycle with this host, run video and audio independently on Windows 11, capture evidence before output cleanup, and submit it through the existing Phase 1 admission boundary.
