# PCN-0263: Bounded ISO BMFF Program Config Channel Topology

## Scope

Review exact channel-count projection and element-identity validation from one
parsed Program Config Element.

## Findings

- SCE, CPE, and LFE contributions are explicit and finite.
- Front, side, back, core, and total counts retain their distinctions.
- Duplicate identities fail by element namespace before projection succeeds.
- No speaker position, decoder, native operation, or physical execution occurs.

## Decision

Accept bounded PCE channel topology. Speaker mapping and decoder composition
remain incomplete.
