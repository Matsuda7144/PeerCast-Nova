# PCN-0352: Caller-Bounded ISO BMFF Cross-Track Merge Traversal

## Decision

Accept pure managed cross-track traversal under one caller-explicit equal-time
policy and a maximum of 262,144 steps. Exact exhaustion and caller truncation
remain distinct, and the immutable output retains exact initial/final cursor
state and validated step-chain provenance. No delivery, native operation,
rendering, or physical execution occurs.
