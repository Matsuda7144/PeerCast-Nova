# PCN-0280: Explicit ISO BMFF AAC Sample Entry Consistency Policy

## Scope

Review explicit admission decisions over ADR-0269 consistency facts.

## Findings

- Exact agreement is admitted without an override marker.
- Strict policy rejects any rate or channel mismatch.
- Effective AAC facts are used on mismatch only under the explicit allow policy.
- Missing facts and undefined policies produce no partial decision.
- Source facts are retained and no container value is rewritten.
- No decoder, native operation, or physical execution occurs.

## Decision

Accept explicit AAC sample-entry consistency policy resolution. Automatic
profile policy selection and decoder binding remain incomplete.
