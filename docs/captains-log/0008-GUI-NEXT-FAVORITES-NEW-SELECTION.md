# Captain's Log #008

Genesis GUI Next Favorites, New, and refresh-selection behavior were validated on a physical Windows run.

The native Favorite button now follows the actual selected row and favorite state: selecting a live channel enables `Add Favorite`, adding it changes the action to `Remove Favorite`, and removing it restores the add action. The New tab now represents newly active channels for the current successful refresh generation rather than retaining previously observed active channels indefinitely.

Manual and automatic Yellow Page refreshes also preserve the native selected channel when that channel remains visible. Selection restoration follows the channel's stable identity rather than its previous row index, so a refreshed channel can move to another row without losing selection. Selection is cleared only when the selected channel is no longer represented by the current visible projection.

> Refreshes may replace rows, but they must not replace the user's channel identity.
