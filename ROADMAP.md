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
- [x] verified - Full translations of the mod string manifest (ui + settings tables) into all 13
      non-English game languages, with the locale resolved from the game's I2 language NAME
      (user-directed 2026-07-20. lang/{ru,de,fr-FR,it,es-ES,es-US,pt-BR,pl,tr,ja,ko,zh-CN,zh-TW},
      key parity with lang/en verified by diff for all 26 files; live sweep cycled all 14
      languages via GameManager.SetGameLanguage and read back 5 strings each through
      Message.Localized - all resolved in-language incl. pt-BR (whose I2 code is empty - hence
      the name map in LocalizationManager) and the ja/ko postfix comparator templates; zero
      missing-string warnings in the log; language restored to English after)
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
      with new save/load section. 2026-07-18 later: the SAME page also appears between chapters
      under per-chapter scene names ("LoadingScreen_Child") - caught live as a dead surface;
      IsActive now keys on the GameLoadingScreenBehaviour component, and the quit-to-menu node
      is guarded (that variant has no BotPanel). Verified live on the between-chapters variant:
      Continue "0 Years" start node, 5 chapter rows "unavailable, not reached", locked refusal
      on Enter, Continue entered Childhood.)
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

- [x] verified - Scene screen: transcript of passage pages, spoken on delivery (SceneStateMachine +
      Messenger driven, once per new page), arrow re-read, silent re-home
      (SceneScreen: pages as text rows in one silent-positions stop, labels resolved at speech
      time from the pager's serialized I2 keys through the game's own InsertCharacterName helper
      (hero name/el substitution) with the block's character name prefixed when a portrait shows.
      Delivery is poll-and-diff on the pager's own _pageIndex: once per new page, queued, silent
      re-home to the new row; on screen entry the navigator's seat announcement is the speech
      (no double). Enter on the newest row advances via the game's NextPage gated on the game's
      NextButton.interactable; older rows re-read only. Escape = ShowPauseMenu. Verified live in
      the Intro scene: entry spoke "At The End of Time" + page 1 once; six pages each delivered
      once via Enter; Up re-read without position chatter; Enter on an old row does not advance;
      Enter on the last page no-ops (next disabled); Escape to pause and back re-announced the
      focused row. Page-row ids are Structural, not Referenced - all rows share the pager
      component and reference-tier reconciliation snapped focus back to row 1 on every rebuild,
      caught live.)
- [x] verified - Scene title + year/date announcements (ChangeSceneTitleEvent, DayTimePopup)
      (2026-07-18: the scene screen's name reads SceneController.Title live, spoken on every
      scene arrival ("The Newborn's Cry" after the 01.04 consequence); the between-scene
      date popups ride GenericPopupScreen: chapter/age popup rows "Childhood" + "4 Years",
      year-case popup "Brother and Sister's Sacrament" / "Year 1122" / description / Continue
      all spoken row-by-row and dismissed by Enter, landing in the next scene with title +
      page 0 delivered.)
- [x] verified - Speaker attribution: portrait character name prefixed to spoken line when present
      (2026-07-18: PageText prefixes GetCharacterTrueName when the block carries a Character -
      verified live in Childhood 01.02.01_2: "Robert Brante: ...", "Lydia Brante: ...",
      "Stephan Brante: ...", "Gloria: ..." each spoken with the game's hero-name substitution
      intact ("little Testname Brante"). Same helper drives InterludeScreen pages.)
- [x] verified - Choice list: all ParameterButtonChanger nodes with availability state; unavailable
      choices announce the failed requirement(s); confirm advances (game's own Button_Click path)
      (2026-07-18: choices as positioned Referenced nodes after the transcript, label folds the
      game's post-click elaboration (I2 key sceneName_buttonName) onto the line. Verified live:
      Intro 4 choices + Childhood scenes 2-4 choices all listed "n of m"; activation runs the
      game's Button_Click (GAME_RESOLVE, sounds, stat effects); double-activation gated by the
      game's GetButtonClickedState. Unavailable path verified in 01.04.01: choice 1 announced
      "unavailable, requires Determination at least 2" composed from the serialized failed check
      (game's localized parameter name + mod operation word), Enter refused with the same reason
      interrupt-spoken and did NOT apply (GetButtonClickedState stayed False). NOTE: this build
      has no separate confirm step - one click applies immediately; "description read-back
      before confirm" is satisfied by folding the description into the choice label itself.)
- [x] verified - Consequence pages after a choice (ConsequenceText flow, next/previous, control
      panel at the end)
      (2026-07-18: the active ItsConsequence pager wins Pager() while resolving; the mid-screen
      pager swap is delivered as new content (page 0 spoken, silent re-home). Verified live in
      Intro and three Childhood scenes: consequence pages deliver once each via Enter, the
      ConsequenceControlPanel's Continue appears as a node when the game shows it, activating it
      leaves the scene and the next scene's title + page 0 deliver on arrival.)
- [x] verified - Willpower/parameter changes spoken when shown (SceneConsequenceGenerator's
      post-choice stat panels; ShowParameterInformationEvent only retitles consequence scenes)
      (2026-07-18: each panel the game shows or swaps is delivered whole, once, off its
      rendered rows (the game's own localized composition), with rows + the chaining Continue
      swept into the graph for re-reading; the dedicated Continue node yields when the game's
      button lives inside the generator. Verified live in 01.05.01 choice 3: relation panel
      "Nathan Brante, Relations +1, = +2 (Sympathy)" delivered, re-delivered when the game's
      animation settled the status row to GRATEFUL, then the parameter panel "Willpower,
      -5 (= 0), Ready for Action" - chained by Enter into the year popup and next scene. Same
      whole-panel delivery added to the interlude's post-close panels (silent re-seat replaces
      the old first-row-only seat announcement); to confirm at the next stat interlude.)
- [x] verified - Status-row dash wording: the game renders a bare em-dash as a status panel's
      value when a character's status clears (driver heard "Sophia, Status, [em-dash]" in
      chapter 2 - readers voice the dash). PanelSweep.Spoken now substitutes Loc state.none
      for a WHOLE-ROW dash (em/en/hyphen) in both the delivery join and the row nodes; game
      prose dashes untouched.
      (2026-07-19: the exact substitution code exercised through the dev server (reflection
      call on PanelSweep.Spoken): em dash, en dash, hyphen, and a whitespace-padded dash all
      return the mod's "none"; prose containing an inner hyphen ("a - b") passes through
      unchanged. In-situ trigger (a live cleared status) rides the user's playthrough.)
- [x] verified - Death trigger flow in text blocks (BlockTextPanel → death window → resume)
      (2026-07-18: DeathScreen (layer 21, popup slot) on the book pattern - one live page row,
      Enter turns via the game's RightButton_Click, arrow buttons with mod labels and
      unavailable refusals. Smoke-tested by firing DeathScenesLoader.LoadFirstDeathScene from
      /eval (death param +1 first; restored after with -delta and WasTriggered reset - it is a
      public bool FIELD on DeathEventInfo, not a property). Full loop over HTTP: setup pages
      one per Enter, six death choices in left-column-then-right reading order
      (ButtonDescription1..6, geometry sort x asc then y desc) with hover-only descriptions
      folded on, "Remain silent" activation, resolve pager swap delivered with silent re-home,
      resolve pages, Continue "3 of 3" back to the resumed scene ("Toy Soldiers" + page
      re-seat). The prefab shows placeholder title/text ("New Text" + Russian block) that the
      game rewrites a beat after ShowDeathPopup; title and current-page rewrites re-deliver,
      verified: "First Death" then the English page 0 spoken after the placeholders.)
- [x] verified - In-scene tooltips reachable by keyboard: condition tooltip (requirements),
      consequence tooltip, parameter value tooltip
      (2026-07-18: Space/F1 on a choice speaks the game's two hover tooltips composed from the
      choice's own model data (Readouts.ChoiceDetails): "Conditions Met, Willpower at least 0,
      now 0, met. Consequences, Willpower -5, now 0, Robert Brante, GRATEFUL, Robert Brante
      relation +1, now 2"; a no-condition choice speaks only "Consequences, Willpower +10,
      now 0"; a detail-less node says "no tooltip". Space on a stat panel row (ParameterGetSet
      via PanelSweep) speaks the scale tooltip: "Willpower. Your remaining inner strength...
      -10 to -1, Exhausted, 0 to 9, Ready for Action, now, 10 to 19, Full of Power, 20 to 30,
      Prepared for Anything" with "now" preceding the current segment. Headers and the now
      word are game strings (ConditionIsMet/ConditionNotMet/Consequence/Now). The not-met
      header and OR-tree branch live only on locked choices - composition shared with the
      verified unavailable announcement; confirm wording when one appears naturally.)
- [x] verified - Scene-phase gating: no activation during page-turn/show/hide animations
      (IsButtonsBlocked, animation events)
      (2026-07-18: keyboard activation now honors the game's animation-time input block - the
      CanvasGroup.blocksRaycasts chain SceneAnimationController switches off during page-turn/
      show/hide - at the shared UiWidgets boundary (Interactable and Click both check
      RaycastsReachable), so Enter cannot double-fire through a fading panel the mouse could
      not reach. Smoke-tested live: blocksRaycasts flipped off on the open interlude popup via
      /eval, Enter on its Continue refused (popup stayed open, stack unchanged); flipped back,
      Enter closed it and the flow proceeded ("Grandfather's Return" event popup delivered).
      Page Enter was already gated on the game's own NextButton.interactable and choice Enter
      on IsButtonInteractable plus the GetButtonClickedState resolve gate (both verified
      earlier). IsButtonsBlocked itself gates only HudController buttons - the Phase 5 HUD
      item must honor it there.)

## Phase 5 - HUD and service windows

- [x] verified - HUD bar: year, chapter, window buttons with unlock state (Tab-stop reachable
      from the scene; arrows + Enter open windows - no per-window hotkeys, per DECISIONS.md);
      back button
      (2026-07-18: HudBar builds the bar as a Tab-stop (info row + one button row, geometry
      order) on SceneScreen and on the new WindowShellScreen that replaced the silent window
      placeholder. Verified live in The Intrusion: Tab "HUD, year 1124, chapter I", Shift+Tab
      back to the passage; ten buttons with the game's own terms (name = I2 key, the tooltip's
      lookup); locked ones inline "Will unlock in Chapter II Adolescence" and refuse Enter with
      the same reason; Enter on Family opened Window_Family through the game's handler (delivery
      "Family", seat "HUD, Family, button, selected, 1 of 11" - selected is the game's own
      pressed marker and the Tab landing); arrows+Enter switched to Personality with the swap
      delivered off the opened-window slot; "Back to Scene" node (game's HUD.Back term, 11 of
      11) and Escape both returned to the scene; IsButtonsBlocked smoke test: BlockHudButtons on
      = Enter silent no-op, off = window opened. Build guard closes the one-frame close race
      that spoke a reconcile line over the scene return.)
- [x] verified - Character window (parameters, scales/segments)
      (2026-07-18: CharacterWindowScreen (shell stands down via its Covered set) - identity
      rows from the window's live pair containers, deaths row from the game's own "Deaths"
      label + Death parameter, parameter rows folding name/value/segment with the scale
      readout on Space. Verified live in chapter 1 by keys only: entry "Personality" then seat
      "Testname Brante"; rows "Age: 6 Years", "Estate: Lowborn", "Occupation: growing up",
      "Deaths, 0 of 4", "Perception 3, Curious", "Determination 0, Spineless", "Willpower 10,
      Full of Power"; Space on Willpower spoke the full scale readout with "now" on the
      current segment; Escape returned to the scene. Entry race fixed: no graph until the
      game's Start() has written the hero name (the seat spoke Russian prefab placeholders
      before the gate - caught live). Teen/Youth panels and the chapter 4+ work block render
      through the same folded rows - confirm wording when a later chapter is reached.)
- [x] verified - Soul window (willpower, death mechanics)
      (2026-07-18: investigated live - cut content, unreachable in the shipped game. No HUD
      button or onClick anywhere wires it (swept every Button's persistent calls live); the
      only code path is HudController.WindowSoulButton_Click, which nothing calls; its I2
      terms are empty (no title, no names/segments for its Volition/Rule/Love parameters -
      only Window_Soul.Description has text), so the game itself would render it blank. No
      dedicated screen built. Coverage verified live by force-opening via the game's own
      WindowSoulButton_Click from /eval: WindowShellScreen took it (full HUD bar, back
      button 11 of 11), Escape returned cleanly to the scene ("The Intrusion" + passage).
      The item's real content - willpower and deaths - lives in the Character window,
      already verified.)
- [x] verified - Family window
      (2026-07-18: FamilyWindowScreen - one row per member with the game's whole info panel
      folded on (tile name/role, estate, relation value with the game's relation word, set
      status), Space reads the description plus the status detail behind the game's help
      icon, Enter runs the tile's own button. Verified live in chapter 1 by keys only: entry
      "Family" then "Gregor Brante, Grandfather, Nobleman of the Mantle, Relations -1
      (Indifference), 1 of 8"; all 8 rows spoke with correct estates/relations; Amalia's row
      carried "TRUE DEATH" and her Space added the status paragraph; Enter on Nathan
      delivered "Nathan Brante selected" (model-watched, spoken once per change, seeded
      quiet on entry); Enter on the hero tile opened the Character window ("Personality" +
      name row, the game's own redirect); Escape returned to the scene. Entry gated on the
      hero tile's Start()-written name. Status skipped when CharacterStatus.Good - the game
      renders a bare dash and placeholder detail there.)
- [x] verified - Family window tree redesign (user, 2026-07-19: the flat list was confusing,
      missed the expandable description text, and sometimes went silent)
      (2026-07-19: FamilyWindowScreen rebuilt as a browsable tree shaped by the game's own
      layout - tiles grouped by their FamilyTree row containers (FirstRow/SecondRow/ThirdRow
      = the generations), rows top-down by container Y, members left-to-right by sibling
      index, positions restarting per generation ("Amalia 1 of 3" ... "Stephan 1 of 3").
      Non-hero members are expandable groups: Right expands ("expanded") then descends into
      the info panel's description paragraph and the status detail behind the game's help
      icon as child rows ("1 of 2"/"2 of 2"); Left collapses; Home/End move within the
      generation; Space still reads the full detail in one go; Enter still selects ("Robert
      Brante selected") and the hero tile (a plain leaf - the game tracks no model data for
      the hero) still opens the Character window. The sometimes-silent bug was the populate
      gate: it required tile text to start with ParametersManager.HeroName, and a save with
      an EMPTY hero name (the user's chapter V save) failed it forever - zero nodes, dead
      screen. New per-tile gate: WhoIs text equals the exact translation
      StatusRelationGetSet.Start writes (prefab placeholders carry Russian or empty text
      there; caught live when Gloria's non-empty Russian placeholder leaked through a
      first-draft empty-check gate). Verified live on that save: entry "Family" + Gregor
      seat, full walk, expand/descend/collapse/ascend, both detail children on Amalia
      (TRUE DEATH), select delivery, hero redirect, Tab to HUD and back, clean reopen.
      sweep.sh 59/59 after.)
- [x] verified - Family window as a visual-layout grid with an Enter info sub-screen (user,
      2026-07-20: replace the expandable-group tree - lay members out as the visual tree,
      Enter opens a menu with all the extra info, Escape closes it)
      (2026-07-20: generations are now StartRow grid rows sharing one row key - Left/Right
      walks a generation ("Robert Brante, Father, 2 of 3"), Up/Down moves between
      generations holding the column (Robert down = hero, up = Robert). Member rows slimmed
      to what the tile shows (name + role + selected). Enter clicks the tile (the game
      selects and fills its panel) and pushes FamilyMemberInfoScreen - the first PushChild
      drill-in: screen name "Robert Brante, Father", then description / estate / relation /
      status rows ("1 of 4".."4 of 4"), Escape pops back ("Family" + the member row with
      selected state). Status row always present like the panel: "Status TRUE DEATH, <help
      icon detail>" for a set status, "Status none" for Good (the game renders a bare dash;
      new shared Readouts.DashAsNone, also now used by PanelSweep). The selection watcher
      is gone - Enter's delivery is the info screen itself, member.selected is now
      Relations-only. Hero tile stays a plain click (Character window redirect, verified).
      Verified live on the chapter V save: full grid walk, info open/browse/escape on
      Robert, Gloria (Status none), Amalia (TRUE DEATH detail), reopen lands on the
      game-selected member. sweep.sh 37/37 after.)
- [x] verified - Family info sub-screen: Space no longer recites the window blurb (user,
      2026-07-20: "the tooltips on space are catching the wrong thing" plus "check all text
      is displayed including the expanded thing")
      (2026-07-20: the sub-screen inherited HelpText() => GameUi.WindowHelp(), so Space on
      any info row spoke the Family window's what-this-is line; override removed - rows have
      no hidden detail, Space now says "no tooltip", tree-row Space still reads the member
      description+status. Completeness audited live for all six non-hero members (Gregor,
      Robert, Lydia, Amalia, Stephan, Gloria): every visible RightPage text (WhoIs, Name,
      Estate, Status label+value, Relations label+value, full Description) is spoken by the
      screen name plus the four rows, and Amalia's status row speaks the TRUE DEATH help-icon
      description inline - the "expanded thing" a sighted player only gets by hovering.)
- [x] verified - HUD bar skips blank locked buttons (user, 2026-07-20: focus landed on a
      bunch of blank buttons that only said "unavailable")
      (2026-07-20: the game authors WillOpen tooltip terms for chapters 2-5 only; a locked
      button outside that set - Character/Family/Destiny/Home in the prologue, all
      ChapterToUnlock=1 - renders as a bare darkened slot with no name and no tooltip, so
      HudBar now emits no node for it (BlankLocked). Locked buttons WITH a term still speak
      the game's own unlock line. Verified live on the Allarick chapter 1 intro: bar = info
      row + Settings + five "Will unlock in Chapter ..." buttons, the four blank slots
      gone; chapter V bar unaffected (all unlocked).)
- [x] verified - HUD window sighted-parity audit, all ten windows (user, 2026-07-20: "the
      destiny screen seems like it's leaking a tun of spoilers. the various other screens
      seem very confusing")
      (2026-07-20: every window compared against its screenshot + live component state on
      the chapter V save. Three fixed: Destiny objective rows now carry achieved/not achieved
      (the game marks state only as black-vs-gray text - a gray "The Anizotte Massacre" read
      as a fact without it) and categories read in on-screen sibling order (Chapter Outcomes
      before Family before Occupation); Home's heir row moved below the household stats where
      the game draws it; the Revolt window dropped its 33 invented victory-condition rows
      (the prefab's win-condition panels are inactive unfilled placeholders and the side-icon
      tooltip event has no broadcaster in the assembly - sighted players see conditions only
      in the one-time side popup and Destiny, both already covered) and its side row now
      pairs the game's own "Your side" header ("Your side: not chosen"). Condition helpers
      moved into InsurrectionSidePopupScreen, their only real consumer; every reflected popup
      field name re-verified resolving live. Verified by graph dump + speech readback on all
      three windows; achieved-word branch exercised by flipping one objective in memory and
      restoring it. Personality, Relations, Province, Occupation, Map, Settings audited
      clean - spoken content matches the sighted surface.)
- [x] verified - Relations window (relation tooltips folded; RelationPopup still pending)
      (2026-07-18: RelationsWindowScreen on the Family-window pattern - tile rows with
      role/estate/relation/status folded on via the shared Readouts character helpers,
      selection delivery, placeholder row for the no-acquaintances case. Verified live in
      chapter 2 scene 1 via the real HUD button: entry "Relations" + "Tommas Guerro, Lowborn,
      Relations 0 (Indifference)", Space read his description paragraph, Enter delivered
      "Tommas Guerro selected", Escape back to the scene. Two guesses corrected live: the
      I2 term is HUD.Relation (HUD.Relations is empty), and the build gate now waits for the
      controller's own model count (unlocked non-family characters) - the prefab placeholder
      sits active with unlocalized Russian text until the controller's Start, and the first
      graph build beat it. RelationPopup still unexamined - kept on the todo list below.)
- [x] verified - RelationPopup (examined: no surface to build)
      (2026-07-18: three findings close this. The RelationPopup class itself is a generic
      show/hide container with a click-away background, referenced by no game code (prefab
      serialized events only) and with ZERO loaded instances at the main menu or in a
      chapter 2 scene with Relations open (Resources.FindObjectsOfTypeAll) - if a story
      scene ever instantiates one, the scene census item below will surface it. Relation
      CHANGES are the SceneConsequenceGenerator NPS panels/relation rows - they instantiate
      as children of the generator, so the already-verified SceneScreen stat-panel sweep
      delivers them ("+1 (Become 3)" composition, Phase 4). The hover RelationTooltip
      (UIManager slot) has full data parity with the mod's folds: name/role/relation
      pair/status on the row labels, description + status detail behind Space
      (Readouts.CharacterDetail) in Family, Relations, and Empire - per the house rule the
      mod reads the model, not the tooltip.)
- [x] verified - Empire window
      (2026-07-18: EmpireWindowScreen - the Overseer and Patriarch office rows as hud.pair
      label-value using the window's own header texts (transform sibling "Name"), Space reads
      Readouts.CharacterDetail where the game tracks a character behind the office (null for
      "None"/hero outcomes), then the province parameter rows through the shared ParameterRows
      sweep. Build gated on the first active parameter row matching its own I2 translation
      (Start()-race guard, Home precedent). Verified live in chapter 2 scene 1 via the real
      HUD button: entry "The Province" (HUD.Empire) + "Overseer Archduke Milanidas"; Space
      read the Archduke's description and the Patriarch Fotis description on his row; rows
      "Power -4, Overseer in Power" / "Church -4, Old Faith" / "Wealth of Magra 6, Security" /
      "Order 8, Peace"; Space on Order read the scale with the now marker; Escape back to
      the scene.)
- [x] verified - Work window
      (2026-07-19: WorkWindowScreen - the Occupation window's post panel read through the
      shared ParameterRows sweep (inactive post panels excluded automatically: 6 parameter
      objects in the prefab, only the active Inquisitor pair exposed). Opened keyboard-only
      from the scene (Tab to HUD, Right to Occupation, Enter); rows "Inquisition Power 7,
      Indisputable Authority" / "Tolerance of Faiths 1, Persecution of Dissent"; Space read
      the Tolerance of Faiths scale with description and the now marker on the 1-to-2
      segment; Escape back to the scene with the passage redelivered. Post name is image-only
      in this window by the game's design - the text lives in the Character window's
      occupation row, already verified. Same Start()-race gate as Home.)
- [x] verified - Home window
      (2026-07-18: HomeWindowScreen - heir row folded as label plus value ("Heir Stephan
      Brante"), then the household parameter rows through the new shared ParameterRows helper
      (extracted from the Character window): "Reputation 3, Tarnished Honor" / "Wealth 5,
      Moderate Means" / "Unity 6, Disagreements", Space read the Unity scale with segments and
      the now marker, Escape closed back to the scene. Entry gated on the first parameter row
      matching its own I2 translation (Start()-race guard). Opened via the game's real HUD
      button click path. Character window re-verified after the ParameterRows refactor
      (identity rows, "Perception 3, Curious", scale readout, Escape). Broken-family panel
      (chapter 3+ СемьяРаспалась objective) coded - same parameter sweep plus the collapse
      objective with Space reading Readouts.ObjectiveDetails - deferred confirmation: needs a
      save where the family has broken.)
- [x] verified - Map window
      (2026-07-18: MapWindowScreen - the window's only readable content is the 15 province
      and city name labels (MapAreaItemBehaviour is hover-highlight only, no click action in
      the decompile; every TMP in the window belongs to an area item - verified by live dump).
      One text row per visible area in prefab layout order, typeahead-searchable. Verified
      live in chapter 2 scene 1: entry "Map of the Empire" (HUD.Map), "Pragos, 1 of 15",
      Down walked Siltrum and Montis, End reached "Orta, 15 of 15", Escape back to the
      scene.)
- [x] verified - Destiny window (+ objective earned shine → spoken notification)
      (2026-07-18: DestinyWindowScreen - chapter tabs as a tab row (locked tabs announce and
      refuse with the unavailable word - the game gives them only a gray), then the active
      chapter's objectives grouped under the game's category headers, achieved marked by the
      mod's achieved word (game shows only black-vs-gray), Space reads description +
      requirement rows composed the earn-popup way (translated operands, symbols as op
      words). Verified live in chapter 1 by keys only: entry "Destiny" + "chapter I, tab,
      selected, 1 of 5"; chapter II tab "unavailable" + Enter refused; Down spoke "Personal
      Life, The Fencing Lesson, 1 of 4"; all four Space readouts spoke description +
      conditions ("Determination at least 4, Train with Father" etc.); Escape returned to
      the scene. Fixed live: tab labels raced TextMeshProLocalization.Start (entry spoke
      Russian) - new UiWidgets.LocalizedLabel resolves the component's own I2 keys. Confirm
      in later chapters: tab-switch delivery (all tabs locked in ch1), an achieved
      objective's row word, an or-group's any-of wording. The timeline pane is dead code in
      the shipped game (empty load handler) - nothing to read. The earned-shine notification
      rides the objective popup (generic popup family), not this window.)
- [x] verified - Insurrection window (+ InsurrectionSidePopup/tooltip)
      (2026-07-19: InsurrectionWindowScreen - title from _windowTitleKey, side panel row
      (chosen side or the game's no-side text), ParameterRows sweep, and the hover-only
      victory conditions composed from the persistent tooltip InsurrectionSidePopupController's
      serialized lists via reflection: per side its subtitle, then peace/war groups headed by
      the popup's own column keys, objective rows (Not-aware, Unlocked as met) and parameter
      rows (diapason as "{name} {min} to {max}" with the empire/rebel LessEqual/MoreEqual met
      logic mirrored from GenerateParameter; no side chosen reads both sides). Shell stands
      down by component check. 2026-07-19 verified live at chapter IV via dev
      ShowWindow(InsurrectionWindow): title "The Anizotte Revolt", side row "not chosen", 5
      parameter rows with scale words, both sides' condition groups spoken with live now/met
      ("The Revolt exactly 0, now 0, met", "Power -4 to -3, now -2, not met" - diapason
      bounds ordered numerically after the empire side's upper-bound-first serialization
      read backwards), objective row "Leading the Revolt, not met", Space on a parameter
      row read the full Revolt scale breakdown. Zero mod errors. Dev-open gotcha
      (not a mod bug): ShowWindow with a null button throws in the game's OnSelect before
      ShowBackButtonEvent, so the back button never arms - close by destroying OpenedWindow
      + reactivating CurrentScenePrefab, mirroring the game's destroy branch.
      2026-07-19 chapter V: chosen-side panel verified live via the real HUD route (The
      Revolt button unlocked at ch V) with the rebel objective dev-unlocked then restored:
      side row "Rebel", 5 parameter rows, only the rebel condition groups built, Escape
      closed back to the scene. Non-tooltip side popup: new InsurrectionSidePopupScreen
      (layer 21) - the generic sweep spoke the popup's serialized Russian title on its
      first frame (labels are code-set in Start) and split condition rows into bare
      name/value texts; the dedicated screen reads title/subtitle from the controller's
      I2 keys and reuses the window's AddSide condition builders. Verified live by
      dev-firing UIManager.ShowInsurrectionSidePopup with rebel unlocked: stack
      popup:insside only, full briefing with live met state ("The Revolt at least 6,
      now 2, not met", "Power 3 to 4" ordered), Continue closed it and the scene
      re-delivered; objective relocked after.)
- [x] verified - War window (WindowsList.WarWindow - confirm where it appears)
      (2026-07-19: cut content, nothing to read. ShowWindow's WarWindow case is an explicit
      no-op sharing a break with GameScene, zero code callers, and
      FindObjectsOfTypeAll<WarWindowController> returns 0 live at chapter IV AND at
      chapter V mid-revolt - the window can never appear.)
- [x] verified - Chapter start window (keyboarded page turner)
      (2026-07-18: ChapterStartScreen - header + page title/position/description rows,
      objectives fold their hover-only description onto the row, parameter rows fold
      name/value/segment, other panels swept; Prev/Next as nodes (image-only buttons, mod
      labels) refusing while the window's _blockPages gate holds; page turns deliver
      title+position+description with focus staying on the pager button. Verified live at
      Childhood start: all 5 pages turned one keypress each, objective rows with descriptions,
      end-stop refusal "unavailable", Begin Chapter swept + activated into scene 01.01.01.
      Content rows verified live at Adolescence start: objective rows spoke name + folded
      description ("Gloria's Secret. You discover the secret society..."), all 6 pages
      delivered title/position/description on one keypress each. 2026-07-19: New Sections
      unlock rows folded - the swept bare-"button" icon (HudController window-open wiring,
      no text child) replaced by one button row per UnlockedItemBehaviour labeled with the
      game's own unlock text; verified live at Peace Time start: "'Occupation' screen is
      now available, button", Enter opened the Occupation window, Escape returned to the
      book with the page redelivered.)
- [x] verified - Chapter final window (stats summary pages)
      (2026-07-18: ChapterFinalScreen - epilogue text rows (second prefab text deduped),
      page title/position/description row, timeline rows folding event + branch + year from
      the year headers ("Birth, Personal Life, year 1118"), objective pages reuse the
      chapterstart fold + ObjectiveDetails on Space, parameter pages reuse ParameterRows
      (name-value trim added for the valueless Deaths row), continue node runs the game's
      NextSceneButton_Click. IsActive gates on the left page's own active+alpha state - the
      controller GameObject outlives readable content on both ends (pregame block, hide fade),
      and without the gate it masks the between-chapters chapter-select screen. Verified live
      at End of Chapter I: all 4 pages paged both directions with deliveries, prev refusal at
      page 0, Space read Determination scales and Deaths scale, continue crossed into
      chapter 2 (loading screen -> cutscene -> picture -> interlude -> Adolescence start).)

## Phase 6 - Popups, cutscenes, special flows

- [x] verified - Generic popup screen family from PopupsEnum: TriggerPopup, ObjectivePopup,
      InfoPopup, EventPopup, ConditionHelpPopup, GameFinalsRemindPopup
      (2026-07-18: GenericPopupScreen replaces the silent popup scaffolding - PanelSweep reads
      the UIManager popup slot: visible TMP/legacy texts as rows (button labels excluded),
      visible buttons as nodes with unavailable state. Verified live: InfoPopup
      "Prologue/Growing up" read row-by-row with hero-name substitution + Continue closed it;
      scene-transition popup (title/year/description/Continue) same; chapter/age popup
      ("Childhood" + "4 Years") and CaseOfYear popup (title/year/description/Continue) read
      row-by-row and dismissed by Enter.
      2026-07-19 remaining members closed: TriggerPopup rides its dedicated
      TriggerScenePopupScreen (verified at chapter entries and the finals' Family Strife).
      Dev-fired via the game's own UIManager entry points and read back over HTTP:
      GameFinalsRemindPopup (full objectives list "The Legion is Defeated"..."True Death" +
      Continue - this fire also caught the LAST first-open race live: its LocalizeText runs
      in Start, so the generic screen now activates one frame after first observing any new
      popup object, which fixed the Russian-title entry announcement and covers the whole
      configure-in-Start class generically), ObjectivePopup ("The Fencing Lesson" with
      conditions "Determination >= 4", "Train with Father", description, Continue), EventPopup
      ("Nathan's Birth", "Year 1118", description, Continue). ConditionHelpPopup: prefab
      instantiated through its PlayerPrefs first-show gate with a synthetic keeper (Continue
      button announced with live unavailable-then-available state); its text rows are the
      same InformationPopupComponent the verified InfoPopup renders through, and a real-key
      test would consume a real popup's first-show flag - no code path calls it anyway.)
- [x] verified - InterludePopup + YearIncrementPopup + CaseOfYear popups (page-turn popups)
      (2026-07-18: InterludeScreen - transcript pattern on the popup's own _pageIndex/_textBlock
      through its InsertCharacterName, close button node when the game reveals it, post-close
      generated stat panels fall back to PanelSweep. Verified live on three interludes (Chapter I
      opening, 3 pages; growing-up interlude, 9 pages; "Year 1122, Winter. The Coming Sacrament"
      mid-chapter intermission with speaker-attributed Lydia Brante pages) plus the year/case
      popups: post-close stat panels read row-by-row ("Stephan Brante", "Relations -1",
      "= 0 (Indifference)") with per-panel Continue chaining to the next panel and out; the
      year-increment "Childhood / 4 Years" and CaseOfYear "Brother and Sister's Sacrament /
      Year 1122" popups spoken via the generic popup sweep and dismissed by Enter.)
- [x] verified - Parameters conversion panel (chapter transition stat conversion)
      (2026-07-18: reached live at the chapter II to III transition (EnableConvertationParams
      interlude "Leaving Home"). Covered by the InterludeScreen post-close PanelSweep with
      three fixes found and verified this run: ParameterComponent name/value/segment texts
      fold onto one row ("Perception 5, Inquisitive") with the game's scale detail on Space
      ("now" marker in the right segment); the image-only LeftArrow/RightArrow pagers speak
      pager.prev/pager.next with unavailable at the edges; deliveries wait for a 0.45s settle
      window (the panel activates with prefab placeholder text one frame before Start
      localizes - pre-fix this spoke Russian plus "dfsdfsfsdfsdf" gibberish, speech 98) and a
      panel that merely grew speaks only the new tail ("Theology 6, Neophyte") with focus left
      alone. All 7 pages traversed over HTTP, one delivery per page swap, adult skills revealed
      one per page, last page's Continue (game's own click) closed into the chapter III start
      book. Zero mod errors logged.)
- [x] verified - Death windows: standard death, fourth-death continue flow, GameOverWindow +
      restart/continue popup
      (2026-07-19 fourth-death trial VERIFIED end to end over HTTP: FourthDeath_* scenes loop
      by same-frame scene reload (no pop frame - destroyed-pager detection via ReferenceEquals
      delivers each new scene's page with silent re-seat), judgment questions delivered and
      answered through the game's own buttons (the trial wires SceneButton_Click on the
      answer Buttons - activation now clicks the Button instead of calling OnButton_Click
      directly, which had forked into an unreachable NRE path), final Continue exits to the
      relive chapter select, rewind popup ("Rewind to Chapter Youth" + description) confirmed,
      Youth interlude + chapter start reached. Standard death verified earlier (line 245
      item). A second trial later the same day traversed COMPLETELY hands-off by the story
      driver (23 steps: judgment pages, answers, Continue, relive select, rewind confirm)
      with zero mod errors.
      2026-07-19 game-over cluster closed: ShowGameRestartPopup and ShowRestartOrContinuePopup(4)
      dev-fired via /eval, both delivered whole by the generic popup sweep (title, description,
      Confirm/Cancel buttons, all navigable; the game-restart popup's four I2 keys have no
      English entries so the game itself renders Russian - parity, not a mod gap, and no code
      path even calls it). The standalone GameOver scene (infinite credits scroll, no code
      loader, no exit) got a minimal GameOverScreen: entry speaks "Game over" + first row,
      arrow reaches the credits block - previously that scene was totally silent. The shipping
      game-over ending Credits_GameOver verified speaking via the existing CreditsScreen
      ("credits", first block "1 of 22"). During diagnosis, interlude page keys became
      popup-instance-qualified: two interlude scenes back to back with no popup-free frame
      would re-seat on an identical structural key and swallow the second interlude's entry
      announcement entirely (the differ never resets while the same screen stays focused).)
- [x] verified - Chapter title splash (StartPictureHelper, "ChildhoodPicture" et al - the
      click-anywhere picture between chapter select and the chapter start book)
      (2026-07-18: ChapterPictureScreen - one node, game's localized title + "Enter continues"
      hint (the whole screen is the control - unusual enough to earn one). Enter runs the
      game's OnPointerClick (hide animation, sfx, Bolt advance). Verified live: "Chapter I.
      Childhood, Enter continues" spoken, Enter advanced to ChildhoodChapterStart.)
- [x] verified - Chapter cutscenes + intro cutscene: narration text spoken, skip works, no dead air
      (2026-07-19: intro Cutscene_1 dev-loaded - entry announces "cutscene, spoken narration,
      any key skips" and the scene carries zero text components (the game's own VOICED narration
      is the content, accessible by design); chapter V entry cutscene exercised earlier the same
      day on the real story path (announce + Enter skip through our input layer). Both variants
      share CutsceneScreen.)
- [x] verified - Timeline (WindowLiveTimelineController / life timeline) readable
      (2026-07-19: LiveTimelineScreen - the LiveTimeline scene (game end, before the finals)
      was fully silent before (empty screen stack). Screen named by the hero's full name row;
      rows follow the scroll content's child order: year marker rows ("1118", "1122") and
      "category, event" rows ("Personal Life Birth", "Family Nathan's Birth", "Family
      Brother and Sister's Sacrament"), 40 nodes for 23 events under 16 years; End Life
      button announced and activated through the game's own click - the flow moved into the
      finals sequence (Family Strife trigger popup + epilogue scene delivered). Reached by
      dev scene-load of LiveTimeline over the chapter IV save.)
- [x] verified - Insurrection-day and other special scene variants (from scene census below)
      (2026-07-19: the revolt-day opener (chapter V first scene, "The Family's Fate") is the
      standard TextController surface and read fully live: trigger popup owned by its
      dedicated screen, 11 passage pages one keypress each, 4 choices with the two
      unavailable ones speaking their failed requirements ("requires Reputation at least 5,
      Stephan Brante" / "requires Gloria"). The census confirmed no other distinct scene
      controller exists (duels and dressed blocks ride the same flow); epilogue and
      LiveTimeline variants verified as their own items.)
- [x] verified - Epilogue scenes (ConsequenceSceneController: 070.* game-over outcomes reached
      after a True Death - same TextController surface as normal scenes, no choices, ends on
      the UniversalParametersGenerator control panel's Continue)
      (2026-07-19: found by the driver as an uncovered screen after the chapter III gallows
      True Death. SceneScreen now recognizes both controllers (shared TitleObject) and sweeps
      the generator panel as its stat-panel root. Live: "Outcome of the Revolt" delivered all
      8 pages over HTTP, End reached "Continue, button", Enter closed onward into the
      FourthDeath judgment flow. Zero mod errors.)
- [x] verified - Scene census: enumerate atypical scenes (duel, province finals, variative
      finals - grep decompile for their controllers), list each here as its own item, then
      verify each
      (2026-07-18 decompile pass: no dedicated duel controller exists - DuelStatusesEnum is
      save-model state and duels ride standard scenes; TextBlockSpecialSceneries is per-block
      dressing (sfx/music/picture/poem/death-trigger) on the covered TextController flow.
      Distinct surfaces found and listed as items below. 2026-07-19 closed: the driver runs
      through chapters II-IV, the finals sequence and the chapter V entry surfaced exactly
      two uncovered screens - epilogue scenes and LiveTimeline - both since built and
      verified as their own items; every other scene the probes hit rode the covered
      TextController/ConsequenceScene surface.)
- [x] verified - CaseForYearWindow (chapter 3+ case-selection window: PeaceCases/WarCases lists,
      left/right pages, confirm popup; distinct from the verified CaseOfYear popup)
      (2026-07-19: CaseForYearScreen (layer 12, over the scene) + Readouts.CaseDetails/
      CaseUnavailableReason. Exercised live at the chapter IV save by opening the window
      through the game's own UIManager.OpenCaseOfYearWindow: focus takeover from the scene
      (stack scene, caseforyear), 9 Inquisitor peace-case rows in page order, grayed rows
      announce the failed check from the serialized conditions ("unavailable, requires Nathan
      Brante relation at least Sympathy", "requires Tolerance of Faiths at least 5"), Space
      reads conditions with live met state plus year consequences ("Conditions Not Met,
      Willpower at least 5, now 5, met, Nathan Brante relation at least 2, now 0, not met.
      Consequences, Willpower -5, now 5, Theology +1, now 12"), Enter ran the game's click
      path into the confirm popup (popup screen took over), cancel returned to the case rows,
      window teardown popped cleanly back to the scene. Caveat: case titles/descriptions are
      scene-bundled I2 terms (not in the global source), so the harness open spoke empty
      titles; the rows read ButtonText live, so the natural story flow supplies them - the
      dev "CaseOfYear" scene is not in build settings, so titles get their real check when
      the story deals the dialog.)
- [x] verified - TriggerScenePopupController (scene-trigger popup with vertical/horizontal
      parameter/relation/status rows - check live whether GenericPopupScreen already covers
      its prefab before writing anything)
      (2026-07-19: checked live by loading 051.070.050_ThomasWedding (dev scene-load, the
      game's own dev-tool pattern) - the scene auto-showed the popup and the generic sweep
      DID read it, but wrongly: the game marks a negated condition only by strikethrough
      styling, so all three "not Tommas Guerro EXILED / SEVERED ALL TIES / TRUE DEATH"
      conditions spoke as if positive. Wrote TriggerScenePopupScreen (layer 21 over the
      generic popup) + Readouts.Trigger*Row: rows recomposed from the same SceneCondition
      model the popup renders (FindCondition on the active scene), negation spoken with the
      not-word, relation rows with the game's relation word. Verified live: title,
      explanation line, "Conditions Met" header, three not-rows, Continue (game's own click)
      closed into the scene's title + first passage delivery. Zero mod errors. Also fired
      NATURALLY at the Family Strife finals scene: parameter row "Unity exactly 0" and
      negated objective row "not The Family Falls Apart" spoken, Continue into the epilogue
      passage - all row types except relation exercised live.)
- [x] verified - ChapterWindowManager sections (later-chapter final summary: Post, BigDeal, Duels,
      Heir panels + work/family parameter sliders - check at the chapter 3 final whether the
      ChapterFinalScreen sweep already reads them)
      (2026-07-19 chapter III final checked live: NO ChapterWindowManager component exists
      there (FindObjectOfType null). Chapter III final itself: 4 pages traversed over HTTP -
      Destiny timeline (8 events with category + year), Personality x2, page 4 renders empty
      in the game itself (title/description/Continue only, panels carry no text) and the
      sweep faithfully delivers "4 of 4" + Continue. Zero mod errors.
      2026-07-19 closed as CUT CONTENT: probed live at every candidate surface - all three
      PeaceFinishScreen_* variants, YouthFinishScreen, PeaceChapterStart_Prefect and
      InsurrectionChapterStart each show 0 instances (FindObjectsOfTypeAll). The component
      lives only on the Prefabs/Windows/Window_chapter prefab, which no code path and no
      UIManager slot ever instantiates (ShowWindow's ChapterWindow case only closes windows),
      and whose 31 texts are serialized Russian editor strings with no I2 components
      ("Работа", "Должность:", "Большое дело:", "Запрет дуэлей:") - a prototype superseded
      by the shipped Occupation and Family windows. The chapter IV final surface itself
      (PeaceFinishScreen_Prefect, dev scene load) verified live: "End of Chapter IV. Peace
      Time", Destiny page "1 of 2" delivered by the existing ChapterFinalScreen.)

## Phase 7 - Whole-game text surfaces

- [x] verified - All remaining tooltip types (TooltipWithTitle, ObjectiveTooltip,
      EventTooltip, SimpleTooltip) reachable and spoken wherever they appear
      (2026-07-18 live audit of every loaded TooltipWithTitleBehavior (43 instances): each
      hover target carries TitleKey/TitleMainText I2 keys read at speech time - the mod folds
      from the same keys, never the tooltip prefab. Gaps found are the four items below; the
      per-surface tooltip data elsewhere (parameter scales, objectives, characters, choice
      conditions/consequences) is already folded and verified per window.
      2026-07-19 carrier census closed: EventTooltip's only carrier is the dead timeline pane,
      SimpleTooltip's carriers (HUD buttons, Destiny tabs) are folded per window, and the last
      MainMenuUITooltip gap - the name request mode toggles, whose explanations exist ONLY in
      hover tooltips - now speaks on Space via each toggle's TooltipKeyHolder key: all three
      descriptions verified live over HTTP (chapter rewind enabled, Iron Man, open
      consequences).)
- [x] verified - Map window detail pass: every city item carries Map.<X>.Description behind its
      TooltipWithTitleBehavior (label IS GetTranslation(TitleKey) via MapItemTranslation, so
      the pairing is authoritative) - fold description onto Space; and the window has an
      inactive-in-ch2 Regions layer (MapRegionItem with Map.<Region>.Description) - find what
      activates it (chapter? toggle?) and cover it
      (2026-07-18: the "inactive Regions" reading was prefab-asset staleness - LIVE both
      layers are active in ch2. Screen now rows every TooltipWithTitleBehavior carrier:
      15 cities (live label) + 6 provinces (invisible hover zones, label from TitleKey with
      the province word - no game string distinguishes them from cities, map.province
      authored). Space reads GetTranslation(TitleMainText) on both. Live: "Pragos, 1 of 21"
      + full city description, "Magra, province, 21 of 21" + province description, Escape
      back to scene clean.)
- [x] verified - Destiny locked chapter tabs: speak the game's own HUD.WillOpen2..5 tooltip title
      as the unavailable reason (currently bare "unavailable"; rule: announce the failed
      requirement with the game's own string)
      (2026-07-18: LockedReason reads the tab's own TooltipWithTitleBehavior.TitleKey via
      state.unavailable_reason. Live on the ch2 save: tabs III/IV/V each spoke their own
      reason ("chapter III, tab, unavailable, Will unlock in Chapter III "Youth", 3 of 5",
      IV "Peace Time", V "The Revolt") and Enter on a locked tab refused with the same
      string; chapter I stayed bare, chapter II spoke selected.)
- [x] verified - Keyboard-only window close crashed the game's own back handler: UIManager.
      OpenedTooltip is only assigned by mouse-hover tooltip handlers, so the first Escape of
      a session NREs in HudController.WindowMainButton_Click (OpenedTooltip.SetActive without
      a null guard), deactivating the back button and stranding the window open. HudBar now
      seeds an inactive stub GameObject into the slot before clicking back; ClickBack also
      logs instead of failing silently when the button is missing.
      (2026-07-18: reproduced on a fresh game session (first ui.back after opening Destiny
      hit the NRE, window stuck); after the fix, open Destiny + ui.back closed cleanly with
      the scene re-announced and zero exceptions in /log. Also fixed the load-transition
      IsActive NRE spam: GameUi.State reads PREGAME while GameManager.Instance is null
      during a save-load scene swap - ~40 "Screen.IsActive threw" log lines per load gone.)
- [x] verified - Window title-row help: every window's TitleRow HelpIcon carries
      Window_<X>.Title/.Description (what-this-window-is help) - expose per window (likely
      Space on a title node or a help row; decide one pattern for all windows)
      (2026-07-18: pattern chosen - Space on a node with no detail of its own falls back to
      Screen.HelpText (new base hook); window screens return GameUi.WindowHelp, which reads
      the open window's Title-parented HelpIcon TooltipWithTitleBehavior and translates its
      TitleMainText fresh per press. The Window_X.Title terms translate EMPTY, so only the
      description speaks; nodes with their own detail are unaffected (fallback only fires
      where OnTooltip is null, replacing the dead "no tooltip" line with real information).
      Live: Home window heir row + Space spoke the game's House description; Escape clean.)
- [x] verified - Pause-menu toggle descriptions: the pause window's toggles carry
      TooltipWithTitleBehavior descriptions (ConsequenceToggle.Description,
      IsPictureAnimatedToggle.Desription (sic)) - fold onto Space on those toggles
      (2026-07-18: the tooltip sits on the toggle's Background CHILD (not a parent) -
      GetComponentInChildren. Live: Space on Hidden Consequences spoke "Choose whether you
      will see the consequences of your possible choices in advance.", on Animated
      Illustrations "Choose whether illustrations in scenes will be animated."; Escape
      resumed the scene.)
- [x] verified - Pause window first-open announces the prefab's serialized RUSSIAN text: on the
      first ShowPauseMenu of a game session the entry announcement spoke "Настройки" and
      "Музыка, slider, 50 percent" while the nav graph a frame later showed English - the
      game's localize pass runs after our focus announcement.
      (2026-07-18: PauseScreen title and slider/toggle/spinner captions now announce through
      UiWidgets.LocalizedLabel (I2 term via the caption's TextMeshProLocalization, rendered
      text fallback). Verified on a fresh game restart, pause opened as the session's first
      window: entry spoke "Settings" + "Music, slider, 50 percent, 1 of 6", all six rows
      English. Exit-confirm popup also checked on its session-first open: "QUIT GAME" + full
      question in English - popups announce off I2 terms already, no race. Other windows read
      ScreenName from GetTranslation directly, immune by construction.)
- [x] verified - Objectives/quest surfaces (GameObjectives, ObjectiveContainerBehaviour)
      (2026-07-18: examined - neither names a UI surface. GameObjectives is a serializable
      data list on the save model; ObjectiveContainerBehaviour only deactivates itself at
      Start when its container is empty. Every real objective surface is already covered
      and verified: Destiny window rows + Space details, chapter final objective sweeps,
      and the objective-earned popup notification.)
- [x] verified - Unlockable/achievement notifications (UnlockedItemBehaviour, GetAchievement)
      (2026-07-18: examined - GetAchievement is a data marker (an ObjectiveEnum field, no
      behavior); Steam achievement toasts render in the Steam overlay, outside the game UI
      and out of mod scope. UnlockedItemBehaviour is a static TMP label localized in Start,
      spoken by whatever screen sweeps the window it sits in; the scene census below will
      surface any instance living on an uncovered surface.)

## Phase 8 - Verification sweeps (standing + final)

- [x] R1 standing - Regression sweep script (scripts/sweep.sh driving the dev server; bash not
      ps1, see DECISIONS.md): asserts on /speech content; scope grows with each verified surface
      toward main menu → new game → first scene → a choice → consequence → each HUD window →
      save → load → quit. Run after every ~4 verified items; keep it green.
      (first run green 14/14: health, mainmenu graph, End/Home speech, tooltip fallback,
      Settings activation + refocus, focus-mode toggle both ways, zero mod error log lines.
      Last run: 2026-07-19 (after game-over cluster, one-frame generic popup deferral,
      instance-qualified interlude keys, GameOverScreen) on the chapter V save, fresh game
      process: first pass 58/59 - the mod-error check caught FamilyWindowScreen.Build crashing
      155 ticks on HeroName being null while the save was still loading (the populate gate
      dereferenced it); null HeroName now reads as "not populated yet". Second pass green
      59/59, zero mod errors.
      Prior run: 2026-07-18 (after Destiny locked tabs, window-close crash fix, Map detail,
      window help, pause toggle descriptions), green 58/58, advance-loop outcome scene-ended.
      Sweep hardened this run after a state-dependent 50/58: on the chapter-ending save
      (02.12.01 The Last Night, no choices) the 5c advance loop finished the scene into
      TeenFinishScreen, where HUD windows cannot open - all 7 window announces failed. Fixes:
      window section (5c2) now runs BEFORE the advance loop; the loop accepts scene-end as a
      valid outcome; speech() strips the "next: N" protocol trailer (check_nonempty could
      never fail on raw output); window escape checks assert scene(0)* refocus via /nav
      instead of nonempty speech. The honest empty-check exposed a real mod gap: Home/End on
      an already-edge-focused node was silent - GraphNavigator.JumpEdge now re-announces the
      current node in full (AnnounceCurrent) as the re-read gesture; verified live (second
      Home at main menu re-speaks "Continue, button, 1 of 5") plus the transcript re-read
      check now passes on real speech.
      Prior run: 2026-07-18 (after Home, chapter start/final, Relations, Empire, Map), green
      59/59 - new 5c2 section opens every covered HUD window by its real button (click eval
      by persistent handler name), asserts the entry announcement against the game's own I2
      term fetched at runtime (chapter-proof), Escape back to the scene each time; buttons
      the loaded save's chapter has not unlocked are skipped. Ran against the chapter 2 save;
      all seven windows present, zero skips.
      Prior green run (after HUD bar, Character, Soul, Family windows), 38/38.
      Gotcha reconfirmed: the sweep's precondition is the MAIN MENU - a mid-scene start
      fails the menu sections and its keypresses page the live scene. Quit to menu first.
      Prior run (after death flow, tooltips, animation gating), green 38/38 -
      includes the event-scene section: transcript row focused on load, safe re-reads leave
      the pager alone, End+Enter driver reaches choices/continue without ever activating a
      choice, choice speaks with position.)
- [x] verified - Save-jump harness: saves (or dev-console jumps) that reach each chapter for
      spot-checks deep into the game
      (2026-07-18: scripts/advance.sh - story-advance driver that pushes the live game forward
      through the mod's navigator, verifying speech per action (rotating choice picks, silence
      and /log-error aborts, stops on chapterstart/unknown screens). The game writes a save per
      chapter reached - those are the jump points. Pulled forward from Phase 8 because every
      remaining Phase 5 window needs a later chapter. First long run: 183 steps on the Testname
      save through the scripted death, The Sacrament and the era interlude, stopping exactly at
      the then-unported chapter final window - the intended discovery loop. Driver hardened
      after it: transient "screen none" retried up to 8 probes (was instant abort), chapterfinal
      added as a clean stop, chapterselect (between-chapters loading screen) advanced via
      Home+Enter (Continue is the start node; End would refuse on a locked chapter station).
      Chapter 2 reached and its save written 2026-07-18. 2026-07-19 closed: the slot holds
      Chapter_1..5.dat snapshots (SaveChapterState writes one per chapter start), and any
      chapter is reachable on demand from the chapter select screen via /eval
      GameLoadingScreenBehaviour.LoadChapterItem_Click(N) - chapter availability is purely
      GameManager.Chapter >= N, and the quit auto-save re-stamps the slot marker. Used live
      to recover the slot after the driver's accidental rewind and to reach the chapter IV
      and V verification sessions.)
- [x] dropped - Full keyboard-only playthrough of the prologue + chapter 1 via dev server
      (user, 2026-07-19: the user will do their own keyboard playthrough regardless, so the
      mod-side bar is every screen confirmed working once, not a full mod-driven playthrough)
- [x] dropped - Sampled sweep across later chapters
      (user, 2026-07-19: same descope - the user's own playthrough covers this)
- [x] dropped - Stability: one multi-hour session
      (user, 2026-07-19: same descope. Data point stands: the 2026-07-18 mono Boehm GC crash
      ("Unexpected mark stack overflow") was dev-tooling aggravation - hundreds of /eval
      dynamic assemblies - not a shipping code path; Release sessions never hot-reload or
      eval, and the multi-hour eval-free driving legs since have been stable)
- [x] verified - Second-pass code review of everything user-facing (self /code-review), fix findings
      (2026-07-19 review agent swept all three projects; five findings, all confirmed against
      source and fixed: (1) critical - unguarded Text() delegates in GraphAnnouncer.LeafText/
      FirstPartText could throw during focus compose and relock every frame (Tick unwinds
      before InputManager runs - permanent keyboard freeze in Release); now guarded + logged
      like the sibling KeyGraph/WatchLive sites. (2) ModuleLoader now disposes a module whose
      Load() threw partway, so half-applied patches cannot leak (host change - deploys at next
      restart, compile-checked via Release build). (3) CaseUnavailableReason relation rows
      spoke the category word INSTEAD of the threshold number; now "N (Word)" like
      TriggerRelationRow. (4) first-open localization race closed on four more surfaces via
      LocalizedLabel: Settings title + all row captions, Map city labels (now TitleKey terms),
      Empire office headers, Character deaths label. (5) ChapterSelect help readout no longer
      interrupts (only OnTooltip site that did). Clean-bill areas: no cached state, no inline
      strings, no silent catches, patch lifecycle correct, dev-server threading correct.
      2026-07-19 fresh-restart first-open pass (new process, ModuleLoader host fix deployed):
      Settings title + all 9 rows, Character (incl. deaths row), Empire (office + parameter
      rows), Map (cities + province), plus Family as a bonus - every announcement English on
      the very first open. The same pass surfaced TWO MORE first-open races, both fixed with
      the same LocalizedLabel pattern: chapter select's Continue row spoke the serialized
      Russian editor text (all three ChapterSelect label reads now localize-aware), and the
      chapter-title splash popup spoke Portuguese through the generic sweep (PanelSweep row/
      join/button reads now localize-aware). Their own first-open confirmation rides the
      Release-smoke restart; the pattern is proven on the five screens above.

## Phase 9 - Wrap-up (personal-only mod: no installer, no player docs, per DECISIONS.md)

- [x] verified - Release build profile: dev server compiled out; verify the Release deploy still
      speaks and navigates (one smoke pass)
      (2026-07-19: Release dlls deployed to the plugin folder, game launched with
      BRANTE_NO_SPEECH=1 (speech must not reach the user's reader and Release has no mute
      endpoint): clean load, all 34 screens registered, module generation 1 ticking (key-repeat
      probe logged from first tick), zero errors after menu settle, port 8772 refused - the
      dev server is compiled out. Speech readback is impossible without the dev tap by
      construction; the speech pipeline is profile-identical host code, and every live
      verification ran the same pipeline. Debug profile redeployed and re-verified live
      afterwards (Continue -> save slot -> chapter V scene delivery).)
- [x] verified - KEYS.md: short key reference for the user, generated from the actual bindings
      (2026-07-19: written from ModModule.RegisterActions and cross-checked against the live
      /nav bindings dump (15 actions, exact match); typeahead and secondary-action wording
      checked against GraphNavigator/LoadWindowScreen code - search focus follows matches so
      Enter activates normally, Backspace's only current use is the load-window delete)
- [x] verified - Final DECISIONS.md review pass; anything that deserves the user's attention
      summarized at the end of the run
      (2026-07-19: full read-through done; endgame closure calls appended (GameOver scene
      judgment, interlude key qualification, Russian game-restart popup parity, Release smoke
      scope); user-facing summary delivered in chat at run end.)
