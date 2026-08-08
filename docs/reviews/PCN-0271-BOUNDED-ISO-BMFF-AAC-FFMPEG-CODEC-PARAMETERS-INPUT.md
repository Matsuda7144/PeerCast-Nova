# PCN-0271: Bounded ISO BMFF AAC FFmpeg Codec Parameters Input

## Scope

Review managed adaptation from owned ISO BMFF AAC facts to existing FFmpeg
codec-parameter input.

## Findings

- The adapter retains exact decoder-extradata provenance.
- Sample rate and channel count use the established factory limits.
- AAC input sets no video dimensions and preserves exact bytes.
- Existing padding/allocation logic is not duplicated or invoked.
- No native transfer, decoder open, or physical execution occurs.

## Decision

Accept bounded AAC codec-parameter input adaptation. Factory execution and
decoder binding remain incomplete.
