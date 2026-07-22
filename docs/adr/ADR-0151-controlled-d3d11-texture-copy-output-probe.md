# ADR-0151: Controlled D3D11 Texture-copy Output Probe

## Status

Accepted for 0.0.10d Phase 5 black-output diagnosis.

## Context

The controlled physical video run and the generated non-black BGRA quadrant probe both completed with one successful DXGI Present acknowledgement, but the operator observed only a black window. FFmpeg, the retained media fixture, decoded-frame selection, and managed color conversion are therefore no longer required to reproduce the failure.

The current production native upload calls `ID3D11DeviceContext::UpdateSubresource` directly on swap-chain back buffer 0. That API has no HRESULT. A later successful `Present` proves that DXGI accepted presentation, but does not independently prove that the CPU bytes reached the back buffer.

## Decision

Add one diagnostic-only comparison path:

1. generate the same opaque 320×240 BGRA red／green／blue／white quadrant frame;
2. create an exact same-size `DXGI_FORMAT_B8G8R8A8_UNORM` D3D11 texture with the frame as initial data;
3. copy that texture to swap-chain back buffer 0 with `CopyResource`;
4. call `Present` exactly once;
5. retain the existing window and output ownership for one bounded observation interval;
6. clean output ownership on the runner thread before stopping the borrowed runner.

The comparison path is not registered as the production buffer processor and does not mutate the existing `UpdateSubresource` path.

## Interpretation

- visible quadrants: swap-chain and Win32 presentation can display the pixels; the direct `UpdateSubresource` upload is the remaining failing boundary;
- black window: the problem remains below both CPU upload strategies, in swap-chain／window presentation or a shared lower D3D11 boundary;
- deterministic rejection: diagnose the first fixed native result before interpreting operator visibility.

## Constraints

- no FFmpeg runtime, media fixture, parser, decoder, scheduler, timing, audio write, retry, polling loop, or second queue;
- exactly one texture creation, one resource copy, and one Present attempt;
- no production upload behavior change before the comparison is physically observed;
- no device identity, HWND, pointer, handle, pixel payload, or external path is published;
- audio and paired verification remain blocked.
