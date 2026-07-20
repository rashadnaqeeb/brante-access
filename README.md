# Brante Access

A screen-reader accessibility mod for The Life and Suffering of Sir Brante. The goal is that the entire game is playable by a blind player with speech and keyboard alone - no mouse, no sighted assistance.

## About this project

This mod was developed entirely by Claude (Anthropic's AI, via Claude Code) with almost no input from me. I set the goal and answered the occasional question; Claude did the reverse engineering, architecture, implementation, and verification, driving the live game over a local dev server and reading back its own speech output to confirm every feature. It was run as a test project to see how far autonomous AI development could go on real, unglamorous software.

## What it does

- Speaks every screen: story passages, choices (including unavailable choices with the reason they are locked), stats, character and family windows, chapter transitions, popups, tooltips, death and game-over screens.
- Full keyboard navigation everywhere the game only supported the mouse. See [KEYS.md](KEYS.md) for the complete key reference.
- Speech output through Prism (JAWS/NVDA), with SAPI and clipboard fallbacks.
- Uses the game's own localized text and its own click handlers, so game logic, sounds, and localization stay intact.

## Installing

Download the latest `BranteAccess-<version>.zip` from the [Releases page](https://github.com/rashadnaqeeb/brante-access/releases) and extract it straight into the game folder (the one containing `The Life and Suffering of Sir Brante.exe`). That is the whole install: the zip bundles BepInEx, the plugin, and the speech library. Full steps and the key reference are in the `BranteAccess-README.txt` and `BranteAccess-KEYS.txt` files inside the zip.

## How it is built

- The game runs on Unity 2018.3 with the Mono backend, which makes it patchable with [BepInEx 5](https://github.com/BepInEx/BepInEx) and Harmony.
- The mod is a BepInEx plugin split into a permanent host (speech backends, hot-reload loader, debug dev server) and a reloadable module containing all feature code, so new behavior can be loaded into the running game in about two seconds.
- Features prefer subscribing to the game's own event bus and reading live UI state over patching; announcements are keyed off the same signals the game's views use.
- In debug builds, a loopback HTTP server exposes eval, input, speech, and log endpoints. Every feature was verified by driving the real game through that server and checking the captured speech, not by inspection.

## Building

Requires the game (Steam or GOG) and the .NET SDK.

1. Clone the repo.
2. Run `setup-bepinex.ps1` once. It finds the game install (Steam and GOG are auto-detected; set the `BRANTE_GAME` environment variable if yours is somewhere unusual) and installs the vendored BepInEx into it.
3. Close the game if it is running, then run `build.ps1`. It builds the mod and deploys the plugin and the speech library into the game folder, and tells you if anything is missing.
4. Launch the game.

If Windows blocks the scripts, run them as `powershell -ExecutionPolicy Bypass -File .\setup-bepinex.ps1` (and the same for `build.ps1`).

To build the distributable zip instead, run `package.ps1`: it makes a Release build (dev server compiled out) and writes `artifacts\BranteAccess-<version>.zip`, laid out to extract straight into the game folder. It only reads the game install for reference assemblies and does not need BepInEx installed there.

`ROADMAP.md` tracks feature status and `DECISIONS.md` records the judgment calls made during development.

## License

MIT - see [LICENSE](LICENSE).

## Third-party components

Vendored under `third_party/` with their licenses: BepInEx (LGPL-2.1), Prism, and Mono.CSharp (MIT). Decompiled game code is used only as a local reference and is not part of this repository.
