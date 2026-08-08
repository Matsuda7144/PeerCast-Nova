# PCN-0274: Bounded ISO BMFF Program Config Channel Slot Map

## Scope

Review ordered channel-slot projection from one validated PCE topology.

## Findings

- Front, side, back, and LFE regions retain their PCE order.
- SCE, CPE first/second components, and LFE remain explicitly distinct.
- Every slot retains its region-local element index and element tag.
- Channel indices are contiguous and the final count must equal the topology.
- The map retains the exact topology and does not copy source configuration.
- No physical speaker position, platform mask, decoder, native operation, or
  physical execution is selected.

## Decision

Accept bounded PCE channel-slot mapping. Speaker-position policy and decoder
layout binding remain incomplete.
