# nmoonfox Desktop

- Independent Windows desktop companion application; respond in Japanese. This repository is separate from the nmoonfox artwork repository and ChatGPT Pets registration.
- .NET 10 / WPF; pure simulation and persisted settings live in Nmoonfox.Core. No third-party runtime dependencies or network features.
- Canonical name is lowercase nmoonfox. Preserve original sprite pixels, 192x208 cells, nine state rows plus sixteen look directions. Row 4 is grounded petting, never a jump.
- Transparent, non-activating pet windows must not steal typing focus. Empty pixels and optional click-through mode pass input to underlying apps. Settings/tray remain available to recover from zero pets or click-through.
- Cap pets at 12. Clamp positions to primary display work area. Persist count/size/speed/topmost/roaming/click-through, but not transient pause/sleep state. Never enable autostart without a request.
- Use seeded deterministic Core tests for behavior and independent agents for complex verification. GUI smoke checks must use an isolated settings directory and terminate cleanly.
- Keep docs/design.md, docs/verification.md and README current. Build/publish before handing off executable; never claim real-desktop interactions passed when only simulated.

## Current status (2026-10-23)

- Version 0.2.0 adds default-on persisted YieldToMouse. Fast mouse reversals make nearby awake pets retreat for 6 seconds with temporary click-through. Mouse-button presses, settings interaction, pause, sleep and dragging suppress the feature. Independent Core tests: 19/19; GUI smoke expanded to 18 checks. Real mouse threshold feel remains for user testing.
- Shake detector uses monotonic DIP samples with short turning-point pauses allowed. Never replace this with speed-only detection; ordinary pointer travel must not trigger it. At boxed-in corners, choose a path with good cursor clearance when monotonic retreat is impossible.

- Version 0.1.0 implemented; standalone win-x64 executable and ZIP in artifacts/nmoonfox-desktop-win-x64 and artifacts/nmoonfox-desktop-win-x64.zip. No remote repository has been created for this app.
- Core independent tests: 13/13 pass. Packaged EXE isolated GUI smoke: 14 checks pass, exit 0. Release verification hashes in artifacts/release-verification.json.
- Current GUI renders inspected under artifacts/release-smoke. Capture a pet window by matching PetId to actor.Id; Dictionary enumeration order can differ from World.Pets after removing and recreating windows.
- UI independent review gave interim findings, but reviewer later hit usage limit. Final revised UI has parent verification only; do not claim full independent GUI signoff. Human mouse input and multiple-DPI setups remain untested.
- Build script: scripts/build.ps1. Publish script downloads Microsoft runtime packages on first use; produces a compressed self-contained executable with bundled native library extraction. No artwork regeneration for this project.
