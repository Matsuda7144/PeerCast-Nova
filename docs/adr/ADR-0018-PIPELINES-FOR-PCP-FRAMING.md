# ADR-0018: System.IO.Pipelines for PCP Framing

- Status: Accepted
- Decision: Use `System.IO.Pipelines` for incremental PCP input.
- Decision: Keep the atom decoder independent from TCP and expose a PipeReader pump.
- Decision: Leaf payload memory is callback-scoped unless explicitly copied.
- Reason: PCP messages can cross read boundaries, and a single read can contain multiple messages.
- Reason: PipeReader provides segmented buffers and explicit consumed/examined positions.
- Consequence: The reader loop must obey strict AdvanceTo and completion rules.
