# ADR-0150: Controlled Known-Pattern Windows Output Probe

## Status

Accepted and physically validated for 0.0.10d Phase 5 black-output diagnosis.

## Context

The controlled physical video run completed deterministically on Windows. The canonical record was accepted, one video delivery was acknowledged by the SystemWindows native evidence boundary, and cleanup preserved the borrowed window and dispatcher. During the explicit three-second observation interval, however, the operator saw only a black window rather than recognizable video.

The deterministic evidence proved only that the declared D3D11 call path returned and Present was acknowledged. It did not prove that the managed COM declaration addressed the intended native method slot or that recognizable pixels reached the screen.

## Decision

Add one diagnostic-only known-pattern output probe that bypasses FFmpeg and media input while reusing the real controlled Windows output ownership path.

The probe:

- creates one visible application-owned runner window and owner-thread dispatcher;
- activates the existing D3D11 and WASAPI Windows resources through the existing activation boundary;
- binds and initializes the existing 320×240 BGRA32 video output and the required audio configuration without writing audio;
- generates one managed 320×240 opaque BGRA frame containing red, green, blue, and white quadrants;
- guarantees all 76,800 pixels are non-black and the payload is exactly 307,200 bytes;
- invokes the production Windows video buffer processor exactly once;
- requires SystemWindows evidence with one Present attempt, one acknowledgement, the exact accepted byte count, and zero WouldBlock, resize, device-loss, conflict, or overflow evidence;
- retains the already presented output for an explicit bounded observation interval without another output operation;
- cleans buffer, stream, binding, and activation ownership on the runner owner thread before stopping the borrowed runner;
- writes one fixed sanitized record containing no path, device, handle, pointer, pixel payload, or operator identity.

The default observation hold is three seconds and the hard maximum is thirty seconds.

## Physical diagnosis and correction

The first known-pattern physical run completed deterministically but displayed black. A later texture-copy comparison terminated twice with access violation at the declared `ID3D11DeviceContext.CopyResource` boundary.

Review against the official D3D11 device-context method order found that both private COM declarations omitted `OMSetBlendState` and `OMSetDepthStencilState`. Every later method addressed a vtable slot two positions too early. The production declaration named `UpdateSubresource` but invoked the native `CopySubresourceRegion` slot, explaining acknowledged Present accounting without the intended BGRA upload.

The correction restored both missing slots and preserved the native `void` signatures used by Nova.

On main `5386637fdca7cab8dfd451491f99422930a67bfb`, the production known-pattern helper then completed with exactly one Present attempt and acknowledgement, 307,200 accepted bytes, zero WouldBlock／resize／device-loss evidence, complete cleanup, and preserved borrowed owners. The operator recognized all four quadrants and recorded:

```text
Known pattern visible: Yes
```

## Diagnostic interpretation

The corrected production `UpdateSubresource` upload and the shared D3D11／DXGI／Win32 presentation path are physically capable of displaying the exact generated non-black pixels. The earlier black physical-video observation is therefore superseded as evidence about the corrected output ABI.

The next required gate is one controlled physical video rerun using the approved external runtime, retained fixture, corrected main, and a new external canonical record. Decoded video visibility remains unclaimed until that run completes.

## Consequences

The probe changes no production decode, scheduler, timing, native evidence, or physical-run record contract. It uses no FFmpeg runtime, fixture, encoded packet, decoded media frame, audio output, retry, fallback, queue, polling loop, timestamp synthesis, or external payload.

Audio validation and paired-record verification remain blocked until corrected physical video is deterministically accepted and visibly recognizable.