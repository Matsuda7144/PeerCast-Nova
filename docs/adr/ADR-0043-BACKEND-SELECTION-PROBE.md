# ADR-0043 Backend Selection Probe

## Status

Accepted and verified in 0.0.5g.

## Decision

The existing controlled decoder lifecycle probe now registers its counter-only
decoder as `IPlaybackDecoderBackend` and reaches it exclusively through
`PlaybackDecoderBackendFactory`. The probe backend declares all currently
recognized containers and remains uninitialized until classification selects it.

No decoded media is produced and no real codec is loaded. Existing content-free
lifecycle counters remain the acceptance evidence.
