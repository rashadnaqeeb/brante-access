# WrathAccess — Accessibility Mod for Pathfinder: Wrath of the Righteous

Screen-reader accessibility mod for blind players. Speaks UI focus, menus,
dialogue, and (later) turn-based combat via Prism (Tolk is retired). Sibling
project to SayTheSpire / SayTheSpire2; reuse those patterns where they fit.

## Game facts
- **Engine**: Unity **2020.3.48f1**, **Mono** scripting backend (not IL2CPP) →
  `Assembly-CSharp.dll` is fully decompilable and Harmony-patchable.
- **Install**: `C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Second Adventure`
- **Managed dir** (reference assemblies): `<Game>\Wrath_Data\Managed`
  - `Assembly-CSharp.dll` — game code, `Kingmaker.*` namespace.
  - `Owlcat.Runtime.UI.dll` — UI controls + the **console navigation / focus** system.
  - `0Harmony.dll` ships with the game.
- **Modding framework**: the game's **native mod system** (`Kingmaker.Modding`,
  "Owlcat modifications") — NO Unity Mod Manager. Mods live in
  `%LocalLow%\Owlcat Games\Pathfinder Wrath Of The Righteous\Modifications\<Name>\`
  and must be listed in `EnabledModifications` in
  `<LocalLow root>\OwlcatModificationManagerSettings.json`. Install = pure file
  copies + one JSON edit (the point: UMM's installer is inaccessible).
  - Layout: `OwlcatModificationManifest.json` + `OwlcatModificationSettings.json`
    (**required even when empty `{}`** — the loader throws if missing, killing the
    mod before its entry point) + `Assemblies\` (managed dlls ONLY — every file in
    there is `Assembly.LoadFrom`'d) + `assets\` at the root (resolved via
    `Main.ModDir`) + empty `Bundles\` + `Blueprints\` (loader throws if absent).
  - Entry: `[OwlcatModificationEnterPoint]` static method, invoked during boot
    (`GameStarter`, before the main menu) with our `OwlcatModification`.
  - No native per-frame hook → our own `Ticker` MonoBehaviour drives the loops,
    pinned `[DefaultExecutionOrder(-10000)]` (before all game scripts; matches the
    frame position UMM's dispatcher had implicitly).
  - Native mods set `IsAnyModActive` → Steam achievements are disabled (UMM was
    invisible to the game). Accepted; may revisit with a patch later.
- **Target framework**: `net48`. Needs the .NET Framework 4.8 targeting pack to build.
- **Harmony**: the game's own bundled `0Harmony.dll` (**2.0.4**) from `<Managed>` —
  the only Harmony in the process. Keep patches within 2.0.x API.

## Decompiled reference (not in this repo)
- `../wotr-decompiled/Assembly-CSharp-full/` — **COMPLETE** (~10,070 types). Game
  code, `Kingmaker.*`. This is the one to use.
- `../wotr-decompiled/Owlcat.Runtime.UI/` — decompiled cleanly, the UI/navigation
  layer (console-nav, MVVM base, controls). Where focus/control logic lives.
- `../wotr-decompiled/Owlcat.Runtime.Core/` — decompiled cleanly.
- `../wotr-decompiled/Assembly-CSharp/` — old PARTIAL ilspycmd dump (~1038 files);
  superseded by `-full`, kept only as a fallback.

### Re-decompiling after a game update
`ilspycmd` (and any naive ICSharpCode run) **stack-overflows** on ~100 of Owlcat's
heavily-generic types — an *infinite* conversion-resolution cycle, uncatchable, so it
kills the process and truncates output. Use the crash-isolating wrapper at
`../wotr-decompiled/_decompiler/` (run `run.sh`): it records progress before each type,
and when the process dies it restarts past the offending index, skipping ~1% and
capturing the rest. ~100 restarts is normal.

## Build & deploy
```
dotnet build
```
Debug build compiles `WrathAccess.dll` and deploys the full native-mod layout to
`%LocalLow%\...\Modifications\WrathAccess\` (manifest + settings json + dlls
under `Assemblies\` + `assets\`), and the native speech dll (`prism.dll`) next to
`Wrath.exe` (**game must be closed** or that copy fails; `dotnet build -c Release`
compiles without deploying). Then restart the game. The mod must be enabled once
in `OwlcatModificationManagerSettings.json` (already done on this machine).

**Alpha distribution = git pull + `deploy.ps1`** (pure copy, no SDK for testers; see
`deploy.ps1` / `scripts/stage.ps1`). The committed Release payload lives in
`deploy/Assemblies/`. A **pre-commit hook** (`.githooks/pre-commit`) re-runs `stage.ps1`
and adds the refreshed dll whenever C# source is staged, so the payload never goes stale.
Enable it once per clone: `git config core.hooksPath .githooks` (bypass with `--no-verify`).

## Logs
Our lines go through `Main.Log` (our `ModLogger`) → Unity's debug log with a
`[WrathAccess]` prefix → **Player.log**
(`%LocalLow%\Owlcat Games\Pathfinder Wrath Of The Righteous\Player.log`).
The game's MOD LOADER logs to the **GameLog**, not Player.log — read
`<LocalLow root>\GameLogFull.txt` (grep `Mods]`) to diagnose load failures
("Apply modification", "Load assembly", "Enter point not found", exceptions).

## Navigation strategy (decided)
**Custom keyboard navigation in Mouse mode** — NOT the gamepad/console-nav system.
Console nav works but is too coarse (linearizes complex screens, no typeahead/panel
jumps) and reshapes the whole UI. Instead we build our own nav over the live
**View/VM tree** (`ViewBase<TVM>` + `GetViewModel()`), read/activate via the
`OwlcatSelectable`/`IConsoleEntity` control family, and scope to the active screen via
`RootUIContext`. We suppress the game's own keybindings with
`Game.Instance.Keyboard.Disabled` while our focus mode owns the keyboard.

## Architecture (current — input substrate)
- `src/Main.cs` — native-mod entry (`[OwlcatModificationEnterPoint] Load`). Boots
  speech, Harmony, registers input; the `Ticker` MonoBehaviour drives the per-frame
  loops; `Enabled` master switch (wired to the game's mod enable/disable).
- `src/Tts.cs` — speech facade over `src/Speech/` (Prism primary, manual-COM SAPI,
  clipboard fallback). Never interrupts by default (SayTheSpire preference).
- `src/Input/` — ported SayTheSpire2 input framework, Unity-backed:
  `InputManager` (registry + per-frame poll), `InputAction`, `InputBinding` +
  `KeyboardBinding` (Unity `KeyCode` polling; needs `UnityEngine.InputLegacyModule`).
- `src/FocusMode.cs` — holds `KeyboardAccess.Disabled.Scope()` to mute game hotkeys.
- Current bindings are proof-of-life test hotkeys (Ctrl+Shift+A toggle focus mode,
  Ctrl+Shift+T speak test) — to be replaced by the real nav action set.

## Hard rules
- **Localize every string the mod speaks or displays** — no hardcoded English in
  `Speak` calls or labels, ever. Speech/UI text: `Loc.T(key[, args])` (or
  `Message.Localized`) + an entry in `assets/locale/enGB/ui.json`. Settings labels:
  pass a `localizationKey` + an entry in `assets/locale/enGB/settings.json`. The
  enGB files are the complete translation manifest; other languages are just a
  dropped-in folder. Sole exception: debug-only tooling (Player.log dumps, dev
  hotkeys). Game content (names, log lines, tooltips) is already localized by the
  game — pass it through, never re-translate.

## Roadmap
1. **(done)** Loader + TTS + UMM doorstop install + full decompile.
2. **(in progress)** Input substrate: InputManager + FocusMode suppression.
3. Output: port the `Message`/localization layer.
4. Active-screen resolver (`RootUIContext`) + screen base.
5. Generic control adapter (`OwlcatSelectable` family: read label/state, activate, hover-for-tooltip).
6. Custom nav model (tab/shift-tab between groups, arrows, typeahead) over the View/VM tree.
7. First screen: main-menu sidebar (`ContextMenuEntityVM` list) → New Game wizard.
8. Later: character creation (level-up UI), combat (model-driven), exploration scan.
