# nmoonfox Desktop

- Independent Windows desktop companion application; respond in Japanese. This repository is separate from the nmoonfox artwork repository and ChatGPT Pets registration.
- .NET 10 / WPF; pure simulation and persisted settings live in Nmoonfox.Core. No third-party runtime dependencies or network features.
- Canonical name is lowercase nmoonfox. Preserve original sprite pixels, 192x208 cells, nine state rows plus sixteen look directions. Row 4 is grounded petting, never a jump.
- Transparent, non-activating pet windows must not steal typing focus. Empty pixels and optional click-through mode pass input to underlying apps. Settings/tray remain available to recover from zero pets or click-through.
- Cap total pets at 12 across displays. Core coordinates are physical virtual-desktop pixels; size/speed preferences remain DIPs. Each actor belongs to one named display and stays within its work area. Persist count/size/speed/display scope/topmost/roaming/click-through/yielding, but not transient pause/sleep/positions. Never enable autostart without a request.
- Use seeded deterministic Core tests for behavior and independent agents for complex verification. GUI smoke checks must use an isolated settings directory and terminate cleanly.
- Keep docs/design.md, docs/verification.md and README current. Build/publish before handing off executable; never claim real-desktop interactions passed when only simulated.
- Public repository: https://github.com/Noon-R/nmoonfox-desktop. Distribute the self-contained Windows ZIP through GitHub Releases, not Git history. Do not publish local settings, smoke logs, SDK caches, signed URLs or private pet IDs. Keep download links, release version, README and checksums synchronized. Do not add or change an artwork license without the owner's instruction.

## Current status (2026-10-23)

- Version 0.3.0 supports all-display distribution (default UseAllDisplays=true), primary-only mode, cross-display dragging, targeted gathering and spreading. Current EXE: artifacts/nmoonfox-desktop-0.3.0-win-x64/nmoonfox-desktop.exe; ZIP: artifacts/nmoonfox-desktop-win-x64.zip. Publish script now uses versioned folders because the old 0.2 EXE in the unversioned folder was running and locked; never terminate the user's running app just to overwrite it. PerMonitorV2, physical native HWND placement, per-display DPI via hidden GetDpiForWindow probe. Display events plus 2-second polling recover disconnected pets and keep settings reachable. Automatic walking/yielding remain within the assigned monitor; dragging is the explicit cross-display transfer.
- Dragged actors animate even while autonomous roaming is paused: existing Curious frames, bounded/smoothed DIP-normalized tilt, inset floating/swaying rendering. Release preserves grounded happy petting. Keep sprites unchanged and preserve ear/tail padding.
- Independent Core suite: 19 existing + 16 multi-display/drag cases pass (runner reports 20 groups including the nested suite). Independent Core/Desktop review completed without required fixes. Hardware smoke environment has one 1920x1032 work area at 100%; mixed-DPI physical interactions remain untested. See docs/verification.md for packaged checks.

- Version 0.2.0 adds default-on persisted YieldToMouse. Fast mouse reversals make nearby awake pets retreat for 6 seconds with temporary click-through. Mouse-button presses, settings interaction, pause, sleep and dragging suppress the feature. Independent Core tests: 19/19; GUI smoke expanded to 18 checks. Real mouse threshold feel remains for user testing.
- Shake detector uses monotonic DIP samples with short turning-point pauses allowed. Never replace this with speed-only detection; ordinary pointer travel must not trigger it. At boxed-in corners, choose a path with good cursor clearance when monotonic retreat is impossible.

- Standalone win-x64 executable and ZIP are in artifacts/nmoonfox-desktop-win-x64 and artifacts/nmoonfox-desktop-win-x64.zip. User authorized public GitHub distribution on 2026-10-23, starting with version 0.2.0.
- Core independent tests: 13/13 pass. Packaged EXE isolated GUI smoke: 14 checks pass, exit 0. Release verification hashes in artifacts/release-verification.json.
- Current GUI renders inspected under artifacts/release-smoke. Capture a pet window by matching PetId to actor.Id; Dictionary enumeration order can differ from World.Pets after removing and recreating windows.
- UI independent review gave interim findings, but reviewer later hit usage limit. Final revised UI has parent verification only; do not claim full independent GUI signoff. Human mouse input and multiple-DPI setups remain untested.
- Build script: scripts/build.ps1. Publish script downloads Microsoft runtime packages on first use; produces a compressed self-contained executable with bundled native library extraction. No artwork regeneration for this project.
