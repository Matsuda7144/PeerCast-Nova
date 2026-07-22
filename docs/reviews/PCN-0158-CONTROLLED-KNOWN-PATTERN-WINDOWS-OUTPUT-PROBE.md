# PCN-0158: Controlled Known-Pattern Windows Output Probe

## Milestone

0.0.10d Phase 5

## Review result

`ImplementedKnownPatternPhysicalRunPending`

## Observed contradiction

The controlled physical video run on main `8c87337bfd6996fa5c018eda512ec143c83be6e4` completed with accepted canonical evidence and one acknowledged video delivery, but the operator observed only a black window during the three-second hold. The operator result is therefore `Video visible: No`.

The accepted evidence proves fixed native call accounting and cleanup. It does not prove that recognizable non-black pixels reached the display.

## Implemented diagnostic boundary

- Added a dedicated `--controlled-windows-known-pattern-probe` mode to `Nova.ControlledWindowsRunner`.
- Added one generated 320×240 BGRA32 four-quadrant pattern.
- The pattern contains exactly 76,800 opaque non-black pixels and 307,200 bytes.
- The probe uses the real application-owned runner, owner dispatcher, Windows activation, binding, stream initialization, buffer processing, D3D11 upload, and Present path.
- FFmpeg, the retained media fixture, parser, decoder, conversion, scheduler, timing, and audio writes are not used.
- Exactly one video processing call is permitted.
- Accepted evidence requires one SystemWindows attempt and acknowledgement, the exact byte count, and zero WouldBlock, resize, device loss, configuration conflict, or counter overflow.
- The presented output remains alive for a bounded observation interval without another decode or output call.
- Output ownership is disposed on the runner owner thread before runner shutdown.
- One canonical sanitized record is retained repository-external.
- Operator observation is recorded separately as `Known pattern visible: Yes` or `Known pattern visible: No`.

## Regression coverage

Cross-platform injected tests verify:

- exact opaque BGRA layout, dimensions, stride, payload length, four colors, and non-black pixel count;
- exactly one present call and accepted evidence accounting;
- output cleanup before runner shutdown;
- preserved borrowed window and dispatcher after output cleanup;
- cancellation during the observation hold without a second present;
- fail-closed observation-duration bounds;
- fixed path-free canonical record fields;
- command parsing for zero, default, maximum, invalid, and nonnumeric holds.

Routine CircleCI remains responsible for Release compilation with analyzers, the full solution test suite, and format verification.

## Required physical result

After merge, execute the known-pattern helper exactly once from the approved clean main commit. Retain the external canonical record and report one operator observation.

- If the four colored quadrants are recognizable, record `Known pattern visible: Yes` and investigate the decoded／converted source frame.
- If the window remains black, record `Known pattern visible: No` and investigate D3D11 upload, swap-chain presentation, and Win32 display behavior.
- If deterministic evidence is rejected, diagnose the first fixed probe boundary before interpreting visibility.

## Deferred work

This review does not claim successful known-pattern display, recognizable physical video, audible audio, audio record admission, paired-record admission, or completion of 0.0.10d. Audio and paired verification remain blocked.
