# PCN-0236: ISO BMFF Edited-sample Packet Candidate

## Scope

Review the codec-neutral aggregate of owned sample bytes, exact timing, and edit
appearances.

## Findings

- Owned payload and parsed media timing are mandatory.
- Presentation end must equal start plus the retained sample duration.
- At least one complete edit appearance remains required.
- Media timescale stays unsigned and unrounded.
- No codec, stream, keyframe, or demux-event policy is inferred.
- No decoder or native operation occurs.

## Decision

Accept the edited-sample packet candidate. Sample-description and codec-specific
demux-event projection remain incomplete.
