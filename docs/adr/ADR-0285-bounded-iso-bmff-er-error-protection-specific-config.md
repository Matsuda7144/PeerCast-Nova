# ADR-0285: Bounded ISO BMFF ER Error Protection Specific Config

## Status

Accepted as a post-0.0.10e non-physical codec-metadata slice.

## Decision

Compose the fixed prefix, complete bounded predefined-set aggregate, and header
protection into one ownership chain whose exact end is the complete
`ErrorProtectionSpecificConfig` boundary. Missing terminal input produces no
partial config.

## Non-goals

`directMapping`, decoder or native integration, and physical execution are not
included.
