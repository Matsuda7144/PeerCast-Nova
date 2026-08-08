# PeerCast Nova Navigation

## Post-0.0.10e bounded ISO BMFF / MP4 parser foundation

PeerCast Nova now has two bounded container-parser families: the existing pure
managed bounded FLV v1 parser and the new bounded ISO BMFF / MP4 parser
foundation. MP4 is therefore the second container format implemented alongside
FLV, not a replacement for the FLV production path.

ADR-0167 through ADR-0172 add the bounded non-physical playback and application
slices that precede the container work: decoded-duration limits, non-native
application information, sample-derived audio duration, the default audio-only
250 ms production cutoff, and the opened-decoder `avcodec_flush_buffers`
boundary.

ADR-0173 through ADR-0300 add the ISO BMFF / MP4 foundation from bounded box
headers and traversal through sample tables, reconciliation and lookup, timing
and edit mapping, edited-sample payload and packet-candidate boundaries,
`avc1` / `avc3`, `mp4a`, bounded owned `avcC`, and the MPEG-4 descriptor
variable-length header parser, followed by bounded AVC sample framing and the
`esds` elementary-stream and decoder-config hierarchy, AudioSpecificConfig and
GASpecificConfig fields, predefined-MP4 SL configuration, and explicit SBR/PS
extension-to-core configuration composition plus immediate implicit SBR/PS
sync extensions for supported direct non-ER core types.
The AVC path additionally classifies framed NAL headers, projects a bounded
`avcC` parameter-set prefix plan, materializes owned length-prefixed or
four-byte-start-code Annex-B payloads with or without parameter sets, and
selects those forms explicitly with source identity, and composes only the form
required by caller-stated decoder payload requirements.
The AAC path additionally exposes the exact GA prefix and the fixed Program
Config Element header and element counts when channel configuration is zero.

Non-immediate AAC sync-extension search, verified runtime capability discovery,
native decoder integration, production MP4 demux composition, nested-box and
streaming policy, and fragmented MP4 remain future work.

- `docs/adr/ADR-0167-opt-in-decoded-buffer-duration-cutoff.md`
- `docs/adr/ADR-0173-bounded-iso-bmff-box-header-parser.md`
- `docs/adr/ADR-0231-bounded-iso-bmff-mpeg4-descriptor-header.md`
- `docs/adr/ADR-0232-bounded-iso-bmff-avc-sample-framing.md`
- `docs/adr/ADR-0233-bounded-iso-bmff-elementary-stream-descriptor.md`
- `docs/adr/ADR-0235-bounded-iso-bmff-audio-specific-config-header.md`
- `docs/adr/ADR-0236-bounded-iso-bmff-ga-specific-config.md`
- `docs/adr/ADR-0237-bounded-iso-bmff-sl-config-descriptor.md`
- `docs/adr/ADR-0238-bounded-iso-bmff-explicit-sbr-ps-extension.md`
- `docs/adr/ADR-0239-bounded-iso-bmff-explicit-extension-ga-composition.md`
- `docs/adr/ADR-0240-bounded-iso-bmff-implicit-sbr-sync-extension.md`
- `docs/adr/ADR-0241-bounded-iso-bmff-implicit-ps-sync-extension.md`
- `docs/adr/ADR-0242-bounded-iso-bmff-avc-nal-unit-classification.md`
- `docs/adr/ADR-0243-bounded-iso-bmff-avc-parameter-set-prefix-plan.md`
- `docs/adr/ADR-0244-bounded-iso-bmff-avc-length-prefixed-payload-materialization.md`
- `docs/adr/ADR-0245-bounded-iso-bmff-avc-annex-b-payload-materialization.md`
- `docs/adr/ADR-0246-bounded-iso-bmff-avc-sample-only-annex-b-materialization.md`
- `docs/adr/ADR-0247-explicit-iso-bmff-avc-payload-selection.md`
- `docs/adr/ADR-0248-explicit-iso-bmff-avc-decoder-payload-requirements.md`
- `docs/adr/ADR-0249-bounded-iso-bmff-avc-payload-composition.md`
- `docs/adr/ADR-0250-bounded-iso-bmff-ga-specific-config-prefix.md`
- `docs/adr/ADR-0251-bounded-iso-bmff-program-config-element-header.md`
- `docs/adr/ADR-0252-bounded-iso-bmff-program-config-element.md`
- `docs/adr/ADR-0253-bounded-iso-bmff-program-config-channel-topology.md`
- `docs/adr/ADR-0254-bounded-iso-bmff-pce-aware-ga-specific-config-composition.md`
- `docs/adr/ADR-0255-bounded-iso-bmff-er-audio-post-config.md`
- `docs/adr/ADR-0256-bounded-iso-bmff-er-implicit-sbr-ps-sync-extension.md`
- `docs/adr/ADR-0257-bounded-iso-bmff-aac-core-format-facts.md`
- `docs/adr/ADR-0258-bounded-iso-bmff-aac-extension-signalling-facts.md`
- `docs/adr/ADR-0259-bounded-iso-bmff-aac-decoder-format-intent.md`
- `docs/adr/ADR-0260-bounded-iso-bmff-aac-decoder-extradata.md`
- `docs/adr/ADR-0261-bounded-iso-bmff-aac-ffmpeg-codec-parameters-input.md`
- `docs/adr/ADR-0262-bounded-iso-bmff-aac-managed-codec-parameters.md`
- `docs/adr/ADR-0263-bounded-iso-bmff-aac-parameter-transfer-plan.md`
- `docs/adr/ADR-0264-bounded-iso-bmff-program-config-channel-slot-map.md`
- `docs/adr/ADR-0265-bounded-iso-bmff-aac-transfer-symbol-coverage.md`
- `docs/adr/ADR-0266-bounded-iso-bmff-aac-declared-decoder-capability-binding.md`
- `docs/adr/ADR-0267-bounded-iso-bmff-er-audio-protection-requirement.md`
- `docs/adr/ADR-0268-bounded-iso-bmff-aac-managed-decoder-preparation.md`
- `docs/adr/ADR-0269-bounded-iso-bmff-aac-sample-entry-consistency-facts.md`
- `docs/adr/ADR-0270-explicit-iso-bmff-aac-sample-entry-consistency-policy.md`
- `docs/adr/ADR-0271-bounded-iso-bmff-aac-sample-entry-managed-decoder-preparation.md`
- `docs/adr/ADR-0272-explicit-iso-bmff-program-config-speaker-position-assignment.md`
- `docs/adr/ADR-0273-bounded-iso-bmff-er-error-protection-specific-config-prefix.md`
- `docs/adr/ADR-0274-bounded-iso-bmff-er-error-protection-first-set-header.md`
- `docs/adr/ADR-0275-bounded-iso-bmff-er-error-protection-first-class.md`
- `docs/adr/ADR-0276-bounded-iso-bmff-er-error-protection-first-set.md`
- `docs/adr/ADR-0277-bounded-iso-bmff-er-error-protection-second-set-header.md`
- `docs/adr/ADR-0278-bounded-iso-bmff-er-error-protection-second-set-first-class.md`
- `docs/adr/ADR-0279-bounded-iso-bmff-er-error-protection-second-set.md`
- `docs/adr/ADR-0280-bounded-iso-bmff-er-error-protection-third-set-header.md`
- `docs/adr/ADR-0281-bounded-iso-bmff-er-error-protection-third-set-first-class.md`
- `docs/adr/ADR-0282-bounded-iso-bmff-er-error-protection-third-set.md`
- `docs/adr/ADR-0283-bounded-iso-bmff-er-error-protection-predefined-sets.md`
- `docs/adr/ADR-0284-bounded-iso-bmff-er-error-protection-header-protection.md`
- `docs/adr/ADR-0285-bounded-iso-bmff-er-error-protection-specific-config.md`
- `docs/adr/ADR-0286-bounded-iso-bmff-er-direct-mapping.md`
- `docs/adr/ADR-0287-explicit-iso-bmff-er-direct-mapping-admission.md`
- `docs/adr/ADR-0288-bounded-iso-bmff-er-configuration-admission.md`
- `docs/adr/ADR-0289-bounded-iso-bmff-er-aac-managed-decoder-preparation.md`
- `docs/adr/ADR-0290-bounded-iso-bmff-program-config-waveformat-channel-layout.md`
- `docs/adr/ADR-0291-bounded-iso-bmff-program-config-channel-reorder-plan.md`
- `docs/adr/ADR-0292-bounded-iso-bmff-program-config-aac-managed-decoder-preparation.md`
- `docs/adr/ADR-0293-bounded-iso-bmff-program-config-interleaved-pcm-reorder.md`
- `docs/adr/ADR-0294-bounded-iso-bmff-program-config-aac-channel-layout-requirement.md`
- `docs/adr/ADR-0295-bounded-iso-bmff-program-config-aac-channel-layout-symbol-coverage.md`
- `docs/adr/ADR-0296-bounded-iso-bmff-program-config-aac-explicit-layout-preparation.md`
- `docs/adr/ADR-0297-bounded-iso-bmff-admitted-er-implicit-sbr-ps-sync-extension.md`
- `docs/adr/ADR-0298-bounded-iso-bmff-admitted-er-aac-decoder-format-intent.md`
- `docs/adr/ADR-0299-bounded-iso-bmff-admitted-er-aac-managed-decoder-preparation.md`
- `docs/adr/ADR-0300-bounded-iso-bmff-codec-packet-projection.md`
- `docs/reviews/PCN-0177-OPT-IN-DECODED-BUFFER-DURATION-CUTOFF.md`
- `docs/reviews/PCN-0183-BOUNDED-ISO-BMFF-BOX-HEADER-PARSER.md`
- `docs/reviews/PCN-0241-BOUNDED-ISO-BMFF-MPEG4-DESCRIPTOR-HEADER.md`
- `docs/reviews/PCN-0242-BOUNDED-ISO-BMFF-AVC-SAMPLE-FRAMING.md`
- `docs/reviews/PCN-0243-BOUNDED-ISO-BMFF-ELEMENTARY-STREAM-DESCRIPTOR.md`
- `docs/reviews/PCN-0245-BOUNDED-ISO-BMFF-AUDIO-SPECIFIC-CONFIG-HEADER.md`
- `docs/reviews/PCN-0246-BOUNDED-ISO-BMFF-GA-SPECIFIC-CONFIG.md`
- `docs/reviews/PCN-0247-BOUNDED-ISO-BMFF-SL-CONFIG-DESCRIPTOR.md`
- `docs/reviews/PCN-0248-BOUNDED-ISO-BMFF-EXPLICIT-SBR-PS-EXTENSION.md`
- `docs/reviews/PCN-0249-BOUNDED-ISO-BMFF-EXPLICIT-EXTENSION-GA-COMPOSITION.md`
- `docs/reviews/PCN-0250-BOUNDED-ISO-BMFF-IMPLICIT-SBR-SYNC-EXTENSION.md`
- `docs/reviews/PCN-0251-BOUNDED-ISO-BMFF-IMPLICIT-PS-SYNC-EXTENSION.md`
- `docs/reviews/PCN-0252-BOUNDED-ISO-BMFF-AVC-NAL-UNIT-CLASSIFICATION.md`
- `docs/reviews/PCN-0253-BOUNDED-ISO-BMFF-AVC-PARAMETER-SET-PREFIX-PLAN.md`
- `docs/reviews/PCN-0254-BOUNDED-ISO-BMFF-AVC-LENGTH-PREFIXED-PAYLOAD-MATERIALIZATION.md`
- `docs/reviews/PCN-0255-BOUNDED-ISO-BMFF-AVC-ANNEX-B-PAYLOAD-MATERIALIZATION.md`
- `docs/reviews/PCN-0256-BOUNDED-ISO-BMFF-AVC-SAMPLE-ONLY-ANNEX-B-MATERIALIZATION.md`
- `docs/reviews/PCN-0257-EXPLICIT-ISO-BMFF-AVC-PAYLOAD-SELECTION.md`
- `docs/reviews/PCN-0258-EXPLICIT-ISO-BMFF-AVC-DECODER-PAYLOAD-REQUIREMENTS.md`
- `docs/reviews/PCN-0259-BOUNDED-ISO-BMFF-AVC-PAYLOAD-COMPOSITION.md`
- `docs/reviews/PCN-0260-BOUNDED-ISO-BMFF-GA-SPECIFIC-CONFIG-PREFIX.md`
- `docs/reviews/PCN-0261-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-ELEMENT-HEADER.md`
- `docs/reviews/PCN-0262-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-ELEMENT.md`
- `docs/reviews/PCN-0263-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-CHANNEL-TOPOLOGY.md`
- `docs/reviews/PCN-0264-BOUNDED-ISO-BMFF-PCE-AWARE-GA-SPECIFIC-CONFIG-COMPOSITION.md`
- `docs/reviews/PCN-0265-BOUNDED-ISO-BMFF-ER-AUDIO-POST-CONFIG.md`
- `docs/reviews/PCN-0266-BOUNDED-ISO-BMFF-ER-IMPLICIT-SBR-PS-SYNC-EXTENSION.md`
- `docs/reviews/PCN-0267-BOUNDED-ISO-BMFF-AAC-CORE-FORMAT-FACTS.md`
- `docs/reviews/PCN-0268-BOUNDED-ISO-BMFF-AAC-EXTENSION-SIGNALLING-FACTS.md`
- `docs/reviews/PCN-0269-BOUNDED-ISO-BMFF-AAC-DECODER-FORMAT-INTENT.md`
- `docs/reviews/PCN-0270-BOUNDED-ISO-BMFF-AAC-DECODER-EXTRADATA.md`
- `docs/reviews/PCN-0271-BOUNDED-ISO-BMFF-AAC-FFMPEG-CODEC-PARAMETERS-INPUT.md`
- `docs/reviews/PCN-0272-BOUNDED-ISO-BMFF-AAC-MANAGED-CODEC-PARAMETERS.md`
- `docs/reviews/PCN-0273-BOUNDED-ISO-BMFF-AAC-PARAMETER-TRANSFER-PLAN.md`
- `docs/reviews/PCN-0274-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-CHANNEL-SLOT-MAP.md`
- `docs/reviews/PCN-0275-BOUNDED-ISO-BMFF-AAC-TRANSFER-SYMBOL-COVERAGE.md`
- `docs/reviews/PCN-0276-BOUNDED-ISO-BMFF-AAC-DECLARED-DECODER-CAPABILITY-BINDING.md`
- `docs/reviews/PCN-0277-BOUNDED-ISO-BMFF-ER-AUDIO-PROTECTION-REQUIREMENT.md`
- `docs/reviews/PCN-0278-BOUNDED-ISO-BMFF-AAC-MANAGED-DECODER-PREPARATION.md`
- `docs/reviews/PCN-0279-BOUNDED-ISO-BMFF-AAC-SAMPLE-ENTRY-CONSISTENCY-FACTS.md`
- `docs/reviews/PCN-0280-EXPLICIT-ISO-BMFF-AAC-SAMPLE-ENTRY-CONSISTENCY-POLICY.md`
- `docs/reviews/PCN-0281-BOUNDED-ISO-BMFF-AAC-SAMPLE-ENTRY-MANAGED-DECODER-PREPARATION.md`
- `docs/reviews/PCN-0282-EXPLICIT-ISO-BMFF-PROGRAM-CONFIG-SPEAKER-POSITION-ASSIGNMENT.md`
- `docs/reviews/PCN-0283-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-SPECIFIC-CONFIG-PREFIX.md`
- `docs/reviews/PCN-0284-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-FIRST-SET-HEADER.md`
- `docs/reviews/PCN-0285-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-FIRST-CLASS.md`
- `docs/reviews/PCN-0286-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-FIRST-SET.md`
- `docs/reviews/PCN-0287-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-SECOND-SET-HEADER.md`
- `docs/reviews/PCN-0288-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-SECOND-SET-FIRST-CLASS.md`
- `docs/reviews/PCN-0289-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-SECOND-SET.md`
- `docs/reviews/PCN-0290-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-THIRD-SET-HEADER.md`
- `docs/reviews/PCN-0291-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-THIRD-SET-FIRST-CLASS.md`
- `docs/reviews/PCN-0292-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-THIRD-SET.md`
- `docs/reviews/PCN-0293-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-PREDEFINED-SETS.md`
- `docs/reviews/PCN-0294-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-HEADER-PROTECTION.md`
- `docs/reviews/PCN-0295-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-SPECIFIC-CONFIG.md`
- `docs/reviews/PCN-0296-BOUNDED-ISO-BMFF-ER-DIRECT-MAPPING.md`
- `docs/reviews/PCN-0297-EXPLICIT-ISO-BMFF-ER-DIRECT-MAPPING-ADMISSION.md`
- `docs/reviews/PCN-0298-BOUNDED-ISO-BMFF-ER-CONFIGURATION-ADMISSION.md`
- `docs/reviews/PCN-0299-BOUNDED-ISO-BMFF-ER-AAC-MANAGED-DECODER-PREPARATION.md`
- `docs/reviews/PCN-0300-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-WAVEFORMAT-CHANNEL-LAYOUT.md`
- `docs/reviews/PCN-0301-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-CHANNEL-REORDER-PLAN.md`
- `docs/reviews/PCN-0302-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-AAC-MANAGED-DECODER-PREPARATION.md`
- `docs/reviews/PCN-0303-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-INTERLEAVED-PCM-REORDER.md`
- `docs/reviews/PCN-0304-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-AAC-CHANNEL-LAYOUT-REQUIREMENT.md`
- `docs/reviews/PCN-0305-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-AAC-CHANNEL-LAYOUT-SYMBOL-COVERAGE.md`
- `docs/reviews/PCN-0306-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-AAC-EXPLICIT-LAYOUT-PREPARATION.md`
- `docs/reviews/PCN-0307-BOUNDED-ISO-BMFF-ADMITTED-ER-IMPLICIT-SBR-PS-SYNC-EXTENSION.md`
- `docs/reviews/PCN-0308-BOUNDED-ISO-BMFF-ADMITTED-ER-AAC-DECODER-FORMAT-INTENT.md`
- `docs/reviews/PCN-0309-BOUNDED-ISO-BMFF-ADMITTED-ER-AAC-MANAGED-DECODER-PREPARATION.md`
- `docs/reviews/PCN-0310-BOUNDED-ISO-BMFF-CODEC-PACKET-PROJECTION.md`

ADR-0158 was skipped by assignment error. It is a permanent numbering-error gap
and must not be retroactively reused, following the recorded ADR-0082 and
ADR-0096 precedent.

## Phase 5 completed audio and paired admission

The separately authorized run from exact main
`6eb3558ec0efd434d9ee1e7221ba86d3c115684f` produced strict canonical
`Completed / Accepted` audio render evidence, passed the independent completed
record preflight, and had same-run operator observations `Audio audible: Yes`
and recognizable retained-fixture beep `Yes`.

ADR-0166 and PCN-0176 connect that 26-line completed render-evidence format
directly to the existing independent video/audio run-set verifier. Legacy audio
records remain supported, terminal records fail closed, no synthetic legacy
record is created, and the verifier performs no physical run.

- `docs/adr/ADR-0166-completed-render-evidence-paired-admission.md`
- `docs/reviews/PCN-0176-COMPLETED-RENDER-EVIDENCE-PAIRED-ADMISSION.md`
- `docs/runbooks/0.0.10d-completed-render-evidence-paired-admission.md`
- `src/Nova.Cli/PlaybackControlledWindowsIndependentRunSetCommand.cs`
- `src/Nova.Cli/PlaybackControlledWindowsIndependentRunSetBoundary.cs`

## Phase 5 corrected physical video validation

The corrected physical video run completed once from clean main `cd7442e18d18acfe2faf43470ef1c97f77ec513f` with the approved external runtime and fixed retained fixture. The canonical record reported `Completed／Accepted`, exactly one delivery, zero retained WouldBlock, stopped runner, disposed lifecycle, preserved borrowed owners, and no external publication. Verify-only single-record preflight returned `VideoAccepted`, and the operator recorded `Video visible: Yes` for recognizable fixture content.

- `tools/Run-ControlledWindowsPhysicalVideo.ps1`
- `docs/runbooks/0.0.10d-controlled-windows-video-revalidation.md`

## Phase 5 video-gated controlled audio run

The accepted visible video record now opens the separately gated audio run. The dedicated Windows helper first requires that exact external record to pass `VideoAccepted`, then executes one physical `audio` run, keeps the accepted WASAPI buffer alive for the default one-second observation interval, retains one external canonical audio record, and requires `AudioAccepted`. It performs no paired verification.

The first audio attempt on clean main
`36ff2d616965975f9d42879cfbf4e23ded99ace1` passed `VideoAccepted` but its
single delivery attempt ended `DeliveryRejected` with zero delivered, zero
retained WouldBlock, and no native audio evidence. Audio preflight and operator
observation were not run; WASAPI reachability remains unproven. The cause was the
generation scheduler's video-first initial reservation conflicting with the
audio-only controlled run while both media kinds were queued. The corrected
controlled lifecycle maps its requested media kind to the existing bounded
scheduler's first successful reservation. Normal production remains video-first
and `DropIncoming`; controlled capacity `16`, `64 MiB`, and `DropOldest` are
unchanged. Corrected physical audio and paired verification remain pending.

The first corrected-selection audio attempt on clean main
`e8b7fcac2f679eb5023ae8ab36571b86773d3950` passed the video gate but
produced zero child stdout bytes and no canonical audio record. The helper exited
`2` and left a zero-byte external audio-record file. Counters, states, and owner
preservation are unknown; audio preflight, audible observation, and paired
verification were not executed. No retry or fallback occurred. Because the old
helper lost the child exit code and stderr boundary, no WASAPI call or crash code
is established, and the result does not refute PR #112.

The controlled diagnostic boundary now promotes only complete canonical stdout
and otherwise writes a separate repository-external sanitized failure record.
The dedicated explicit flag reports only fixed native audio stage names; raw
stderr, paths, device identities, pointers, handles, HRESULTs, and media data are
not recorded. The next physical audio command is diagnostic only and remains
blocked from final acceptance and paired verification.

The first diagnostic invocation from main
`4968e40f801f72bb06a71007a04a47911b6ac377` built the Release runner but
failed inside the Windows PowerShell 5.1 helper before physical-child start.
Neither record was created; physical output, preflight, audible observation, and
paired verification were not run. The corrected boundary keeps the documented
`powershell.exe -NoProfile -ExecutionPolicy Bypass` host while moving child
process construction, concurrent byte capture, diagnostics, and atomic record
promotion into the `net10.0`
`--capture-controlled-windows-audio-child` command. PowerShell 7 is not required,
and this host correction adds no WASAPI or final acceptance evidence.

- `docs/adr/ADR-0148-video-gated-controlled-audio-run.md`
- `docs/reviews/PCN-0156-VIDEO-GATED-CONTROLLED-AUDIO-RUN.md`
- `src/Nova.Cli/PlaybackControlledWindowsAudioChildCaptureCommand.cs`
- `tools/Run-ControlledWindowsPhysicalAudio.ps1`
- `docs/runbooks/0.0.10d-controlled-windows-audio-validation.md`

The later diagnostic run on clean main
`e7e82b01a1eb323954b8ef2375b025064a796a8d` passed the video gate and audio
preflight and produced a `Completed` / `Accepted` record with one delivery,
zero WouldBlock, stopped/disposed lifecycle, and preserved borrowed owners.
The operator recorded `Audio audible: No`: only a short noise-like sound was
heard, not recognizable fixture audio. Deterministic evidence remains valid,
recognizable physical audio remains incomplete, and paired verification is
blocked.

The approved four-frame run on clean main
`63f8be0cc5663ccda0f82bd5eee1c5a3dd3201d2` was executed exactly once and
must not be repeated. It passed the video gate but stopped with
`DeliveryRejected / None`, decode operations `3`, outer delivery attempts `2`,
delivered `1`, and retained WouldBlock `0`. The runner stopped and lifecycle
disposed. The operator recorded `Audio audible: No` after hearing only a
noise-like transient.

The later run on clean main `955ffcdad693391eed1535987c96589eb479741e`
was also executed exactly once and must not be repeated. It reproduced the same
`DeliveryRejected / None`, `3 / 2 / 1 / 0` decode, attempt, delivered, and
retained-WouldBlock counters, with `Audio audible: No`. Because PR #120 had
already added bounded handling for `RetainedWait`, the earlier claim that the
second outer result was necessarily `RetainedWait` is disproven. The exact fixed
lifecycle result was then isolated by the delivery-result diagnostic.
Paired verification remains blocked.

## Phase 5 controlled known-audio tone probe

The diagnostic-only probe generates one fixed managed 80 ms, 1,000 Hz,
48 kHz stereo float tone, submits exactly one production audio-buffer write,
requires one ReleaseBuffer and Start acknowledgement, holds without additional
output, and performs production-first cleanup. It bypasses FFmpeg, fixture,
parser, decoder, scheduler, timing, physical run records, and video Present.
The canonical path-free record distinguishes a recognizable beep from a
canonical PCM/WASAPI interpretation failure without claiming final physical
audio acceptance.

- `docs/adr/ADR-0149-controlled-known-audio-tone-probe.md`
- `docs/reviews/PCN-0157-CONTROLLED-KNOWN-AUDIO-TONE-PROBE.md`
- `src/Nova.Cli/PlaybackControlledWindowsKnownAudioToneProbe.cs`
- `tools/Run-ControlledWindowsKnownAudioToneProbe.ps1`

The separately approved physical probe on clean main
`2ca34405b8bdd81611aa2414092c91a95a8d6d51` completed with accepted evidence,
one acknowledged write and Start, stopped/disposed cleanup, preserved borrowed
owners, and operator observation `Known audio tone audible: Yes`. The 80 ms
1,000 Hz beep was recognizable. Canonical Float32 production WASAPI output is
therefore operational; paired verification was still blocked at that historical
stage while fixture audio assembly was isolated. Final paired admission later
completed under PCN-0176.

## Phase 5 controlled decoded-audio slice probe

This diagnostic reuses the approved runtime, retained fixture, existing FLV
demux/native AAC decode, and managed conversion. It copies the chronological
first 80 ms as three complete 1,024-sample frames plus 768 samples from the
fourth, yielding one private 3,840-frame / 30,720-byte canonical Float32 stereo
buffer. Only the complete aggregate reaches one production audio write. The
scheduler and normal one-frame composition are bypassed but unchanged.

- `docs/adr/ADR-0152-controlled-decoded-audio-slice-probe.md`
- `docs/reviews/PCN-0160-CONTROLLED-DECODED-AUDIO-SLICE-PROBE.md`
- `docs/runbooks/0.0.10d-controlled-decoded-audio-slice-probe.md`
- `src/Nova.Playback/PlaybackDecodedAudioSliceCollector.cs`
- `src/Nova.Cli/PlaybackControlledWindowsDecodedAudioSliceProbe.cs`
- `tools/Run-ControlledWindowsDecodedAudioSliceProbe.ps1`

## Phase 5 controlled audio retained timing resume

Controlled audio alone resumes `RetainedWait` after one upward-rounded
`1024 / 48000` AAC-frame cadence, with at most two resumes for one outer
delivery. The same scheduler item remains retained; no decode, transfer,
enqueue, replacement reservation, conversion, or native write occurs while
timing is early. `RetainedWouldBlock` and every other non-delivery result remain
terminal. Video and normal production timing are unchanged.

The post-merge physical reproduction at `955ffcdad693391eed1535987c96589eb479741e`
did not change the outer failure boundary. Therefore the timing-resume code
remains bounded and reviewed, but `RetainedWait` was not the actual second outer
terminal result.

- `docs/adr/ADR-0155-controlled-audio-retained-timing-resume.md`
- `docs/reviews/PCN-0163-CONTROLLED-AUDIO-RETAINED-TIMING-RESUME.md`
- `docs/runbooks/0.0.10d-controlled-audio-retained-timing-resume.md`
- `src/Nova.Cli/SystemPlaybackControlledWindowsPhysicalRunOperations.cs`
- `tests/Nova.Cli.Tests/SystemPlaybackControlledWindowsPhysicalRunOperationsTests.cs`

## Phase 5 controlled audio delivery-result diagnostic

The dedicated diagnostic reuses the unchanged physical-run composition through
an operations decorator. It records the exact fixed result returned by each
outer delivery call, in order, while delegating all work unchanged. The separate
record is bounded to the four-delivery audio target and explicitly states that it
is not an audio-acceptance record.

The approved diagnostic on clean main
`fe427fdc6094f55ea1a1f321217eff635febf898` executed exactly once. It recorded
`AudioDelivered` followed by `VideoDropped`, with decode operations `3`, outer
delivery attempts `2`, delivered `1`, retained WouldBlock `0`, and operator
observation `Audio audible: No`. No failure record was produced, and the command
must not be repeated.

- `docs/adr/ADR-0156-controlled-audio-delivery-result-diagnostic.md`
- `docs/reviews/PCN-0164-CONTROLLED-AUDIO-DELIVERY-RESULT-DIAGNOSTIC.md`
- `docs/runbooks/0.0.10d-controlled-audio-delivery-result-diagnostic.md`
- `src/Nova.Cli/PlaybackControlledWindowsDeliveryDiagnosticCommand.cs`
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsDeliveryDiagnosticCommandTests.cs`
- `tools/Run-ControlledWindowsPhysicalAudioDeliveryDiagnostic.ps1`

## Phase 5 controlled audio fixed-kind scheduler reservation

The exact `AudioDelivered` then `VideoDropped` sequence proves that the normal
A/V scheduler preference changed to video after the first audio delivery. The
controlled audio generation now opts into fixed-kind reservation: every
scheduler reservation searches audio only and never falls back to video.
Unmatched video remains scheduler-owned until ordinary bounded overflow, reset,
or generation cleanup.

The new scheduler option defaults to disabled. Controlled video and normal
production preserve initial preference, caller-driven A/V alternation, and
opposite-kind fallback. The implementation review performs no physical output.

- `docs/adr/ADR-0157-controlled-audio-fixed-kind-scheduler-reservation.md`
- `docs/reviews/PCN-0165-CONTROLLED-AUDIO-FIXED-KIND-SCHEDULER-RESERVATION.md`
- `docs/runbooks/0.0.10d-controlled-audio-fixed-kind-scheduler-reservation.md`
- `src/Nova.Playback/BoundedPlaybackScheduler.cs`
- `src/Nova.Cli/SystemPlaybackControlledWindowsPhysicalRunOperations.cs`
- `tests/Nova.Playback.Tests/BoundedPlaybackSchedulerRestrictedOutputTests.cs`

## Phase 5 corrected Windows output probes

The private `ID3D11DeviceContext` declarations omitted `OMSetBlendState` and `OMSetDepthStencilState`, shifting every later COM vtable call by two slots. The correction restored the official method order and native `void` signatures.

On main `5386637fdca7cab8dfd451491f99422930a67bfb`, both the diagnostic `CreateTexture2D+CopyResource` path and the production direct `UpdateSubresource` path completed with one Present acknowledgement, exact accepted bytes, complete cleanup, and visible red／green／blue／white quadrants.

- `docs/adr/ADR-0150-controlled-known-pattern-windows-output-probe.md`
- `docs/reviews/PCN-0158-CONTROLLED-KNOWN-PATTERN-WINDOWS-OUTPUT-PROBE.md`
- `docs/runbooks/0.0.10d-controlled-windows-known-pattern-probe.md`
- `tools/Run-ControlledWindowsKnownPatternProbe.ps1`
- `docs/adr/ADR-0151-controlled-d3d11-texture-copy-output-probe.md`
- `docs/reviews/PCN-0159-CONTROLLED-D3D11-TEXTURE-COPY-OUTPUT-PROBE.md`
- `docs/runbooks/0.0.10d-controlled-windows-texture-copy-probe.md`
- `tools/Run-ControlledWindowsTextureCopyProbe.ps1`
- `src/Nova.Playback/PlaybackWindowsOutputTextureCopyProbeBoundary.cs`
- `tests/Nova.Playback.Tests/PlaybackWindowsD3D11ComSignatureTests.cs`

## Phase 5 single physical run record preflight

A verify-only command reads one repository-external canonical physical record through the same bounded UTF-8 reader and submits it to the independent run-set admission boundary used by paired verification. It starts no runner, runtime, fixture, decode, or physical output and never prints the record path.

- `docs/adr/ADR-0147-single-physical-run-record-preflight.md`
- `docs/reviews/PCN-0155-SINGLE-PHYSICAL-RUN-RECORD-PREFLIGHT.md`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunRecordPreflightCommand.cs`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunRecordFile.cs`
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsPhysicalRunRecordPreflightCommandTests.cs`

## Phase 5 bounded physical output observation hold

The controlled physical-run composition keeps an already delivered item and its existing output lifecycle alive for one bounded operator-observation interval before evidence capture and cleanup. No decode, scheduler delivery, retry, resubmission, native output operation, timestamp change, or evidence mutation occurs during the hold.

- `docs/adr/ADR-0146-bounded-physical-output-observation-hold.md`
- `docs/reviews/PCN-0154-BOUNDED-PHYSICAL-OUTPUT-OBSERVATION-HOLD.md`
- `src/Nova.Cli/PlaybackControlledWindowsPhysicalRunComposition.cs`
- `tests/Nova.Cli.Tests/PlaybackControlledWindowsPhysicalObservationHoldTests.cs`

## Phase 5 production timing and decoded timestamps

The bounded FLV decoder configures the exact parser stream time base through public FFmpeg APIs. The production timing path anchors the first valid decoded presentation timestamp to the first monotonic playback-position read and preserves later source-relative offsets. Raw timestamps are not rewritten, tolerances are unchanged, and no retry or fallback path is added.

- `docs/adr/ADR-0144-explicit-decoder-packet-time-base-propagation.md`
- `docs/reviews/PCN-0152-DECODED-TIMESTAMP-PROPAGATION.md`
- `src/Nova.Playback/PlaybackDecoderFfmpegPacketTimeBaseConfiguration.cs`
- `docs/adr/ADR-0145-production-timing-presentation-origin.md`
- `docs/reviews/PCN-0153-PRODUCTION-TIMING-PRESENTATION-ORIGIN.md`
- `src/Nova.Playback/PlaybackAvTimingDecisionBoundary.cs`

## 現在地

**Post-0.0.10e — Bounded ISO BMFF / MP4 Parser Foundation — Complete through ADR-0300**

- ADR-0167 through ADR-0172 complete the bounded non-physical playback and application prerequisites;
- ADR-0173 through ADR-0300 complete the current managed ISO BMFF / MP4 parser foundation through bounded `avcC`, AVC sample framing and NAL classification, parameter-set planning, owned length-prefixed／Annex-B materialization, explicit requirement resolution and bounded composition, the `esds` hierarchy, General Audio and predefined-MP4 SL configuration, explicit plus supported immediate implicit SBR/PS signalling, syntactic Program Config Element parsing, channel-count topology, ordered channel-slot mapping, explicit speaker-position assignment, WaveFormat layout projection, managed-only channel-reorder planning, PCE-aware managed decoder preparation, bounded interleaved-PCM reorder materialization, explicit FFmpeg channel-layout requirements, declared-symbol coverage and end-to-end managed preparation, PCE-aware GA continuation, supported ER `epConfig`, complete bounded Error Protection Specific Config ownership, direct-mapping facts and enabled-only admission, source-bound ER configuration admission and managed AAC preparation, admitted ER immediate implicit SBR/PS, exact decoder-format intent and final managed decoder preparation, AAC core and extension-signalling facts, decoder-format intent, owned extradata, FFmpeg codec-parameter input adaptation, managed padded parameter ownership, managed-only native-transfer requirement planning, bounded declared-symbol coverage, declared decoder-capability binding, end-to-end managed AAC decoder preparation, `mp4a`/effective-format consistency facts, explicit mismatch admission policy, sample-entry-aware managed decoder preparation, and codec-neutral AVC/AAC packet projection;
- ISO BMFF / MP4 is the second bounded container-parser family alongside the existing FLV v1 parser;
- current implementation includes box traversal, movie／track／media metadata, sample tables and reconciliation, chunk／sample／byte-range／`mdat` lookup, timing and edit mapping, edited-sample payload and packet-candidate boundaries, `avc1`／`avc3`／`mp4a` sample-entry projection, bounded AVC sample framing, and bounded MPEG-4 audio descriptor metadata through explicit SBR/PS-to-GASpecificConfig composition and predefined-MP4 SL configuration;
- AAC ER non-immediate sync-extension search, Program Config Element runtime decoder-layout binding, AVC runtime capability discovery and decoder integration, production MP4 demux composition, nested-box and streaming policy, and fragmented MP4 remain incomplete;
- current reviews span `docs/reviews/PCN-0177-OPT-IN-DECODED-BUFFER-DURATION-CUTOFF.md` through `docs/reviews/PCN-0310-BOUNDED-ISO-BMFF-CODEC-PACKET-PROJECTION.md`;
- ADR-0158 is a permanent numbering-error gap caused by assignment error, following ADR-0082 and ADR-0096, and must not be retroactively reused;
- next: bind the admitted ER decoder-format intent to managed decoder preparation without native execution.

**0.0.10e — Shared Windows Application Ownership Boundary — Complete**

- 0.0.10d is complete; Issue #129 is closed after accepted same-run audio evidence and final independent paired admission;
- `Nova.Windows` now owns the single reusable application window, Win32 message loop, owner thread, and bounded dispatcher implementation;
- `Nova.Cli` references that shared host for controlled commands rather than owning or duplicating it;
- `Nova.Playback` continues to borrow only the dispatcher contract and window token;
- `Nova.App` now creates exactly one shared host, waits for close or cancellation, then stops and disposes it without any controlled-command startup;
- `Nova.App` now provides an immutable, size-bounded legal/About model from the exact retained FFmpeg notice, provenance, and runtime-replacement text and projects it only through the shared dispatcher with a finite lifetime;
- `Nova.App` now has a framework-dependent `win-x64` portable publish contract that copies the project license material and exact FFmpeg legal allowlist without any external runtime payload;
- the portable output remains the only implemented distribution boundary; installer, signing, and update mechanics require separate future ADRs;
- current reviews: `docs/reviews/PCN-0170-SHARED-WINDOWS-APPLICATION-OWNERSHIP.md`, `docs/reviews/PCN-0171-USER-FACING-WINDOWS-STARTUP-COMPOSITION.md`, `docs/reviews/PCN-0172-BOUNDED-LEGAL-ABOUT-PROJECTION.md`, `docs/reviews/PCN-0173-FRAMEWORK-DEPENDENT-WINDOWS-PORTABLE-PACKAGE.md`, and `docs/reviews/PCN-0174-INSTALLER-SIGNING-UPDATE-BOUNDARY.md`;
- next: no release mechanism is selected; installer, signing, and update mechanics remain separate reviewed decisions.

**0.0.10d — Controlled Physical Windows Video／Audio Output Validation — Complete**

- authoritative state: `docs/reviews/PCN-0166-CONTROLLED-AUDIO-RENDER-AUTHORIZATION-STATE.md`, `docs/reviews/PCN-0167-MANAGED-AUDIO-FAILURE-BOUNDARY.md`, and Issue #129; documentation, CI, and readiness do not grant or select physical authorization;
- corrected decoded physical video: `Completed／Accepted`, `VideoAccepted`, visible `Yes`;
- known 80 ms tone and decoded fixture slice: accepted and recognizable;
- ordinary controlled fixture audio: consumed readiness-gated runs at `7fbeb8b0dabd9c6af5b2dc9d065d66fd05faf53d`, `77a947f98e7a9449523b49a39bc8b4e8d7ed430b`, `774342a03c33713b1b7f70e2cb664397c04f8d04`, and `847b0ea4cff8aca48c103e499c0bf2bca3fbc6b5` each had operator observation `Audio audible: Yes` for a recognizable beep;
- the `7fbeb8...` run ended `DeliveryRejected / PreCleanupCheckpointRejected`; the `77a947f...` run ended `DeliveryRejected / PreCleanupTraceCardinalityRejected`;
- static source review identified a two-stage trace loss across production generation cleanup and later lifecycle production-owner cleanup;
- PR #201 merged as main `7e31627151f5a6dba7fb9e3f0b03cd08be3b14cf` and retains the immutable scheduler trace across both ownership boundaries without changing physical-output behavior;
- the prior cardinality rejection was an evidence-retention asymmetry, not proof of an extra native audio write;
- the later `774342a...` run ended child exit `1`, stdout／stderr bytes `0／3122`, final stage `ActivationCleanupCompleted`, controlled result `None`, and terminal rejection `None`; it retained version 5 sanitized failure SHA-256 `F8B8A0553A6DDF0935BFD2C075FFE32E541659E3A13C1275130E655363B5FAF5`;
- the historical private stderr classification for that run cannot be proven from the retained record and is not inferred;
- PR #203 merged as main `e265a2b47731214a2f26a0b4a4227721158490b2` and prospectively bounds unexpected direct managed-command failures as exact `Faulted` output with fixed private phase diagnostics and `ProjectionRejected` for snapshot／terminal-projection failures;
- PR #205 merged as main `46a9ff163e8b0fb4582175d4ff1d56cd753fdeb9` and adds strict 22-line child failure record v6 with exactly one allowlisted managed phase, v2-v5 readability, and legacy v5 serializer/preflight compatibility;
- the `847b0ea...` run ended `DeliveryRejected / PreCleanupTraceCardinalityRejected`, retained an `826`-byte／`22`-line failure record with SHA-256 `BF1E4BD3304142CB53737F63C1D1849BF3B67546464C53188E874E21A01E50C1`, and again had `Audio audible: Yes`;
- static tracing established that the dispatcher-backed Windows generation path did not forward `CaptureDeliveryObservations`, so the scheduler trace remained empty while the native audio trace was retained;
- PR #213 merged as main `a6ec858162b1170cc35a5bbc94ae4068f4335090` and forwards that setting without changing physical-output or authorization behavior;
- the later `e0809e9...` run retained canonical terminal SHA-256 `859651D2DED3417F4F55BBAD1E210D1B48AC9E274681D669D6CF1C516E851C78`, ended `DeliveryRejected / None` after two acknowledged native writes, and had `Audio audible: Yes` for a recognizable beep;
- its third audio sequence requested an approximately `6 ms` retained wait, but the fixed `21.3334 ms` controlled delay plus Windows scheduling overhead made the resumed sequence `AudioLate`;
- `docs/adr/ADR-0165-controlled-audio-observed-timing-resume.md`, `docs/reviews/PCN-0175-CONTROLLED-AUDIO-OBSERVED-TIMING-RESUME.md`, and `docs/runbooks/0.0.10d-controlled-audio-observed-timing-resume.md` define the bounded non-physical correction and same-sequence trace coalescing;
- the final accepted run at `6eb3558...` produced the canonical completed record, passed independent preflight, and established same-run physical completion, formal audio acceptance, and recognizable fixture audibility;
- retained contradictory ordinary／diagnostic results remain historical evidence and must not be rerun;
- prospective completed／terminal／child-failure／helper-failure retention and strict offline review are implemented;
- PR #192 through PR #205 added bounded run-result and terminal-rejection retention, exact audio stream-format validation, bounded readiness failure disclosure, direct readiness child capture, granular pre-cleanup rejection diagnostics, terminal trace retention, managed command failure bounding, and managed failure phase retention;
- all recorded readiness and physical authorizations through `6eb3558...` are consumed; all existing readiness and outcome paths are not reusable; no current readiness capture or physical audio run is authorized;
- final independent paired verification completed on merged main `4f38de92ce66d988479f5e22b263de9f84b32086`, accepted both immutable retained records, executed no physical run, and preserved both hashes;
- Issue #129 is closed as completed;
- the concrete non-physical gap identified by `e0809e9...` is addressed prospectively by ADR-0165 and PCN-0175;
- approved runtime: `btbn.ffmpeg.20260721.win64.lgpl-shared`;
- fixture: `66,939` bytes, SHA-256 `32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15`;
- no runtime path, fixture path, record path, device／endpoint identity, HWND, thread ID, pointer, handle, HRESULT, raw process output, or payload is retained in the repository.

Any later physical audio run requires a separately selected exact `main`, final-head and post-merge CircleCI success, a newly captured and independently revalidated canonical readiness record, fresh repository-external outcome paths, a new explicit one-run authorization, and exactly one invocation through the readiness-required entrypoint. Readiness, a documentation-only merge, and the literal invocation guard are not authorization.

## CI

GitHub Actions利用枠消費中はCircleCIをroutine Linux merge gateとして使用します。最終headでrestore、Release build with analyzers、full solution tests、format verificationを通してからmergeします。Physical Windows execution remains a separate explicit authorization boundary and is never implied by routine CI.

## 最新の入口

- `README.md`
- `ROADMAP.md`
- `CHANGELOG.md`
- `docs/adr/ADR-0167-opt-in-decoded-buffer-duration-cutoff.md`
- `docs/adr/ADR-0173-bounded-iso-bmff-box-header-parser.md`
- `docs/adr/ADR-0231-bounded-iso-bmff-mpeg4-descriptor-header.md`
- `docs/adr/ADR-0232-bounded-iso-bmff-avc-sample-framing.md`
- `docs/adr/ADR-0233-bounded-iso-bmff-elementary-stream-descriptor.md`
- `docs/adr/ADR-0235-bounded-iso-bmff-audio-specific-config-header.md`
- `docs/adr/ADR-0236-bounded-iso-bmff-ga-specific-config.md`
- `docs/adr/ADR-0237-bounded-iso-bmff-sl-config-descriptor.md`
- `docs/adr/ADR-0238-bounded-iso-bmff-explicit-sbr-ps-extension.md`
- `docs/adr/ADR-0239-bounded-iso-bmff-explicit-extension-ga-composition.md`
- `docs/adr/ADR-0240-bounded-iso-bmff-implicit-sbr-sync-extension.md`
- `docs/adr/ADR-0241-bounded-iso-bmff-implicit-ps-sync-extension.md`
- `docs/adr/ADR-0242-bounded-iso-bmff-avc-nal-unit-classification.md`
- `docs/adr/ADR-0243-bounded-iso-bmff-avc-parameter-set-prefix-plan.md`
- `docs/adr/ADR-0244-bounded-iso-bmff-avc-length-prefixed-payload-materialization.md`
- `docs/adr/ADR-0245-bounded-iso-bmff-avc-annex-b-payload-materialization.md`
- `docs/adr/ADR-0246-bounded-iso-bmff-avc-sample-only-annex-b-materialization.md`
- `docs/adr/ADR-0247-explicit-iso-bmff-avc-payload-selection.md`
- `docs/adr/ADR-0248-explicit-iso-bmff-avc-decoder-payload-requirements.md`
- `docs/adr/ADR-0249-bounded-iso-bmff-avc-payload-composition.md`
- `docs/adr/ADR-0250-bounded-iso-bmff-ga-specific-config-prefix.md`
- `docs/adr/ADR-0251-bounded-iso-bmff-program-config-element-header.md`
- `docs/adr/ADR-0252-bounded-iso-bmff-program-config-element.md`
- `docs/adr/ADR-0253-bounded-iso-bmff-program-config-channel-topology.md`
- `docs/adr/ADR-0254-bounded-iso-bmff-pce-aware-ga-specific-config-composition.md`
- `docs/adr/ADR-0255-bounded-iso-bmff-er-audio-post-config.md`
- `docs/adr/ADR-0256-bounded-iso-bmff-er-implicit-sbr-ps-sync-extension.md`
- `docs/adr/ADR-0257-bounded-iso-bmff-aac-core-format-facts.md`
- `docs/adr/ADR-0258-bounded-iso-bmff-aac-extension-signalling-facts.md`
- `docs/adr/ADR-0259-bounded-iso-bmff-aac-decoder-format-intent.md`
- `docs/adr/ADR-0260-bounded-iso-bmff-aac-decoder-extradata.md`
- `docs/adr/ADR-0261-bounded-iso-bmff-aac-ffmpeg-codec-parameters-input.md`
- `docs/adr/ADR-0262-bounded-iso-bmff-aac-managed-codec-parameters.md`
- `docs/adr/ADR-0263-bounded-iso-bmff-aac-parameter-transfer-plan.md`
- `docs/adr/ADR-0264-bounded-iso-bmff-program-config-channel-slot-map.md`
- `docs/adr/ADR-0265-bounded-iso-bmff-aac-transfer-symbol-coverage.md`
- `docs/adr/ADR-0266-bounded-iso-bmff-aac-declared-decoder-capability-binding.md`
- `docs/adr/ADR-0267-bounded-iso-bmff-er-audio-protection-requirement.md`
- `docs/adr/ADR-0268-bounded-iso-bmff-aac-managed-decoder-preparation.md`
- `docs/adr/ADR-0269-bounded-iso-bmff-aac-sample-entry-consistency-facts.md`
- `docs/adr/ADR-0270-explicit-iso-bmff-aac-sample-entry-consistency-policy.md`
- `docs/adr/ADR-0271-bounded-iso-bmff-aac-sample-entry-managed-decoder-preparation.md`
- `docs/adr/ADR-0272-explicit-iso-bmff-program-config-speaker-position-assignment.md`
- `docs/adr/ADR-0273-bounded-iso-bmff-er-error-protection-specific-config-prefix.md`
- `docs/adr/ADR-0274-bounded-iso-bmff-er-error-protection-first-set-header.md`
- `docs/adr/ADR-0275-bounded-iso-bmff-er-error-protection-first-class.md`
- `docs/adr/ADR-0276-bounded-iso-bmff-er-error-protection-first-set.md`
- `docs/adr/ADR-0277-bounded-iso-bmff-er-error-protection-second-set-header.md`
- `docs/adr/ADR-0278-bounded-iso-bmff-er-error-protection-second-set-first-class.md`
- `docs/adr/ADR-0279-bounded-iso-bmff-er-error-protection-second-set.md`
- `docs/adr/ADR-0280-bounded-iso-bmff-er-error-protection-third-set-header.md`
- `docs/adr/ADR-0281-bounded-iso-bmff-er-error-protection-third-set-first-class.md`
- `docs/adr/ADR-0282-bounded-iso-bmff-er-error-protection-third-set.md`
- `docs/adr/ADR-0283-bounded-iso-bmff-er-error-protection-predefined-sets.md`
- `docs/adr/ADR-0284-bounded-iso-bmff-er-error-protection-header-protection.md`
- `docs/adr/ADR-0285-bounded-iso-bmff-er-error-protection-specific-config.md`
- `docs/adr/ADR-0286-bounded-iso-bmff-er-direct-mapping.md`
- `docs/adr/ADR-0287-explicit-iso-bmff-er-direct-mapping-admission.md`
- `docs/adr/ADR-0288-bounded-iso-bmff-er-configuration-admission.md`
- `docs/adr/ADR-0289-bounded-iso-bmff-er-aac-managed-decoder-preparation.md`
- `docs/adr/ADR-0290-bounded-iso-bmff-program-config-waveformat-channel-layout.md`
- `docs/adr/ADR-0291-bounded-iso-bmff-program-config-channel-reorder-plan.md`
- `docs/adr/ADR-0292-bounded-iso-bmff-program-config-aac-managed-decoder-preparation.md`
- `docs/adr/ADR-0293-bounded-iso-bmff-program-config-interleaved-pcm-reorder.md`
- `docs/adr/ADR-0294-bounded-iso-bmff-program-config-aac-channel-layout-requirement.md`
- `docs/adr/ADR-0295-bounded-iso-bmff-program-config-aac-channel-layout-symbol-coverage.md`
- `docs/adr/ADR-0296-bounded-iso-bmff-program-config-aac-explicit-layout-preparation.md`
- `docs/adr/ADR-0297-bounded-iso-bmff-admitted-er-implicit-sbr-ps-sync-extension.md`
- `docs/adr/ADR-0298-bounded-iso-bmff-admitted-er-aac-decoder-format-intent.md`
- `docs/adr/ADR-0299-bounded-iso-bmff-admitted-er-aac-managed-decoder-preparation.md`
- `docs/adr/ADR-0300-bounded-iso-bmff-codec-packet-projection.md`
- `docs/reviews/PCN-0177-OPT-IN-DECODED-BUFFER-DURATION-CUTOFF.md`
- `docs/reviews/PCN-0183-BOUNDED-ISO-BMFF-BOX-HEADER-PARSER.md`
- `docs/reviews/PCN-0241-BOUNDED-ISO-BMFF-MPEG4-DESCRIPTOR-HEADER.md`
- `docs/reviews/PCN-0242-BOUNDED-ISO-BMFF-AVC-SAMPLE-FRAMING.md`
- `docs/reviews/PCN-0243-BOUNDED-ISO-BMFF-ELEMENTARY-STREAM-DESCRIPTOR.md`
- `docs/reviews/PCN-0245-BOUNDED-ISO-BMFF-AUDIO-SPECIFIC-CONFIG-HEADER.md`
- `docs/reviews/PCN-0246-BOUNDED-ISO-BMFF-GA-SPECIFIC-CONFIG.md`
- `docs/reviews/PCN-0247-BOUNDED-ISO-BMFF-SL-CONFIG-DESCRIPTOR.md`
- `docs/reviews/PCN-0248-BOUNDED-ISO-BMFF-EXPLICIT-SBR-PS-EXTENSION.md`
- `docs/reviews/PCN-0249-BOUNDED-ISO-BMFF-EXPLICIT-EXTENSION-GA-COMPOSITION.md`
- `docs/reviews/PCN-0250-BOUNDED-ISO-BMFF-IMPLICIT-SBR-SYNC-EXTENSION.md`
- `docs/reviews/PCN-0251-BOUNDED-ISO-BMFF-IMPLICIT-PS-SYNC-EXTENSION.md`
- `docs/reviews/PCN-0252-BOUNDED-ISO-BMFF-AVC-NAL-UNIT-CLASSIFICATION.md`
- `docs/reviews/PCN-0253-BOUNDED-ISO-BMFF-AVC-PARAMETER-SET-PREFIX-PLAN.md`
- `docs/reviews/PCN-0254-BOUNDED-ISO-BMFF-AVC-LENGTH-PREFIXED-PAYLOAD-MATERIALIZATION.md`
- `docs/reviews/PCN-0255-BOUNDED-ISO-BMFF-AVC-ANNEX-B-PAYLOAD-MATERIALIZATION.md`
- `docs/reviews/PCN-0256-BOUNDED-ISO-BMFF-AVC-SAMPLE-ONLY-ANNEX-B-MATERIALIZATION.md`
- `docs/reviews/PCN-0257-EXPLICIT-ISO-BMFF-AVC-PAYLOAD-SELECTION.md`
- `docs/reviews/PCN-0258-EXPLICIT-ISO-BMFF-AVC-DECODER-PAYLOAD-REQUIREMENTS.md`
- `docs/reviews/PCN-0259-BOUNDED-ISO-BMFF-AVC-PAYLOAD-COMPOSITION.md`
- `docs/reviews/PCN-0260-BOUNDED-ISO-BMFF-GA-SPECIFIC-CONFIG-PREFIX.md`
- `docs/reviews/PCN-0261-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-ELEMENT-HEADER.md`
- `docs/reviews/PCN-0262-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-ELEMENT.md`
- `docs/reviews/PCN-0263-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-CHANNEL-TOPOLOGY.md`
- `docs/reviews/PCN-0264-BOUNDED-ISO-BMFF-PCE-AWARE-GA-SPECIFIC-CONFIG-COMPOSITION.md`
- `docs/reviews/PCN-0265-BOUNDED-ISO-BMFF-ER-AUDIO-POST-CONFIG.md`
- `docs/reviews/PCN-0266-BOUNDED-ISO-BMFF-ER-IMPLICIT-SBR-PS-SYNC-EXTENSION.md`
- `docs/reviews/PCN-0267-BOUNDED-ISO-BMFF-AAC-CORE-FORMAT-FACTS.md`
- `docs/reviews/PCN-0268-BOUNDED-ISO-BMFF-AAC-EXTENSION-SIGNALLING-FACTS.md`
- `docs/reviews/PCN-0269-BOUNDED-ISO-BMFF-AAC-DECODER-FORMAT-INTENT.md`
- `docs/reviews/PCN-0270-BOUNDED-ISO-BMFF-AAC-DECODER-EXTRADATA.md`
- `docs/reviews/PCN-0271-BOUNDED-ISO-BMFF-AAC-FFMPEG-CODEC-PARAMETERS-INPUT.md`
- `docs/reviews/PCN-0272-BOUNDED-ISO-BMFF-AAC-MANAGED-CODEC-PARAMETERS.md`
- `docs/reviews/PCN-0273-BOUNDED-ISO-BMFF-AAC-PARAMETER-TRANSFER-PLAN.md`
- `docs/reviews/PCN-0274-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-CHANNEL-SLOT-MAP.md`
- `docs/reviews/PCN-0275-BOUNDED-ISO-BMFF-AAC-TRANSFER-SYMBOL-COVERAGE.md`
- `docs/reviews/PCN-0276-BOUNDED-ISO-BMFF-AAC-DECLARED-DECODER-CAPABILITY-BINDING.md`
- `docs/reviews/PCN-0277-BOUNDED-ISO-BMFF-ER-AUDIO-PROTECTION-REQUIREMENT.md`
- `docs/reviews/PCN-0278-BOUNDED-ISO-BMFF-AAC-MANAGED-DECODER-PREPARATION.md`
- `docs/reviews/PCN-0279-BOUNDED-ISO-BMFF-AAC-SAMPLE-ENTRY-CONSISTENCY-FACTS.md`
- `docs/reviews/PCN-0280-EXPLICIT-ISO-BMFF-AAC-SAMPLE-ENTRY-CONSISTENCY-POLICY.md`
- `docs/reviews/PCN-0281-BOUNDED-ISO-BMFF-AAC-SAMPLE-ENTRY-MANAGED-DECODER-PREPARATION.md`
- `docs/reviews/PCN-0282-EXPLICIT-ISO-BMFF-PROGRAM-CONFIG-SPEAKER-POSITION-ASSIGNMENT.md`
- `docs/reviews/PCN-0283-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-SPECIFIC-CONFIG-PREFIX.md`
- `docs/reviews/PCN-0284-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-FIRST-SET-HEADER.md`
- `docs/reviews/PCN-0285-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-FIRST-CLASS.md`
- `docs/reviews/PCN-0286-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-FIRST-SET.md`
- `docs/reviews/PCN-0287-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-SECOND-SET-HEADER.md`
- `docs/reviews/PCN-0288-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-SECOND-SET-FIRST-CLASS.md`
- `docs/reviews/PCN-0289-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-SECOND-SET.md`
- `docs/reviews/PCN-0290-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-THIRD-SET-HEADER.md`
- `docs/reviews/PCN-0291-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-THIRD-SET-FIRST-CLASS.md`
- `docs/reviews/PCN-0292-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-THIRD-SET.md`
- `docs/reviews/PCN-0293-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-PREDEFINED-SETS.md`
- `docs/reviews/PCN-0294-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-HEADER-PROTECTION.md`
- `docs/reviews/PCN-0295-BOUNDED-ISO-BMFF-ER-ERROR-PROTECTION-SPECIFIC-CONFIG.md`
- `docs/reviews/PCN-0296-BOUNDED-ISO-BMFF-ER-DIRECT-MAPPING.md`
- `docs/reviews/PCN-0297-EXPLICIT-ISO-BMFF-ER-DIRECT-MAPPING-ADMISSION.md`
- `docs/reviews/PCN-0298-BOUNDED-ISO-BMFF-ER-CONFIGURATION-ADMISSION.md`
- `docs/reviews/PCN-0299-BOUNDED-ISO-BMFF-ER-AAC-MANAGED-DECODER-PREPARATION.md`
- `docs/reviews/PCN-0300-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-WAVEFORMAT-CHANNEL-LAYOUT.md`
- `docs/reviews/PCN-0301-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-CHANNEL-REORDER-PLAN.md`
- `docs/reviews/PCN-0302-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-AAC-MANAGED-DECODER-PREPARATION.md`
- `docs/reviews/PCN-0303-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-INTERLEAVED-PCM-REORDER.md`
- `docs/reviews/PCN-0304-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-AAC-CHANNEL-LAYOUT-REQUIREMENT.md`
- `docs/reviews/PCN-0305-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-AAC-CHANNEL-LAYOUT-SYMBOL-COVERAGE.md`
- `docs/reviews/PCN-0306-BOUNDED-ISO-BMFF-PROGRAM-CONFIG-AAC-EXPLICIT-LAYOUT-PREPARATION.md`
- `docs/reviews/PCN-0307-BOUNDED-ISO-BMFF-ADMITTED-ER-IMPLICIT-SBR-PS-SYNC-EXTENSION.md`
- `docs/reviews/PCN-0308-BOUNDED-ISO-BMFF-ADMITTED-ER-AAC-DECODER-FORMAT-INTENT.md`
- `docs/reviews/PCN-0309-BOUNDED-ISO-BMFF-ADMITTED-ER-AAC-MANAGED-DECODER-PREPARATION.md`
- `docs/reviews/PCN-0310-BOUNDED-ISO-BMFF-CODEC-PACKET-PROJECTION.md`

### Completed physical validation archive

- `docs/reviews/PCN-0166-CONTROLLED-AUDIO-RENDER-AUTHORIZATION-STATE.md`
- `docs/reviews/PCN-0167-MANAGED-AUDIO-FAILURE-BOUNDARY.md`
- [Issue #129](https://github.com/Matsuda7144/PeerCast-Nova/issues/129)
- `docs/runbooks/0.0.10d-controlled-audio-render-readiness.md`
- `tools/Test-ControlledWindowsPhysicalAudioRenderEvidenceReadiness.ps1`
- `docs/runbooks/0.0.10d-controlled-audio-render-readiness-capture.md`
- `tools/Save-ControlledWindowsPhysicalAudioRenderEvidenceReadiness.ps1`
- `docs/runbooks/0.0.10d-controlled-audio-render-readiness-record-preflight.md`
- `docs/runbooks/0.0.10d-controlled-audio-render-authorization-entrypoint.md`
- `tools/Run-ControlledWindowsPhysicalAudioRenderEvidenceWithReadiness.ps1`
- `docs/runbooks/0.0.10d-controlled-audio-render-evidence.md`
