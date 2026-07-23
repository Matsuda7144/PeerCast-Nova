# PCN-0157: Controlled Known-Audio Tone Probe

## Milestone

0.0.10d Phase 5

## Review result

`ImplementedAndPhysicallyObserved`

## Scope

The diagnostic command creates one fixed pure-managed
`PcmFloat32Interleaved` payload: 48,000 Hz, stereo identical channels, 1,000 Hz,
80 ms, 3,840 frames, 30,720 bytes, peak 0.20, and linear 5 ms fades. It submits
that payload once to the existing production audio buffer processor and records
the existing native evidence.

It reuses the application-owned runner and owner-thread dispatcher, Windows
output activation, binding, stream initialization, evidence capture, cleanup,
and borrowed-owner preservation. It bypasses FFmpeg, external runtime, media
fixture, FLV parser, decoder, managed FFmpeg conversion, scheduler, timing,
physical audio record, and video Present.

## Admission

`Completed` / `Accepted` requires:

- the exact fixed format and payload dimensions;
- one write attempt and acknowledgement;
- one Start acknowledgement;
- zero retained WouldBlock and zero video Present attempts;
- stopped runner and disposed output lifecycle;
- preserved borrowed window and dispatcher;
- no external binary or payload publication.

The helper requires Windows, exact clean `main`, an explicitly approved commit,
and a new repository-external record path. It restores and Release-builds the
runner, invokes the command once, and atomically promotes only a complete
canonical stdout record. It requests operator observation only after
deterministic acceptance.

## Failure policy

WouldBlock, Start rejection, cancellation, invalid evidence, and ownership or
cleanup contradictions fail closed without retry, redelivery, second write,
loop, fallback, device recreation, second queue, or operator question.

## Non-goals

The probe is not final physical audio acceptance, a fixture-audio replacement,
production tone generation, paired verification, normal-playback synthesis, a
buffer-duration change, or a multi-frame delivery correction.

No physical probe is executed by this implementation review.

## Separate physical observation

Clean main `2ca34405b8bdd81611aa2414092c91a95a8d6d51` produced helper exit `0`
and a `Completed` / `Accepted` canonical record: 48,000 Hz stereo
`PcmFloat32Interleaved`, 1,000 Hz, 80 ms, 3,840 sample frames, 30,720 bytes,
write attempts/acknowledgements `1` / `1`, Start acknowledged, zero retained
WouldBlock, stopped runner, disposed lifecycle, and preserved borrowed window
and dispatcher. The operator recorded `Known audio tone audible: Yes`, having
recognized a clear beep.

Exactly one physical probe ran. No retry, second write, fixture media run,
additional probe, paired verification, fallback, or device recreation ran.
Thus the canonical Float32-to-production-WASAPI endpoint path is operational;
the earlier fixture result is narrowed to fixture content, single-frame
duration, decoded conversion, or frame assembly. Paired verification remains
blocked.
