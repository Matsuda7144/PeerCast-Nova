# PCN-0270: Bounded ISO BMFF AAC Decoder Extradata

## Scope

Review exact bounded ownership of AAC decoder configuration bytes.

## Findings

- Materialization follows only the retained AudioSpecificConfig source.
- Empty and oversized configurations fail before allocation.
- The emitted bytes are an exact privately owned copy.
- Format intent remains attached as provenance.
- No native allocation, decoder mutation, or physical execution occurs.

## Decision

Accept bounded AAC decoder extradata ownership. Native padding, capability
validation, and decoder binding remain incomplete.
