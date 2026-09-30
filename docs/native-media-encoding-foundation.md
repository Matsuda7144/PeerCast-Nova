# Native H.264 / AAC encoding foundation

`Nova.Encoding` is a finite software encoding boundary for owned raw media.
Its models and sessions have no capture, channel, GUI, recording, or publishing
contracts. The FFmpeg implementation references `Nova.Playback` solely to reuse
the existing verified runtime lease and native module/symbol gate; this slice
does not relocate those mature runtime boundaries.

## Initial contracts

- H.264: `libopenh264`, even YUV420P dimensions from 16 to 4096, constant
  frame-index PTS, explicit 1/fps timebase, no B frames or skipped inputs.
  Encoder extradata supplies SPS/PPS. The actual SPS baseline profile is
  validated, an avcC record is built, and Annex B (or declared AVCC lengths)
  is explicitly normalized to four-byte AVCC access units. Matching in-band
  parameter sets are moved out of avc1 samples; configuration changes fail.
- AAC: FFmpeg's `aac`, AAC-LC, 48 kHz stereo, 1024 normalized float PCM sample
  frames per call. Interleaved managed input is copied into native FLTP planes.
  ASC comes from the opened encoder, and output is raw AAC, without an ADTS
  container. Native DTS/PTS, duration, and initial padding remain available.
- Both sessions serialize operations, copy input and output payloads, require
  contiguous input PTS, retain delayed packets during finite EOS, and reject
  input after completion. Completion is idempotent; disposal is idempotent;
  operations after disposal throw. Invalid input does not consume an input
  timestamp. Native failures dispose the session.

Each session owns its ABI gate/module references, context, refcounted frame
buffer, and packet. Packets are copied before unref. Allocation/open failure
releases previously allocated resources. Callers retain the verified runtime
lease until sessions are disposed. No native pointers or backing arrays are
exposed through the public media models.

`AcquireForEncoding` adds encoder symbols to the established gate only when
requested. Normal decoder acquisition keeps its original requirements. The ABI
remains FFmpeg 8.1 (libavcodec 62/libavutil 60), Windows x64, with the existing
verified-directory/System32 loader and dependency checks.

## Acceptance

`MediaEncodingTests` covers managed format bounds, ownership, AVC framing and
configuration validation, and deterministic source generation.
`MediaEncodingNativeAcceptanceTests` requires the existing pinned host runtime
via `NOVA_FMP4_FFMPEG_TEST_RUNTIME`; it never reads the retained encoded fixture.
It generates 96 moving 64x48 frames at 30 fps and 150 stereo PCM blocks containing
a 440 Hz sine, both lasting 3.2 seconds. It verifies encoder lifecycle/ownership,
then directly decodes each encoded track through Nova before muxing.

The integration test preserves AAC priming packets. At a common 48 kHz mux
timescale it shifts both tracks by the encoder-reported initial padding rather
than dropping negative-timestamp packets. This finite acceptance deliberately
retains the leading priming interval; gapless priming removal is not implemented.
Four fragments respect the existing parser's default 64-sample batch limit.
Reparse compares track roles/configuration and each sample's timing, random
access and payload independently of serialized track-run order. A separate
native decode validates the newly muxed file and EOS.

To retain generated artifacts, set a new, non-existing external directory in
`NOVA_ENCODING_ACCEPTANCE_OUTPUT_DIRECTORY` and the tested commit SHA in
`NOVA_ENCODING_ACCEPTANCE_HEAD`. The manifest records packet/flush counts,
configuration and file hashes, timing shift, and semantic/decode gate results.

Physical presentation is a separate opt-in test using the existing Nova DXGI /
WASAPI output runner. Set `NOVA_ENCODING_PHYSICAL_RUN_ONCE=RUN-ONCE`,
`NOVA_ENCODING_ACCEPTANCE_ARTIFACT_DIRECTORY`, the exact matching
`NOVA_ENCODING_ACCEPTANCE_HEAD`, and the verified runtime directory. It checks
component and combined hashes and prior gate results before opening a window.
Human confirmation of motion and audible tone is required in addition to
presentation acknowledgements, EOS, and teardown.

This proves only the finite synthetic raw encode/mux/decode/presentation slice.
Capture, live publishing/relay/WAN, codec reconfiguration, hardware encoding,
long-duration operation, gapless playback, and other-player interoperability
require separate acceptance.

The native send/receive and draining contract follows the pinned
[FFmpeg 8.1.3 public API](https://github.com/FFmpeg/FFmpeg/blob/n8.1.3/libavcodec/avcodec.h).
