# ADR-0071 Windows Output Resource Binding Boundary

## Status

Accepted for the 0.0.6v technical boundary only. FFmpeg backend activation,
bundling, and distribution remain unapproved, and license review remains
`Pending`. This Accepted status does not approve any of them.

## Decision

Add a finite boundary that binds the resources activated in 0.0.6u to the
video renderer and audio sink configuration contracts defined in 0.0.6s. The
boundary accepts only BGRA32 video and interleaved signed-16 or float-32 PCM
audio configurations. It records independent video/audio state and generation
without creating output resources.

Binding returns only fixed bound, activation-required, unsupported-platform,
format-unsupported, already-bound, device-lost, invalid-environment, cancelled,
disposed, or failure results. Undefined values, missing activation resources, and exceptions fail
closed. Non-Windows rejects configuration before the native binding interface
or activation borrow scope is accessed.

## Borrowing and ownership

The 0.0.6u activation boundary remains the sole owner of COM interfaces and OS
handles. It exposes only an internal synchronized callback scope containing
opaque disposable resource identities; no pointer, handle, HRESULT, or disposal
right enters the public binding contract. Activation disposal waits for the
borrow callback to return before releasing its resources.
Production borrowing is restricted to the activation-owning thread; a thread
mismatch returns `InvalidEnvironment` before native binding access.

The binding boundary serializes configure, reset, and disposal. Video binds
before audio. Any partial failure invokes audio then video unbind callbacks.
Reset and disposal close admission, wait for an active callback to finish, and
unbind in the same reverse order. Binding never disposes an activation resource
and repeated reset/disposal cannot repeat a successful native unbind.

Successful configure records the supplied video and audio generations. Reset
removes both bindings and installs the requested next generation. Device loss
is a fixed state that requires reset before reconfiguration.

## Non-goals

No window, swap chain, back buffer, texture, shader, upload, drawing, `Present`,
WASAPI mix-format query, format negotiation, audio-client initialization,
buffer access, stream start, default-device monitoring, scheduler connection,
A/V synchronization, FFmpeg activation, demux, playback, runtime bundling, or
licensing decision is included.
