# PCN-0252: Bounded ISO BMFF AVC NAL-unit Classification

## Scope

Review non-copying AVC NAL-header classification over the bounded framed sample.

## Findings

- Missing frame input is distinct.
- Forbidden-zero-bit and zero-type headers fail without partial output.
- Reference class and NAL type are projected directly from the first byte.
- VCL, IDR, and parameter-set facts are available per unit and in aggregate.
- Every classification retains the original framed NAL object.
- No RBSP, decoder, native operation, demux event, or physical execution occurs.

## Decision

Accept bounded AVC NAL-unit classification. Slice semantics and production
packet composition remain incomplete.
