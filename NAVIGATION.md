# PeerCast Nova Navigation

## Post-0.0.10e bounded ISO BMFF / MP4 managed foundation

PeerCast Nova now has two bounded container-parser families: the existing pure
managed bounded FLV v1 parser and the new bounded ISO BMFF / MP4 parser
foundation. MP4 is therefore the second container format implemented alongside
FLV, not a replacement for the FLV production path.

ADR-0167 through ADR-0172 add the bounded non-physical playback and application
slices that precede the container work: decoded-duration limits, non-native
application information, sample-derived audio duration, the default audio-only
250 ms production cutoff, and the opened-decoder `avcodec_flush_buffers`
boundary.

ADR-0173 through ADR-0473 add the ISO BMFF / MP4 foundation from bounded box
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
The AAC path additionally covers bounded GA and Program Config Element parsing,
ER Error Protection Specific Config, exact channel topology and managed reorder
planning/materialization, effective format and extradata projection, declared
capability and symbol coverage, and managed decoder preparation ownership.
It also provides caller-bounded one-bit-step implicit SBR marker search after
exact direct-GA or admitted-ER configuration, with exact marker provenance and
continuation into the existing immediate PS parser, plus a factory-validated
caller-explicit choice between immediate-only and bounded-search resolution.

### Current managed packet-to-event chain

| ADR | Review | Managed boundary |
| --- | --- | --- |
| [ADR-0300](docs/adr/ADR-0300-bounded-iso-bmff-codec-packet-projection.md) | [PCN-0310](docs/reviews/PCN-0310-BOUNDED-ISO-BMFF-CODEC-PACKET-PROJECTION.md) | Exact codec-neutral current packet |
| [ADR-0301](docs/adr/ADR-0301-bounded-iso-bmff-track-codec-packet.md) | [PCN-0311](docs/reviews/PCN-0311-BOUNDED-ISO-BMFF-TRACK-CODEC-PACKET.md) | Exact track metadata binding |
| [ADR-0302](docs/adr/ADR-0302-bounded-iso-bmff-movie-track-codec-packet.md) | [PCN-0312](docs/reviews/PCN-0312-BOUNDED-ISO-BMFF-MOVIE-TRACK-CODEC-PACKET.md) | Exact movie timing binding |
| [ADR-0303](docs/adr/ADR-0303-bounded-iso-bmff-movie-codec-packet-index.md) | [PCN-0313](docs/reviews/PCN-0313-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-INDEX.md) | Bounded caller-ordered stream index |
| [ADR-0304](docs/adr/ADR-0304-explicit-iso-bmff-movie-codec-stream-selection.md) | [PCN-0314](docs/reviews/PCN-0314-EXPLICIT-ISO-BMFF-MOVIE-CODEC-STREAM-SELECTION.md) | Explicit video/audio selection |
| [ADR-0305](docs/adr/ADR-0305-bounded-iso-bmff-movie-codec-packet-event-projection.md) | [PCN-0315](docs/reviews/PCN-0315-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-PROJECTION.md) | Bounded packet event projection |
| [ADR-0306](docs/adr/ADR-0306-exact-iso-bmff-movie-codec-packet-event-order.md) | [PCN-0316](docs/reviews/PCN-0316-EXACT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-ORDER.md) | Exact rational cross-stream order |
| [ADR-0307](docs/adr/ADR-0307-exact-iso-bmff-movie-codec-packet-event-time.md) | [PCN-0317](docs/reviews/PCN-0317-EXACT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-TIME.md) | Exact rational seconds metadata |
| [ADR-0308](docs/adr/ADR-0308-bounded-iso-bmff-movie-codec-packet-event-queue-snapshot.md) | [PCN-0318](docs/reviews/PCN-0318-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-SNAPSHOT.md) | Bounded immutable queue snapshot |
| [ADR-0309](docs/adr/ADR-0309-bounded-iso-bmff-movie-codec-packet-event-queue-cursor.md) | [PCN-0319](docs/reviews/PCN-0319-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-CURSOR.md) | Bounded immutable queue cursor |
| [ADR-0310](docs/adr/ADR-0310-exact-iso-bmff-movie-codec-packet-event-queue-acknowledgement.md) | [PCN-0320](docs/reviews/PCN-0320-EXACT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-ACKNOWLEDGEMENT.md) | Exact one-step acknowledgement |
| [ADR-0311](docs/adr/ADR-0311-exact-iso-bmff-movie-codec-packet-event-queue-clock-time.md) | [PCN-0321](docs/reviews/PCN-0321-EXACT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-CLOCK-TIME.md) | Caller-rate exact rational clock time |
| [ADR-0312](docs/adr/ADR-0312-caller-explicit-iso-bmff-movie-codec-packet-event-queue-integral-clock-time.md) | [PCN-0322](docs/reviews/PCN-0322-CALLER-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-INTEGRAL-CLOCK-TIME.md) | Caller-explicit integral clock time |
| [ADR-0313](docs/adr/ADR-0313-caller-explicit-iso-bmff-movie-codec-packet-event-queue-clock-origin.md) | [PCN-0323](docs/reviews/PCN-0323-CALLER-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-CLOCK-ORIGIN.md) | Caller-explicit signed clock origin |
| [ADR-0314](docs/adr/ADR-0314-bounded-iso-bmff-movie-codec-packet-event-queue-deadline.md) | [PCN-0324](docs/reviews/PCN-0324-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DEADLINE.md) | Bounded ready/expiration deadlines |
| [ADR-0315](docs/adr/ADR-0315-caller-explicit-iso-bmff-movie-codec-packet-event-queue-deadline-observation.md) | [PCN-0325](docs/reviews/PCN-0325-CALLER-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DEADLINE-OBSERVATION.md) | Caller-explicit deadline observation |
| [ADR-0316](docs/adr/ADR-0316-caller-explicit-iso-bmff-movie-codec-packet-event-queue-dispatch-eligibility.md) | [PCN-0326](docs/reviews/PCN-0326-CALLER-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-ELIGIBILITY.md) | Caller-explicit dispatch eligibility |
| [ADR-0317](docs/adr/ADR-0317-bounded-iso-bmff-movie-codec-packet-event-queue-dispatch-candidate-selection.md) | [PCN-0327](docs/reviews/PCN-0327-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-CANDIDATE-SELECTION.md) | Bounded candidate selection |
| [ADR-0318](docs/adr/ADR-0318-explicit-iso-bmff-movie-codec-packet-event-queue-dispatch-target-set.md) | [PCN-0328](docs/reviews/PCN-0328-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-TARGET-SET.md) | Explicit opaque target identities |
| [ADR-0319](docs/adr/ADR-0319-exact-iso-bmff-movie-codec-packet-event-queue-dispatch-target-binding.md) | [PCN-0329](docs/reviews/PCN-0329-EXACT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-TARGET-BINDING.md) | Exact media target binding |
| [ADR-0320](docs/adr/ADR-0320-bounded-iso-bmff-movie-codec-packet-event-queue-dispatch-request-projection.md) | [PCN-0330](docs/reviews/PCN-0330-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-REQUEST-PROJECTION.md) | Bounded dispatch request metadata |
| [ADR-0321](docs/adr/ADR-0321-explicit-iso-bmff-movie-codec-packet-event-queue-dispatch-target-capability-set.md) | [PCN-0331](docs/reviews/PCN-0331-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-TARGET-CAPABILITY-SET.md) | Explicit target capabilities |
| [ADR-0322](docs/adr/ADR-0322-bounded-iso-bmff-movie-codec-packet-event-queue-dispatch-target-resolution.md) | [PCN-0332](docs/reviews/PCN-0332-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-TARGET-RESOLUTION.md) | Bounded capability resolution |
| [ADR-0323](docs/adr/ADR-0323-explicit-iso-bmff-movie-codec-packet-event-queue-dispatch-runtime-target-registry.md) | [PCN-0333](docs/reviews/PCN-0333-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-RUNTIME-TARGET-REGISTRY.md) | Opaque runtime target registry |
| [ADR-0324](docs/adr/ADR-0324-bounded-iso-bmff-movie-codec-packet-event-queue-dispatch-runtime-target-lookup.md) | [PCN-0334](docs/reviews/PCN-0334-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-RUNTIME-TARGET-LOOKUP.md) | Bounded runtime identity lookup |
| [ADR-0325](docs/adr/ADR-0325-bounded-iso-bmff-movie-codec-packet-event-queue-dispatch-invocation-intent.md) | [PCN-0335](docs/reviews/PCN-0335-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-INVOCATION-INTENT.md) | Bounded invocation intent |
| [ADR-0326](docs/adr/ADR-0326-caller-explicit-iso-bmff-movie-codec-packet-event-queue-dispatch-invocation-admission.md) | [PCN-0336](docs/reviews/PCN-0336-CALLER-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-INVOCATION-ADMISSION.md) | Caller-explicit invocation admission |
| [ADR-0327](docs/adr/ADR-0327-bounded-iso-bmff-movie-codec-packet-event-queue-dispatch-admitted-invocation-selection.md) | [PCN-0337](docs/reviews/PCN-0337-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-ADMITTED-INVOCATION-SELECTION.md) | Bounded admitted invocation selection |

This chain represents one current packet per selected track. Non-immediate AAC
sync-extension search, default timing policies, clock acquisition, repeated observation, runtime instance access and invocation, actual dispatch, cursor mutation, delivery outcome, scheduling,
multi-sample traversal, verified runtime capability discovery, native decoder
integration, production MP4 demux composition, nested-box and streaming policy,
and fragmented MP4 remain future work.

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
- `docs/adr/ADR-0301-bounded-iso-bmff-track-codec-packet.md`
- `docs/adr/ADR-0302-bounded-iso-bmff-movie-track-codec-packet.md`
- `docs/adr/ADR-0303-bounded-iso-bmff-movie-codec-packet-index.md`
- `docs/adr/ADR-0304-explicit-iso-bmff-movie-codec-stream-selection.md`
- `docs/adr/ADR-0305-bounded-iso-bmff-movie-codec-packet-event-projection.md`
- `docs/adr/ADR-0306-exact-iso-bmff-movie-codec-packet-event-order.md`
- `docs/adr/ADR-0307-exact-iso-bmff-movie-codec-packet-event-time.md`
- `docs/adr/ADR-0308-bounded-iso-bmff-movie-codec-packet-event-queue-snapshot.md`
- `docs/adr/ADR-0309-bounded-iso-bmff-movie-codec-packet-event-queue-cursor.md`
- `docs/adr/ADR-0310-exact-iso-bmff-movie-codec-packet-event-queue-acknowledgement.md`
- `docs/adr/ADR-0311-exact-iso-bmff-movie-codec-packet-event-queue-clock-time.md`
- `docs/adr/ADR-0312-caller-explicit-iso-bmff-movie-codec-packet-event-queue-integral-clock-time.md`
- `docs/adr/ADR-0313-caller-explicit-iso-bmff-movie-codec-packet-event-queue-clock-origin.md`
- `docs/adr/ADR-0314-bounded-iso-bmff-movie-codec-packet-event-queue-deadline.md`
- `docs/adr/ADR-0315-caller-explicit-iso-bmff-movie-codec-packet-event-queue-deadline-observation.md`
- `docs/adr/ADR-0316-caller-explicit-iso-bmff-movie-codec-packet-event-queue-dispatch-eligibility.md`
- `docs/adr/ADR-0317-bounded-iso-bmff-movie-codec-packet-event-queue-dispatch-candidate-selection.md`
- `docs/adr/ADR-0318-explicit-iso-bmff-movie-codec-packet-event-queue-dispatch-target-set.md`
- `docs/adr/ADR-0319-exact-iso-bmff-movie-codec-packet-event-queue-dispatch-target-binding.md`
- `docs/adr/ADR-0320-bounded-iso-bmff-movie-codec-packet-event-queue-dispatch-request-projection.md`
- `docs/adr/ADR-0321-explicit-iso-bmff-movie-codec-packet-event-queue-dispatch-target-capability-set.md`
- `docs/adr/ADR-0322-bounded-iso-bmff-movie-codec-packet-event-queue-dispatch-target-resolution.md`
- `docs/adr/ADR-0323-explicit-iso-bmff-movie-codec-packet-event-queue-dispatch-runtime-target-registry.md`
- `docs/adr/ADR-0324-bounded-iso-bmff-movie-codec-packet-event-queue-dispatch-runtime-target-lookup.md`
- `docs/adr/ADR-0325-bounded-iso-bmff-movie-codec-packet-event-queue-dispatch-invocation-intent.md`
- `docs/adr/ADR-0326-caller-explicit-iso-bmff-movie-codec-packet-event-queue-dispatch-invocation-admission.md`
- `docs/adr/ADR-0327-bounded-iso-bmff-movie-codec-packet-event-queue-dispatch-admitted-invocation-selection.md`
- `docs/adr/ADR-0328-caller-bounded-iso-bmff-implicit-sbr-sync-extension-search.md`
- `docs/adr/ADR-0329-caller-explicit-iso-bmff-implicit-sbr-sync-extension-resolution-policy.md`
- `docs/adr/ADR-0330-bounded-iso-bmff-track-codec-packet-sequence.md`
- `docs/adr/ADR-0331-bounded-iso-bmff-track-codec-packet-sequence-cursor.md`
- `docs/adr/ADR-0332-exact-iso-bmff-track-codec-packet-sequence-acknowledgement.md`
- `docs/adr/ADR-0333-bounded-iso-bmff-movie-track-codec-packet-sequence-set.md`
- `docs/adr/ADR-0334-explicit-iso-bmff-movie-track-codec-packet-sequence-selection.md`
- `docs/adr/ADR-0335-bounded-iso-bmff-selected-track-codec-packet-sequence-cursor-set.md`
- `docs/adr/ADR-0336-exact-iso-bmff-selected-sequence-packet-head-snapshot.md`
- `docs/adr/ADR-0337-exact-iso-bmff-selected-sequence-head-movie-time-comparison.md`
- `docs/adr/ADR-0338-caller-explicit-iso-bmff-sequence-advance-role-selection.md`
- `docs/adr/ADR-0339-exact-iso-bmff-selected-sequence-cursor-set-advance.md`
- `docs/adr/ADR-0340-exact-iso-bmff-cross-track-sequence-merge-step.md`
- `docs/adr/ADR-0341-bounded-iso-bmff-cross-track-merge-step-sequence.md`
- `docs/adr/ADR-0342-caller-bounded-iso-bmff-cross-track-merge-traversal.md`
- `docs/adr/ADR-0343-exact-iso-bmff-merge-traversal-continuation.md`
- `docs/adr/ADR-0344-exact-iso-bmff-packet-merge-completion.md`
- `docs/adr/ADR-0345-exact-iso-bmff-completed-merge-step-view.md`
- `docs/adr/ADR-0346-bounded-iso-bmff-completed-merge-event-projection.md`
- `docs/adr/ADR-0347-bounded-iso-bmff-completed-merge-event-cursor.md`
- `docs/adr/ADR-0348-exact-iso-bmff-completed-merge-event-acknowledgement.md`
- `docs/adr/ADR-0349-caller-bounded-iso-bmff-completed-merge-event-traversal.md`
- `docs/adr/ADR-0350-exact-iso-bmff-completed-merge-event-traversal-continuation.md`
- `docs/adr/ADR-0351-exact-iso-bmff-completed-merge-event-traversal-completion.md`
- `docs/adr/ADR-0352-exact-iso-bmff-completed-merge-event-traversal-acknowledgement-view.md`
- `docs/adr/ADR-0353-exact-iso-bmff-completed-merge-event-traversal-time.md`
- `docs/adr/ADR-0354-caller-explicit-iso-bmff-completed-merge-event-traversal-clock-time.md`
- `docs/adr/ADR-0355-caller-explicit-iso-bmff-completed-merge-event-traversal-integral-clock-time.md`
- `docs/adr/ADR-0356-caller-explicit-iso-bmff-completed-merge-event-traversal-clock-origin.md`
- `docs/adr/ADR-0357-bounded-iso-bmff-completed-merge-event-traversal-deadline.md`
- `docs/adr/ADR-0358-caller-explicit-iso-bmff-completed-merge-event-traversal-deadline-observation.md`
- `docs/adr/ADR-0359-caller-explicit-iso-bmff-completed-merge-event-traversal-dispatch-eligibility.md`
- `docs/adr/ADR-0360-bounded-iso-bmff-completed-merge-event-traversal-dispatch-candidate-selection.md`
- `docs/adr/ADR-0361-explicit-iso-bmff-completed-merge-event-traversal-dispatch-target-set.md`
- `docs/adr/ADR-0362-exact-iso-bmff-completed-merge-event-traversal-dispatch-target-binding.md`
- `docs/adr/ADR-0363-bounded-iso-bmff-completed-merge-event-traversal-dispatch-request.md`
- `docs/adr/ADR-0364-explicit-iso-bmff-completed-merge-event-traversal-dispatch-target-capability-set.md`
- `docs/adr/ADR-0365-exact-iso-bmff-completed-merge-event-traversal-dispatch-target-resolution.md`
- `docs/adr/ADR-0366-explicit-iso-bmff-completed-merge-event-traversal-dispatch-runtime-target-registry.md`
- `docs/adr/ADR-0367-exact-iso-bmff-completed-merge-event-traversal-dispatch-runtime-target-lookup.md`
- `docs/adr/ADR-0368-caller-identified-iso-bmff-completed-merge-event-traversal-dispatch-invocation-intent.md`
- `docs/adr/ADR-0369-caller-explicit-iso-bmff-completed-merge-event-traversal-dispatch-invocation-admission.md`
- `docs/adr/ADR-0370-bounded-iso-bmff-completed-merge-event-traversal-dispatch-admitted-invocation-selection.md`
- `docs/adr/ADR-0371-caller-bounded-iso-bmff-track-sample-sequence-plan.md`
- `docs/adr/ADR-0372-exact-iso-bmff-track-sample-edit-admission-projection.md`
- `docs/adr/ADR-0373-bounded-iso-bmff-track-admitted-sample-selection.md`
- `docs/adr/ADR-0374-exact-iso-bmff-track-admitted-sample-byte-range-projection.md`
- `docs/adr/ADR-0375-exact-iso-bmff-track-admitted-sample-media-data-projection.md`
- `docs/adr/ADR-0376-bounded-iso-bmff-track-owned-sample-payload-projection.md`
- `docs/adr/ADR-0377-exact-iso-bmff-track-edited-sample-packet-candidate-projection.md`
- `docs/adr/ADR-0378-exact-iso-bmff-track-sample-description-selection-projection.md`
- `docs/adr/ADR-0379-exact-iso-bmff-track-sample-entry-parse-projection.md`
- `docs/adr/ADR-0380-exact-iso-bmff-track-codec-configuration-projection.md`
- `docs/adr/ADR-0381-bounded-iso-bmff-track-codec-packet-composition-projection.md`
- `docs/adr/ADR-0382-exact-iso-bmff-track-codec-packet-binding-projection.md`
- `docs/adr/ADR-0383-exact-iso-bmff-movie-track-codec-packet-binding-projection.md`
- `docs/adr/ADR-0384-exact-iso-bmff-movie-track-codec-packet-sequence-projection.md`
- `docs/adr/ADR-0385-exact-iso-bmff-movie-track-codec-packet-sequence-set-projection.md`
- `docs/adr/ADR-0386-caller-explicit-iso-bmff-movie-track-codec-packet-sequence-selection-projection.md`
- `docs/adr/ADR-0387-exact-iso-bmff-selected-sequence-initial-cursor-set-projection.md`
- `docs/adr/ADR-0388-caller-bounded-iso-bmff-projected-sequence-merge-traversal.md`
- `docs/adr/ADR-0389-exact-iso-bmff-projected-sequence-merge-continuation.md`
- `docs/adr/ADR-0390-exact-iso-bmff-projected-sequence-merge-completion.md`
- `docs/adr/ADR-0391-exact-iso-bmff-projected-completed-merge-step-view.md`
- `docs/adr/ADR-0392-bounded-iso-bmff-projected-completed-merge-events.md`
- `docs/adr/ADR-0393-exact-iso-bmff-projected-completed-event-initial-cursor.md`
- `docs/adr/ADR-0394-caller-bounded-iso-bmff-projected-completed-event-traversal.md`
- `docs/adr/ADR-0395-exact-iso-bmff-projected-completed-event-traversal-continuation.md`
- `docs/adr/ADR-0396-exact-iso-bmff-projected-completed-event-traversal-completion.md`
- `docs/adr/ADR-0397-exact-iso-bmff-projected-completed-event-acknowledgement-view.md`
- `docs/adr/ADR-0398-exact-iso-bmff-projected-completed-event-traversal-time.md`
- `docs/adr/ADR-0399-caller-explicit-iso-bmff-projected-completed-event-traversal-clock-time.md`
- `docs/adr/ADR-0400-caller-explicit-iso-bmff-projected-completed-event-traversal-integral-clock-time.md`
- `docs/adr/ADR-0401-caller-explicit-iso-bmff-projected-completed-event-traversal-clock-origin.md`
- `docs/adr/ADR-0402-bounded-iso-bmff-projected-completed-event-traversal-deadline.md`
- `docs/adr/ADR-0403-caller-explicit-iso-bmff-projected-completed-event-traversal-deadline-observation.md`
- `docs/adr/ADR-0404-caller-explicit-iso-bmff-projected-completed-event-traversal-dispatch-eligibility.md`
- `docs/adr/ADR-0405-caller-bounded-iso-bmff-projected-completed-event-traversal-dispatch-candidate-selection.md`
- `docs/adr/ADR-0406-explicit-iso-bmff-projected-completed-event-traversal-dispatch-target-set.md`
- `docs/adr/ADR-0407-exact-iso-bmff-projected-completed-event-traversal-dispatch-target-binding.md`
- `docs/adr/ADR-0408-bounded-iso-bmff-projected-completed-event-traversal-dispatch-request.md`
- `docs/adr/ADR-0409-explicit-iso-bmff-projected-completed-event-traversal-dispatch-target-capability-set.md`
- `docs/adr/ADR-0410-exact-iso-bmff-projected-completed-event-traversal-dispatch-target-resolution.md`
- `docs/adr/ADR-0411-explicit-iso-bmff-projected-completed-event-traversal-dispatch-runtime-target-registry.md`
- `docs/adr/ADR-0412-exact-iso-bmff-projected-completed-event-traversal-dispatch-runtime-target-lookup.md`
- `docs/adr/ADR-0413-caller-identified-iso-bmff-projected-completed-event-traversal-dispatch-invocation-intent.md`
- `docs/adr/ADR-0414-caller-explicit-iso-bmff-projected-completed-event-traversal-dispatch-invocation-admission.md`
- `docs/adr/ADR-0415-caller-bounded-iso-bmff-projected-completed-event-traversal-dispatch-admitted-invocation-selection.md`
- `docs/adr/ADR-0416-bounded-iso-bmff-track-extends-box.md`
- `docs/adr/ADR-0417-bounded-iso-bmff-movie-fragment-header.md`
- `docs/adr/ADR-0418-bounded-iso-bmff-track-fragment-header.md`
- `docs/adr/ADR-0419-bounded-iso-bmff-track-fragment-decode-time.md`
- `docs/adr/ADR-0420-bounded-iso-bmff-track-run.md`
- `docs/adr/ADR-0421-bounded-iso-bmff-fragment-sample-default-resolution.md`
- `docs/adr/ADR-0422-bounded-iso-bmff-fragment-sample-timing-projection.md`
- `docs/adr/ADR-0423-bounded-iso-bmff-fragment-run-data-origin-resolution.md`
- `docs/adr/ADR-0424-bounded-iso-bmff-fragment-sample-byte-range-projection.md`
- `docs/adr/ADR-0425-bounded-iso-bmff-fragment-sample-metadata-projection.md`
- `docs/adr/ADR-0426-bounded-iso-bmff-fragment-sample-rational-time-projection.md`
- `docs/adr/ADR-0427-bounded-iso-bmff-fragment-sample-presentation-order.md`
- `docs/adr/ADR-0428-bounded-iso-bmff-movie-fragment-sequence-continuity.md`
- `docs/adr/ADR-0429-bounded-iso-bmff-track-fragment-decode-continuity.md`
- `docs/adr/ADR-0430-bounded-iso-bmff-fragment-continuity-binding.md`
- `docs/adr/ADR-0431-bounded-iso-bmff-fragment-continuity-chain.md`
- `docs/adr/ADR-0432-bounded-iso-bmff-fragment-continuity-summary.md`
- `docs/adr/ADR-0433-bounded-iso-bmff-fragment-sample-batch.md`
- `docs/adr/ADR-0434-bounded-iso-bmff-fragment-sample-batch-chain.md`
- `docs/adr/ADR-0435-bounded-iso-bmff-fragment-sample-batch-presentation-merge.md`
- `docs/adr/ADR-0436-bounded-iso-bmff-fragment-sample-merge-cursor.md`
- `docs/adr/ADR-0437-exact-iso-bmff-fragment-sample-merge-cursor-step.md`
- `docs/adr/ADR-0438-bounded-iso-bmff-fragment-sample-merge-traversal.md`
- `docs/adr/ADR-0439-exact-iso-bmff-fragment-sample-merge-traversal-completion.md`
- `docs/adr/ADR-0440-bounded-iso-bmff-fragment-sample-merge-traversal-acknowledgement-view.md`
- `docs/adr/ADR-0441-exact-iso-bmff-fragment-sample-merge-traversal-acknowledgement-view-source-projection.md`
- `docs/adr/ADR-0442-exact-iso-bmff-fragment-sample-merge-traversal-time-projection.md`
- `docs/adr/ADR-0443-exact-iso-bmff-fragment-sample-merge-traversal-time-source-projection.md`
- `docs/adr/ADR-0444-caller-explicit-iso-bmff-fragment-sample-merge-traversal-integral-time-projection.md`
- `docs/adr/ADR-0445-exact-iso-bmff-fragment-sample-merge-traversal-integral-time-source-projection.md`
- `docs/adr/ADR-0446-caller-explicit-iso-bmff-fragment-sample-merge-traversal-clock-origin-projection.md`
- `docs/adr/ADR-0447-exact-iso-bmff-fragment-sample-merge-traversal-clock-origin-source-projection.md`
- `docs/adr/ADR-0448-bounded-iso-bmff-fragment-sample-merge-traversal-deadline-projection.md`
- `docs/adr/ADR-0449-exact-iso-bmff-fragment-sample-merge-traversal-deadline-source-projection.md`
- `docs/adr/ADR-0450-caller-explicit-iso-bmff-fragment-sample-merge-traversal-deadline-observation.md`
- `docs/adr/ADR-0451-exact-iso-bmff-fragment-sample-merge-traversal-deadline-observation-source-projection.md`
- `docs/adr/ADR-0452-caller-explicit-iso-bmff-fragment-sample-merge-traversal-deadline-eligibility.md`
- `docs/adr/ADR-0453-exact-iso-bmff-fragment-sample-merge-traversal-deadline-eligibility-source-projection.md`
- `docs/adr/ADR-0454-caller-bounded-iso-bmff-fragment-sample-merge-traversal-eligible-selection.md`
- `docs/adr/ADR-0455-exact-iso-bmff-fragment-sample-merge-traversal-eligible-selection-source-projection.md`
- `docs/adr/ADR-0456-exact-iso-bmff-fragment-sample-merge-traversal-request-projection.md`
- `docs/adr/ADR-0457-exact-iso-bmff-fragment-sample-merge-traversal-request-source-projection.md`
- `docs/adr/ADR-0458-caller-explicit-iso-bmff-fragment-sample-merge-traversal-consumer-identity.md`
- `docs/adr/ADR-0459-exact-iso-bmff-fragment-sample-merge-traversal-consumer-binding.md`
- `docs/adr/ADR-0460-caller-explicit-iso-bmff-fragment-sample-merge-traversal-consumer-batch-identity.md`
- `docs/adr/ADR-0461-bounded-iso-bmff-fragment-sample-merge-traversal-consumer-batch-projection.md`
- `docs/adr/ADR-0462-bounded-iso-bmff-fragment-sample-merge-traversal-submission-intent-projection.md`
- `docs/adr/ADR-0463-caller-explicit-iso-bmff-fragment-sample-merge-traversal-submission-classification.md`
- `docs/adr/ADR-0464-caller-bounded-iso-bmff-fragment-sample-merge-traversal-submission-selection.md`
- `docs/adr/ADR-0465-exact-iso-bmff-fragment-sample-merge-traversal-submission-manifest-projection.md`
- `docs/adr/ADR-0466-exact-iso-bmff-fragment-sample-merge-traversal-submission-manifest-source-projection.md`
- `docs/adr/ADR-0467-declarative-iso-bmff-fragment-sample-merge-traversal-submission-manifest-summary.md`
- `docs/adr/ADR-0468-bounded-iso-bmff-fragment-sample-merge-traversal-submission-manifest-segments.md`
- `docs/adr/ADR-0469-bounded-iso-bmff-fragment-sample-merge-traversal-submission-manifest-segment-cursor.md`
- `docs/adr/ADR-0470-exact-iso-bmff-fragment-sample-merge-traversal-submission-manifest-segment-cursor-step.md`
- `docs/adr/ADR-0471-bounded-iso-bmff-fragment-sample-merge-traversal-submission-manifest-segment-traversal.md`
- `docs/adr/ADR-0472-exact-iso-bmff-fragment-sample-merge-traversal-submission-manifest-segment-completion.md`
- `docs/adr/ADR-0473-exact-iso-bmff-fragment-sample-merge-traversal-submission-manifest-completed-segment-view.md`
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
- `docs/reviews/PCN-0311-BOUNDED-ISO-BMFF-TRACK-CODEC-PACKET.md`
- `docs/reviews/PCN-0312-BOUNDED-ISO-BMFF-MOVIE-TRACK-CODEC-PACKET.md`
- `docs/reviews/PCN-0313-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-INDEX.md`
- `docs/reviews/PCN-0314-EXPLICIT-ISO-BMFF-MOVIE-CODEC-STREAM-SELECTION.md`
- `docs/reviews/PCN-0315-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-PROJECTION.md`
- `docs/reviews/PCN-0316-EXACT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-ORDER.md`
- `docs/reviews/PCN-0317-EXACT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-TIME.md`
- `docs/reviews/PCN-0318-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-SNAPSHOT.md`
- `docs/reviews/PCN-0319-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-CURSOR.md`
- `docs/reviews/PCN-0320-EXACT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-ACKNOWLEDGEMENT.md`
- `docs/reviews/PCN-0321-EXACT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-CLOCK-TIME.md`
- `docs/reviews/PCN-0322-CALLER-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-INTEGRAL-CLOCK-TIME.md`
- `docs/reviews/PCN-0323-CALLER-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-CLOCK-ORIGIN.md`
- `docs/reviews/PCN-0324-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DEADLINE.md`
- `docs/reviews/PCN-0325-CALLER-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DEADLINE-OBSERVATION.md`
- `docs/reviews/PCN-0326-CALLER-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-ELIGIBILITY.md`
- `docs/reviews/PCN-0327-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-CANDIDATE-SELECTION.md`
- `docs/reviews/PCN-0328-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-TARGET-SET.md`
- `docs/reviews/PCN-0329-EXACT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-TARGET-BINDING.md`
- `docs/reviews/PCN-0330-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-REQUEST-PROJECTION.md`
- `docs/reviews/PCN-0331-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-TARGET-CAPABILITY-SET.md`
- `docs/reviews/PCN-0332-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-TARGET-RESOLUTION.md`
- `docs/reviews/PCN-0333-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-RUNTIME-TARGET-REGISTRY.md`
- `docs/reviews/PCN-0334-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-RUNTIME-TARGET-LOOKUP.md`
- `docs/reviews/PCN-0335-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-INVOCATION-INTENT.md`
- `docs/reviews/PCN-0336-CALLER-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-INVOCATION-ADMISSION.md`
- `docs/reviews/PCN-0337-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-ADMITTED-INVOCATION-SELECTION.md`
- `docs/reviews/PCN-0338-CALLER-BOUNDED-ISO-BMFF-IMPLICIT-SBR-SYNC-EXTENSION-SEARCH.md`
- `docs/reviews/PCN-0339-CALLER-EXPLICIT-ISO-BMFF-IMPLICIT-SBR-SYNC-EXTENSION-RESOLUTION-POLICY.md`
- `docs/reviews/PCN-0340-BOUNDED-ISO-BMFF-TRACK-CODEC-PACKET-SEQUENCE.md`
- `docs/reviews/PCN-0341-BOUNDED-ISO-BMFF-TRACK-CODEC-PACKET-SEQUENCE-CURSOR.md`
- `docs/reviews/PCN-0342-EXACT-ISO-BMFF-TRACK-CODEC-PACKET-SEQUENCE-ACKNOWLEDGEMENT.md`
- `docs/reviews/PCN-0343-BOUNDED-ISO-BMFF-MOVIE-TRACK-CODEC-PACKET-SEQUENCE-SET.md`
- `docs/reviews/PCN-0344-EXPLICIT-ISO-BMFF-MOVIE-TRACK-CODEC-PACKET-SEQUENCE-SELECTION.md`
- `docs/reviews/PCN-0345-BOUNDED-ISO-BMFF-SELECTED-TRACK-CODEC-PACKET-SEQUENCE-CURSOR-SET.md`
- `docs/reviews/PCN-0346-EXACT-ISO-BMFF-SELECTED-SEQUENCE-PACKET-HEAD-SNAPSHOT.md`
- `docs/reviews/PCN-0347-EXACT-ISO-BMFF-SELECTED-SEQUENCE-HEAD-MOVIE-TIME-COMPARISON.md`
- `docs/reviews/PCN-0348-CALLER-EXPLICIT-ISO-BMFF-SEQUENCE-ADVANCE-ROLE-SELECTION.md`
- `docs/reviews/PCN-0349-EXACT-ISO-BMFF-SELECTED-SEQUENCE-CURSOR-SET-ADVANCE.md`
- `docs/reviews/PCN-0350-EXACT-ISO-BMFF-CROSS-TRACK-SEQUENCE-MERGE-STEP.md`
- `docs/reviews/PCN-0351-BOUNDED-ISO-BMFF-CROSS-TRACK-MERGE-STEP-SEQUENCE.md`
- `docs/reviews/PCN-0352-CALLER-BOUNDED-ISO-BMFF-CROSS-TRACK-MERGE-TRAVERSAL.md`
- `docs/reviews/PCN-0353-EXACT-ISO-BMFF-MERGE-TRAVERSAL-CONTINUATION.md`
- `docs/reviews/PCN-0354-EXACT-ISO-BMFF-PACKET-MERGE-COMPLETION.md`
- `docs/reviews/PCN-0355-EXACT-ISO-BMFF-COMPLETED-MERGE-STEP-VIEW.md`
- `docs/reviews/PCN-0356-BOUNDED-ISO-BMFF-COMPLETED-MERGE-EVENT-PROJECTION.md`
- `docs/reviews/PCN-0357-BOUNDED-ISO-BMFF-COMPLETED-MERGE-EVENT-CURSOR.md`
- `docs/reviews/PCN-0358-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-ACKNOWLEDGEMENT.md`
- `docs/reviews/PCN-0359-CALLER-BOUNDED-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL.md`
- `docs/reviews/PCN-0360-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-CONTINUATION.md`
- `docs/reviews/PCN-0361-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-COMPLETION.md`
- `docs/reviews/PCN-0362-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-ACKNOWLEDGEMENT-VIEW.md`
- `docs/reviews/PCN-0363-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-TIME.md`
- `docs/reviews/PCN-0364-CALLER-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-CLOCK-TIME.md`
- `docs/reviews/PCN-0365-CALLER-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-INTEGRAL-CLOCK-TIME.md`
- `docs/reviews/PCN-0366-CALLER-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-CLOCK-ORIGIN.md`
- `docs/reviews/PCN-0367-BOUNDED-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DEADLINE.md`
- `docs/reviews/PCN-0368-CALLER-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DEADLINE-OBSERVATION.md`
- `docs/reviews/PCN-0369-CALLER-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-ELIGIBILITY.md`
- `docs/reviews/PCN-0370-BOUNDED-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-CANDIDATE-SELECTION.md`
- `docs/reviews/PCN-0371-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-TARGET-SET.md`
- `docs/reviews/PCN-0372-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-TARGET-BINDING.md`
- `docs/reviews/PCN-0373-BOUNDED-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-REQUEST.md`
- `docs/reviews/PCN-0374-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-TARGET-CAPABILITY-SET.md`
- `docs/reviews/PCN-0375-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-TARGET-RESOLUTION.md`
- `docs/reviews/PCN-0376-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-RUNTIME-TARGET-REGISTRY.md`
- `docs/reviews/PCN-0377-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-RUNTIME-TARGET-LOOKUP.md`
- `docs/reviews/PCN-0378-CALLER-IDENTIFIED-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-INVOCATION-INTENT.md`
- `docs/reviews/PCN-0379-CALLER-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-INVOCATION-ADMISSION.md`
- `docs/reviews/PCN-0380-BOUNDED-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-ADMITTED-INVOCATION-SELECTION.md`
- `docs/reviews/PCN-0381-CALLER-BOUNDED-ISO-BMFF-TRACK-SAMPLE-SEQUENCE-PLAN.md`
- `docs/reviews/PCN-0382-EXACT-ISO-BMFF-TRACK-SAMPLE-EDIT-ADMISSION-PROJECTION.md`
- `docs/reviews/PCN-0383-BOUNDED-ISO-BMFF-TRACK-ADMITTED-SAMPLE-SELECTION.md`
- `docs/reviews/PCN-0384-EXACT-ISO-BMFF-TRACK-ADMITTED-SAMPLE-BYTE-RANGE-PROJECTION.md`
- `docs/reviews/PCN-0385-EXACT-ISO-BMFF-TRACK-ADMITTED-SAMPLE-MEDIA-DATA-PROJECTION.md`
- `docs/reviews/PCN-0386-BOUNDED-ISO-BMFF-TRACK-OWNED-SAMPLE-PAYLOAD-PROJECTION.md`
- `docs/reviews/PCN-0387-EXACT-ISO-BMFF-TRACK-EDITED-SAMPLE-PACKET-CANDIDATE-PROJECTION.md`
- `docs/reviews/PCN-0388-EXACT-ISO-BMFF-TRACK-SAMPLE-DESCRIPTION-SELECTION-PROJECTION.md`
- `docs/reviews/PCN-0389-EXACT-ISO-BMFF-TRACK-SAMPLE-ENTRY-PARSE-PROJECTION.md`
- `docs/reviews/PCN-0390-EXACT-ISO-BMFF-TRACK-CODEC-CONFIGURATION-PROJECTION.md`
- `docs/reviews/PCN-0391-BOUNDED-ISO-BMFF-TRACK-CODEC-PACKET-COMPOSITION-PROJECTION.md`
- `docs/reviews/PCN-0392-EXACT-ISO-BMFF-TRACK-CODEC-PACKET-BINDING-PROJECTION.md`
- `docs/reviews/PCN-0393-EXACT-ISO-BMFF-MOVIE-TRACK-CODEC-PACKET-BINDING-PROJECTION.md`
- `docs/reviews/PCN-0394-EXACT-ISO-BMFF-MOVIE-TRACK-CODEC-PACKET-SEQUENCE-PROJECTION.md`
- `docs/reviews/PCN-0395-EXACT-ISO-BMFF-MOVIE-TRACK-CODEC-PACKET-SEQUENCE-SET-PROJECTION.md`
- `docs/reviews/PCN-0396-CALLER-EXPLICIT-ISO-BMFF-MOVIE-TRACK-CODEC-PACKET-SEQUENCE-SELECTION-PROJECTION.md`
- `docs/reviews/PCN-0397-EXACT-ISO-BMFF-SELECTED-SEQUENCE-INITIAL-CURSOR-SET-PROJECTION.md`
- `docs/reviews/PCN-0398-CALLER-BOUNDED-ISO-BMFF-PROJECTED-SEQUENCE-MERGE-TRAVERSAL.md`
- `docs/reviews/PCN-0399-EXACT-ISO-BMFF-PROJECTED-SEQUENCE-MERGE-CONTINUATION.md`
- `docs/reviews/PCN-0400-EXACT-ISO-BMFF-PROJECTED-SEQUENCE-MERGE-COMPLETION.md`
- `docs/reviews/PCN-0401-EXACT-ISO-BMFF-PROJECTED-COMPLETED-MERGE-STEP-VIEW.md`
- `docs/reviews/PCN-0402-BOUNDED-ISO-BMFF-PROJECTED-COMPLETED-MERGE-EVENTS.md`
- `docs/reviews/PCN-0403-EXACT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-INITIAL-CURSOR.md`
- `docs/reviews/PCN-0404-CALLER-BOUNDED-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL.md`
- `docs/reviews/PCN-0405-EXACT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-CONTINUATION.md`
- `docs/reviews/PCN-0406-EXACT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-COMPLETION.md`
- `docs/reviews/PCN-0407-EXACT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-ACKNOWLEDGEMENT-VIEW.md`
- `docs/reviews/PCN-0408-EXACT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-TIME.md`
- `docs/reviews/PCN-0409-CALLER-EXPLICIT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-CLOCK-TIME.md`
- `docs/reviews/PCN-0410-CALLER-EXPLICIT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-INTEGRAL-CLOCK-TIME.md`
- `docs/reviews/PCN-0411-CALLER-EXPLICIT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-CLOCK-ORIGIN.md`
- `docs/reviews/PCN-0412-BOUNDED-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DEADLINE.md`
- `docs/reviews/PCN-0413-CALLER-EXPLICIT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DEADLINE-OBSERVATION.md`
- `docs/reviews/PCN-0414-CALLER-EXPLICIT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-ELIGIBILITY.md`
- `docs/reviews/PCN-0415-CALLER-BOUNDED-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-CANDIDATE-SELECTION.md`
- `docs/reviews/PCN-0416-EXPLICIT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-TARGET-SET.md`
- `docs/reviews/PCN-0417-EXACT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-TARGET-BINDING.md`
- `docs/reviews/PCN-0418-BOUNDED-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-REQUEST.md`
- `docs/reviews/PCN-0419-EXPLICIT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-TARGET-CAPABILITY-SET.md`
- `docs/reviews/PCN-0420-EXACT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-TARGET-RESOLUTION.md`
- `docs/reviews/PCN-0421-EXPLICIT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-RUNTIME-TARGET-REGISTRY.md`
- `docs/reviews/PCN-0422-EXACT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-RUNTIME-TARGET-LOOKUP.md`
- `docs/reviews/PCN-0423-CALLER-IDENTIFIED-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-INVOCATION-INTENT.md`
- `docs/reviews/PCN-0424-CALLER-EXPLICIT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-INVOCATION-ADMISSION.md`
- `docs/reviews/PCN-0425-CALLER-BOUNDED-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-ADMITTED-INVOCATION-SELECTION.md`
- `docs/reviews/PCN-0426-BOUNDED-ISO-BMFF-TRACK-EXTENDS-BOX.md`
- `docs/reviews/PCN-0427-BOUNDED-ISO-BMFF-MOVIE-FRAGMENT-HEADER.md`
- `docs/reviews/PCN-0428-BOUNDED-ISO-BMFF-TRACK-FRAGMENT-HEADER.md`
- `docs/reviews/PCN-0429-BOUNDED-ISO-BMFF-TRACK-FRAGMENT-DECODE-TIME.md`
- `docs/reviews/PCN-0430-BOUNDED-ISO-BMFF-TRACK-RUN.md`
- `docs/reviews/PCN-0431-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-DEFAULT-RESOLUTION.md`
- `docs/reviews/PCN-0432-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-TIMING-PROJECTION.md`
- `docs/reviews/PCN-0433-BOUNDED-ISO-BMFF-FRAGMENT-RUN-DATA-ORIGIN-RESOLUTION.md`
- `docs/reviews/PCN-0434-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-BYTE-RANGE-PROJECTION.md`
- `docs/reviews/PCN-0435-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-METADATA-PROJECTION.md`
- `docs/reviews/PCN-0436-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-RATIONAL-TIME-PROJECTION.md`
- `docs/reviews/PCN-0437-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-PRESENTATION-ORDER.md`
- `docs/reviews/PCN-0438-BOUNDED-ISO-BMFF-MOVIE-FRAGMENT-SEQUENCE-CONTINUITY.md`
- `docs/reviews/PCN-0439-BOUNDED-ISO-BMFF-TRACK-FRAGMENT-DECODE-CONTINUITY.md`
- `docs/reviews/PCN-0440-BOUNDED-ISO-BMFF-FRAGMENT-CONTINUITY-BINDING.md`
- `docs/reviews/PCN-0441-BOUNDED-ISO-BMFF-FRAGMENT-CONTINUITY-CHAIN.md`
- `docs/reviews/PCN-0442-BOUNDED-ISO-BMFF-FRAGMENT-CONTINUITY-SUMMARY.md`
- `docs/reviews/PCN-0443-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-BATCH.md`
- `docs/reviews/PCN-0444-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-BATCH-CHAIN.md`
- `docs/reviews/PCN-0445-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-BATCH-PRESENTATION-MERGE.md`
- `docs/reviews/PCN-0446-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-CURSOR.md`
- `docs/reviews/PCN-0447-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-CURSOR-STEP.md`
- `docs/reviews/PCN-0448-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL.md`
- `docs/reviews/PCN-0449-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-COMPLETION.md`
- `docs/reviews/PCN-0450-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-ACKNOWLEDGEMENT-VIEW.md`
- `docs/reviews/PCN-0451-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-ACKNOWLEDGEMENT-VIEW-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0452-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-TIME-PROJECTION.md`
- `docs/reviews/PCN-0453-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-TIME-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0454-CALLER-EXPLICIT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-INTEGRAL-TIME-PROJECTION.md`
- `docs/reviews/PCN-0455-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-INTEGRAL-TIME-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0456-CALLER-EXPLICIT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-CLOCK-ORIGIN-PROJECTION.md`
- `docs/reviews/PCN-0457-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-CLOCK-ORIGIN-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0458-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-DEADLINE-PROJECTION.md`
- `docs/reviews/PCN-0459-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-DEADLINE-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0460-CALLER-EXPLICIT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-DEADLINE-OBSERVATION.md`
- `docs/reviews/PCN-0461-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-DEADLINE-OBSERVATION-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0462-CALLER-EXPLICIT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-DEADLINE-ELIGIBILITY.md`
- `docs/reviews/PCN-0463-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-DEADLINE-ELIGIBILITY-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0464-CALLER-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-ELIGIBLE-SELECTION.md`
- `docs/reviews/PCN-0465-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-ELIGIBLE-SELECTION-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0466-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-REQUEST-PROJECTION.md`
- `docs/reviews/PCN-0467-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-REQUEST-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0468-CALLER-EXPLICIT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-CONSUMER-IDENTITY.md`
- `docs/reviews/PCN-0469-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-CONSUMER-BINDING.md`
- `docs/reviews/PCN-0470-CALLER-EXPLICIT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-CONSUMER-BATCH-IDENTITY.md`
- `docs/reviews/PCN-0471-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-CONSUMER-BATCH-PROJECTION.md`
- `docs/reviews/PCN-0472-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-INTENT-PROJECTION.md`
- `docs/reviews/PCN-0473-CALLER-EXPLICIT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-CLASSIFICATION.md`
- `docs/reviews/PCN-0474-CALLER-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-SELECTION.md`
- `docs/reviews/PCN-0475-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-PROJECTION.md`
- `docs/reviews/PCN-0476-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0477-DECLARATIVE-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-SUMMARY.md`
- `docs/reviews/PCN-0478-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-SEGMENTS.md`
- `docs/reviews/PCN-0479-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-SEGMENT-CURSOR.md`
- `docs/reviews/PCN-0480-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-SEGMENT-CURSOR-STEP.md`
- `docs/reviews/PCN-0481-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-SEGMENT-TRAVERSAL.md`
- `docs/reviews/PCN-0482-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-SEGMENT-COMPLETION.md`
- `docs/reviews/PCN-0483-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-COMPLETED-SEGMENT-VIEW.md`

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

**Post-0.0.10e — Bounded ISO BMFF / MP4 Managed Foundation — Complete through ADR-0473**

- ADR-0167 through ADR-0172 complete the bounded non-physical playback and application prerequisites;
- ADR-0173 through ADR-0231 establish bounded box traversal, movie/track/media metadata, sample-table reconciliation and lookup, edit mapping, edited-sample ownership, sample entries, `avcC`, and MPEG-4 descriptor headers;
- ADR-0232 through ADR-0249 complete the current managed AVC framing, classification, payload-form selection, materialization, and bounded composition chain;
- ADR-0250 through ADR-0299 complete the current managed AAC configuration, ER protection, channel topology/layout, format intent, parameter ownership, declared-capability binding, and decoder-preparation chain;
- ADR-0300 through ADR-0327 complete the current one-packet-per-selected-track metadata chain through exact track/movie binding, bounded indexing, explicit video/audio selection, event projection, exact rational cross-stream ordering, exact rational seconds metadata, a bounded immutable queue snapshot, pure cursor traversal, exact one-step acknowledgement metadata, caller-rate exact rational clock time, caller-explicit integral rounding and signed origin, deadline observation, explicit eligibility, bounded candidate selection, opaque target identities, exact media target binding, bounded dispatch requests, declarative capabilities, exact capability resolution, opaque runtime registrations, runtime identity lookup, caller-identified source-ordered invocation intent, caller-explicit admission classification, and bounded admitted selection;
- ADR-0328 adds caller-bounded non-immediate implicit SBR marker search after exact direct-GA or admitted-ER configuration and retains exact search provenance;
- ADR-0329 requires a factory-validated caller-explicit immediate-only or bounded-search resolution policy and retains the exact selected path;
- ADR-0330 owns bounded same-track already composed packet sequences with exact shared sources and strictly increasing sample numbers;
- ADR-0331 traverses those exact entries through a pure immutable cursor with explicit exhaustion and no delivery claim;
- ADR-0332 retains one exact cursor/entry/successor transition without claiming delivery or consumption;
- ADR-0333 owns bounded caller-ordered same-movie track-sequence sets with unique track IDs and stable stream indexes;
- ADR-0334 retains caller-explicit video and/or audio sequence selection without default inference or fallback;
- ADR-0335 creates immutable initial cursors for exactly the selected sequence roles with no partial result;
- ADR-0336 projects exact current entries without cursor advance or comparison and retains explicit exhaustion;
- ADR-0337 compares exact rational leading movie intervals without tie-breaking or selecting a cursor to advance;
- ADR-0338 applies caller-explicit equal-interval preference and retains the exact selected advance role without cursor mutation;
- ADR-0339 advances exactly the selected cursor by one immutable transition and preserves the non-selected cursor by reference;
- ADR-0340 projects one caller-indexed exact cross-track step with complete selected-source and successor-state provenance;
- ADR-0341 owns bounded contiguous single-policy step chains with reference-exact cursor continuity;
- ADR-0342 performs caller-bounded pure managed cross-track packet traversal with explicit exhaustion versus truncation;
- ADR-0343 continues exact truncated final cursor state without re-emitting prior steps and supports finite segment chaining;
- ADR-0344 certifies exact packet-level merge completion from exhausted cursors and cumulative count equality;
- ADR-0345 reconstructs one exact contiguous global step view across initial and continuation segments;
- ADR-0346 projects every complete packet appearance into one caller-bounded overflow-safe exact rational event order;
- ADR-0347 traverses the exact completed event order through a bounded immutable cursor with explicit exhaustion;
- ADR-0348 retains one exact source/event/successor cursor transition without claiming consumption or delivery;
- ADR-0349 composes caller-bounded exact event-transition chains with explicit exhaustion versus truncation;
- ADR-0350 continues exact truncated event traversal without re-emitting prior acknowledgements;
- ADR-0351 certifies whole root-remaining event traversal from exact count and final exhaustion;
- ADR-0352 reconstructs one contiguous global acknowledgement view across exact traversal segments;
- ADR-0353 projects exact rational seconds for every completed traversal acknowledgement;
- ADR-0354 applies one caller-explicit clock rate as overflow-safe exact rational ticks without reading a clock;
- ADR-0355 through ADR-0358 add caller-explicit integral rounding and origin, exact deadlines, and pure caller-tick observation;
- ADR-0359 and ADR-0360 add caller-explicit late eligibility and bounded source-order candidate selection;
- ADR-0361 through ADR-0363 add opaque media target IDs, exact binding, and bounded request metadata;
- ADR-0364 and ADR-0365 add bounded caller-declared target capabilities and exact request resolution;
- ADR-0366 and ADR-0367 add opaque runtime target registrations and exact identity-only lookup;
- ADR-0368 adds caller-identified source-ordered immutable invocation intent metadata;
- ADR-0369 and ADR-0370 add caller-explicit admission classification and bounded admitted selection;
- ADR-0371 adds caller-bounded contiguous sample-number planning from exact reconciled counts;
- ADR-0372 and ADR-0373 add exact sample edit-admission classification and bounded admitted selection;
- ADR-0374 adds exact admitted-sample absolute byte-range and description-index projection;
- ADR-0375 through ADR-0377 add bounded `mdat` containment, owned payloads, and exact packet candidates;
- ADR-0378 and ADR-0379 add exact sample-description selection and bounded audio/visual sample-entry parsing;
- ADR-0380 adds exact bounded `avcC` / `esds` codec-configuration parsing;
- ADR-0381 through ADR-0383 add bounded managed AVC/AAC packet composition and exact track/movie binding;
- ADR-0384 through ADR-0387 add exact sequence projection, bounded same-movie sequence sets, explicit stream selection, and immutable initial cursors;
- ADR-0388 through ADR-0390 add caller-bounded merge traversal, exact continuation, and exact completion certification;
- ADR-0391 through ADR-0393 add exact completed-step reconstruction, bounded completed-event ordering, and immutable initial event cursors;
- ADR-0394 through ADR-0396 add caller-bounded projected event traversal, exact continuation without replay, and exact completion certification;
- ADR-0397 adds exact global acknowledgement reconstruction across projected traversal segments;
- ADR-0398 through ADR-0400 add exact rational seconds, caller-rate rational ticks, and caller-explicit integral rounding;
- ADR-0401 and ADR-0402 add caller-explicit signed clock origins and bounded immutable deadlines;
- ADR-0403 through ADR-0408 add caller-tick observation, explicit eligibility, bounded candidates, opaque targets, exact binding, and immutable request metadata;
- ADR-0409 through ADR-0412 add caller-declared capabilities, exact resolution, opaque runtime identities, and identity-only lookup;
- ADR-0413 through ADR-0415 add caller-identified immutable intent, explicit admission, and bounded admitted selection without invocation;
- ADR-0416 starts strict bounded fragmented-MP4 `trex` track-default parsing;
- ADR-0417 adds strict bounded fragmented-MP4 `mfhd` sequence-header parsing;
- ADR-0418 through ADR-0420 add strict bounded fragmented-MP4 `tfhd`, `tfdt`, and bounded `trun` leaf-box parsing;
- ADR-0421 resolves one bounded fragment run's sample defaults through exact `trun`, `tfhd`, and `trex` precedence;
- ADR-0422 projects exact arbitrary-precision decode and presentation timing from `tfdt` plus one resolved run;
- ADR-0423 through ADR-0425 resolve explicit run data origins, project arbitrary-precision sample byte ranges, and join exact fragment timing/range metadata;
- ADR-0426 binds nonzero media timescale to fragment metadata as exact arbitrary-precision rational time;
- ADR-0427 orders one fragment by exact presentation, decode, and source-index keys;
- ADR-0428 through ADR-0432 classify sequence/decode continuity, bind both dimensions, validate exact bounded transition chains, and summarize discontinuities without reset;
- ADR-0433 through ADR-0473 bind exact sample batches to continuity transitions, produce a bounded cross-fragment presentation merge, traverse its exact immutable cursor-step chain, revalidate exhaustive completion, reconstruct source-bound acknowledgement metadata without claiming delivery, and expose exact timing, deadline, eligibility, caller-bounded selection, target-free request metadata, caller-explicit opaque consumer binding, bounded opaque batch metadata, non-invoking submission-intent classification, bounded included-intent selection, exact non-payload manifest metadata, full-chain manifest source binding, declarative aggregate facts, bounded contiguous fragment segments, an immutable segment cursor-step boundary, bounded exhaustive segment traversal, exact metadata-only completion certification, and non-acknowledging completed-segment reconstruction;
- ISO BMFF / MP4 is the second bounded container-parser family alongside the existing FLV v1 parser;
- automatic AAC sync-search defaults, default timing policies, clock acquisition, repeated observation, runtime instance access and invocation, actual dispatch, cursor mutation, delivery outcome, scheduling, automatic multi-sample packet composition and cross-track sequence merge, native application of managed PCE layout, AVC runtime capability discovery and decoder integration, production MP4 demux composition, nested-box and streaming policy, and fragmented MP4 remain incomplete;
- current reviews span `docs/reviews/PCN-0177-OPT-IN-DECODED-BUFFER-DURATION-CUTOFF.md` through `docs/reviews/PCN-0483-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-COMPLETED-SEGMENT-VIEW.md`;
- ADR-0158 is a permanent numbering-error gap caused by assignment error, following ADR-0082 and ADR-0096, and must not be retroactively reused;
- next: bind the completed-segment view back to the exact manifest source without acknowledgement, payload access, authorization, invocation, dispatch, or delivery claims.

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
- `docs/adr/ADR-0301-bounded-iso-bmff-track-codec-packet.md`
- `docs/adr/ADR-0302-bounded-iso-bmff-movie-track-codec-packet.md`
- `docs/adr/ADR-0303-bounded-iso-bmff-movie-codec-packet-index.md`
- `docs/adr/ADR-0304-explicit-iso-bmff-movie-codec-stream-selection.md`
- `docs/adr/ADR-0305-bounded-iso-bmff-movie-codec-packet-event-projection.md`
- `docs/adr/ADR-0306-exact-iso-bmff-movie-codec-packet-event-order.md`
- `docs/adr/ADR-0307-exact-iso-bmff-movie-codec-packet-event-time.md`
- `docs/adr/ADR-0308-bounded-iso-bmff-movie-codec-packet-event-queue-snapshot.md`
- `docs/adr/ADR-0309-bounded-iso-bmff-movie-codec-packet-event-queue-cursor.md`
- `docs/adr/ADR-0310-exact-iso-bmff-movie-codec-packet-event-queue-acknowledgement.md`
- `docs/adr/ADR-0311-exact-iso-bmff-movie-codec-packet-event-queue-clock-time.md`
- `docs/adr/ADR-0312-caller-explicit-iso-bmff-movie-codec-packet-event-queue-integral-clock-time.md`
- `docs/adr/ADR-0313-caller-explicit-iso-bmff-movie-codec-packet-event-queue-clock-origin.md`
- `docs/adr/ADR-0314-bounded-iso-bmff-movie-codec-packet-event-queue-deadline.md`
- `docs/adr/ADR-0315-caller-explicit-iso-bmff-movie-codec-packet-event-queue-deadline-observation.md`
- `docs/adr/ADR-0316-caller-explicit-iso-bmff-movie-codec-packet-event-queue-dispatch-eligibility.md`
- `docs/adr/ADR-0317-bounded-iso-bmff-movie-codec-packet-event-queue-dispatch-candidate-selection.md`
- `docs/adr/ADR-0318-explicit-iso-bmff-movie-codec-packet-event-queue-dispatch-target-set.md`
- `docs/adr/ADR-0319-exact-iso-bmff-movie-codec-packet-event-queue-dispatch-target-binding.md`
- `docs/adr/ADR-0320-bounded-iso-bmff-movie-codec-packet-event-queue-dispatch-request-projection.md`
- `docs/adr/ADR-0321-explicit-iso-bmff-movie-codec-packet-event-queue-dispatch-target-capability-set.md`
- `docs/adr/ADR-0322-bounded-iso-bmff-movie-codec-packet-event-queue-dispatch-target-resolution.md`
- `docs/adr/ADR-0323-explicit-iso-bmff-movie-codec-packet-event-queue-dispatch-runtime-target-registry.md`
- `docs/adr/ADR-0324-bounded-iso-bmff-movie-codec-packet-event-queue-dispatch-runtime-target-lookup.md`
- `docs/adr/ADR-0325-bounded-iso-bmff-movie-codec-packet-event-queue-dispatch-invocation-intent.md`
- `docs/adr/ADR-0326-caller-explicit-iso-bmff-movie-codec-packet-event-queue-dispatch-invocation-admission.md`
- `docs/adr/ADR-0327-bounded-iso-bmff-movie-codec-packet-event-queue-dispatch-admitted-invocation-selection.md`
- `docs/adr/ADR-0328-caller-bounded-iso-bmff-implicit-sbr-sync-extension-search.md`
- `docs/adr/ADR-0329-caller-explicit-iso-bmff-implicit-sbr-sync-extension-resolution-policy.md`
- `docs/adr/ADR-0330-bounded-iso-bmff-track-codec-packet-sequence.md`
- `docs/adr/ADR-0331-bounded-iso-bmff-track-codec-packet-sequence-cursor.md`
- `docs/adr/ADR-0332-exact-iso-bmff-track-codec-packet-sequence-acknowledgement.md`
- `docs/adr/ADR-0333-bounded-iso-bmff-movie-track-codec-packet-sequence-set.md`
- `docs/adr/ADR-0334-explicit-iso-bmff-movie-track-codec-packet-sequence-selection.md`
- `docs/adr/ADR-0335-bounded-iso-bmff-selected-track-codec-packet-sequence-cursor-set.md`
- `docs/adr/ADR-0336-exact-iso-bmff-selected-sequence-packet-head-snapshot.md`
- `docs/adr/ADR-0337-exact-iso-bmff-selected-sequence-head-movie-time-comparison.md`
- `docs/adr/ADR-0338-caller-explicit-iso-bmff-sequence-advance-role-selection.md`
- `docs/adr/ADR-0339-exact-iso-bmff-selected-sequence-cursor-set-advance.md`
- `docs/adr/ADR-0340-exact-iso-bmff-cross-track-sequence-merge-step.md`
- `docs/adr/ADR-0341-bounded-iso-bmff-cross-track-merge-step-sequence.md`
- `docs/adr/ADR-0342-caller-bounded-iso-bmff-cross-track-merge-traversal.md`
- `docs/adr/ADR-0343-exact-iso-bmff-merge-traversal-continuation.md`
- `docs/adr/ADR-0344-exact-iso-bmff-packet-merge-completion.md`
- `docs/adr/ADR-0345-exact-iso-bmff-completed-merge-step-view.md`
- `docs/adr/ADR-0346-bounded-iso-bmff-completed-merge-event-projection.md`
- `docs/adr/ADR-0347-bounded-iso-bmff-completed-merge-event-cursor.md`
- `docs/adr/ADR-0348-exact-iso-bmff-completed-merge-event-acknowledgement.md`
- `docs/adr/ADR-0349-caller-bounded-iso-bmff-completed-merge-event-traversal.md`
- `docs/adr/ADR-0350-exact-iso-bmff-completed-merge-event-traversal-continuation.md`
- `docs/adr/ADR-0351-exact-iso-bmff-completed-merge-event-traversal-completion.md`
- `docs/adr/ADR-0352-exact-iso-bmff-completed-merge-event-traversal-acknowledgement-view.md`
- `docs/adr/ADR-0353-exact-iso-bmff-completed-merge-event-traversal-time.md`
- `docs/adr/ADR-0354-caller-explicit-iso-bmff-completed-merge-event-traversal-clock-time.md`
- `docs/adr/ADR-0355-caller-explicit-iso-bmff-completed-merge-event-traversal-integral-clock-time.md`
- `docs/adr/ADR-0356-caller-explicit-iso-bmff-completed-merge-event-traversal-clock-origin.md`
- `docs/adr/ADR-0357-bounded-iso-bmff-completed-merge-event-traversal-deadline.md`
- `docs/adr/ADR-0358-caller-explicit-iso-bmff-completed-merge-event-traversal-deadline-observation.md`
- `docs/adr/ADR-0359-caller-explicit-iso-bmff-completed-merge-event-traversal-dispatch-eligibility.md`
- `docs/adr/ADR-0360-bounded-iso-bmff-completed-merge-event-traversal-dispatch-candidate-selection.md`
- `docs/adr/ADR-0361-explicit-iso-bmff-completed-merge-event-traversal-dispatch-target-set.md`
- `docs/adr/ADR-0362-exact-iso-bmff-completed-merge-event-traversal-dispatch-target-binding.md`
- `docs/adr/ADR-0363-bounded-iso-bmff-completed-merge-event-traversal-dispatch-request.md`
- `docs/adr/ADR-0364-explicit-iso-bmff-completed-merge-event-traversal-dispatch-target-capability-set.md`
- `docs/adr/ADR-0365-exact-iso-bmff-completed-merge-event-traversal-dispatch-target-resolution.md`
- `docs/adr/ADR-0366-explicit-iso-bmff-completed-merge-event-traversal-dispatch-runtime-target-registry.md`
- `docs/adr/ADR-0367-exact-iso-bmff-completed-merge-event-traversal-dispatch-runtime-target-lookup.md`
- `docs/adr/ADR-0368-caller-identified-iso-bmff-completed-merge-event-traversal-dispatch-invocation-intent.md`
- `docs/adr/ADR-0369-caller-explicit-iso-bmff-completed-merge-event-traversal-dispatch-invocation-admission.md`
- `docs/adr/ADR-0370-bounded-iso-bmff-completed-merge-event-traversal-dispatch-admitted-invocation-selection.md`
- `docs/adr/ADR-0371-caller-bounded-iso-bmff-track-sample-sequence-plan.md`
- `docs/adr/ADR-0372-exact-iso-bmff-track-sample-edit-admission-projection.md`
- `docs/adr/ADR-0373-bounded-iso-bmff-track-admitted-sample-selection.md`
- `docs/adr/ADR-0374-exact-iso-bmff-track-admitted-sample-byte-range-projection.md`
- `docs/adr/ADR-0375-exact-iso-bmff-track-admitted-sample-media-data-projection.md`
- `docs/adr/ADR-0376-bounded-iso-bmff-track-owned-sample-payload-projection.md`
- `docs/adr/ADR-0377-exact-iso-bmff-track-edited-sample-packet-candidate-projection.md`
- `docs/adr/ADR-0378-exact-iso-bmff-track-sample-description-selection-projection.md`
- `docs/adr/ADR-0379-exact-iso-bmff-track-sample-entry-parse-projection.md`
- `docs/adr/ADR-0380-exact-iso-bmff-track-codec-configuration-projection.md`
- `docs/adr/ADR-0381-bounded-iso-bmff-track-codec-packet-composition-projection.md`
- `docs/adr/ADR-0382-exact-iso-bmff-track-codec-packet-binding-projection.md`
- `docs/adr/ADR-0383-exact-iso-bmff-movie-track-codec-packet-binding-projection.md`
- `docs/adr/ADR-0384-exact-iso-bmff-movie-track-codec-packet-sequence-projection.md`
- `docs/adr/ADR-0385-exact-iso-bmff-movie-track-codec-packet-sequence-set-projection.md`
- `docs/adr/ADR-0386-caller-explicit-iso-bmff-movie-track-codec-packet-sequence-selection-projection.md`
- `docs/adr/ADR-0387-exact-iso-bmff-selected-sequence-initial-cursor-set-projection.md`
- `docs/adr/ADR-0388-caller-bounded-iso-bmff-projected-sequence-merge-traversal.md`
- `docs/adr/ADR-0389-exact-iso-bmff-projected-sequence-merge-continuation.md`
- `docs/adr/ADR-0390-exact-iso-bmff-projected-sequence-merge-completion.md`
- `docs/adr/ADR-0391-exact-iso-bmff-projected-completed-merge-step-view.md`
- `docs/adr/ADR-0392-bounded-iso-bmff-projected-completed-merge-events.md`
- `docs/adr/ADR-0393-exact-iso-bmff-projected-completed-event-initial-cursor.md`
- `docs/adr/ADR-0394-caller-bounded-iso-bmff-projected-completed-event-traversal.md`
- `docs/adr/ADR-0395-exact-iso-bmff-projected-completed-event-traversal-continuation.md`
- `docs/adr/ADR-0396-exact-iso-bmff-projected-completed-event-traversal-completion.md`
- `docs/adr/ADR-0397-exact-iso-bmff-projected-completed-event-acknowledgement-view.md`
- `docs/adr/ADR-0398-exact-iso-bmff-projected-completed-event-traversal-time.md`
- `docs/adr/ADR-0399-caller-explicit-iso-bmff-projected-completed-event-traversal-clock-time.md`
- `docs/adr/ADR-0400-caller-explicit-iso-bmff-projected-completed-event-traversal-integral-clock-time.md`
- `docs/adr/ADR-0401-caller-explicit-iso-bmff-projected-completed-event-traversal-clock-origin.md`
- `docs/adr/ADR-0402-bounded-iso-bmff-projected-completed-event-traversal-deadline.md`
- `docs/adr/ADR-0403-caller-explicit-iso-bmff-projected-completed-event-traversal-deadline-observation.md`
- `docs/adr/ADR-0404-caller-explicit-iso-bmff-projected-completed-event-traversal-dispatch-eligibility.md`
- `docs/adr/ADR-0405-caller-bounded-iso-bmff-projected-completed-event-traversal-dispatch-candidate-selection.md`
- `docs/adr/ADR-0406-explicit-iso-bmff-projected-completed-event-traversal-dispatch-target-set.md`
- `docs/adr/ADR-0407-exact-iso-bmff-projected-completed-event-traversal-dispatch-target-binding.md`
- `docs/adr/ADR-0408-bounded-iso-bmff-projected-completed-event-traversal-dispatch-request.md`
- `docs/adr/ADR-0409-explicit-iso-bmff-projected-completed-event-traversal-dispatch-target-capability-set.md`
- `docs/adr/ADR-0410-exact-iso-bmff-projected-completed-event-traversal-dispatch-target-resolution.md`
- `docs/adr/ADR-0411-explicit-iso-bmff-projected-completed-event-traversal-dispatch-runtime-target-registry.md`
- `docs/adr/ADR-0412-exact-iso-bmff-projected-completed-event-traversal-dispatch-runtime-target-lookup.md`
- `docs/adr/ADR-0413-caller-identified-iso-bmff-projected-completed-event-traversal-dispatch-invocation-intent.md`
- `docs/adr/ADR-0414-caller-explicit-iso-bmff-projected-completed-event-traversal-dispatch-invocation-admission.md`
- `docs/adr/ADR-0415-caller-bounded-iso-bmff-projected-completed-event-traversal-dispatch-admitted-invocation-selection.md`
- `docs/adr/ADR-0416-bounded-iso-bmff-track-extends-box.md`
- `docs/adr/ADR-0417-bounded-iso-bmff-movie-fragment-header.md`
- `docs/adr/ADR-0418-bounded-iso-bmff-track-fragment-header.md`
- `docs/adr/ADR-0419-bounded-iso-bmff-track-fragment-decode-time.md`
- `docs/adr/ADR-0420-bounded-iso-bmff-track-run.md`
- `docs/adr/ADR-0421-bounded-iso-bmff-fragment-sample-default-resolution.md`
- `docs/adr/ADR-0422-bounded-iso-bmff-fragment-sample-timing-projection.md`
- `docs/adr/ADR-0423-bounded-iso-bmff-fragment-run-data-origin-resolution.md`
- `docs/adr/ADR-0424-bounded-iso-bmff-fragment-sample-byte-range-projection.md`
- `docs/adr/ADR-0425-bounded-iso-bmff-fragment-sample-metadata-projection.md`
- `docs/adr/ADR-0426-bounded-iso-bmff-fragment-sample-rational-time-projection.md`
- `docs/adr/ADR-0427-bounded-iso-bmff-fragment-sample-presentation-order.md`
- `docs/adr/ADR-0428-bounded-iso-bmff-movie-fragment-sequence-continuity.md`
- `docs/adr/ADR-0429-bounded-iso-bmff-track-fragment-decode-continuity.md`
- `docs/adr/ADR-0430-bounded-iso-bmff-fragment-continuity-binding.md`
- `docs/adr/ADR-0431-bounded-iso-bmff-fragment-continuity-chain.md`
- `docs/adr/ADR-0432-bounded-iso-bmff-fragment-continuity-summary.md`
- `docs/adr/ADR-0433-bounded-iso-bmff-fragment-sample-batch.md`
- `docs/adr/ADR-0434-bounded-iso-bmff-fragment-sample-batch-chain.md`
- `docs/adr/ADR-0435-bounded-iso-bmff-fragment-sample-batch-presentation-merge.md`
- `docs/adr/ADR-0436-bounded-iso-bmff-fragment-sample-merge-cursor.md`
- `docs/adr/ADR-0437-exact-iso-bmff-fragment-sample-merge-cursor-step.md`
- `docs/adr/ADR-0438-bounded-iso-bmff-fragment-sample-merge-traversal.md`
- `docs/adr/ADR-0439-exact-iso-bmff-fragment-sample-merge-traversal-completion.md`
- `docs/adr/ADR-0440-bounded-iso-bmff-fragment-sample-merge-traversal-acknowledgement-view.md`
- `docs/adr/ADR-0441-exact-iso-bmff-fragment-sample-merge-traversal-acknowledgement-view-source-projection.md`
- `docs/adr/ADR-0442-exact-iso-bmff-fragment-sample-merge-traversal-time-projection.md`
- `docs/adr/ADR-0443-exact-iso-bmff-fragment-sample-merge-traversal-time-source-projection.md`
- `docs/adr/ADR-0444-caller-explicit-iso-bmff-fragment-sample-merge-traversal-integral-time-projection.md`
- `docs/adr/ADR-0445-exact-iso-bmff-fragment-sample-merge-traversal-integral-time-source-projection.md`
- `docs/adr/ADR-0446-caller-explicit-iso-bmff-fragment-sample-merge-traversal-clock-origin-projection.md`
- `docs/adr/ADR-0447-exact-iso-bmff-fragment-sample-merge-traversal-clock-origin-source-projection.md`
- `docs/adr/ADR-0448-bounded-iso-bmff-fragment-sample-merge-traversal-deadline-projection.md`
- `docs/adr/ADR-0449-exact-iso-bmff-fragment-sample-merge-traversal-deadline-source-projection.md`
- `docs/adr/ADR-0450-caller-explicit-iso-bmff-fragment-sample-merge-traversal-deadline-observation.md`
- `docs/adr/ADR-0451-exact-iso-bmff-fragment-sample-merge-traversal-deadline-observation-source-projection.md`
- `docs/adr/ADR-0452-caller-explicit-iso-bmff-fragment-sample-merge-traversal-deadline-eligibility.md`
- `docs/adr/ADR-0453-exact-iso-bmff-fragment-sample-merge-traversal-deadline-eligibility-source-projection.md`
- `docs/adr/ADR-0454-caller-bounded-iso-bmff-fragment-sample-merge-traversal-eligible-selection.md`
- `docs/adr/ADR-0455-exact-iso-bmff-fragment-sample-merge-traversal-eligible-selection-source-projection.md`
- `docs/adr/ADR-0456-exact-iso-bmff-fragment-sample-merge-traversal-request-projection.md`
- `docs/adr/ADR-0457-exact-iso-bmff-fragment-sample-merge-traversal-request-source-projection.md`
- `docs/adr/ADR-0458-caller-explicit-iso-bmff-fragment-sample-merge-traversal-consumer-identity.md`
- `docs/adr/ADR-0459-exact-iso-bmff-fragment-sample-merge-traversal-consumer-binding.md`
- `docs/adr/ADR-0460-caller-explicit-iso-bmff-fragment-sample-merge-traversal-consumer-batch-identity.md`
- `docs/adr/ADR-0461-bounded-iso-bmff-fragment-sample-merge-traversal-consumer-batch-projection.md`
- `docs/adr/ADR-0462-bounded-iso-bmff-fragment-sample-merge-traversal-submission-intent-projection.md`
- `docs/adr/ADR-0463-caller-explicit-iso-bmff-fragment-sample-merge-traversal-submission-classification.md`
- `docs/adr/ADR-0464-caller-bounded-iso-bmff-fragment-sample-merge-traversal-submission-selection.md`
- `docs/adr/ADR-0465-exact-iso-bmff-fragment-sample-merge-traversal-submission-manifest-projection.md`
- `docs/adr/ADR-0466-exact-iso-bmff-fragment-sample-merge-traversal-submission-manifest-source-projection.md`
- `docs/adr/ADR-0467-declarative-iso-bmff-fragment-sample-merge-traversal-submission-manifest-summary.md`
- `docs/adr/ADR-0468-bounded-iso-bmff-fragment-sample-merge-traversal-submission-manifest-segments.md`
- `docs/adr/ADR-0469-bounded-iso-bmff-fragment-sample-merge-traversal-submission-manifest-segment-cursor.md`
- `docs/adr/ADR-0470-exact-iso-bmff-fragment-sample-merge-traversal-submission-manifest-segment-cursor-step.md`
- `docs/adr/ADR-0471-bounded-iso-bmff-fragment-sample-merge-traversal-submission-manifest-segment-traversal.md`
- `docs/adr/ADR-0472-exact-iso-bmff-fragment-sample-merge-traversal-submission-manifest-segment-completion.md`
- `docs/adr/ADR-0473-exact-iso-bmff-fragment-sample-merge-traversal-submission-manifest-completed-segment-view.md`
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
- `docs/reviews/PCN-0311-BOUNDED-ISO-BMFF-TRACK-CODEC-PACKET.md`
- `docs/reviews/PCN-0312-BOUNDED-ISO-BMFF-MOVIE-TRACK-CODEC-PACKET.md`
- `docs/reviews/PCN-0313-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-INDEX.md`
- `docs/reviews/PCN-0314-EXPLICIT-ISO-BMFF-MOVIE-CODEC-STREAM-SELECTION.md`
- `docs/reviews/PCN-0315-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-PROJECTION.md`
- `docs/reviews/PCN-0316-EXACT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-ORDER.md`
- `docs/reviews/PCN-0317-EXACT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-TIME.md`
- `docs/reviews/PCN-0318-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-SNAPSHOT.md`
- `docs/reviews/PCN-0319-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-CURSOR.md`
- `docs/reviews/PCN-0320-EXACT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-ACKNOWLEDGEMENT.md`
- `docs/reviews/PCN-0321-EXACT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-CLOCK-TIME.md`
- `docs/reviews/PCN-0322-CALLER-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-INTEGRAL-CLOCK-TIME.md`
- `docs/reviews/PCN-0323-CALLER-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-CLOCK-ORIGIN.md`
- `docs/reviews/PCN-0324-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DEADLINE.md`
- `docs/reviews/PCN-0325-CALLER-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DEADLINE-OBSERVATION.md`
- `docs/reviews/PCN-0326-CALLER-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-ELIGIBILITY.md`
- `docs/reviews/PCN-0327-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-CANDIDATE-SELECTION.md`
- `docs/reviews/PCN-0328-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-TARGET-SET.md`
- `docs/reviews/PCN-0329-EXACT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-TARGET-BINDING.md`
- `docs/reviews/PCN-0330-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-REQUEST-PROJECTION.md`
- `docs/reviews/PCN-0331-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-TARGET-CAPABILITY-SET.md`
- `docs/reviews/PCN-0332-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-TARGET-RESOLUTION.md`
- `docs/reviews/PCN-0333-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-RUNTIME-TARGET-REGISTRY.md`
- `docs/reviews/PCN-0334-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-RUNTIME-TARGET-LOOKUP.md`
- `docs/reviews/PCN-0335-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-INVOCATION-INTENT.md`
- `docs/reviews/PCN-0336-CALLER-EXPLICIT-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-INVOCATION-ADMISSION.md`
- `docs/reviews/PCN-0337-BOUNDED-ISO-BMFF-MOVIE-CODEC-PACKET-EVENT-QUEUE-DISPATCH-ADMITTED-INVOCATION-SELECTION.md`
- `docs/reviews/PCN-0338-CALLER-BOUNDED-ISO-BMFF-IMPLICIT-SBR-SYNC-EXTENSION-SEARCH.md`
- `docs/reviews/PCN-0339-CALLER-EXPLICIT-ISO-BMFF-IMPLICIT-SBR-SYNC-EXTENSION-RESOLUTION-POLICY.md`
- `docs/reviews/PCN-0340-BOUNDED-ISO-BMFF-TRACK-CODEC-PACKET-SEQUENCE.md`
- `docs/reviews/PCN-0341-BOUNDED-ISO-BMFF-TRACK-CODEC-PACKET-SEQUENCE-CURSOR.md`
- `docs/reviews/PCN-0342-EXACT-ISO-BMFF-TRACK-CODEC-PACKET-SEQUENCE-ACKNOWLEDGEMENT.md`
- `docs/reviews/PCN-0343-BOUNDED-ISO-BMFF-MOVIE-TRACK-CODEC-PACKET-SEQUENCE-SET.md`
- `docs/reviews/PCN-0344-EXPLICIT-ISO-BMFF-MOVIE-TRACK-CODEC-PACKET-SEQUENCE-SELECTION.md`
- `docs/reviews/PCN-0345-BOUNDED-ISO-BMFF-SELECTED-TRACK-CODEC-PACKET-SEQUENCE-CURSOR-SET.md`
- `docs/reviews/PCN-0346-EXACT-ISO-BMFF-SELECTED-SEQUENCE-PACKET-HEAD-SNAPSHOT.md`
- `docs/reviews/PCN-0347-EXACT-ISO-BMFF-SELECTED-SEQUENCE-HEAD-MOVIE-TIME-COMPARISON.md`
- `docs/reviews/PCN-0348-CALLER-EXPLICIT-ISO-BMFF-SEQUENCE-ADVANCE-ROLE-SELECTION.md`
- `docs/reviews/PCN-0349-EXACT-ISO-BMFF-SELECTED-SEQUENCE-CURSOR-SET-ADVANCE.md`
- `docs/reviews/PCN-0350-EXACT-ISO-BMFF-CROSS-TRACK-SEQUENCE-MERGE-STEP.md`
- `docs/reviews/PCN-0351-BOUNDED-ISO-BMFF-CROSS-TRACK-MERGE-STEP-SEQUENCE.md`
- `docs/reviews/PCN-0352-CALLER-BOUNDED-ISO-BMFF-CROSS-TRACK-MERGE-TRAVERSAL.md`
- `docs/reviews/PCN-0353-EXACT-ISO-BMFF-MERGE-TRAVERSAL-CONTINUATION.md`
- `docs/reviews/PCN-0354-EXACT-ISO-BMFF-PACKET-MERGE-COMPLETION.md`
- `docs/reviews/PCN-0355-EXACT-ISO-BMFF-COMPLETED-MERGE-STEP-VIEW.md`
- `docs/reviews/PCN-0356-BOUNDED-ISO-BMFF-COMPLETED-MERGE-EVENT-PROJECTION.md`
- `docs/reviews/PCN-0357-BOUNDED-ISO-BMFF-COMPLETED-MERGE-EVENT-CURSOR.md`
- `docs/reviews/PCN-0358-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-ACKNOWLEDGEMENT.md`
- `docs/reviews/PCN-0359-CALLER-BOUNDED-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL.md`
- `docs/reviews/PCN-0360-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-CONTINUATION.md`
- `docs/reviews/PCN-0361-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-COMPLETION.md`
- `docs/reviews/PCN-0362-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-ACKNOWLEDGEMENT-VIEW.md`
- `docs/reviews/PCN-0363-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-TIME.md`
- `docs/reviews/PCN-0364-CALLER-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-CLOCK-TIME.md`
- `docs/reviews/PCN-0365-CALLER-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-INTEGRAL-CLOCK-TIME.md`
- `docs/reviews/PCN-0366-CALLER-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-CLOCK-ORIGIN.md`
- `docs/reviews/PCN-0367-BOUNDED-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DEADLINE.md`
- `docs/reviews/PCN-0368-CALLER-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DEADLINE-OBSERVATION.md`
- `docs/reviews/PCN-0369-CALLER-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-ELIGIBILITY.md`
- `docs/reviews/PCN-0370-BOUNDED-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-CANDIDATE-SELECTION.md`
- `docs/reviews/PCN-0371-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-TARGET-SET.md`
- `docs/reviews/PCN-0372-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-TARGET-BINDING.md`
- `docs/reviews/PCN-0373-BOUNDED-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-REQUEST.md`
- `docs/reviews/PCN-0374-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-TARGET-CAPABILITY-SET.md`
- `docs/reviews/PCN-0375-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-TARGET-RESOLUTION.md`
- `docs/reviews/PCN-0376-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-RUNTIME-TARGET-REGISTRY.md`
- `docs/reviews/PCN-0377-EXACT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-RUNTIME-TARGET-LOOKUP.md`
- `docs/reviews/PCN-0378-CALLER-IDENTIFIED-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-INVOCATION-INTENT.md`
- `docs/reviews/PCN-0379-CALLER-EXPLICIT-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-INVOCATION-ADMISSION.md`
- `docs/reviews/PCN-0380-BOUNDED-ISO-BMFF-COMPLETED-MERGE-EVENT-TRAVERSAL-DISPATCH-ADMITTED-INVOCATION-SELECTION.md`
- `docs/reviews/PCN-0381-CALLER-BOUNDED-ISO-BMFF-TRACK-SAMPLE-SEQUENCE-PLAN.md`
- `docs/reviews/PCN-0382-EXACT-ISO-BMFF-TRACK-SAMPLE-EDIT-ADMISSION-PROJECTION.md`
- `docs/reviews/PCN-0383-BOUNDED-ISO-BMFF-TRACK-ADMITTED-SAMPLE-SELECTION.md`
- `docs/reviews/PCN-0384-EXACT-ISO-BMFF-TRACK-ADMITTED-SAMPLE-BYTE-RANGE-PROJECTION.md`
- `docs/reviews/PCN-0385-EXACT-ISO-BMFF-TRACK-ADMITTED-SAMPLE-MEDIA-DATA-PROJECTION.md`
- `docs/reviews/PCN-0386-BOUNDED-ISO-BMFF-TRACK-OWNED-SAMPLE-PAYLOAD-PROJECTION.md`
- `docs/reviews/PCN-0387-EXACT-ISO-BMFF-TRACK-EDITED-SAMPLE-PACKET-CANDIDATE-PROJECTION.md`
- `docs/reviews/PCN-0388-EXACT-ISO-BMFF-TRACK-SAMPLE-DESCRIPTION-SELECTION-PROJECTION.md`
- `docs/reviews/PCN-0389-EXACT-ISO-BMFF-TRACK-SAMPLE-ENTRY-PARSE-PROJECTION.md`
- `docs/reviews/PCN-0390-EXACT-ISO-BMFF-TRACK-CODEC-CONFIGURATION-PROJECTION.md`
- `docs/reviews/PCN-0391-BOUNDED-ISO-BMFF-TRACK-CODEC-PACKET-COMPOSITION-PROJECTION.md`
- `docs/reviews/PCN-0392-EXACT-ISO-BMFF-TRACK-CODEC-PACKET-BINDING-PROJECTION.md`
- `docs/reviews/PCN-0393-EXACT-ISO-BMFF-MOVIE-TRACK-CODEC-PACKET-BINDING-PROJECTION.md`
- `docs/reviews/PCN-0394-EXACT-ISO-BMFF-MOVIE-TRACK-CODEC-PACKET-SEQUENCE-PROJECTION.md`
- `docs/reviews/PCN-0395-EXACT-ISO-BMFF-MOVIE-TRACK-CODEC-PACKET-SEQUENCE-SET-PROJECTION.md`
- `docs/reviews/PCN-0396-CALLER-EXPLICIT-ISO-BMFF-MOVIE-TRACK-CODEC-PACKET-SEQUENCE-SELECTION-PROJECTION.md`
- `docs/reviews/PCN-0397-EXACT-ISO-BMFF-SELECTED-SEQUENCE-INITIAL-CURSOR-SET-PROJECTION.md`
- `docs/reviews/PCN-0398-CALLER-BOUNDED-ISO-BMFF-PROJECTED-SEQUENCE-MERGE-TRAVERSAL.md`
- `docs/reviews/PCN-0399-EXACT-ISO-BMFF-PROJECTED-SEQUENCE-MERGE-CONTINUATION.md`
- `docs/reviews/PCN-0400-EXACT-ISO-BMFF-PROJECTED-SEQUENCE-MERGE-COMPLETION.md`
- `docs/reviews/PCN-0401-EXACT-ISO-BMFF-PROJECTED-COMPLETED-MERGE-STEP-VIEW.md`
- `docs/reviews/PCN-0402-BOUNDED-ISO-BMFF-PROJECTED-COMPLETED-MERGE-EVENTS.md`
- `docs/reviews/PCN-0403-EXACT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-INITIAL-CURSOR.md`
- `docs/reviews/PCN-0404-CALLER-BOUNDED-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL.md`
- `docs/reviews/PCN-0405-EXACT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-CONTINUATION.md`
- `docs/reviews/PCN-0406-EXACT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-COMPLETION.md`
- `docs/reviews/PCN-0407-EXACT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-ACKNOWLEDGEMENT-VIEW.md`
- `docs/reviews/PCN-0408-EXACT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-TIME.md`
- `docs/reviews/PCN-0409-CALLER-EXPLICIT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-CLOCK-TIME.md`
- `docs/reviews/PCN-0410-CALLER-EXPLICIT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-INTEGRAL-CLOCK-TIME.md`
- `docs/reviews/PCN-0411-CALLER-EXPLICIT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-CLOCK-ORIGIN.md`
- `docs/reviews/PCN-0412-BOUNDED-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DEADLINE.md`
- `docs/reviews/PCN-0413-CALLER-EXPLICIT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DEADLINE-OBSERVATION.md`
- `docs/reviews/PCN-0414-CALLER-EXPLICIT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-ELIGIBILITY.md`
- `docs/reviews/PCN-0415-CALLER-BOUNDED-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-CANDIDATE-SELECTION.md`
- `docs/reviews/PCN-0416-EXPLICIT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-TARGET-SET.md`
- `docs/reviews/PCN-0417-EXACT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-TARGET-BINDING.md`
- `docs/reviews/PCN-0418-BOUNDED-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-REQUEST.md`
- `docs/reviews/PCN-0419-EXPLICIT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-TARGET-CAPABILITY-SET.md`
- `docs/reviews/PCN-0420-EXACT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-TARGET-RESOLUTION.md`
- `docs/reviews/PCN-0421-EXPLICIT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-RUNTIME-TARGET-REGISTRY.md`
- `docs/reviews/PCN-0422-EXACT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-RUNTIME-TARGET-LOOKUP.md`
- `docs/reviews/PCN-0423-CALLER-IDENTIFIED-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-INVOCATION-INTENT.md`
- `docs/reviews/PCN-0424-CALLER-EXPLICIT-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-INVOCATION-ADMISSION.md`
- `docs/reviews/PCN-0425-CALLER-BOUNDED-ISO-BMFF-PROJECTED-COMPLETED-EVENT-TRAVERSAL-DISPATCH-ADMITTED-INVOCATION-SELECTION.md`
- `docs/reviews/PCN-0426-BOUNDED-ISO-BMFF-TRACK-EXTENDS-BOX.md`
- `docs/reviews/PCN-0427-BOUNDED-ISO-BMFF-MOVIE-FRAGMENT-HEADER.md`
- `docs/reviews/PCN-0428-BOUNDED-ISO-BMFF-TRACK-FRAGMENT-HEADER.md`
- `docs/reviews/PCN-0429-BOUNDED-ISO-BMFF-TRACK-FRAGMENT-DECODE-TIME.md`
- `docs/reviews/PCN-0430-BOUNDED-ISO-BMFF-TRACK-RUN.md`
- `docs/reviews/PCN-0431-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-DEFAULT-RESOLUTION.md`
- `docs/reviews/PCN-0432-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-TIMING-PROJECTION.md`
- `docs/reviews/PCN-0433-BOUNDED-ISO-BMFF-FRAGMENT-RUN-DATA-ORIGIN-RESOLUTION.md`
- `docs/reviews/PCN-0434-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-BYTE-RANGE-PROJECTION.md`
- `docs/reviews/PCN-0435-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-METADATA-PROJECTION.md`
- `docs/reviews/PCN-0436-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-RATIONAL-TIME-PROJECTION.md`
- `docs/reviews/PCN-0437-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-PRESENTATION-ORDER.md`
- `docs/reviews/PCN-0438-BOUNDED-ISO-BMFF-MOVIE-FRAGMENT-SEQUENCE-CONTINUITY.md`
- `docs/reviews/PCN-0439-BOUNDED-ISO-BMFF-TRACK-FRAGMENT-DECODE-CONTINUITY.md`
- `docs/reviews/PCN-0440-BOUNDED-ISO-BMFF-FRAGMENT-CONTINUITY-BINDING.md`
- `docs/reviews/PCN-0441-BOUNDED-ISO-BMFF-FRAGMENT-CONTINUITY-CHAIN.md`
- `docs/reviews/PCN-0442-BOUNDED-ISO-BMFF-FRAGMENT-CONTINUITY-SUMMARY.md`
- `docs/reviews/PCN-0443-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-BATCH.md`
- `docs/reviews/PCN-0444-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-BATCH-CHAIN.md`
- `docs/reviews/PCN-0445-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-BATCH-PRESENTATION-MERGE.md`
- `docs/reviews/PCN-0446-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-CURSOR.md`
- `docs/reviews/PCN-0447-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-CURSOR-STEP.md`
- `docs/reviews/PCN-0448-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL.md`
- `docs/reviews/PCN-0449-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-COMPLETION.md`
- `docs/reviews/PCN-0450-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-ACKNOWLEDGEMENT-VIEW.md`
- `docs/reviews/PCN-0451-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-ACKNOWLEDGEMENT-VIEW-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0452-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-TIME-PROJECTION.md`
- `docs/reviews/PCN-0453-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-TIME-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0454-CALLER-EXPLICIT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-INTEGRAL-TIME-PROJECTION.md`
- `docs/reviews/PCN-0455-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-INTEGRAL-TIME-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0456-CALLER-EXPLICIT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-CLOCK-ORIGIN-PROJECTION.md`
- `docs/reviews/PCN-0457-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-CLOCK-ORIGIN-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0458-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-DEADLINE-PROJECTION.md`
- `docs/reviews/PCN-0459-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-DEADLINE-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0460-CALLER-EXPLICIT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-DEADLINE-OBSERVATION.md`
- `docs/reviews/PCN-0461-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-DEADLINE-OBSERVATION-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0462-CALLER-EXPLICIT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-DEADLINE-ELIGIBILITY.md`
- `docs/reviews/PCN-0463-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-DEADLINE-ELIGIBILITY-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0464-CALLER-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-ELIGIBLE-SELECTION.md`
- `docs/reviews/PCN-0465-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-ELIGIBLE-SELECTION-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0466-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-REQUEST-PROJECTION.md`
- `docs/reviews/PCN-0467-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-REQUEST-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0468-CALLER-EXPLICIT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-CONSUMER-IDENTITY.md`
- `docs/reviews/PCN-0469-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-CONSUMER-BINDING.md`
- `docs/reviews/PCN-0470-CALLER-EXPLICIT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-CONSUMER-BATCH-IDENTITY.md`
- `docs/reviews/PCN-0471-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-CONSUMER-BATCH-PROJECTION.md`
- `docs/reviews/PCN-0472-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-INTENT-PROJECTION.md`
- `docs/reviews/PCN-0473-CALLER-EXPLICIT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-CLASSIFICATION.md`
- `docs/reviews/PCN-0474-CALLER-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-SELECTION.md`
- `docs/reviews/PCN-0475-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-PROJECTION.md`
- `docs/reviews/PCN-0476-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-SOURCE-PROJECTION.md`
- `docs/reviews/PCN-0477-DECLARATIVE-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-SUMMARY.md`
- `docs/reviews/PCN-0478-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-SEGMENTS.md`
- `docs/reviews/PCN-0479-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-SEGMENT-CURSOR.md`
- `docs/reviews/PCN-0480-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-SEGMENT-CURSOR-STEP.md`
- `docs/reviews/PCN-0481-BOUNDED-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-SEGMENT-TRAVERSAL.md`
- `docs/reviews/PCN-0482-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-SEGMENT-COMPLETION.md`
- `docs/reviews/PCN-0483-EXACT-ISO-BMFF-FRAGMENT-SAMPLE-MERGE-TRAVERSAL-SUBMISSION-MANIFEST-COMPLETED-SEGMENT-VIEW.md`

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
