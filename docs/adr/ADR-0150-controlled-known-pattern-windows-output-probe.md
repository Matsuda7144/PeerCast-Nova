# ADR-0150: Controlled Known-Pattern Windows Output Probe

## Status

Accepted for 0.0.10d Phase 5 black-output diagnosis.

## Context

The controlled physical video run completed deterministically on Windows. The canonical record was accepted, one video delivery was acknowledged by the SystemWindows native evidence boundary, and cleanup preserved the borrowed window and dispatcher. During the explicit three-second observation interval, however, the operator saw only a black window rather than a recognizable test pattern.

The deterministic evidence proves that the existing D3D11 call returned success. It does not prove that the decoded and converted pixels were non-black or that a recognizable image reached the screen. Changing decoder conversion, D3D11 upload, swap-chain behavior, timing, or the retained fixture without first isolating the fault would risk replacing one unproven assumption with another.

## Decision

Add one diagnostic-only known-pattern output probe that bypasses FFmpeg and media input while reusing the real controlled Windows output ownership path.

The probe:

- creates one visible application-owned runner window and owner-thread dispatcher;
- activates the existing D3D11 and WASAPI Windows resources through the existing activation boundary;
- binds and initializes the existing 320×240 BGRA32 video output and the required audio configuration without writing audio;
- generates one managed 320×240 opaque BGRA frame containing red, green, blue, and white quadrants;
- guarantees all 76,800 pixels are non-black and the payload is exactly 307,200 bytes;
- invokes the existing Windows video buffer processor exactly once;
- requires SystemWindows evidence with one present attempt, one acknowledgement, the exact accepted byte count, and zero WouldBlock, resize, device-loss, conflict, or overflow evidence;
- retains the already presented output for an explicit bounded observation interval without another output operation;
- cleans buffer, stream, binding, and activation ownership on the runner owner thread before stopping the borrowed runner;
- writes one fixed sanitized record containing no path, device, handle, pointer, pixel payload, or operator identity.

The default observation hold is three seconds and the hard maximum is thirty seconds.

## Diagnostic interpretation

- `Known pattern visible: Yes` isolates the black physical-video result above the Windows output path. The next investigation is the selected decoded frame and managed conversion content.
- `Known pattern visible: No` reproduces black output with known non-black pixels and isolates the fault in the Windows output or window-presentation path.
- A rejected deterministic record is diagnosed at its first fixed output boundary before any interpretation of operator visibility.

Operator observation remains separate from deterministic evidence.

## Consequences

The probe changes no production decode, scheduler, timing, native evidence, or physical-run record contract. It uses no FFmpeg runtime, fixture, encoded packet, decoded media frame, audio output, retry, fallback, queue, polling loop, timestamp synthesis, or external payload.

Audio validation and paired-record verification remain blocked until the black-output contradiction is resolved. This ADR does not claim recognizable video, audible audio, paired admission, or completion of 0.0.10d.
