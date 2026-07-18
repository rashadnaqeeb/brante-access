# Decisions Log

Judgment calls made during the autonomous build, newest first. Convention: follow the
reference mods (wotr-access, Non-Visual Calculus) unless Brante gives a reason not to;
every deviation gets an entry here with the reason. The user reviews this file, not a
stream of questions.

## New-game flow judgment calls (2026-07-18, Phase 3)

- **Cutscene Update bodies are UN-patched** (CutsceneIntro, ChapterCutscene removed from
  FocusModePatches, 14 -> 12 methods): their key reads ARE the accessible skip path - a blind
  player pressing any key to skip is exactly the intended interaction. Suppressing them while a
  cutscene screen held focus trapped the player in an unskippable scene. CutsceneScreen claims
  NO input category so every key reaches the game untouched; the mod only announces what is
  playing and how to skip.
- **Text entry is modal** (NameRequestScreen): activating the name field hands the keyboard to
  the game's TMP_InputField (CapturesRawInput; InputManager stands down entirely), the mod
  echoes typed characters by diffing the field's own text (model state, never our key events),
  Enter/Escape end the mode via the field's own submit/cancel. Mirrors wotr-access's key-capture
  pattern.
- **The dev /input route mirrors physical-press gating**: Dispatch now stands down during raw
  input capture AND refuses actions whose category no screen claims ("not dispatched: category
  UI inactive"). Found live: during the cutscene, /input ui.down dispatched even though a
  physical DownArrow would have done nothing - the driver must never be able to do what a key
  cannot, or verification lies.
- **Explicit start beats the selected-member landing heuristic** (GraphBuilder.SetStart ->
  GraphRender.ExplicitStart): the name-request form is one stop whose radios have a selected
  member, so the wotr landing rule (remembered -> selected -> first) landed initial focus on a
  checked radio mid-form instead of the subtitle at the top. A screen that declares a start
  means it; the selected-member rule still applies to stops without one.
- **Start button availability is read from the model** (name length >= 2), not
  Button.interactable: the game dims the button both for short names AND as a double-click
  guard after Start fires, so interactable alone announced a misleading "unavailable" during
  the transition. The failed-requirement line is mod-authored (namerequest.start_unavailable) -
  the game's only signal is the dimmed sprite, no string exists.
- **Main menu screen gates on PREGAME** (GameUi.State): RUNNING flips inside SetCharacterName
  BEFORE the menu scene unloads, so without the gate the closing name popup refocused the menu
  and spoke stale lines over the scene transition.
- **"Chapter start" defers to its dedicated Phase 5 item**: the ChapterStartWindowController
  first exists when Chapter 1 begins, which requires playing through the prologue - unreachable
  until the Phase 4 event screen lands. The Phase 3 new-game item covers the flow up to the
  first playable scene (name -> disclaimer -> intro cutscene -> Intro event loads).

## Main menu screen judgment calls (2026-07-18, Phase 3)

- **Discord URL buttons are in the graph** (stop 2, game's own TMP labels): the "no unspoken
  interactive element" bar includes them. Their activation (opens a browser) is deliberately NOT
  exercised by the sweep - a browser launch on an unattended run is noise; the click path is the
  same UiWidgets.Click verified on Settings.
- **ExtraButtons is scene-scoped to "MainMenu"**: FindObjectsOfType sees additively loaded
  scenes (Settings/LoadWindow), and their same-named buttons collided as duplicate control ids -
  found by the regression sweep on its second run, not by eye.
- **The roadmap's "language selection" on the main menu does not exist** - the language dropdown
  lives in the Settings scene (verified live). It is covered by the Settings window item.
- **Sweep error check counts only lines after the run's start logCursor**: the ring buffer keeps
  old (already fixed) errors for the whole session; counting from 0 made every later run red on
  history instead of regressions.

## Navigator port judgment calls (2026-07-18, Phase 2)

- **Regression sweep is scripts/sweep.sh (bash), not the roadmap's sweep.ps1**: the agent's
  shell tool cannot invoke powershell.exe (auto-mode classifier, CLAUDE.md gotcha), and a sweep
  the agent can't run itself is dead weight in an autonomous loop. curl + string asserts need
  nothing PowerShell offers.
- **Missing-locale warnings log once per key per generation** (and reset on language swap):
  announcement parts resolve every frame, so the unthrottled version flooded thousands of
  identical lines in seconds (seen live when a key shipped before its lang file deployed).

- **Main-menu button labels are mod-authored** (`mainmenu.*` in ui.txt): the game renders those
  buttons as per-language SPRITES (LocalizationResources.ContinueSprite etc.) - no game string
  exists to reuse, so authoring is the rule-sanctioned fallback. If a matching I2 term surfaces
  later (the pause menu may share wording), swap to it and drop the keys.
- **Main-menu Build() covers the five CustomMainMenuButton entries only.** The scene's extra
  wishlist/Discord/publisher URL buttons are plain Buttons without that component; they join in
  the Phase 3 main-menu item (the "no unspoken interactive element" bar applies there). A button
  whose enum value the mapping doesn't know announces its GameObject name - audible, so the gap
  is discoverable, never silent.
- **Ported empty `catch {}` blocks became logged catches** (Mod.Warn) - wotr tolerated silent
  probe failures; CLAUDE.md's no-silent-failures rule is binding here.
- **lang/ deploys with MODULE builds too** (both csprojs copy it): the module re-reads lang on
  every reload, and a lang edit + /reload must not require a host build (found live: new keys
  spoke as raw key names until the deployed copy caught up).
- **Enter on an unavailable (non-interactable) button speaks "unavailable" and does NOT invoke
  onClick** - Button.onClick.Invoke() ignores interactable, so the gate is ours.

## Framework judgment calls (2026-07-18, Phase 2)

- **Localization layer built with the input manager** (roadmap items 1 and 4 landed together):
  the focus-mode announcement and binding display labels are user-facing strings, and the
  no-inline-strings rule means the Loc table had to exist before the first announcement.
- **Mod strings are flat `key = value` text files** under `lang/<code>/<table>.txt` - the game
  ships no JSON library and pulling one in for two-column data is not worth it. English is
  always loaded as the fallback layer; language follows I2's live CurrentLanguageCode via a
  per-frame poll (verified live with an en->ru->en swap).
- **Input categories trimmed to Global + UI** (wotr has seven): Brante is a menu-driven
  narrative game - no world cursor, no exploration layer. New categories get added only when a
  screen genuinely needs a different key layer (likely candidate: name-entry text capture).
- **Game-input suppression = Harmony prefix (`!FocusMode.Active`) on the 14 input-only game
  Update bodies** (complete survey of every Input.GetKeyDown site in Assembly-CSharp). Brante
  has no KeyboardAccess-style lever to hold, so the flag+prefix IS the lever. Deliberately not
  patched: NameRequestWindow.Update (drives button interactable per frame - Phase 3 handles it
  with text entry), GameManager/Console dev-console toggles (dev-gated, harmless). Focus mode
  defaults ON (the mod's whole audience runs focused); F10 toggles it for a sighted co-pilot.
- **/input dispatches by action key** (e.g. `focusmode`) through InputManager.Dispatch - the
  same routing a real press takes minus the physical key poll. Physical-key behavior (F10,
  held-key typematic repeat) is verified in the Phase 8 keyboard-only playthrough instead.
- **Main-menu sub-surfaces are additive scene loads, not window slots** (discovered live):
  MainMenuController loads "Settings" and "LoadWindow" via SceneManager additive loads and
  Credits via a full scene swap - UIManager.OpenedWindow stays null. Screens for these poll
  `SceneManager.GetSceneByName(name).isLoaded` (GameUi.IsSceneLoaded). The generic
  window/popup/pause screens registered now are silent scaffolding proving the stack against
  the real UIManager slots; Phase 3 replaces them with per-surface speaking screens.
- **Screen names resolve at speech time** (Message, not a construction-time string) so a
  mid-session language change reaches screen announcements too.

## Bring-up judgment calls (2026-07-18, Phase 1)

- **Unique module AssemblyName per Debug build** (`BranteAccess.Module.g<utc-stamp>`), because
  this Mono dedupes byte-loads by simple assembly name and a fixed name made hot reload silently
  re-run OLD code (bumping version/MVID did not help - proven live). Deployed file name stays
  fixed; the REPL references only the newest generation. Release builds keep the plain name.
- **/screenshot returns a png file path**, not image bytes - the dev server is a raw-TCP
  text-only responder (port of wotr's), and the driver reads local files anyway.
- **Plugin.Instance and Speech are public**, not internal: /eval-compiled code lives in its own
  assembly and needs `Plugin.Instance.Speech.Speak(...)` for smoke tests. Loader stays internal.
- **Unattended mute = `AudioListener.volume = 0` from /eval**: global, session-only, does not
  touch the user's saved audio settings; SoundManager only manages music-source fades.
- **/nav, /type added beside /input** (DescribeNav / TypeText from Core's IDevDriver), matching
  the Disco endpoint intent; the module is probed by cast per request so the newest generation
  always answers.

## Locked with the user (mid-run, 2026-07-18)

- **Hot-reload host/module split, from the start** (user instruction; supersedes the seed
  "no split" decision below). Project is split like Non-Visual Calculus so feature code
  hot-reloads without restarting the game: permanent **Host** (BepInEx plugin: speech native
  handles, dev server, module loader, per-frame pump) + **Core** (contracts only, stable type
  identity across the boundary) + reloadable **Module** (all feature/screen/input code -
  day-to-day work goes here). Mono has no assembly unloading, so each reload Assembly.Loads
  fresh bytes (the dll on disk is never locked - read, not loaded from path), swaps in only on
  successful Load, and disposes the old module (per-load unique Harmony id + UnpatchSelf).
  Old module generations leak until process exit - acceptable, dev-only. Host/Core changes
  still need a full game restart; the reload reports its generation so staleness is detectable.

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
- ~~No hot-reload host/module split for now~~ SUPERSEDED: the user directed the split be
  adopted from the start (see "Locked with the user (mid-run)" above).
- **Game content text is never re-authored**: passage/choice text comes from the game's I2
  table at its own keys; mod-authored strings live in the mod's enGB localization manifest.
