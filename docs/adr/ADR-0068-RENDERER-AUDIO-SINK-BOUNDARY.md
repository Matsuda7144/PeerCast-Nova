# ADR-0068 Renderer / Audio Sink Boundary

## Status

Accepted for 0.0.6s.

## Decision

Video rendering and audio-device output use separate interfaces and separate
admission gates. Video accepts only canonical packed BGRA32 frames. Audio
accepts only canonical signed-16 or float-32 interleaved PCM frames. Both
boundaries revalidate format, generation, dimensions or audio configuration,
duration, and exact payload length before invoking an implementation. Payloads
are bounded to 256 MiB.

Configure, submit, flush, and reset are awaited and return fixed results.
Device-lost, format-unsupported, temporarily-unavailable, rejected, cancelled,
disposed, and integration-failure states do not escape as implementation
exceptions. Successful reset removes the active configuration; resize, format
change, and device generation change require explicit reconfiguration.

The caller retains ownership of canonical frames. Payload memory is valid only
for the awaited implementation callback. An implementation that needs the data
after return must copy it during that callback. A boundary does not own or
dispose the supplied renderer/device implementation. Disposal closes new
admission without disposing its semaphore, allowing an active callback to
finish and waiting calls to converge on a fixed disposed result. Video and
audio never share a lock.

## Non-goals

No OS graphics/audio API, window, swap chain, device, queue, background task,
thread, timer, pacing, buffering, resampling, synchronization correction,
scheduler integration, demux, backend activation, or FFmpeg distribution is
added.
