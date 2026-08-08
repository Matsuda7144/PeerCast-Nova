# PCN-0277: Bounded ISO BMFF ER Audio Protection Requirement

## Scope

Review exact ER `epConfig` requirement projection for values zero through three.

## Findings

- Every value retains the exact GA source and post-`epConfig` bit position.
- Values zero and one require no additional protection structure.
- Values two and three explicitly require Error Protection Specific Config.
- No variable structure is skipped and ADR-0255 remains fail-closed for 2/3.
- Missing, non-ER, and truncated inputs produce no partial facts.
- No decoder, native operation, or physical execution occurs.

## Decision

Accept bounded ER protection requirement facts. Error Protection Specific Config
parsing and subsequent continuation remain incomplete.
