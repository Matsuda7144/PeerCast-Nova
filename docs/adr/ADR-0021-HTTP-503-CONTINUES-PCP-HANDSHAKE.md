# ADR-0021: HTTP 503 Continues PCP Negotiation

- Status: Accepted
- Decision: Treat HTTP 200 and 503 as valid entry responses for the PCP control exchange.
- Reason: A relay-full peer can still exchange HELO/OLEH, send alternative HOST atoms, and terminate with QUIT.
- Consequence: HTTP status alone does not determine the final compatibility outcome.
