# Captain's Log #005

The first compatibility probe is ready.

It knows how to knock on the legacy HTTP door, identify itself with HELO, read
OLEH, and distinguish OK from a relay-full QUIT.

We also corrected a dangerous early assumption: not every atom name fills all
four bytes.

> Compatibility begins by respecting the exact bytes already at sea.
