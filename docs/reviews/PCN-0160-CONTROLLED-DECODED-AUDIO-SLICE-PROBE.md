# PCN-0160: Controlled Decoded-Audio Slice Probe

## Milestone

0.0.10d Phase 5

## Review result

`ImplementedAwaitingSeparatePhysicalExecution`

## Scope

The diagnostic-only command and helper verify the approved external FFmpeg
runtime and retained 66,939-byte fixture, then reuse the existing FLV demux,
native AAC decode, and managed canonical audio conversion. The collector admits
only continuous same-generation 48 kHz stereo Float32 frames and builds the
chronological first 80 ms:

- three full 1,024-sample frames;
- 768 sample frames from the fourth frame;
- 3,840 output sample frames;
- 30,720 output bytes;
- no copy of the fourth frame's remaining 256 sample frames.

The aggregate remains private until complete. Every source completion retains
the existing awaited ownership and one-release contract.

## Output admission

The completed aggregate is written exactly once through the same production
Windows audio buffer path as the accepted known-tone probe. Admission requires
one write attempt and acknowledgement, one Start acknowledgement, zero
WouldBlock, zero device loss, zero video Present, stopped runner, disposed
output lifecycle, and preserved borrowed window and dispatcher.

The scheduler and ordinary one-frame physical composition are bypassed but
unchanged. No retry, second write, resampling, remix, normalization, fade,
synthesis, repetition, stretch, silence, timestamp synthesis, frame selection,
reordering, fallback, or device recreation is added.

## Helper

`tools/Run-ControlledWindowsDecodedAudioSliceProbe.ps1` requires Windows,
exact clean `main` with matching `HEAD` and `origin/main`, the approved runtime,
the retained fixture, and a new repository-external record path. It validates
the five load-role DLL sizes and SHA-256 values and fixture size/digest,
restores and Release-builds the runner, invokes the probe command once, and
atomically promotes only a complete canonical path-free record.

Only `Completed` / `Accepted` requests:

```text
Decoded fixture audio recognizable: Yes / No
```

A deterministic rejection stops before operator observation. Physical
execution, final audio acceptance, and paired verification are outside this
implementation review.
