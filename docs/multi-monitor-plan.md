# Multi-monitor implementation plan / 0.3.0

1. Extend the pure Core with named display work areas in physical pixels and per-display scale. Keep the one-display API for existing tests. Spawn/spread across active displays; roaming and yielding stay inside the assigned display, never in virtual-desktop gaps. Dragging transfers between displays using the pointer and a normalized grab anchor.
2. Preserve DIP size and speed preferences, converting them per actor. Fit oversized pets inside unusually small work areas. Preserve display identity and local position on rearrangement; recover pets to the nearest surviving display on disconnect. Default to all displays, with a persisted primary-only option.
3. Declare PerMonitorV2, enumerate physical monitor work areas, and position non-activating HWNDs in physical pixels. WPF owns rendering DPI. Reset the mouse gesture at display transitions and normalize samples by local scale. Re-enumerate on display/work-area/DPI changes, with a slow fallback check.
4. Add display count, all-displays switch, spread and gather-to-current-display actions. Keep settings/tray recoverable on topology changes. Do not save monitor-specific positions or change the total pet count.
5. Independently test negative origins, vertical/L-shaped/gapped arrangements, mixed scales, long-running roaming/yielding, dragging, primary-only mode, resizing and unplugging. Run existing Core tests and packaged native-window smoke checks. Record actual detected displays and distinguish hardware checks from simulated topology tests.
6. Update Japanese usage docs, verification evidence and persistent context; package version 0.3.0 and publish a new GitHub release under the existing publication authorization.

Automatic walking stays within each display. Users transfer a pet by dragging, or redistribute all pets with the spread button. This avoids pets disappearing across empty regions between screens.

References: [Microsoft WPF DPI sample](https://github.com/microsoft/WPF-Samples/blob/main/PerMonitorDPI/readme.md), [GetDpiForWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow), [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos).

User follow-up adds a held-pet animation. Reuse existing Curious expressions with procedural floating/swaying and movement-dependent tilt; validate clocks, release reaction and containment without regenerating artwork.
