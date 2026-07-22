# PCN-0149: Application-Owned Windows Physical Runner Harness

## Milestone

0.0.10d Phase 3

## Review result

`RunnerHarnessImplementedPhysicalRunPending`

## Implemented surface

The branch adds:

- `PlaybackWindowsPhysicalRunnerHost` in `Nova.Cli`;
- `SystemPlaybackWindowsPhysicalRunnerNative`;
- one visible Win32 top-level window and standard message loop;
- one application-owned owner thread;
- an `IPlaybackWindowsOutputDispatcher` implementation;
- bounded pending-operation admission;
- explicit start, stop, native-close, and async-disposal settlement;
- focused `Nova.Cli.Tests` coverage;
- ADR-0141 and this review record.

No `Nova.Playback` ownership is widened.

## Ownership review

The application host owns the thread, HWND, window class, message loop, and pending application work. The playback lifecycle borrows only the dispatcher contract and window token.

The host does not own:

- FFmpeg runtime or decoder generation;
- encoded or decoded media;
- scheduler reservations;
- D3D11, DXGI, or WASAPI playback resources;
- evidence admission;
- production stop or output teardown ordering.

Those responsibilities remain with the existing production and output boundaries.

## Platform review

The production native path requires Windows 11 or later. Linux and unsupported Windows versions return `PlatformUnsupported` before starting a thread.

Routine CI injects a fake native pump and does not call User32.

## Window review

The system path:

- registers one unique window class per host;
- creates one visible top-level window;
- adjusts the outer size for the requested client area;
- publishes the nonzero HWND as a borrowed `long` window token;
- dispatches one private `WM_APP` work message;
- handles close and destroy through the ordinary Win32 sequence;
- unregisters the class after the loop exits.

The host clears the window token after loop termination.

## Dispatcher review

Admission is allowed only while the runner is `Running` and not closing.

The pending count is bounded from 1 through 64 by immutable startup options. An operation beyond the configured limit returns `Faulted` immediately. No hidden retry or secondary queue exists.

Pre-cancelled work returns `Cancelled` without execution. Accepted work is drained serially on the owner thread. Owner-thread nested invocation executes inline.

When stop begins, queued work that has not started is settled as `Shutdown`. New work is rejected as `Shutdown`. The currently executing operation is allowed to settle before the message loop exits.

## State and sanitized counters

The public snapshot contains only:

- runner state and latest result;
- borrowed window token;
- managed owner-thread identifier;
- pending count;
- accepted, completed, cancelled, and rejected counters;
- message-loop and admission-closed flags.

It contains no HWND-derived child resource, device identifier, pointer, handle other than the temporary borrowed top-level window token, runtime path, media bytes, frame content, or evidence artifact.

## Focused verification

`PlaybackWindowsPhysicalRunnerHostTests` proves:

1. startup publishes one application-owned window and owner thread;
2. dispatcher work observes owner-thread access and completes once;
3. unsupported platform rejects before native startup;
4. invalid options fail closed before thread creation;
5. pre-cancelled work is not retained or executed;
6. bounded admission rejects excess work without retry;
7. stop settles queued work as `Shutdown` and rejects later work;
8. native startup failure settles `Faulted`;
9. native window close ends the loop and closes admission.

The fake pump owns a real test thread and explicit work／stop signals, but it does not model User32 or physical output.

## Scope guard

This phase does not:

- add the harness to ordinary CLI startup;
- load an external runtime or controlled fixture;
- instantiate the approved playback lifecycle;
- decode H.264 or AAC;
- call D3D11, DXGI, or WASAPI;
- submit Phase 1 evidence;
- claim visible video, audible audio, or complete 0.0.10d validation;
- add retry, fallback, device recreation, timer-driven playback, or resident production.

## Remaining physical work

A later explicit command or dedicated runner entrypoint must compose:

1. this application-owned host;
2. the approved external-runtime lifecycle;
3. one controlled fixture and fixed runtime identity;
4. independent finite video and audio runs;
5. pre-cleanup native evidence capture;
6. production-first output cleanup;
7. Phase 1 evidence admission.

Until accepted `SystemWindows` video and audio records are obtained on Windows 11, physical validation remains pending.
