# ADR-0072 Windows Output Stream Initialization Boundary

## Status

Accepted for the 0.0.6w technical boundary only. FFmpeg backend activation,
bundling, and distribution remain unapproved, and license review remains
`Pending`. This Accepted status does not approve any of them.

## Decision

Add a finite, injectable initialization boundary above the activated and bound
Windows output resources. Video and audio retain independent state and
generation. Public contracts contain only fixed values; native pointers, OS
handles, and raw HRESULTs remain internal.

Video configuration uses an opaque positive window token rather than an OS
handle and validates finite dimensions, BGRA32, and generation. The internal
native boundary owns any partial future swap-chain/render-target candidates and
must unwind them in reverse order. Resize/recreate, format mismatch, and device
loss are fixed results. No upload, drawing, or `Present` occurs.

The production seam resolves the opaque token with `IsWindow`, creates a
published DXGI swap chain, obtains its D3D11 texture, and creates a render-target
view. Successful resources are transferred immediately to the 0.0.6u owner;
failed candidates are released in view, buffer, swap-chain, factory order.

Audio configuration validates interleaved S16/F32, sample rate, channel count,
generation, and a buffer duration from 1 ms through 2 seconds. The internal
native boundary represents mix-format retrieval, supported-format negotiation,
shared event-driven `IAudioClient` initialization, and event association as one
finite call with fixed outcomes. It does not obtain buffers, write samples, or
call `Start`.

The production audio seam reactivates an activation-owned `IAudioClient`, calls
`GetMixFormat`, builds and checks an S16/F32 `WAVEFORMATEX`, initializes shared
event-driven mode for the bounded duration, and associates the activation-owned
event. Temporary mix/closest/requested formats are always released. Reset is
delegated through the typed activation-owned view.

Initialization requires matching 0.0.6v bound generations and borrows 0.0.6u
resources only inside its synchronized owning-thread callback. Ownership is
never duplicated. Video initializes before audio; partial failure, reset, and
dispose clean audio before video. Reset and dispose close admission and wait for
in-flight initialization to return. Undefined values, unknown native results,
exceptions, missing resources, and thread mismatch fail closed.

## Non-goals

No frame upload, shader, drawing, `Present`, audio buffer access, write,
`Start`, default-device monitoring, scheduler integration, A/V synchronization,
FFmpeg activation, demux, playback, runtime bundling/distribution, or licensing
approval is included.
