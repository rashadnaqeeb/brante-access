# Decisions Log

Judgment calls made during the autonomous build, newest first. Convention: follow the
reference mods (wotr-access, Non-Visual Calculus) unless Brante gives a reason not to;
every deviation gets an entry here with the reason. The user reviews this file, not a
stream of questions.

## Locked with the user (scaffolding session, 2026-07-18)

- **Positional counts**: Wrath style - "n of m" spoken on list items by default, per-user
  setting to disable. Transcript lines never carry counts (both references agree there).
- **No per-window hotkeys.** The HUD is navigated like a sighted player uses it: one path in
  (Tab reaches the HUD bar), arrows along the buttons, Enter opens the window. No Ctrl+letter
  jump keys to memorize.
- **Saves**: no existing saves the user cares about - the run may freely create, load, and
  delete save slots.
- **Personal-only mod.** No installer, no player docs, no public release. The run ends at the
  verification sweeps plus a Release build profile. Keep code clean, but ship-phase work is cut.
- **No changelog file** - that convention exists in the reference mods because they ship
  releases; this project doesn't.

## Seed decisions (scaffolding session, 2026-07-18)

- **Mod name**: Brante Access (assembly/plugin `BranteAccess`). Rename is cheap until Phase 3;
  say the word.
- **Loader**: BepInEx 5 x64 Mono (game has no native mod system; matches Unity 2018.3/net48;
  installer-friendly file-copy install).
- **Framework**: port wotr-access's core (speech, input, screen stack, graph navigator,
  localization) rather than writing fresh; adapt the screen layer to uGUI/Messenger instead of
  MVVM. Dev server endpoint set from Non-Visual Calculus, implemented on the Mono stack
  (Mono.CSharp REPL, not Roslyn).
- **Dev server port**: 8772 (Disco uses 8771; distinct so both games can run tooling
  side by side).
- **Speech policy**: queued by default, interrupt only on focus moves under key repeat -
  carried from the reference mods.
- **Keybinding baseline** (rebindable later): arrows navigate / page, Tab and Shift+Tab between
  panels (the HUD bar is a Tab-stop - see the no-hotkeys decision above), Enter primary,
  Backspace secondary, Space tooltip/details, Escape back, typeahead by typing. The game's own
  A/D page keys stay working.
- **No hot-reload host/module split** for now (Disco's architecture): Brante restarts fast;
  plain restart loop until restart friction proves otherwise.
- **Game content text is never re-authored**: passage/choice text comes from the game's I2
  table at its own keys; mod-authored strings live in the mod's enGB localization manifest.
