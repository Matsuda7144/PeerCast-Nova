# Captain's Log #006

Genesis GUI Next now opens the channel context menu on the first right-click in a
physical Windows run, including immediately after startup before any left-click
selection.

The failure was not row identity, hit-testing, focus, or foreground ownership. The
popup loop was being entered too early from the `WM_RBUTTONUP` processing stack,
which could leave a real Windows popup active but invisible. The preferred native
route now follows the standard `WM_CONTEXTMENU` boundary instead.

Rendered-row identity, offline-favorite safety, and the keyboard context-menu path
remain separate contracts and continue to be regression-tested.

> Native behavior is proven at the message boundary, not inferred from a green test.
