# ADR-0151: Controlled D3D11 Texture-copy Output Probe

## Status

Accepted and physically validated for 0.0.10d Phase 5 black-output diagnosis.

## Context

The controlled physical video run and the generated non-black BGRA quadrant probe both completed with one successful DXGI Present acknowledgement, but the operator observed only a black window. FFmpeg, the retained media fixture, decoded-frame selection, and managed color conversion were therefore no longer required to reproduce the failure.

The production native upload calls `ID3D11DeviceContext::UpdateSubresource` directly on swap-chain back buffer 0. That API has no HRESULT. A later successful `Present` proves that DXGI accepted presentation, but does not independently prove that the CPU bytes reached the back buffer or that the managed COM declaration addressed the intended native slot.

## Decision

Add one diagnostic-only comparison path:

1. generate the same opaque 320×240 BGRA red／green／blue／white quadrant frame;
2. create an exact same-size `DXGI_FORMAT_B8G8R8A8_UNORM` D3D11 texture with the frame as initial data;
3. copy that texture to swap-chain back buffer 0 with `CopyResource`;
4. call `Present` exactly once;
5. retain the existing window and output ownership for one bounded observation interval;
6. clean output ownership on the runner thread before stopping the borrowed runner.

The comparison path is not registered as the production buffer processor.

## Physical-run corrections

The first physical comparison run terminated before record creation with access violation `0xC0000005` at `ID3D11DeviceContext.CopyResource`. Adding `PreserveSig` to the native `void` declaration did not change the physical result: the corrected rerun terminated at the same boundary with the same access violation.

Review against the official `ID3D11DeviceContext` method order found that both Nova COM declarations omitted `OMSetBlendState` and `OMSetDepthStencilState` between `OMSetRenderTargetsAndUnorderedAccessViews` and `SOSetTargets`. Every declared method after that omission therefore addressed a vtable slot two positions too early.

Consequences were deterministic:

- the probe declaration named `CopyResource` but invoked the native `RSSetScissorRects` slot, producing the access violation;
- the production declaration named `UpdateSubresource` but invoked the native `CopySubresourceRegion` slot, so the later successful Present did not contain the intended BGRA upload and the operator observed black output.

The correction restores both missing slots in both declarations, retains `PreserveSig` on the native `void` methods that Nova invokes, and adds reflection coverage for the critical method order and signatures. It changes no retry, scheduling, decoding, timing, fixture, or presentation-count contract.

## Physical validation

On clean main `5386637fdca7cab8dfd451491f99422930a67bfb`, the corrected texture-copy helper completed with:

- `Result: Completed`;
- upload path `CreateTexture2D+CopyResource`;
- exactly one Present attempt and acknowledgement;
- 307,200 accepted bytes;
- zero WouldBlock, resize, and device-loss evidence;
- stopped runner, completed output cleanup, and preserved borrowed window／dispatcher;
- no FFmpeg runtime, media fixture, audio, paired verification, retry, additional Present, fallback, or instrumentation.

The operator recognized the red／green／blue／white quadrants and recorded:

```text
Texture-copy pattern visible: Yes
```

The common D3D11／DXGI／Win32 presentation boundary is therefore physically capable of displaying the exact generated pixels after the vtable correction.

## Interpretation

The corrected texture-copy path and corrected production `UpdateSubresource` path were both subsequently observed displaying the same known pattern. The shared D3D11 output ABI and presentation boundary are no longer the active black-output contradiction.

The next required gate is controlled physical video revalidation with the approved external runtime and retained fixture.

## Constraints

- no FFmpeg runtime, media fixture, parser, decoder, scheduler, timing, audio write, retry, polling loop, or second queue in this probe;
- exactly one texture creation, one resource copy, and one Present attempt;
- production retains one direct `UpdateSubresource` and one Present attempt; only the COM ABI mapping was corrected;
- no device identity, HWND, pointer, handle, pixel payload, or external path is published;
- audio and paired verification remain blocked until corrected physical video is accepted and visibly recognizable.