# Captain's Log #004

PCP atoms can now arrive one byte at a time or several at once.

The decoder keeps incomplete atoms aboard, closes parent trees in order, rejects
truncated voyages at EOF, and records the exact byte where framing failed.

> TCP carries bytes. Framing gives them boundaries.
