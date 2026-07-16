# PCN-0048 Decoded Output Contract

## Scope

Milestone 0.0.5e adds a backend-neutral data-plane callback between a decoder
selected by `PlaybackDecoderRunner` and future audio/video consumers.

## Flow

```text
bounded encoded lease
        |
        v
IPlaybackDecoder.DecodeAsync
        |
        +-- await WriteAudioAsync(canonical PCM frame)
        |
        +-- await WriteVideoAsync(canonical BGRA32 frame)
        |
        v
callback completes -> decoded payload memory may be reused
```

The runner passes one sink through `PlaybackDecoderCreationContext`. Existing
lifecycle-only diagnostics receive `NullPlaybackDecodedFrameSink`, which
cooperatively discards output.

## Canonical audio

- `PcmSigned16Interleaved` or `PcmFloat32Interleaved`;
- explicit sample rate, channel count, and samples per channel;
- payload length must exactly match the interleaved layout;
- frame duration is finite and positive;
- negative presentation timestamps remain valid for preroll.

## Canonical video

- packed `Bgra32`;
- explicit width, height, and stride;
- stride must hold one packed row;
- payload length must exactly equal `stride * height`;
- frame duration is finite and positive.

## Ownership and backpressure

Decoded payload memory is callback-scoped. The sink may inspect it while the
callback is active and must copy it before returning if retention is needed.
The decoder must await the callback. Therefore a blocked sink blocks the active
decoder callback and the runner retains at most the already bounded active
encoded input/bootstrap ownership.

## Discontinuity

Frames expose only `None` or `DecoderReset` initially. A backend marks the
first affected output after reset. Output-queue drops are not modeled until a
bounded renderer/audio queue policy is selected.

## Exclusions

- no codec package or native binary;
- no FFmpeg, Media Foundation, GStreamer, or libVLC selection;
- no decoded queue, renderer, audio device, clock, or A/V synchronization;
- no claim of audible or visible playback.
