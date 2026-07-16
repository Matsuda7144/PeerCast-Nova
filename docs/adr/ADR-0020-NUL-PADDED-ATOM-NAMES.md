# ADR-0020: NUL-Padded PCP Atom Names

- Status: Accepted
- Decision: PCP atom names accept one to four ASCII characters and are NUL-padded to four wire bytes.
- Reason: Legacy control names include `sid`, `ver`, and `ok`.
- Consequence: Equality uses all four wire bytes; display text strips trailing NUL bytes.
