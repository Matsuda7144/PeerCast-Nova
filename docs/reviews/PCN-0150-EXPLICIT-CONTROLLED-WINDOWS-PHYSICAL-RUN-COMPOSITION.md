# PCN-0150: Explicit Controlled Windows Physical Run Composition

## Milestone

0.0.10d Phase 4

## Review result

`CompositionImplementedPhysicalExecutionPending`

## Implemented surface

- bounded, digest-first controlled FLV fixture replay;
- one-shot video or audio composition state machine;
- production connection to the application-owned runner and approved external-runtime lifecycle;
- finite receive, drain, and delivery limits;
- pre-cleanup native evidence capture;
- lifecycle cleanup before runner shutdown;
- post-cleanup lifecycle-state and borrowed-owner verification;
- Phase 1 evidence admission;
- an explicit sanitized command surface;
- dedicated `Nova.ControlledWindowsRunner` executable registered in the solution;
- focused network and CLI tests.

## Admission review

The composition rejects before runner startup when the runtime or fixture path is not an explicit Windows drive path, the validation kind is absent, the digest is not 64 hexadecimal characters, or a byte／operation bound is outside its fixed range.

The command does not locate, download, extract, install, replace, or fall back to another runtime or fixture. Ordinary `Nova.Cli` startup does not invoke the command.

## Fixture review

The external fixture is read only within the configured maximum. Its complete SHA-256 is calculated before packet replay. A mismatch calls no packet consumer.

After verification, the bounded payload is exposed as sequential callback-scoped `PcpChannelPacket` views. Packet sequence and stream position are monotonic. The payload is cleared after replay and is never written to Git, CI artifacts, build output, publish output, installer content, or evidence text.

## Lifecycle review

The production operations use:

- `PlaybackWindowsPhysicalRunnerHost`;
- `PlaybackFfmpegExternalRuntimeManifest.BuiltInCandidate`;
- a policy permitting the approved external native runtime but not bundled runtime use;
- `PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary`;
- the exact borrowed window token and dispatcher;
- the existing production scheduler and output path.

No second decoder owner, scheduler, output adapter, or device-recovery path is introduced.

## Finite decode and delivery review

The caller supplies fixed maximum decode and delivery operation counts. The composition performs no delay, timer, polling, hidden retry, or work resubmission.

`NeedMoreInput` or `StreamRequiresDrain` moves subsequent bounded calls to drain. A selected-media delivery ends decoding successfully. `RetainedWouldBlock` is recorded once and ends the run as rejected; it is not retried.

## Evidence and cleanup review

Native buffer evidence is captured before lifecycle cleanup. The lifecycle is then stopped through its existing fixed production-first contract.

Before any cleanup evidence is submitted, production operations require:

- lifecycle state `Disposed`;
- video and audio buffer states `Disposed`;
- video and audio stream states `Disposed`;
- video and audio binding states `Disposed`.

Missing postconditions fault the run before Phase 1 admission. After those checks, the composition also verifies that:

- the runner remains in `Running` state;
- the original nonzero window token remains present;
- the message loop remains active;
- dispatcher admission remains open;
- one owner-thread dispatcher operation completes and observes owner-thread access.

The composition then submits the pre-cleanup native snapshot, exact runtime build ID, verified fixture digest, generation values, caller-visible delivery counters, zero retry／fallback／recreation counters, and the reviewed cleanup contract through `PlaybackControlledWindowsOutputCaptureBoundary`.

The application runner is stopped after evidence admission or after any earlier failure.

## Focused tests

The composition tests cover:

1. accepted video evidence and lifecycle-before-runner cleanup;
2. accepted audio write and first-start evidence;
3. digest rejection before decode;
4. one caller-visible `WouldBlock` without retry;
5. borrowed-owner contradiction rejection;
6. configuration rejection before runner startup;
7. finite decode-limit exhaustion with mandatory cleanup.

The fixture tests cover exact digest replay, zero exposure on mismatch, consumer rejection without retry, and size／configuration rejection. Command tests cover missing required arguments, invalid media kind, and out-of-range operation limits without starting the composition.

## Dedicated entrypoint and sanitized output

`Nova.ControlledWindowsRunner` is a separate solution project. It handles Ctrl+C through a cancellation token, invokes the public command boundary, and propagates its fixed exit code. It does not initialize ordinary `Nova.Cli` modules or commands.

The command reports only milestone, fixed results, validation kind, approved runtime build ID, fixture digest, byte／packet counts, operation counters, evidence result, borrowed-owner booleans, and confirmation that no external binary or payload was published.

It does not print runtime path, fixture path, device or endpoint identity, HWND, thread ID, native pointer, handle, packet bytes, decoded bytes, or media content.

## Scope guard

This phase does not claim:

- that the dedicated executable was run on a physical Windows 11 machine;
- visible D3D11 output;
- audible WASAPI output;
- accepted physical video or audio evidence;
- combined A／V playback;
- synchronization quality;
- recovery, fallback, continuous playback, GUI, installer, or recorder readiness.

## Remaining work

Run the dedicated executable on an identified Windows 11 physical runner with the exact approved external runtime and retained controlled fixture. Video and audio must run independently. Only accepted `SystemWindows` records may close 0.0.10d physical validation.
