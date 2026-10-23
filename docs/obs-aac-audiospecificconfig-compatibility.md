# Bounded OBS AAC AudioSpecificConfig compatibility

Audit predecessor: `a5651357fe2955f07ee9fd14be1d145d01fc6d08`.
Main/merge-base: `1193569f79274a2797e544f8796c85183227eda2`.
PR #259 remains an isolated Draft on `feature/obs-flv-ingest-foundation`.

## Physical evidence and reproduction

The prior real OBS run `PR259-OBS-DIAG-20261003003849-A5651357`
was rejected because the Nova validator rejected a 5-byte AAC
AudioSpecificConfig at its exact 2-byte length gate. The recorded diagnostic was
`AacSequenceHeader / UnsupportedAudioSpecificConfig`, message type 8,
chunk stream 4, message stream 1, declared/assembled payload 7 bytes,
packet type 0, config length 5, bootstrap state Metadata. Accepted packets,
bytes, HEAD and DATA were all zero.

**Original ASC bytes were not retained.** Their fields and semantic form are
unknown; source behavior does not recover them.

The Nova-owned **synthetic managed 5-byte ASC compatibility fixture** is
`11 90 56 E5 00`: AOT 2 (AAC-LC), frequency index 3 (48 kHz), channel
configuration 2 (stereo), GASpecificConfig bits 000 (1024 samples,
no core-coder dependency, no GA extension), syncExtensionType 0x2b7,
extension AOT 5, sbrPresentFlag 0, seven zero alignment bits.
This explicitly signals SBR absence; it does not enable HE-AAC or PS.
It is not claimed byte-identical to OBS.

Framed as `AF 00 11 90 56 E5 00`, it reproduces packet type 0,
config length 5 and payload length 7. Before production edits the new acceptance
test failed 0 passed / 1 failed at the predecessor's exact-length gate with
"Only two-byte AAC-LC AudioSpecificConfig is supported." The same test passes
after semantic validation. Controls retain `12 10` (44.1 kHz stereo) and
`11 90` (48 kHz stereo), plus every previous indexed mono/stereo rate.

## Read-only path audit and reuse

- `ObsFlvPublishingSession.ValidateAac` required exactly two bytes, AOT 2,
  frequency index 0..12, channels 1/2 and the low three GA bits zero. This was
  an ingest shortcut; tag construction and configuration retention copy the
  complete original body. Duplicate comparison is byte based; replacement
  remains terminal.
- `Fmp4Muxer.IsValidAacConfiguration` already parsed AAC-LC fields, explicit
  frequencies, GA flags and exactly the SBR-absent sync extension with zero
  alignment. That Nova-owned grammar is extracted to
  `Core.Media.AacLcAudioSpecificConfigParser`, then reused by muxer and ingest.
  The muxer's wider existing channel/rate/frame policy is retained; ingest
  applies its narrower policy after parsing. No upstream code is copied.
- `PlaybackBoundedFlvParser.ProcessAacSequenceHeader` applies extradata budgets,
  reads AOT/frequency/channels (including explicit rate), and copies the full
  original ASC into the session. Its private header parser does not validate
  the complete tail, so it is not an appropriate ingest validator.
- `PlaybackIsoBmffAudioSpecificConfigHeaderParser` parses a descriptor-backed
  header, not the complete LC grammar. Network depending on Playback would
  also reverse the current Playback -> Network dependency. It is unchanged.
- `PlaybackBoundedFlvDecoderParameterBinding` supplies the complete
  `CodecExtradata` to `ForAac` and the native decoder parameter lease; no
  two-byte reconstruction occurs.
- FLV recording start/timestamp streams inspect sound format and packet type
  and retain complete bootstrap tags; they do not interpret ASC fields.
- `AacFfmpegEncoderSession` retains full native extradata and checks its own
  48 kHz stereo LC profile; encoding configuration types constrain that native
  source. No capture or normal encoder changes are needed.
- Existing two-byte ingest fixtures remain controls. Existing fMP4 tests
  exercise explicit rates, SBR-absent tail, short frames and unsupported
  continuations; they guard the mechanically extracted grammar.

## Acceptance and rejection policy

Ingest accepts AOT 2, channelConfiguration 1 or 2, and sample rates
96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000,
11025, 8000 or 7350 Hz. Frequency index 0..12 or an explicit 24-bit frequency
equal to one of those rates is allowed. All three GA flags must be zero.
Only an immediately following 0x2b7 / AOT 5 / SBR-absent sync extension
and its seven zero alignment bits is additionally permitted.

The bounded grammar accepts lengths 2 (indexed core), 5 (indexed core plus
SBR-absent tail, or explicit-frequency core), or 8 (explicit core plus that
tail), maximum 8 bytes. Full validated bytes survive unchanged in FLV HEAD,
relay/HTTP bootstrap and playback extradata; no fields are reconstructed.

Reject truncated/malformed fields, reserved indices 13/14, zero or unsupported
explicit rate, AOT other than LC (including escaped AOT/SBR/PS), PCE,
other channel configurations, short frames, core-coder dependency, GA
extension, active SBR/other sync extensions, nonzero alignment, arbitrary
extra zero/garbage bytes and oversized configs. The shared parser exposes
facts for its wider fMP4 caller; ingest's whitelist defines this narrower scope.
No general FFmpeg AAC compatibility is claimed.

All invalid ASC forms retain the finite diagnostic
`AacSequenceHeader / UnsupportedAudioSpecificConfig` and safe bounded context.
No taxonomy expansion is needed. Valid byte-identical duplicates remain
allowed and byte-different replacement remains
`FlvBootstrap / CodecReplacement`, even for semantically equivalent configs.

## Managed composition and unchanged boundaries

Managed tests cover semantic controls and malformed/unsupported cases through
owner completion. Early/late headless roundtrips exercise RTMP -> metadata ->
5-byte AAC -> AVC -> HEAD -> DATA -> owner -> PCP relay and HTTP-FLV -> parser.
They assert exact complete configs, canonical metadata/AVC/AAC bootstrap
ordering, stream description, exact audio/video samples and timing, counts,
drain and deregistration. These are synthetic managed connections.

Handshake, command sequencing, metadata tolerance, AVC validation,
bootstrap ordering, relay/HTTP production code, ownership and reconnect
behavior are unchanged.

## Semantic references

Protocol field meanings were checked against primary FFmpeg documentation:
[MPEG-4 audio configuration](https://ffmpeg.org/doxygen/7.1/mpeg4audio_8c_source.html)
and [AAC encoder AudioSpecificConfig emission](https://ffmpeg.org/doxygen/7.1/aacenc_8c_source.html)
(`put_audio_specific_config`, explicitly marking SBR absent).
These are semantic references only, not implementation sources or evidence of
the original OBS bytes. The extracted implementation already belongs to Nova.

## Local managed validation (Windows Release)

All counts below are passed / failed / skipped. Named Network subsets are
included in the Network and full-solution totals, not added to them.

| Scope | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| New ASC compatibility tests | 41 | 0 | 0 |
| Rejection diagnostics | 68 | 0 | 0 |
| OBS-shaped compatibility | 17 | 0 | 0 |
| Ingest host | 27 | 0 | 0 |
| FLV publishing | 28 | 0 | 0 |
| Publish channel owner | 16 | 0 | 0 |
| Relay fanout | 14 | 0 | 0 |
| HTTP-FLV | 6 | 0 | 0 |
| Headless relay/HTTP/parser roundtrip | 4 | 0 | 0 |
| Playback focused FLV/parser/decoder binding/roundtrip | 37 | 0 | 0 |
| Core fMP4/FLV recording focused | 134 | 0 | 0 |
| Nova.Network entire project | 521 | 0 | 0 |
| Nova.Core entire project | 310 | 0 | 0 |
| Nova.Playback entire project | 3104 | 0 | 15 |
| Full solution | 4394 | 0 | 15 |

Release build succeeds with 0 warnings / 0 errors. Format verification and
diff whitespace checks pass. The initial expected red reproduction had one
environmental CS1668 warning from an invalid inherited LIB path; subsequent
validation clears LIB only in the validation process. Exact resulting-head
GHA evidence is recorded separately after the additive commit and normal push.

## Classification and next slice

`AAC AUDIOSPECIFICCONFIG COMPATIBILITY HARDENED` applies to the managed
grammar and tested propagation, subject to local/exact-head CI validation.

`NO REAL OBS RETRY PERFORMED`

`REAL OBS PHYSICAL A/V ACCEPTANCE NOT YET PROVEN AFTER FIX`

After successful exact-head CI, recommend a separately authorized one-shot
real OBS interoperability revalidation. First establish that the AAC rejection
is gone and metadata -> AAC -> AVC -> HEAD -> DATA succeeds. Only then should
that separately authorized run decide whether to admit a viewer and attempt
physical A/V acceptance. No physical attempt is part of this implementation.
