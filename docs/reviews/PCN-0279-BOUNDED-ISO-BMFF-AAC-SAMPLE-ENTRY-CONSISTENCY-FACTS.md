# PCN-0279: Bounded ISO BMFF AAC Sample Entry Consistency Facts

## Scope

Review source-bound comparison of `mp4a` declarations and effective AAC facts.

## Findings

- The sample entry and decoder-format intent retain exact source identity.
- Fixed-point integrality, frequency agreement, and channel agreement remain
  separate facts.
- Rate-only and channel-only mismatches remain distinguishable.
- No source value is rewritten and no mismatch is admitted or rejected.
- No speaker mask, decoder, native operation, or physical execution occurs.

## Decision

Accept bounded AAC sample-entry consistency facts. Mismatch policy and decoder
layout binding remain incomplete.
