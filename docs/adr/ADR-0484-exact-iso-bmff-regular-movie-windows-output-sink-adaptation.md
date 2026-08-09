# ADR-0484: Exact ISO BMFF Regular Movie Windows Output Sink Adaptation

## Status

Accepted as the canonical sink adapter from ADR-0483 into the existing Windows
output buffer-processing lifecycle.

## Decision

Borrow one live `PlaybackWindowsOutputBufferProcessingBoundary` and retain
caller-supplied nonnegative video and audio generations. Implement the
canonical decoded-frame sink contract by projecting packed BGRA32 video into
an exact `PlaybackWindowsVideoFrameBuffer` and interleaved signed-16 or float-32
PCM into an exact `PlaybackWindowsAudioSampleBuffer`. Preserve dimensions,
stride, sample format, sample rate, channel count, sample count, payload memory,
and the caller-supplied role generation without copying callback-scoped payload
memory.

Serialize sink callbacks and increment per-role processed counts only when the
existing output boundary reports `Processed`. Propagate caller cancellation.
Treat every other output result or exception as fail-closed sink rejection so
ADR-0483 cannot consume a scheduler reservation as if output succeeded. The
adapter does not own or dispose the shared Windows output boundary.

## Consequences

Regular MP4 canonical decoded frames can now enter the same generation-tagged
Windows output buffer contract already used by existing playback paths. The
adapter preserves callback-scoped memory and shared output ownership.

## Non-goals

Timing admission, wait/late/drop policy, output initialization or reset,
device-loss recovery, D3D11 or WASAPI activation during verification,
application orchestration, native execution, and physical execution are not
included.
