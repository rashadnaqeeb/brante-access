# Brante Access - Work Queue

The single source of truth for progress. Statuses: `todo` → `built` (code complete, compiles,
deployed) → `verified` (exercised against the LIVE game via the dev server, speech output
checked; one-line evidence note appended). Work top to bottom within a phase; phases mostly in
order, but a blocked item may be skipped with a note rather than stalling the run.

Rules of engagement: read CLAUDE.md first. After each verified item: update this file, commit.
After every ~4 verified items, run the standing regression sweep (Phase 8, R1). Judgment calls
go to DECISIONS.md, not to the user.

## Phase 0 - Scaffold

- [x] verified - Git repo, .gitignore, CLAUDE.md, ROADMAP.md, DECISIONS.md (this session)
- [x] verified - Decompiled reference at brante-decompiled/ (507 + 447 types, full bodies)

## Phase 1 - Bring-up (dev server before features)

- [x] verified - Install vendored BepInEx (third_party/bepinex, 5.4.23.5 win x64 Mono) into the
      game folder; game still boots clean with an empty plugin
      (LogOutput.log: "Chainloader startup complete"; later loads our plugin)
- [x] verified - Solution + three-project split ported from Non-Visual Calculus (user directive,
      see DECISIONS.md): permanent Host plugin + Core contracts + reloadable Module (net472),
      BepInEx logging with prefix, auto-deploy on Debug build; document layout in CLAUDE.md
      (all three build+deploy; /health: "module=generation 1"; log lines prefixed Brante Access)
- [x] verified - Per-frame pump (host MonoBehaviour) driving Module.Tick, master enable flag
      (/health frame counter advances; /eval jobs execute on main thread; Tick gated on Enabled)
- [x] verified - Speech pipeline ported from wotr-access: Prism primary, manual-COM SAPI fallback,
      clipboard last resort; Tts facade with rich-text strip + never-interrupt default;
      BRANTE_NO_SPEECH headless mode
      (prism acquired JAWS backend live and spoke; /speech captured it; SAPI/clipboard code
      ported but not exercised - prism loads first on this machine)
- [x] verified - Dev HTTP server on 127.0.0.1:8772: /health, /eval (Mono.CSharp REPL, persistent
      state), /speech ring buffer + cursor + wait
      (/eval "1+1" => 2; eval Speak returned "[speech] 0: dev server smoke test" in-band;
      /speech?since= cursors verified)
- [x] verified - Hot reload proven end to end: edit Module code while the game runs, dotnet build
      (module dll read from bytes, never file-locked), POST /reload (and F6), changed behavior
      observable with NO game restart; failed reload keeps the old module running
      (edited DescribeNav string, module-only build with game running, /reload -> new text live;
      corrupt dll reload kept generation + old module answering, failure spoken and logged;
      needed the unique-assembly-name fix, see DECISIONS.md and CLAUDE.md gotchas. F6 rides the
      identical ReloadModule path; not separately exercised - no keyboard injection yet)
- [x] verified - Dev server round 2: /input, /wait, /gui, /focus, /typeinfo, /log, /screenshot,
      eval speech-settle capture; runInBackground forced
      (all answered live: /wait true+timeout paths, /gui dumped canvases with TMP text, /typeinfo
      found SceneStateMachine, /log?grep= filters, /screenshot wrote png; whole session ran with
      the game window unfocused)
- [x] verified - Game launch/kill from CLI proven (record working commands + appid in CLAUDE.md);
      full loop demonstrated: build → launch → /health → /eval speaks → kill
      (steam.exe -applaunch 1272160 + taskkill used repeatedly this session, commands already in
      CLAUDE.md; full loop ran twice including a cold restart)
- [x] verified - Game audio mute mechanism for unattended runs (settings or SoundManager), recorded
      in CLAUDE.md (/eval AudioListener.volume=0 read back 0; session-only, never touches the
      user's real settings)

## Phase 2 - Core framework (ported from wotr-access, adapted to uGUI)

- [x] verified - Input manager: action registry, keyboard bindings, OS-matched key repeat,
      category priority; game-input suppression strategy decided and working (EventSystem +
      the game's own Update handlers - see TextController/A/D keys) - focus-mode toggle
      (/input focusmode dispatched through InputManager.Dispatch spoke "focus mode off"/"on"
      with interrupt; OS typematic read live: delay 0.5s, interval 0.033s; suppression = Harmony
      prefix on the 14 input-only game Update bodies, gate checked via /eval in BOTH focus
      states; module reload swapped all 14 patches to the new generation id with zero leftovers.
      Physical key presses (F10, held-key repeat) cannot be injected via the dev server - that
      slice rides the Phase 8 keyboard-only playthrough)
- [x] verified - Screen stack: ScreenManager poll-and-diff over UIManager.OpenedWindow /
      OpenedPopup / OpenedTooltip / SceneStateMachine + scene name; lifecycle, layers,
      child screens
      (mainmenu screen pushed and focused on first tick, "main menu" spoken; settings opened
      via the game's own MainMenuController.OpenSettingsWindow: settings(10) pushed above
      mainmenu(0), focused, "settings" spoken; closed via SettingsWindow.BackToMainMenuButton_Click:
      popped, mainmenu refocused and re-announced; PushChild/RemoveChild moved focus both ways
      with speech; live categories from the stack shown by /nav. Discovery: the main menu opens
      Settings/LoadWindow as ADDITIVE scenes, not UIManager slots - see DECISIONS.md)
- [x] verified - Graph navigator port: KeyGraph, GraphBuilder, GraphAnnouncer, ControlId two-tier
      reconciliation, Tab-stops, regions, typeahead search
      (full wotr port + GraphNavigator glue, Screen graph members, ui.* action set, seams wired;
      main menu got a real Build() over live CustomMainMenuButton components as the smoke surface.
      Live via /input + /speech: arrow moves spoke with interrupt ("New game, button, 2 of 5"),
      End/Home jumped with one announcement, down-at-edge consumed silently, Tab on a single stop
      consumed, Space spoke "no tooltip", Enter on unavailable Continue spoke "unavailable" without
      activating, Enter on Settings ran the game's own onClick (scene loaded, stack flipped,
      "settings" queued), closing Settings restored focus with "main menu" + "Settings, button,
      3 of 5", live watch spoke "unavailable" when the game flipped interactable under focus.
      Typeahead + physical key repeat read hardware input (Input.inputString/GetKey) and cannot be
      injected over HTTP - they ride the Phase 8 keyboard-only playthrough)
- [x] verified - Localization layer for mod strings (enGB manifest); pass-through of game strings
      via I2 LocalizationManager
      (done together with the input item - labels/announcements need it, see DECISIONS.md.
      lang/en tables loaded; Message.Localized("ui","focusmode.on") resolved live; I2
      pass-through translated a term from the game's 17552-term list; live language swap
      en->ru->en followed by the per-frame poll within 1s each way, and with no lang/ru dir the
      mod string fell back to English, not a raw key)
- [x] verified - uGUI adapter helpers: read TMP text under a node, invoke Button handlers, gate on
      CanvasGroup/interactable visibility (the visibility-gate primitives)
      (Game/UiWidgets: LabelText sweeps TMP then legacy Text, Visible walks the CanvasGroup
      alpha chain, Interactable = Selectable.IsInteractable, Click = ExecuteEvents pointer-click.
      Live via /eval: labels read "Back to Main Menu" off the Settings BackButton, Visible true
      on shown buttons / false on the hidden pause window, Interactable false only on saveless
      Continue, Click opened Settings through the game's pointer path with "settings" spoken.
      MainMenuScreen refactored onto the helpers; sweep re-run green 14/14 through them)

## Phase 3 - Main menu and meta UI

- [x] verified - Main menu screen (continue/new game/load/settings/credits/exit + language
      selection); first spoken screen
      (2026-07-18: 7-node graph over two Tab-stops - 5 sprite CustomMainMenuButtons with
      mod-authored labels + 2 Discord URL buttons with game-text labels, scene-scoped so
      additively loaded Settings/LoadWindow buttons never leak in (sweep caught the duplicate-id
      crash before scoping). Live: End "Quit, button, 5 of 5", Tab "Game's community on Discord,
      button, 1 of 2", Shift+Tab restored "Continue, button", Enter opened Settings through the
      game's click path. Ground truth: Continue IS the load entry (LoadGameButton_Click opens
      LoadWindow), and language selection lives in the Settings scene, not the menu - both
      covered by their own Phase 3 items. Continue's unavailable-when-saveless gate verified in
      the Phase 2 uGUI-adapter item; activation with saves rides the save/load item.
      sweep.sh green 16/16)
- [x] verified - New-game flow: disclaimer, intro, name request window (text entry), chapter start
      (2026-07-18: NameRequestScreen - subtitle, modal edit field ("Sir blank Brante, edit"),
      two radio pairs off the live toggles ("Enabled, radio button, selected"), Start gated on
      the model (name >= 2 letters) announcing the failed requirement; text entry captures raw
      input (InputManager stands down; dev route mirrors it) and echoes characters from the
      field's own text. DisclaimerScreen spoke all 3 pages on Enter. CutsceneScreen announced
      "cutscene, spoken narration, any key skips", claims no input category (ui.down refused
      "category UI inactive" while focusmode still fired), cutscene Updates un-patched so the
      game's own skip keys stay live - scene auto-advanced to Intro proving Update runs. Main
      menu drops from the stack the frame RUNNING flips: speech went Start -> disclaimer text
      with no stale menu refocus. Chapter start window first exists at Chapter 1, unreachable
      until the prologue is playable - rides its dedicated Phase 5 item, see DECISIONS.md)
- [x] verified - Settings window (sliders, toggles, language dropdown) - includes muting game audio
      by keyboard
      (2026-07-18: 9-row form graph in the game's visual order, captions/values all game text.
      Live: Music slider left/left/right spoke "40/30/40 percent" with the game's stored volume
      tracking (music max is 0.6 - percent is normalized slider position, matching the visual);
      Sound slid to "0 percent" = keyboard audio mute (GameSettings.SoundVolume 0) and back;
      VSync Enter toggled "on"/"off" with the setting flipping; Screen format spinner cycled
      Fullscreen -> Borderless Window -> Window and back via the game's arrow buttons, with the
      Resolution row's gate following live (locked: "unavailable, windowed screen format only";
      in Window mode the reason disappears and the dropdown adjusts); language spinner
      English -> Русский -> English with the mod locale following each way; Escape ran the game's
      BackToMainMenuButton_Click (SaveSettings + unload), mainmenu refocused. sweep.sh 18/18
      including a new Settings section driving slider + Escape-close through the game path)
- [x] verified - Save/load windows incl. chapter-select page, delete confirmation popup
      (2026-07-18: LoadWindow - slots as single rows (game slot text + date), Enter loads,
      Backspace = game's delete confirm popup (description start node, Delete/Cancel; Escape
      cancels; confirmed Delete removed the slot live, list refreshed "1 of 2" with silent
      re-home). Chapter select (GameLoadingScreen) - Continue start node folding the age text,
      5 chapter items with model lock state ("unavailable, not reached" spoken on Enter),
      Space reads the game's restart help, restart confirm popup (title "Rewind to Chapter
      Childhood", Escape cancels), Quit to Main Menu returns clean. Full resume chain live:
      Continue -> slot -> chapter select -> Continue -> Intro scene RUNNING with no stale
      menu chatter (menu gated on the game's Continue-disabled handoff flag). sweep.sh 24/24
      with new save/load section)
- [x] verified - Pause/Escape menu + exit confirmation popup
      (2026-07-18: PauseScreen off WindowPause's serialized refs - Music/Sound sliders (left
      spoke "40 percent", game volume applied), Hidden Consequences + Animated Illustrations
      toggles ("on"/"off" through the game's click handlers), Quit to Main Menu, Resume; window
      title is the game's own "Settings" text; language row and save/load buttons ship inactive
      and are skipped. Escape closes via the game's ShowPauseMenu toggle; Enter on Resume closes
      too. Exit confirm (shared ConfirmPopupScreen base with chapter restart) spoke "QUIT GAME"
      + autosave description, Escape cancelled, Quit returned to main menu PREGAME cleanly.
      Focus-mode suppression re-gated on ScreenManager.Current != null: with an empty stack the
      game's own keys (Escape to pause, A/D paging) work stock - verified by invoking the live
      Harmony prefix (False with mainmenu active, True in-game stack-empty). sweep.sh 28/28)
- [x] verified - Credits window (skippable)
      (CreditsScreen: 22 credit blocks as text rows in reading order (sibling order under the
      Credits container - world y is unlaid-out on the build frame and produced reversed rows,
      caught live), labels read live from scene TMPs, markup stripped at the speech boundary.
      Escape skips via the game's own paths (LoadMainMenu(); ItsGameOver keyboard branch
      mirrored for the ending variant - that variant pends live verification until an ending
      save exists). Verified live: fresh entry announces "credits" + block 1 "1 of 22", End
      reaches "22 of 22", Escape returns with "main menu" spoken; the roll also auto-returns
      by itself when finished (game-side) and the stack pops cleanly. sweep.sh 32/32)

## Phase 4 - The event scene (the heart of the game)

- [ ] todo - Scene screen: transcript of passage pages, spoken on delivery (SceneStateMachine +
      Messenger driven, once per new page), arrow re-read, silent re-home
- [ ] todo - Scene title + year/date announcements (ChangeSceneTitleEvent, DayTimePopup)
- [ ] todo - Speaker attribution: portrait character name prefixed to spoken line when present
- [ ] todo - Choice list: all ParameterButtonChanger nodes with availability state; unavailable
      choices announce the failed requirement(s); choice description read-back before confirm;
      confirm advances (game's own Button_Click path)
- [ ] todo - Consequence pages after a choice (ConsequenceText flow, next/previous, control
      panel at the end)
- [ ] todo - Willpower/parameter changes spoken when shown (ShowParameterInformationEvent,
      ShowCharacterInformationEvent, ParameterSlider/ConsequenceComponent surfaces)
- [ ] todo - Death trigger flow in text blocks (BlockTextPanel → death window → resume)
- [ ] todo - In-scene tooltips reachable by keyboard: condition tooltip (requirements),
      consequence tooltip, parameter value tooltip
- [ ] todo - Scene-phase gating: no activation during page-turn/show/hide animations
      (IsButtonsBlocked, animation events)

## Phase 5 - HUD and service windows

- [ ] todo - HUD bar: year, chapter, window buttons with unlock state (Tab-stop reachable from
      the scene; arrows + Enter open windows - no per-window hotkeys, per DECISIONS.md); back
      button
- [ ] todo - Character window (parameters, scales/segments)
- [ ] todo - Soul window (willpower, death mechanics)
- [ ] todo - Family window
- [ ] todo - Relations window (+ RelationPopup, relation tooltips)
- [ ] todo - Empire window
- [ ] todo - Work window
- [ ] todo - Home window
- [ ] todo - Map window
- [ ] todo - Destiny window (+ objective earned shine → spoken notification)
- [ ] todo - Insurrection window (+ InsurrectionSidePopup/tooltip)
- [ ] todo - War window (WindowsList.WarWindow - confirm where it appears)
- [ ] todo - Chapter start window (keyboarded page turner)
- [ ] todo - Chapter final window (stats summary pages)

## Phase 6 - Popups, cutscenes, special flows

- [ ] todo - Generic popup screen family from PopupsEnum: TriggerPopup, ObjectivePopup,
      InfoPopup, EventPopup, ConditionHelpPopup, GameFinalsRemindPopup
- [ ] todo - InterludePopup + YearIncrementPopup + CaseOfYear popups (page-turn popups)
- [ ] todo - Parameters conversion panel (chapter transition stat conversion)
- [ ] todo - Death windows: standard death, fourth-death continue flow, GameOverWindow +
      restart/continue popup
- [ ] todo - Chapter cutscenes + intro cutscene: narration text spoken, skip works, no dead air
- [ ] todo - Timeline (WindowLiveTimelineController / life timeline) readable
- [ ] todo - Insurrection-day and other special scene variants (from scene census below)
- [ ] todo - Scene census: enumerate atypical scenes (duel, province finals, variative finals -
      grep decompile for their controllers), list each here as its own item, then verify each

## Phase 7 - Whole-game text surfaces

- [ ] todo - All remaining tooltip types (TooltipWithTitle, ObjectiveTooltip, EventTooltip,
      SimpleTooltip) reachable and spoken wherever they appear
- [ ] todo - Objectives/quest surfaces (GameObjectives, ObjectiveContainerBehaviour) spoken
- [ ] todo - Any unlockable/achievement notifications (UnlockedItemBehaviour, GetAchievement)

## Phase 8 - Verification sweeps (standing + final)

- [x] R1 standing - Regression sweep script (scripts/sweep.sh driving the dev server; bash not
      ps1, see DECISIONS.md): asserts on /speech content; scope grows with each verified surface
      toward main menu → new game → first scene → a choice → consequence → each HUD window →
      save → load → quit. Run after every ~4 verified items; keep it green.
      (first run green 14/14: health, mainmenu graph, End/Home speech, tooltip fallback,
      Settings activation + refocus, focus-mode toggle both ways, zero mod error log lines.
      Last run: 2026-07-18, green)
- [ ] todo - Save-jump harness: saves (or dev-console jumps) that reach each chapter for
      spot-checks deep into the game
- [ ] todo - Full keyboard-only playthrough of the prologue + chapter 1 via dev server, zero
      mouse, no unspoken interactive element encountered (log any gap as a new roadmap item)
- [ ] todo - Sampled sweep across later chapters (3+ scenes per chapter incl. one special
      scene each)
- [ ] todo - Stability: one multi-hour session, no speech loss, no leaked Harmony errors in /log
- [ ] todo - Second-pass code review of everything user-facing (self /code-review), fix findings

## Phase 9 - Wrap-up (personal-only mod: no installer, no player docs, per DECISIONS.md)

- [ ] todo - Release build profile: dev server compiled out; verify the Release deploy still
      speaks and navigates (one smoke pass)
- [ ] todo - KEYS.md: short key reference for the user, generated from the actual bindings
- [ ] todo - Final DECISIONS.md review pass; anything that deserves the user's attention
      summarized at the end of the run
