# ADR-0073: Windows Output Buffer Processing Boundary

## Status

Accepted for the 0.0.6x technical boundary only. FFmpeg backend activation,
bundling, and distribution remain unapproved, and license review remains
`Pending`. This Accepted status does not approve any of them.

## Context

0.0.6w initializes activation-owned D3D11/DXGI and WASAPI resources but does
not move decoded data or start output. The next boundary must process exactly
one managed decoded video frame or audio buffer without introducing a
scheduler, continuous loop, synchronization policy, or a second native owner.

## Decision

`PlaybackWindowsOutputBufferProcessingBoundary` exposes independent awaited
video and audio operations with fixed public results. It validates BGRA32 video
dimensions, packed stride, byte length, and generation, and validates
interleaved S16/F32 audio format, rate, channels, frame count, byte length, and
generation before native access.

The production video seam obtains the initialized swap-chain back buffer,
copies one packed BGRA32 frame with `ID3D11DeviceContext.UpdateSubresource`,
binds the activation-owned render-target view, and performs one bounded
`IDXGISwapChain.Present`. It adds no shader, scaling, or color conversion.

The production audio seam obtains the activation-owned `IAudioRenderClient`,
checks `IAudioClient.GetCurrentPadding`, obtains exactly the requested buffer,
copies all managed samples, releases it exactly once, and calls `Start` only on
the first successful submission after initialize/reset. Insufficient capacity
is `WouldBlock`; partial writes are not successful.

All typed native views are internal and valid only inside the synchronized
0.0.6u activation borrow. COM interfaces and OS handles remain owned by 0.0.6u.
Raw pointers, handles, and HRESULT values never enter the public contract.
Unknown HRESULTs, undefined values, missing resources, and exceptions fail
closed. Dispose closes new work, waits finitely for in-flight operations, then
cleans audio before video; reset and dispose are idempotent.

Non-Windows construction selects an unsupported seam and platform validation
returns before native access.

## Consequences

0.0.6x implements a single-call output-processing boundary and production
native seam. It does not implement scheduler integration, a continuous playback
loop, A/V synchronization or drift correction, default-device monitoring,
FFmpeg activation, demux/codec wiring, runtime distribution, or licensing
approval. Real-device behavior remains a separate controlled validation.
