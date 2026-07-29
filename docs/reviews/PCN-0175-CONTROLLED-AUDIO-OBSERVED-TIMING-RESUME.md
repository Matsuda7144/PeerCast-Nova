# PCN-0175: Controlled audio observed timing resume review

## Scope

Review the non-physical correction derived from the consumed
`e0809e9b711d5bb5101130a26ca19944a94a0d31` run. No readiness or physical
command is part of this review.

## Retained run

- entrypoint exit: `1`;
- result: `DeliveryRejected / None`;
- decode / outer delivery / delivered: `4 / 3 / 2`;
- native write attempts / acknowledgements: `2 / 2`;
- accepted frames / bytes: `2048 / 16384`;
- third sequence: `Wait` for approximately `6 ms`, then `AudioLate`;
- completed record: absent;
- terminal record: present, SHA-256
  `859651D2DED3417F4F55BBAD1E210D1B48AC9E274681D669D6CF1C516E851C78`;
- operator observation: recognizable beep, `Audio audible: Yes`.

## Static diagnosis

The controlled wrapper ignored the timing boundary's requested wait and slept
one fixed `21.3334 ms` AAC cadence. Scheduler and Windows timer overhead could
therefore turn a short early wait into lateness beyond the default `20 ms`
tolerance. The native writer had already acknowledged the first two frames and
did not reject the third; the third never reached a native write.

The trace writer appended both the retained wait and its resumed result. Since
the completed codec requires four unique delivered sequences in a fixed
four-slot trace, a normal timing resume could also make otherwise successful
evidence overflow or contain duplicate sequences.

## Reviewed correction

- only the opt-in controlled evidence path reads the latest bounded delivery
  observation;
- an exact positive `Wait` duration not exceeding the fixed cadence becomes the
  cancellation-aware delay;
- every untrusted value falls back to the existing cadence;
- later observations for the same sequence replace that sequence's prior state;
- inconsistent same-sequence identity fails closed as overflow;
- four different audio sequences and the fifth-sequence overflow bound remain
  unchanged.

## Preserved behavior

- production defaults and timing tolerances;
- two timing resumes per outer delivery;
- four required outer audio deliveries;
- fixture, decode, scheduler reservation, buffer, endpoint, WASAPI writes, and
  cleanup;
- no resume for WouldBlock or any result other than `RetainedWait`;
- no readiness, authorization, or record schema change.

## Verification

- focused Playback trace tests;
- focused CLI controlled timing tests;
- Release solution build;
- full solution tests;
- format verification;
- final diff inspection;
- final-head and post-merge CircleCI before any later authorization decision.
