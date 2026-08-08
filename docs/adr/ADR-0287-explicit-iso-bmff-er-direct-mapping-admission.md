# ADR-0287: Explicit ISO BMFF ER Direct-Mapping Admission

## Status

Accepted as a post-0.0.10e non-physical policy slice.

## Decision

Define one explicit policy, `RequireEnabled`. A present true mapping is admitted;
false, missing input, and undefined policy values are rejected without producing
a decision. The decision retains the exact parsed mapping and selected policy.

## Non-goals

Implementation of the standard's unspecified false continuation, decoder or
native integration, and physical execution are not included.
