# ADR-0045 Decoder Backend Provenance Policy

## Status

Accepted and verified in 0.0.5i.

## Decision

Every decoder backend declares fixed provenance: first/third-party origin,
built-in/optional/bundled distribution, approved/pending/rejected license
review, and native-code presence. The default host policy permits only approved
first-party managed backends without native code.

Third-party, bundled, and native backends require separate explicit policy
flags. Pending or rejected review is unconditionally rejected. Policy is
evaluated before classification or backend initialization.
