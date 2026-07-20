# PCN-0120: Source-bound Opened Native Decoder Session Validation

## Scope

Validate the 0.0.9b finite composition from one live FLV parameter binding and distinct video/audio gate ownership into one opened H.264/AAC decoder session.

## Verified behavior

- exact source reference, stream IDs, and time bases are retained;
- H.264 create, transfer, and open complete before AAC begins;
- shared gates are rejected;
- binding and both gates are consumed on every result;
- partial native ownership is cleaned up and never returned as success;
- source disposal fails closed;
- success owns two contexts and disposes audio before video;
- source session is not owned by the native session;
- packet send, metadata writes, flush, drain, PCP composition, activation, and output remain unchanged.

## Tests

Six injected-operation tests cover successful ordering, video transfer failure, audio open failure, source disposal between streams, shared-gate rejection, and idempotent cleanup.

The first CircleCI build found CS9124 in a test fake. The primary-constructor capture was removed by using the initialized `CodecId` property. The corrected head passed restore, Release build, all tests, and format verification.

Status: Complete; final documentation head must pass CircleCI before merge.
