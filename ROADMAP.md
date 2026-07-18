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

- [ ] todo - Install vendored BepInEx (third_party/bepinex, 5.4.23.5 win x64 Mono) into the
      game folder; game still boots clean with an empty plugin
- [ ] todo - Solution + plugin skeleton (net48, BranteAccess), BepInEx logging with prefix,
      auto-deploy on Debug build; document exact build/deploy in CLAUDE.md
- [ ] todo - Per-frame pump (host MonoBehaviour), master enable flag
- [ ] todo - Speech pipeline ported from wotr-access: Prism primary, manual-COM SAPI fallback,
      clipboard last resort; Tts facade with rich-text strip + never-interrupt default;
      BRANTE_NO_SPEECH headless mode
- [ ] todo - Dev HTTP server on 127.0.0.1:8772: /health, /eval (Mono.CSharp REPL, persistent
      state), /speech ring buffer + cursor + wait
- [ ] todo - Dev server round 2: /input, /wait, /gui, /focus, /typeinfo, /log, /screenshot,
      eval speech-settle capture; runInBackground forced
- [ ] todo - Game launch/kill from CLI proven (record working commands + appid in CLAUDE.md);
      full loop demonstrated: build → launch → /health → /eval speaks → kill
- [ ] todo - Game audio mute mechanism for unattended runs (settings or SoundManager), recorded
      in CLAUDE.md

## Phase 2 - Core framework (ported from wotr-access, adapted to uGUI)

- [ ] todo - Input manager: action registry, keyboard bindings, OS-matched key repeat,
      category priority; game-input suppression strategy decided and working (EventSystem +
      the game's own Update handlers - see TextController/A/D keys) - focus-mode toggle
- [ ] todo - Screen stack: ScreenManager poll-and-diff over UIManager.OpenedWindow /
      OpenedPopup / OpenedTooltip / SceneStateMachine + scene name; lifecycle, layers,
      child screens
- [ ] todo - Graph navigator port: KeyGraph, GraphBuilder, GraphAnnouncer, ControlId two-tier
      reconciliation, Tab-stops, regions, typeahead search
- [ ] todo - Localization layer for mod strings (enGB manifest); pass-through of game strings
      via I2 LocalizationManager
- [ ] todo - uGUI adapter helpers: read TMP text under a node, invoke Button handlers, gate on
      CanvasGroup/interactable visibility (the visibility-gate primitives)

## Phase 3 - Main menu and meta UI

- [ ] todo - Main menu screen (continue/new game/load/settings/credits/exit + language
      selection); first spoken screen
- [ ] todo - New-game flow: disclaimer, intro, name request window (text entry), chapter start
- [ ] todo - Settings window (sliders, toggles, language dropdown) - includes muting game audio
      by keyboard
- [ ] todo - Save/load windows incl. chapter-select page, delete confirmation popup
- [ ] todo - Pause/Escape menu + exit confirmation popup
- [ ] todo - Credits window (skippable)

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

- [ ] R1 standing - Regression sweep script (scripts/sweep.ps1 driving the dev server): main
      menu → new game → first scene → a choice → consequence → each HUD window → save → load →
      quit; asserts on /speech content. Run after every ~4 verified items; keep it green.
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
