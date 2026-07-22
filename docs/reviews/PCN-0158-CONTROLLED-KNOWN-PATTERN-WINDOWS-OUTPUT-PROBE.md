# PCN-0158: Controlled Known-Pattern Windows Output Probe

## Milestone

0.0.10d Phase 5

## Review result

`CompletedKnownPatternVisible`

## Original contradiction

The controlled physical video run on main `8c87337bfd6996fa5c018eda512ec143c83be6e4` completed with accepted canonical evidence and one acknowledged video delivery, but the operator observed only a black window during the three-second hold. The operator result was `Video visible: No`.

The accepted evidence proved fixed call accounting and cleanup, but the later diagnosis showed that the private `ID3D11DeviceContext` declaration did not address the intended native upload slot.

## Implemented diagnostic boundary

- Added a dedicated `--controlled-windows-known-pattern-probe` mode to `Nova.ControlledWindowsRunner`.
- Added one generated 320×240 BGRA32 four-quadrant pattern.
- The pattern contains exactly 76,800 opaque non-black pixels and 307,200 bytes.
- The probe uses the real application-owned runner, owner dispatcher, Windows activation, binding, stream initialization, production buffer processing, D3D11 upload, and Present path.
- FFmpeg, the retained media fixture, parser, decoder, conversion, scheduler, timing, and audio writes are not used.
- Exactly one video processing call is permitted.
- Accepted evidence requires one SystemWindows attempt and acknowledgement, the exact byte count, and zero WouldBlock, resize, device loss, configuration conflict, or counter overflow.
- The presented output remains alive for a bounded observation interval without another decode or output call.
- Output ownership is disposed on the runner owner thread before runner shutdown.
- One canonical sanitized record is retained repository-external.
- Operator observation is recorded separately as `Known pattern visible: Yes` or `Known pattern visible: No`.

## Root cause incorporated by revalidation

Both private D3D11 device-context COM declarations omitted `OMSetBlendState` and `OMSetDepthStencilState`. The resulting two-slot shift caused the production declaration named `UpdateSubresource` to invoke native `CopySubresourceRegion` instead.

The corrected declarations restore the official method order and preserve the invoked native `void` signatures. Reflection tests now fix the critical method sequence and signatures.

## Physical result

The corrected production probe was executed once from clean main `5386637fdca7cab8dfd451491f99422930a67bfb`.

- helper exit: `0`;
- `Result: Completed`;
- pattern: `Bgra32QuadrantsV1`;
- Present attempts／acknowledgements: `1／1`;
- accepted bytes: `307200`;
- WouldBlock／resize／device loss: `0／0／0`;
- runner: `Stopped`;
- output cleanup: completed;
- borrowed window and dispatcher: preserved;
- FFmpeg runtime and media fixture: unused;
- audio and paired verification: unexecuted;
- retry, additional Present, fallback, and instrumentation: unexecuted;
- operator observation: `Known pattern visible: Yes`.

This physically validates the corrected production `UpdateSubresource` upload and the shared D3D11／DXGI／Win32 presentation path for the exact generated pixels.

## Regression coverage

Cross-platform injected tests verify:

- exact opaque BGRA layout, dimensions, stride, payload length, four colors, and non-black pixel count;
- exactly one Present call and accepted evidence accounting;
- output cleanup before runner shutdown;
- preserved borrowed window and dispatcher after output cleanup;
- cancellation during the observation hold without a second Present;
- fail-closed observation-duration bounds;
- fixed path-free canonical record fields;
- command parsing for zero, default, maximum, invalid, and nonnumeric holds;
- official D3D11 device-context method order and invoked signatures.

## Next gate

Run the controlled physical video helper exactly once from the newly approved clean main commit, using the approved external runtime, retained fixture, and a new external canonical record. Require deterministic video admission and record `Video visible: Yes` or `Video visible: No`.

Audio and paired verification remain blocked until corrected physical video is accepted and visibly recognizable.