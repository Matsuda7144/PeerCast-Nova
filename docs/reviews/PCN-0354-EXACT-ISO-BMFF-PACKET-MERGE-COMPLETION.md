# PCN-0354: Exact ISO BMFF Packet Merge Completion

## Decision

Accept an immutable packet-level completion certificate only when an exhausted
traversal source preserves exact role sequences and cumulative step count equals
the root initial remaining-packet total. Zero-step already-exhausted completion
is valid. No presentation-event completeness, delivery, native operation,
rendering, or physical execution is claimed.
