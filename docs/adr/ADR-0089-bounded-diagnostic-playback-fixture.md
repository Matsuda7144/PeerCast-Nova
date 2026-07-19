# ADR-0089: Bounded Diagnostic Playback Fixture

## Status

Accepted for diagnostic-only encoded input. It does not approve FFmpeg activation or general media demux.

## Context

ADR-0088 divided the integration probe into five phases but identified a missing input boundary before `RuntimeDecode`: the existing public playback contracts accept codec parameters and encoded packets, while no finite contract supplies exactly one H.264 video packet and one AAC audio packet.

A general MP4 or FLV demuxer would add container parsing, stream selection, seeking, repeated packet reads, and native ownership before the integration lifecycle is ready. That scope would risk becoming a second production path.

## Decision

Add `BoundedDiagnosticPlaybackFixtureFormat`, a diagnostic-only binary envelope with:

- one H.264 video packet and one AAC audio packet, in fixed order;
- one non-negative presentation timestamp and one positive duration per packet;
- a fixed version and reserved byte;
- a maximum video payload of 1 MiB;
- a maximum audio payload of 256 KiB;
- no metadata strings, paths, stream names, container probing, seeking, retry, or packet loop;
- exact-length validation, including rejection of truncation and trailing data;
- fixed enum results only.

The format copies payloads into managed arrays after lengths and timing have been validated. It does not retain the source stream and does not transfer ownership of the destination decoder, scheduler, sink, runtime lease, or native resources.

## Integration boundary

This fixture is an input contract, not a decoder adapter. ADR-0088 remains responsible for the staged integration plan. A later adapter may translate the two validated packets into existing codec-parameter and packet-processing contracts, but only after license approval and a separate ownership review.

The fixture is not referenced by `PlaybackDecoderBackendFactory`, `IPlaybackDecoderBackend`, `PlaybackDecoderBackendPolicy`, normal CLI startup, or production playback activation.

## Consequences

The project can now test bounded diagnostic input independently of FFmpeg and Windows output. It still cannot claim a real-media start-recover-stop run, MP4/FLV support, decoded output, audible playback, or visible playback.
