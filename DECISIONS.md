# Decisions Log

Judgment calls made during the autonomous build, newest first. Convention: follow the
reference mods (wotr-access, Non-Visual Calculus) unless Brante gives a reason not to;
every deviation gets an entry here with the reason. The user reviews this file, not a
stream of questions.

## Cutscene subtitle speech respects the subtitles setting (2026-07-21, user-directed)

- Cutscene subtitles are I2 terms on SubtitlesTextChanger (verified live: all 37 terms
  translated in all 14 languages), the only in-language channel for the 12 languages with
  no voice-over (audio falls back to English everywhere except Russian/English).
- When ShowSubtitlesInCutscenes is off, the mod does NOT speak subtitle lines. The user
  rejected reading the Keys list to bypass the setting: if a player turns a setting off,
  they want it off. Same gate as the game's own view; the settings menu is accessible, so
  turning it on is always in the player's reach.

## Escape falls through to the game's own gate (2026-07-20, user-reported)

- **Found on the interlude popup: Escape was dead there** while a sighted vanilla player CAN
  pause during interludes (UIManager.Update opens pause gated only on IsEscButtonBlocked,
  verified False live mid-interlude). Cause: GameInputPatches suppresses UIManager.Update
  whenever one of our screens is active, and Escape's replacement is the focused screen's
  Back action - which InterludeScreen (and several other overlay screens) never declared.
- **Fix is one global fallback, not per-screen wiring**: ui.back now carries a Performed
  handler (GameUi.GlobalEscape) that InputManager already fires for any UI press the
  navigator does not consume. It replays exactly the suppressed vanilla read: toggle pause
  iff UIManager exists and IsEscButtonBlocked is false. Because it is gated on the very
  flag vanilla gates on, every screen matches vanilla automatically - cutscenes, credits
  and loading (flag true) stay Escape-dead as designed, and no future screen can reintroduce
  the gap by forgetting a Back action.
- **SceneScreen's own Back action removed** as redundant with the fallback - and it was
  UNGATED ShowPauseMenu, so it could pause where vanilla blocks (pregame/resolve phases).
  Screens whose surface has a specific vanilla Escape meaning (pause close, confirm cancel,
  credits skip, window back) keep their Back actions, which consume before the fallback.
- **Tab on the interlude was investigated and is correct**: one control group, and the HUD,
  though drawn, is genuinely dead for mouse users too during event scenes (every
  HudController click handler checks IsButtonsBlocked, True live) - nothing to reach.

## Full translation of the mod manifest (2026-07-20, user-directed)

- **Locale folders are named by I2 language code** (ru, de, fr-FR, it, es-ES, es-US, pl, tr,
  ja, ko, zh-CN, zh-TW) **except pt-BR, whose I2 code is empty in the shipped language source**
  (verified live). Because of that gap the mod resolves the folder from the I2 language NAME
  via a complete 14-entry map in LocalizationManager; the I2 code remains only as a fallback
  for languages a future game update might add.
- **Game vocabulary reused in translations** where the game names the concept: Iron Man mode
  (es-ES "Modo Duro", pl "Żelazny Człowiek", tr "Demir Adam", it "Uomo di Ferro", pt-BR
  "Homem de Ferro", zh-TW "鐵人格鬥者"; Russian has no Iron Man name - its UI frames the
  feature as "Возврат к главам"/"Запрещен", so the Russian string says chapter returns are
  forbidden), the relation stat (de "Verbindung"), Settings/Quit/Main-menu words from the
  game's own terms.
- **Comparator word order is a per-language template decision**: ja/ko templates are
  {value}{op} because their comparators are postfix counters (3以上, 3 이상); Turkish keeps
  prefix order using the spoken math-symbol forms (büyüktür/küçüktür/eşittir) plus
  en az / en fazla. This is exactly what the "word order lives in {0} templates" rule exists
  for - no code changed for it.
- **Role words follow NVDA's localized control vocabulary** per language (e.g. de
  "Schaltfläche"/"Auswahlschalter", pl "lista rozwijana", zh-CN "编辑框") - expert
  screen-reader users already know these exact words.
- **Key names stay untranslated where the physical key is meant** (Enter), except languages
  whose standard keyboards label it otherwise (es-ES "Intro"; de "Eingabe").

## Endgame closure calls (2026-07-19 evening, Phases 6/7/9)

- **The "silent GameOver entry" investigation split into one artifact and one real bug.**
  The observed silence was a dev artifact: UIManager's popup slot persists across raw
  LoadScene calls, so a stale chapter V interlude popup sat unconfigured through every
  dev-loaded scene and there was genuinely no new content to announce. The real bug found
  underneath: interlude page nodes used bare structural keys ("interlude:page:0"), so two
  interlude scenes back to back with no popup-free frame re-seat on an identical key while
  the screen stays focused and the differ never resets - the second interlude would be
  swallowed whole. Page keys are now qualified by popup instance id; the game instantiates
  a fresh popup per interlude, so real transitions always produce a new key.
- **The standalone GameOver scene got a minimal screen despite looking like cut content**
  (no code path loads it, its credits are serialized Russian dev strings, the scene has no
  exit - the shipping game-over ending is Credits_GameOver via WindowCreditsController).
  Bolt graphs are opaque, so reachability cannot be ruled out, and a player landing there
  would face total silence with no exit; two live-read text rows cost ~50 lines.
- **GameOverGameRestartPopup speaks Russian and stays that way**: its four I2 keys have no
  English entries, so the game itself shows Russian to every player - the mod reads the
  game's rendering (parity rule), and no code path calls the popup anyway.
- **Release smoke is log-scoped**: the dev server IS the speech readback channel and it is
  compiled out of Release by design, so the smoke verifies clean load, 34 screens
  registered, module tick, no errors, port closed. The speech pipeline is identical code in
  both profiles (backends live in the host, not behind #if DEBUG). The user's installed
  deploy remains the Debug profile, which is what every live verification ran against.

## Finals driver aftermath and save recovery (2026-07-19, Phase 8)

- **Killed the finals driver at the chapter-rewind confirm**: the fourth-death judgment
  offered "return to the mortal world"; the random driver confirmed a rewind to Childhood,
  which would have replayed a whole verified chapter for nothing. The finals evidence
  (epilogue slides, judgment choice rows, chapter select handoff) was already captured, so
  the run was stopped rather than allowed to loop.
- **The rewind reset the save slot's Chapter marker to 1** (chapter select then said "not
  reached" for II-V; availability is just GameManager.Chapter >= N). The slot's per-chapter
  snapshots (Chapter_2..4.dat) survived - only Autosave and Chapter_1 were rewritten - and
  `GameLoadingScreenBehaviour.LoadChapterItem_Click(4)` (the game's own handler, dev-invoked
  once from the chapter select) loaded Chapter_4 and restored the marker. No Chapter_5.dat
  was ever written by the playthroughs; chapter V access goes through playing into it or
  `LoadChapterState(5)`, which falls back to default parameters (fine for UI verification).
- **Full Saves backup** taken before any recovery, at the session scratchpad
  (`Saves-backup-20260719`). If save state ever looks wrong to the user, that snapshot is
  from 2026-07-19 evening, post-rewind but pre-recovery.
- **Game launch is now the exe directly, not steam -applaunch** (recorded in CLAUDE.md):
  -applaunch silently no-ops while Steam still believes the game is running, which cost a
  boot cycle after a taskkill; the direct exe boots fine with Steam in the background.

## Game-over and death-flow judgment calls (2026-07-19, Phase 6/8)

- **The story driver keeps the save alive with a sparse dev-eval (harness only, never the
  mod)**: three chapter IV runs in a row ended in a fourth-death trial, and each trial
  rewinds the entire chapter - random-choice driving was net-zero progress. advance.sh now
  resets the Death counter (the trial fires at 4) and floors Will at 15 once every 60 steps
  via /eval, so standard deaths still occur and verify their window in place while the
  chapter can actually finish. ~10 evals per 600-step leg, two orders of magnitude under
  the documented ~400-eval GC crash. This mutates only the throwaway test save; shipping
  behavior is untouched.

- **Epilogue scenes ride SceneScreen, not a new screen**: ConsequenceSceneController
  (070.* outcomes) is the same TextController transcript minus choices; a dedicated screen
  would duplicate the pager, delivery, and HUD wiring for zero spoken difference. SceneScreen
  detects either controller and treats the UniversalParametersGenerator control panel as its
  stat-panel root (rows deliver, Continue sweeps in).
- **Death-window text swaps settle 0.45s before speaking** (same constant as the interlude
  panel gate): a newly activated resolve pager displays the prefab's raw Russian placeholder
  for a few frames before the game populates it, and it was heard live (speech 470). Page
  turns stay immediate - the game sets their text synchronously in the click handler.
- **The story driver only aborts on mod errors; the game's own logged errors are noted and
  driving continues**: game-code exceptions can fire on paths the game survives, and
  aborting on them would block coverage past the surface that logged them. Mod errors
  still abort hard. (The fourth-death NREs first noted here later proved to be reachable
  only through a mod-side activation bug - see the next entry - but the driver policy
  stands: note, continue, and investigate after the run.)
- **The fourth-death trial's scene-reload is same-frame, and the mod must survive it**: the
  trial loops FourthDeath_* scenes (one per judgment question) by reloading the scene
  synchronously from a Bolt "Click" trigger - the death screen never observes an inactive
  frame, so no pop/push, and the watched pager becomes a DESTROYED Unity object that reads
  as null under Unity's overloaded ==. DeathScreen distinguishes real null (OnPop cleared,
  navigator seat announcement speaks) from destroyed (same-screen swap, deliver the new
  page after settle) with ReferenceEquals. Heard live: reload spoke only "next page,
  button, 2 of 2" before the fix; title + full page after.
- **Death choices activate through the game's Button, never a hand-picked method**: the
  first DeathScreen build called DeathChoiceButtonBehaviour.OnButton_Click() directly. In
  standard death windows that IS the button's onClick, but the fourth-death trial wires
  SceneButton_Click (broadcast GoToNextDeathSceneEvent, straight to the next trial scene)
  on the same prefab family. The direct call forked the game into prefab-leftover resolves
  and a Continue that is unreachable in real play - its Button_Click opens the broken
  AchievementPopup path (NULL DeathObjective, prefab fields unassigned), producing a
  three-NRE cascade (SetAchievementInfo, popup Start, DisactivePopup) and a self-disabled
  dead Continue every trial iteration. A mouse player never sees any of this. Activation
  is now UiWidgets.Click(choice.gameObject) so whatever handler the scene actually wires
  runs; wedged states were cleared by replaying the un-run tail of the game's own handler
  from the dev server (Bolt "Click" on TalismanFlowMachine / GoToNextDeathSceneEvent),
  which advanced cleanly with zero new errors.

## Conversion panel and panel-sweep judgment calls (2026-07-18, Phase 6)

- **Panel-swap deliveries repeat the panel's static title and description**: the conversion
  panel keeps one title/description across its 7 pages, so each page swap re-reads them
  before the changed rows. A row-level diff would trim the repeat but also drop rows that
  legitimately reappear on the new page (Perception is listed on several pages) - lost
  information for saved words. Kept whole; the navigator's interrupt-on-focus-move already
  lets an expert user cut a delivery short by paging on.
- **Growth-only deliveries speak just the new tail**: when a settling panel's swept content
  merely grows (rows stagger in slower than the 0.45s settle window), the already-spoken
  prefix is not repeated and focus stays put. Detected by string-prefix match on the sweep
  signature - a changed panel never prefix-matches because its row text differs, so it gets
  the full delivery plus silent re-seat.
- **Pager arrows are labeled by prefab object name** (LeftArrow/RightArrow to pager.prev/
  pager.next): the game's pager buttons are image-only everywhere, and the prefab names are
  stable structure like the transform.Find paths used elsewhere. The spoken words come from
  the mod's table, so the no-hardcoded-game-text rule is untouched.
- **ParameterComponent fold lives in PanelSweep, not per-screen**: every swept surface with
  stat rows (conversion panel, generated stat panels, popup family) gets one stop per stat
  with scales on Space, matching the dedicated windows' ParameterRows behavior. Texts under
  the component that are not the name/value/segment trio still sweep as their own rows, so
  composite rows (relation panels' character names) lose nothing.

## Map window judgment calls (2026-07-18, Phase 7)

- **Province rows carry a mod-authored "province" word (map.province template)**: the game
  distinguishes provinces from cities purely by map layout (regions are invisible hover
  zones), and no game string names the distinction - the description prose is too long for
  a label. Cities stay bare names, matching their on-screen labels.

## Window-close crash workaround (2026-07-18, Phase 5)

- **The mod writes ONE piece of game state: UIManager.OpenedTooltip gets an inactive stub
  GameObject seeded before clicking the HUD back button.** The game's own back handler
  dereferences that slot without a null guard, and only mouse-hover tooltip code ever
  assigns it - so every keyboard-only session crashed (NRE) on its first window close,
  stranding the window open with a dead back button. The alternative was a Harmony patch
  on WindowMainButton_Click; seeding the slot the game itself expects to be filled is
  smaller and lets the game's own code run unmodified. Any real tooltip open replaces
  the stub; the stub is inactive so the handler's SetActive(false) is a no-op. This is a
  bug workaround, not state caching - the never-cache rule is about reads.

## Empire window judgment calls (2026-07-18, Phase 5)

- **Office rows are hud.pair with the window's own header texts**: the Overseer and
  Patriarch rows pair the sibling "Name" header TMP ("Overseer" / "Patriarch") with the
  holder-name TMP, reusing the existing hud.pair template rather than a new key. The header
  text is the game's own localized label, read live at speech time.
- **Space reads a character description only where the game tracks a character**: the
  controller's Imperator/Patriarkh CharacterIdentification carries .Character for tracked
  office holders and leaves it null for "None"/hero outcomes; the tooltip stays silent on
  null rather than inventing a "no details" line - matching the game, which shows nothing.

## Relations window judgment calls (2026-07-18, Phase 5)

- **Build gate is the controller's own model query, not the placeholder**: the window prefab
  ships with the placeholder active and its text unlocalized (its TextMeshProLocalization
  runs on enable, which never happens when characters exist); the tile list only appears in
  the controller's Start. Gating on the placeholder spoke raw Russian on the first frame
  (caught live). The gate now counts unlocked non-family characters exactly as
  GenerateCharacterList does and waits until that many tiles exist; the placeholder is
  trusted only for a genuinely empty list, where the game activating it has also localized it.
- **ScreenName term is HUD.Relation (singular)**: HUD.Relations returns empty; the singular
  term is the HUD button's own label term and translates to "Relations".

## Chapter final window judgment calls (2026-07-18, Phase 5)

- **Timeline rows fold the year from the year headers above them**: the game renders the
  chapter timeline as year-header items interleaved with event items in one list; a speech
  row per event folds "event, branch, year N" (year label and value are the game's own
  texts), so year headers are not separate stops. Same fold rule as elsewhere: detail rides
  the item, one keypress saved per row.
- **Timeline rows get no Space detail**: their ObjectiveInitializer components are blank in
  the shipped game (Objective null, description empty - verified live), and TimelineComponent
  carries only the texts already folded on. Nothing withheld; there is nothing more to read.
- **IsActive gates on the left page's active+alpha, not controller presence**: the
  ChapterFinalWindowController GameObject exists before the pregame gate lifts and lingers
  at alpha 0 through the hide animation while the next chapter loads. Presence-only IsActive
  masked the between-chapters chapter-select screen (caught live: screen stack stayed
  "chapterfinal" a full minute after the continue press). Hidden is not closed, both ways.
- **A duplicate loading-screen cover was written and then deleted**: the between-chapters
  loading screen looked uncovered (screen stack empty), so a LifeTimelineScreen was built and
  live-verified - then ROADMAP review showed ChapterSelectScreen already owns that surface
  (verified earlier, including this variant); the stack had been empty only because of the
  chapterfinal masking bug above. The duplicate was removed the same session; the surface
  walk it performed stands as extra evidence for the shared row layout. Lesson recorded:
  grep the Screens directory for the game component name before writing a new cover.
- **ParameterRows trims the name-value pair**: the chapter final's Deaths row has an empty
  value text (the game shows only the segment word), which left a dangling space before the
  comma; the shared label now trims, all callers inherit.

## Home window judgment calls (2026-07-18, Phase 5)

- **Parameter rows extracted to a shared ParameterRows helper**: the Home window fills its
  ParameterComponent rows byte-for-byte the way the Character window does (same controller
  code shape: Name from I2, TextValue from the model, Descr from CheckSegment), so the folded
  row plus Space-scale-readout node moved from CharacterWindowScreen into Screens/
  ParameterRows and both windows call it. Future stat windows (Empire era panels) reuse it.
- **Populate gate compares a parameter name with its own I2 translation**: Home has no hero
  name to gate on (Character precedent); instead the first ParameterComponent's Name text
  must equal GetTranslation of its ParameterGetSet asset's enum name - exactly the write the
  controller's Start() performs, so the gate opens on the game's own populate result for
  both the normal and broken-family panels.
- **Heir row is plain hud.pair from the live component texts**: the controller's SetHeir
  already resolves the heir name (hero full name with the noble-sword el, or the sibling's
  true name) into the TextValue text; re-deriving it mod-side would duplicate GetHeirName
  logic for nothing. Guarded by Visible(Heir) since the broken panel hides it.
- **Broken-family panel coded but unverifiable in chapter 1**: same parameter sweep (the
  active-panel component search covers whichever panel the game shows) plus the collapse
  objective row reading Readouts.ObjectiveDetails on Space. Needs a chapter 3+ save where
  СемьяРаспалась is set - listed as a deferred confirmation on the ROADMAP item.
- **Dev-only gotcha, not a mod behavior**: opening a window by calling the HUD handler with
  a null sender NREs inside UIManager.Show at OnSelect and skips the ShowBackButtonEvent
  broadcast (back button never appears). Verification opens windows by clicking the real
  HUD button GameObject through UiWidgets.Click.

## Destiny window judgment calls (2026-07-18, Phase 5)

- **Objective conditions speak the earn-popup's rendering, not the hover panel's**: the
  window's own hover panel prints raw serialized keys ("TrainWithDad", "Activity"), while
  the earn-popup translates operands through I2 with the el substitution ("Train with
  Father", "Determination"). The popup path is the authored one; Readouts.ObjectiveDetails
  follows it, with or-flagged rows folded into one any-of group. Operator symbols (the
  serialized "≥"/"≤"/...) map onto the existing choice.op words - same reasoning as choice
  requirements: readers voice bare symbols unreliably.
- **Achieved state is a mod word** ("achieved"): the game marks an earned objective only by
  black-vs-gray name color, no string exists. Unachieved is the common state and stays
  silent.
- **Locked chapter tabs refuse with the plain unavailable word**: the game attaches no
  reason string to them (gray only); inventing "reach chapter N" wording would be
  re-authoring game text.
- **UiWidgets.LocalizedLabel guards the TextMeshProLocalization race**: that game component
  localizes its label in Start(), one frame after a window prefab instantiates, so entry
  announcements read the serialized Russian. The helper resolves the component's own I2
  keys (the same composition its handler applies) instead of the raced TMP text - use it
  for any label carrying that component.
- **The Destiny timeline pane is dead code** in the shipped game: its
  DestinyWindowLoadCompleteEvent handler is empty and nothing else instantiates timeline
  rows, so the screen reads nothing there. The interlude timeline is a separate controller
  and a separate ROADMAP item.

## Family window grid + info sub-screen judgment calls (2026-07-20)

Supersedes the 2026-07-19 flattened-treeview design below: the user directed the layout to
mirror the visual tree exactly, with Enter opening a menu of the extra info and Escape
closing it.

- **Generations are grid rows sharing one row key**: StartRow("family:gen") per generation
  makes Left/Right walk a generation and Up/Down move between generations preserving the
  column position (Down then Up round-trips), which is the closest audible analogue of the
  visual layout. Columns only approximate the drawn tree (tile counts differ per row and
  the connecting lines are an opaque sprite), so no parent/child column mapping is claimed.
- **Member rows speak what the tile shows** (name, role, selected) - estate, relation,
  status, and the description move to the info sub-screen, like the sighted panel. Space
  keeps the one-keypress description+status readout on the row itself.
- **Enter = the game's own click plus PushChild(FamilyMemberInfoScreen)** - first use of the
  child-screen chain. The click keeps the sighted panel in sync and runs the game's select
  logic and sound; the sub-screen reads the same model calls at speech time. Escape
  (ActionIds.Back) removes the child; the Family screen's kept state re-lands on the member.
- **The selection watcher is removed**: every selection change is now our own Enter, whose
  delivery is the info screen itself - "X selected" on top of it would be pure redundancy.
  The row still carries the selected-state part (spoken on focus, and it makes reopening
  land on the game-selected member via the stop-landing heuristic). member.selected stays
  for the Relations window.
- **Status row is always present, like the panel**: "Status <term>" via hud.pair; a set
  status folds the game's help-icon detail on (tooltip.section); CharacterStatus.Good
  translates to a bare dash, which maps to state.none through the new shared
  Readouts.DashAsNone (PanelSweep's private copy now delegates to it).
- **Info sub-screen rows are read-only labels in panel content, mod order**: description
  first (the marquee content - Enter then immediately hears it), then estate (bare word;
  the game has no Estate header term), relation (the game's Relation label), status.
- **The info sub-screen has no HelpText** (2026-07-20 bug fix): inheriting the Family
  window's WindowHelp made Space recite the window's what-this-is blurb inside a member's
  panel - wrong scope. The rows already carry the panel's full content including the status
  help-icon detail, so Space honestly says "no tooltip"; window help stays reachable on the
  tree behind Escape.

## HUD bar blank locked buttons (2026-07-20)

- **Skip = exactly "nothing visually readable"**: the game authors HUD.WillOpen terms for
  chapters 2-5 only; a locked button whose ChapterToUnlock has no term (the prologue's
  Character/Family/Destiny/Home, all ChapterToUnlock=1) renders as a darkened icon slot
  with no name and no tooltip - a sighted player learns nothing, so the bar emits no node.
  Locked buttons with a term keep speaking the game's own unlock line, and names are still
  never spoken while locked (the game withholds them until unlock).

## HUD window sighted-parity audit judgment calls (2026-07-20)

- **Destiny objectives always carry a state word**: the game marks achieved-vs-not only as
  black-vs-gray title text, invisible to speech - a gray "Dark Times" title read bare sounds
  like something that happened (the user's "leaking a ton of spoilers" impression). The
  titles themselves are NOT spoilers: the game deliberately shows every objective of the
  active Post from chapter start, gray with hoverable description and conditions, as goals
  to aim for. Parity is the state word, not hiding rows.
- **Destiny categories read in sibling order, not field order**: the window's serialized
  category fields (Person, Family, Work, Final) do not match the on-screen column; the
  shared parent's sibling order does (Chapter Outcomes first on late chapters). Ordering by
  GetSiblingIndex keeps the spoken order whatever the game shows, per panel, forever.
- **Revolt window speaks only its sighted surface**: the previous build appended both sides'
  full victory-condition trees (33 rows with live met state) sourced from the side popup's
  model. No sighted player can see those in this window: the prefab's win-condition panels
  are inactive unfilled placeholders (untranslated editor text, never touched by code) and
  the side icons' tooltip is dead code (TooltipEventHandler listens for
  "InsurrectionSideEnter"; no broadcaster exists in the assembly). Conditions stay where the
  game really shows them - the one-time side popup (screen kept, helpers moved there) and
  the Destiny objective hovers. Reading more than a sighted player sees is the same parity
  bug as reading less.
- **Home heir row last**: the game draws the heir line at the bottom of the page, below the
  household stats; spoken order now matches.
- **Popup smoke-test stopped at reflection**: force-showing the one-time side popup would
  set its PlayerPrefs seen-flag and its close button broadcasts "CheckEventsInState" into a
  live story scene - real risk to the user's playthrough for code that moved verbatim and
  was already live-verified via the window's identical path. Every reflected field name was
  instead re-verified resolving against the live type (missing fields throw loudly at first
  use, per the class comment).

## Family window tree redesign judgment calls (2026-07-19)

- **Flattened treeview, not a 2D grid**: the user asked for the family tree to be "browsable
  in a tree shape". A grid (Up/Down between generations, Left/Right within one) matches the
  visual layout but forecloses expansion - the engine's tree semantics live on Left/Right at
  row edges, and BeginGroup cannot open inside a horizontal row. The flattened treeview gives
  both asks with the standard screen-reader tree gestures: Up/Down walks members in visual
  tree order, Right/Left expand into and collapse the per-member description text.
- **Generations come from the game's layout, not hardcoded genealogy**: tiles are grouped by
  their row container under FamilyTree (FirstRow/SecondRow/ThirdRow in the prefab), rows
  ordered by container Y, members by sibling index. Parent/marriage lines exist only as the
  opaque Grid sprite, so per-person parentage is NOT modeled - the roles the game writes on
  each tile (Grandfather, Father, Ex-wife, You) already carry the genealogy.
- **No authored generation labels**: a "Parents"-style header would be mod-invented genealogy
  (Amalia is the father's ex-wife, not the hero's parent) and pure chatter for expert users;
  the role word is the second thing spoken on every row. Generation boundaries are conveyed
  by the per-generation position restart (PushContext with an empty label per row).
- **Detail children use structural-only ControlIds**: a child carrying the tile as its
  Reference would win the header's tier-1 focus match during Reconcile and yank focus off
  the child on every rebuild (every frame). Headers keep the tile reference; children are
  ControlId.Structural(headerKey + ":description"/"":status").
- **Populate gate is the WhoIs text matching the translation Start writes.** The old gate
  (tile name StartsWith HeroName) went permanently silent on a save whose hero name is empty
  - HeroName='' is a legitimate saved state, not "still loading". A draft gate on "WhoIs
  non-empty" leaked Gloria's tile pre-populate (her prefab placeholder carries non-empty
  serialized Russian). The exact-translation compare mirrors StatusRelationGetSet.Start
  byte-for-byte: non-hero tiles GetTranslation(CharacterObject.WhoIs), hero tile
  GetTranslation("Hero"). In a locale where the placeholder equals the translation the gate
  passes early, and the text spoken is the text the game would write anyway.
- **Children are built only while expanded** (b.IsExpanded before declaring them) so the
  ParametersManager description/status lookups stay off the per-frame path for collapsed
  members; TreeRight's empty-expansion auto-recollapse speaks "no details" if a member ever
  has neither child.

## Family window judgment calls (2026-07-18, Phase 5)

- **The per-character info panel folds onto the member rows** instead of mirroring the game's
  select-then-read flow: each row re-runs the same ParametersManager calls the game's own
  select handler makes (relation, status, estate) at speech time, and Space reads the
  description plus the status tooltip. Selection still works (Enter runs the tile's button)
  but is never required to hear anything.
- **Status is skipped when CharacterStatus.Good**: the game renders a bare emdash for the
  status value and placeholder junk ("Status description") for its detail, and hides its
  help icon. Non-Good statuses speak the game's status term and description verbatim.
- **Hero row reads name and role only, no model calls**: the game itself never queries
  relation/status/description for the Hero (its select handler redirects to the Character
  window instead - GetCharacterStatusKey would throw on the Hero, who is absent from the
  characters list). Enter on the hero tile follows the game's redirect.
- **Tile selection is announced as a model-watched delivery** ("{name} selected"): selecting
  changes no focus, so the row's Selected-state word alone would never be heard. OnUpdate
  watches which tile the game disabled (SelectCharacter's own marker) and speaks once per
  change; seeded on focus so entering a window with a pre-existing selection stays quiet.
- **Member names come from the live tile texts, not GetCharacterTrueName**: the game's own
  tiles are already populated with the true names (including the sword-noble and Gloria
  status variants), and KeyChapterParametersController.GetCharacterTrueName NREs for
  characters outside the master list. Two-line tile names collapse to one spoken line.

## Soul window judgment calls (2026-07-18, Phase 5)

- **No dedicated screen - the Soul window is cut content.** Nothing in the shipped game
  reaches it: no HUD button exists for it, a live sweep of every Button's persistent onClick
  wiring found no caller, and the only code path (HudController.WindowSoulButton_Click) is
  itself never called. Its localization is abandoned too - Window_Soul.Title is empty and its
  three parameters (Volition, Rule, Love) have no name or segment terms, so the game itself
  would render blank rows; a dedicated screen could only speak naked numbers. The
  WindowShellScreen covers it generically (verified live by force-opening through the game's
  own handler: HUD bar + back button present, Escape returned to the scene). If a game update
  ever wires it in, the shell announces it and the folded-parameter-row pattern from the
  Character window drops in.
- **The ROADMAP item's stated content (willpower, death mechanics) lives elsewhere**:
  willpower is an ordinary Character-window parameter row and deaths are the Character
  window's skull-strip row, both already verified there.

## Character window judgment calls (2026-07-18, Phase 5)

- **Dedicated window screens stand the shell down by name**: WindowShellScreen keeps a Covered
  set of window prefab names; a dedicated screen (same layer, name-gated IsActive) adds its
  window there. One list, no registration protocol.
- **Freshly-shown windows gate their graph on the game's own populate result.** The prefab
  instantiates with serialized placeholder text and the game fills it a beat later (the
  Character window's entry seat spoke Russian prefab text live). The gate is a field the
  game's Start() writes (hero name == ParametersManager.HeroName), not a frame count.
- **Pair rows read the game's layout**: a label-value pair ("Age:" + "6 Years") is spoken as
  the pair container's visible texts in order - both words are the game's, no authored labels.
- **The skull strip speaks as a count**: its game label ("Deaths") plus the Death parameter
  value out of the strip length ("Deaths, 0 of 4") - the sprites are the only visual encoding,
  the parameter is the model behind them.

## HUD bar judgment calls (2026-07-18, Phase 5)

- **Bar labels come from the game's own tooltip lookup**: a HUD button's GameObject name IS its
  I2 term (SimpleTooltipPointerEnterHandler translates sender.name), so the bar speaks the
  exact localized window names the game shows on hover, including "Back to Scene" (HUD.Back).
  A locked button's inline reason and its Enter refusal are the game's own HUD.WillOpen{n}
  tooltip string.
- **The pressed-window marker is the bar's selected state.** HudButtonBehavior._isButtonClicked
  is the game's own record of which window's button is pressed; it drives the "selected" word,
  the Tab landing on the open window's button, and the spoken window name on open/switch.
  Read live via reflection - never mirrored.
- **Window shell screen replaces the silent window placeholder**: while a game window has no
  dedicated screen yet, the HUD bar is its whole graph - so any window can be opened, switched
  (delivery keyed off the game's one opened-window slot), and left (back node or Escape, both
  running the game's own back button). Dedicated window screens will take over per window;
  the shell stays as the fallback for the rest.
- **IsButtonsBlocked is honored by construction**: bar activation goes through Button.onClick
  into the game's own Window*_Click handlers, which all check the flag themselves. A blocked
  Enter is the same silent no-op the sighted player's click is (verified live both ways).
- **Enter on the selected button closes its window** - that is the game's own toggle in
  UIManager.Show (same-name window destroys and returns to the scene). Kept as-is: it matches
  the mouse behavior and gives a second way home.
- **Home/End are vertical by design** (first/last along the column axis); on the one-row bar
  they jump between the info row and the buttons, and the ends of the row are reached with
  arrows. Not worth a per-screen override.

## Animation gating judgment calls (2026-07-18, Phase 4)

- **Keyboard activation honors blocksRaycasts.** The game blocks input during scene animations
  by switching CanvasGroups' blocksRaycasts off, which only stops the MOUSE - our ExecuteEvents
  path goes around the raycaster. UiWidgets.Click and Interactable now walk the group chain and
  refuse what a mouse click could not reach. The gate lives at the shared boundary, not
  per-screen, so every current and future surface inherits it.
- **A blocked activation with no refusal branch is a silent no-op**, matching what the sighted
  player gets clicking a mid-animation panel (nothing). Nodes that already carry refusal
  branches speak the unavailable word as before.

## Tooltip readout judgment calls (2026-07-18, Phase 4)

- **Tooltips are composed from the model, never opened.** Space speaks what the game's hover
  tooltip renders, built from the same serialized data (ParameterButtonChanger conditions and
  effects, ParameterGetSet's Parameter asset) - the house rule against reading a tooltip view
  that lags focus. The game's tooltip gates are honored exactly: conditions only when
  NeedCheckPossible, consequences only when UseConsequence and not NoConsequence and not
  story mode.
- **Every condition row carries met or not met plus the live current value** ("now 0" - the
  game's own Now word). The visual tooltip encodes met-ness as row color; speech needs words,
  and current values are what the sighted player reads off the same tooltip.
- **The current scale segment is marked by the game's "now" word spoken first** ("now, 10 to
  19, Full of Power"): the game marks it with bold and color; "selected" would be wrong (it is
  a value fact, not a selection).

## Death window judgment calls (2026-07-18, Phase 4)

- **DeathScreen sits at layer 21** for the same popup-slot reason as the interlude: the death
  prefab lands in UIManager's one popup slot, so the specific screen must outrank the generic
  popup sweep (20); the slot guarantees they never coexist.
- **Death choices read in column-major order** (left page column top to bottom, then right):
  the six buttons live under two parents (LeftButtons, RightPage) and FindObjectsOfType
  returns reverse-instantiation order, so the sort is geometric - x ascending then y
  descending - which reproduces the game's own ButtonDescription1..6 numbering. Sibling index
  alone interleaves the columns.
- **Placeholder rewrites re-deliver.** The death prefab shows its serialized placeholder title
  and page text ("New Text", an untranslated block) for a beat after ShowDeathPopup before the
  game writes the real content; entry announcements catch the placeholder. Title and
  current-page text are signature-diffed each tick and re-spoken on rewrite - same tolerance
  as stat-panel re-delivery: the player must hear the final state, and the mod cannot know
  which rewrite is the last.
- **Death flow is smoke-tested by firing the game's own loader from /eval** (death parameter
  +1, DeathScenesLoader.LoadFirstDeathScene()), since reaching a real death organically takes
  chapters. Restoration afterward: SetParameterValue with the negative delta (the API is
  additive) and WasTriggered = false on the fired DeathEventInfo (a public field). The real
  trigger path (TextController's IsDeathTrigger block) calls the identical loader entry.

## Stat-change delivery judgment calls (2026-07-18, Phase 4)

- **Stat panels are delivered whole** (all visible rows joined), not row-by-row via focus: the
  panel appears under a player whose focus sits on the Continue button, so a seat announcement
  would read one row or nothing. The joined rendered text is the game's own localized
  composition; the swept rows stay in the graph for re-reading.
- **A panel is re-delivered when the game's own animation rewrites a row** (status "—" settling
  to GRATEFUL re-spoke the panel). Tolerated: the final state is what the player must hear, and
  suppressing it would require guessing which row changes are cosmetic.
- **The scene's dedicated Continue node yields to the sweep's button node** when the game's
  Continue lives inside the stat generator - two nodes sharing one backing button would
  double-list it and confuse reference-tier focus reconciliation.

## Chapter-flow and choice judgment calls (2026-07-18, Phases 4-6)

- **One click on a choice applies it - there is no confirm step in this build.** The game's
  own GetButtonClickedState double-click gate is what the mouse UI uses, and our activation
  path drives it twice in one Enter, matching how the game treats a committed click. The
  description folded onto the choice row is the read-back; a mod-invented confirm dialog
  would add a keypress the sighted flow does not have.
- **Choice list was taken before the scene title/date item** because the live game sat on an
  open choice list when the work cycle started - verifying against live state beats queue
  order.
- **InterludeScreen sits at layer 21, above the generic popup's 20.** Both read
  UIManager's popup slot; when the open popup IS the interlude, the specific screen must win
  or the sweep reads the same panel with no pager semantics.
- **PanelSweep exists for game-composed surfaces** (generated stat panels, information
  popups, the begin-chapter page): there the rendered TMP/legacy text IS the game's own
  localized output of its model, so reading it live is reuse, not caching, and per-surface
  adapters would duplicate the game's composition.
- **Pager arrows get mod labels** (pager.next / pager.prev): the buttons are image-only,
  no game string exists, which is the sole condition for authoring one.
- **Chapter select IsActive is behaviour presence, not scene name**: the same
  GameLoadingScreenBehaviour page appears additively from the load window AND standalone
  between chapters under scene names like LoadingScreen_Child; the between-chapters variant
  has no BotPanel, so the back-to-menu node is conditional on the transform existing.
- **After a chapter-start page turn, focus deliberately stays on the pager button** (it is
  Referenced and survives the rebuild), so reading the whole book is one keypress per page;
  the page announcement itself is the delivery.
- **Tolerated noise, revisit in polish**: pager/begin buttons speak stop positions ("2 of
  2"); scene boundaries can double-announce (popup re-seat, stale chapter title spoken
  before ChangeSceneTitleEvent lands). Logged so the sweep does not chase them as bugs.
- **"Tempos de paz" popup title is Portuguese leaking in the game's own I2 data** for the
  transition popup; we speak exactly what the game shows, so no mod-side fix.

## Scene transcript judgment calls (2026-07-18, Phase 4)

- **Transcript rows use Structural ids, not Referenced.** Every page row shares the single
  TextController as its backing component, and reference-tier focus reconciliation matches the
  first node with that reference - focus snapped back to page 1 on every rebuild (caught live:
  End bounced straight back). Rule of thumb recorded: Referenced ids need a DISTINCT component
  per node; rows carved out of one component's data are Structural.
- **Only the newest page row advances on Enter** (through the game's NextPage, gated on the
  game's own NextButton.interactable). Older rows are read-only history: re-reading must never
  mutate game state, and the game's own PreviousPage re-pages the shared panel, so it is not
  used for review at all. This is strictly better than the game's keyboard, which can only
  re-page.
- **The scene screen claims Escape for ShowPauseMenu** - the game's own Escape read (in
  UIManager.Update) is focus-suppressed while a screen is active; this fulfils the handoff
  noted in the pause-menu entry.
- **Speaker prefix rides the page row** ("Name: text", from the block's Character through the
  game's GetCharacterTrueName) - built now, pending live verification when a portrait line is
  reached (the Intro has none); the speaker-attribution ROADMAP item stays open for that.

## Credits judgment calls (2026-07-18, Phase 3)

- **Skip is Escape only, not the game's Escape/Space/Enter trio.** In mod screens Space is
  tooltip and Enter is activate; Escape is the house back key everywhere. Splitting the trio
  would make credits the one screen where Enter closes instead of activating. The skip invokes
  the game's own handlers (LoadMainMenu(), or the ItsGameOver keyboard branch mirrored from the
  suppressed Update body); the ending-credits variant is code-mirrored but pends live
  verification until an ending save exists.
- **Screen name is a mod key ("credits")**: the scene has no title text and the menu button's
  label is not an I2 term (checked the full terms list for an English "Credits" match - none).
- **Rows are ordered by sibling index, not world y.** The graph builds on the scene's first
  frame, before the layout group positions the blocks; a y-sort captured pre-layout garbage and
  read the roll backwards (caught live - fresh entry announced the final THANK YOU block as row
  1). Sibling order under the Credits container is the reading order and is stable from frame
  one.
- **The full roll auto-returns to the menu on its own** (game-side, not the controller - likely
  the scroll animation's end). The screen just pops with the scene; no mod handling needed.

## Pause menu judgment calls (2026-07-18, Phase 3)

- **Focus-mode suppression now requires an active mod screen** (prefix skips game Update bodies
  only while FocusMode.Active AND ScreenManager.Current != null): with an empty stack - a
  surface the mod has no screen for yet - the game's own keys (Escape opens pause, A/D page
  text) are the only working keyboard, and dead keys are strictly worse than stock keys. Once
  the Phase 4 scene screen exists it will claim the surface and own Escape itself.
- **The pause screen announces the game's own window title, which is "Settings"** - same word
  as the main-menu Settings scene. Both are the game's labels; inventing a different word would
  break the label-with-the-game's-header rule. The two screens' contents differ audibly from
  the first node on.
- **Pause rows ship partly dead in this build**: the language cycler row and the Save/Load
  buttons are inactive GameObjects (Russian leftover text on the buttons). Inactive rows are
  skipped by the visible gate; the spinner code path stays for a future game update that
  re-enables the row.
- **Exit and chapter-restart confirmations share one ConfirmPopupScreen base**: both game
  popups carry identical serialized fields (_title/_description/_confirmButtonText/
  _cancelButtonText, read by reflection - no hierarchy names assumed). The exit popup never
  occupies UIManager's popup slot, so the generic popup PredicateScreen cannot mask it.
- **Escape on the pause screen runs UIManager.ShowPauseMenu** (the exact handler the game's
  suppressed Escape read calls), not the Resume button's animator-parameterized CloseWindow -
  same result, stock path.

## Save/load and chapter select judgment calls (2026-07-18, Phase 3)

- **Save slot control ids carry the game's SlotId** ("loadwindow:slot:0"): every slot clone's
  SaveSlot component sits on a GameObject named "Body", so reference-based ids alone collide as
  duplicates. The SlotId is the game's own stable slot identity.
- **A save slot is ONE row**: the game's slot text (hero, chapter, year, scene title) folded
  with its date, Enter loads, Backspace opens the game's delete confirmation
  (ShowSaveDeleteConfirmPopup). Delete as the secondary action instead of a separate stop
  follows the fold-detail-onto-the-item rule.
- **Main menu button availability keys off the model, not Button.interactable**: the game
  disables menu buttons as post-click guards (Continue while its window opens, New Game for 1s
  after click), and the live interactable watch spoke a bogus "unavailable" the moment a window
  opened over the focused button. Only Continue has a real unavailable state (no saves on
  disk); a click landing during a guard is the game's own silent no-op, same as the dimmed
  button sighted players see.
- **"Saves exist but Continue is disabled" gates the whole main menu screen off**: only
  LoadGameButton_Click leaves Continue disabled (until the load window's back button broadcasts
  UpdateButtonsState), so that state is the game's own "menu handed off to the load flow" flag.
  Without it the menu re-announced itself in the frames between LoadWindow, GameLoadingScreen,
  and the loaded scene.
- **Chapter select (GameLoadingScreen scene)**: lock state reads the item model
  (LoadChapterItemBehavior.Interactable + GameManager.IronManMode) - Button.interactable there
  is hover animation, not availability. Locked reasons are mod-authored ("not reached" /
  "Iron Man mode"); the game's only signals are sprite swaps. The age text (what the slider
  position shows) folds onto the Continue label; if the game withholds Continue (pending
  restart choice), the age becomes a plain text node instead. The game's restart help line is
  the Space tooltip on chapter items. Escape is deliberately dead on this page - the game
  blocks its own Esc there and Quit to Main Menu is an explicit button; auto-quitting on
  Escape would discard the loaded save silently.
- **Restart confirmation popup reads its four serialized TMP fields by reflection**
  (LoadPreviousChapterConfirmPopup._title etc., buttons = the TMPs' parent Buttons): no
  hierarchy names assumed, so a prefab relayout doesn't silently break it.

## Settings window judgment calls (2026-07-18, Phase 3)

- **Slider values announce as normalized percent** (position within the slider's own min..max),
  not the raw value: the game caps music at 0.6 raw, and the visual slider shows position - a
  half-full slider is "50 percent" to sighted and blind players alike.
- **The left/right cycler rows (Language, Screen format) use the combo box role** and adjust on
  Left/Right through the game's own arrow-button click handlers - to a screen-reader user an
  arrow-adjustable value picker IS a combo box; inventing a "spinner" role word would violate
  the no-per-screen-synonyms rule.
- **Resolution's unavailability reason is mod-authored** ("windowed screen format only"): the
  game's only signal is dimming the dropdown; the rule that unavailable controls announce their
  failed requirement needs a string and none exists in the game.
- **Settings row detection keys off SettingsWindow's own serialized references** (w.Sound,
  w.PictureAnimation, ...) matched to container rows, not name/heuristic scans - the game's own
  wiring is the ground truth for which control is which. Captions are TMP, found live (the
  CLAUDE.md both-text-types gotcha, hit in practice: legacy-Text-only scan built 1 node of 9).
- **The screen name is the game's own window title** ("Settings" TMP text), replacing the
  mod-authored screen.settings key - reuse beats authoring.

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
- **Unrevealed content never spoken by name** (2026-07-19): chapter select lists only chapters
  the save has reached (the game keeps later ones permanently deactive and unlabeled); a locked
  HUD button announces only the game's HUD.WillOpen{n} tooltip line, never the window's name -
  the game's TooltipEventHandler withholds the name term until Unlocked, so the mod's speech
  surface matches the sighted text surface exactly.
- **Focus mode toggle removed** (2026-07-20, user directive): the F10 toggle, the `focusmode`
  action, the FocusMode flag, and the focusmode.on/off + bind.focusmode strings (all 14
  languages) are gone - needing to toggle was a dev concept; the mod's keyboard ownership is
  unconditional. FocusModePatches renamed GameInputPatches; its predicate is now just
  `ScreenManager.Current == null` (stand down only where the mod has no screen). Earlier
  entries mentioning FocusMode/F10 describe the pre-removal design.
- **Verbosity toggle Ctrl+V** (2026-07-20, user directive): global `mod.verbosity` action;
  concise mode drops the Role and Position announcement kinds from navigator readouts via
  `GraphAnnouncer.PartFilter` (wotr-access's "Concise" preset minus tooltip - a Space/F1
  readout here is an explicit request, not a passive part). Feedback is the new mode word
  alone (verbosity.verbose/.concise, all 14 languages). State persists to plugin-folder
  prefs.txt (module statics reset every hot reload, so an unpersisted toggle would silently
  revert); default verbose. The chapter start/final page deliveries keep their "n of m" -
  that is content position in a pager delivery, not a navigation announcement.
- **Chapter start book flattened; the visible page follows focus** (2026-07-20, user
  directive: pager buttons + jump-back-to-top were confusing): all pages' rows build as one
  list from every panel (inactive panels' components hold their model text, so rows read
  fine before their page shows), each page's rows tagged with a per-page RegionKey; the
  screen's OnUpdate reads Navigation.FocusedRegionKey (new accessor beside FocusedStopKey)
  and clicks the game's own Prev/Next until the shown page matches - the game's visuals,
  dots and tooltips stay coherent, and Ctrl+Up/Down page jumps come free with regions. Page
  flips are silent: the row focus lands on is the speech. Button activation re-syncs the
  page first (the click path needs the panel active and raycastable), and the unavailable
  state only speaks on the shown page - an inactive panel misreports every button as
  unavailable. Gotcha reconfirmed: this Unity's GetComponentInParent skips inactive
  objects - the under-a-button text exclusion walks transforms explicitly.
- **Public release packaging** (2026-07-20, user directive): reverses the Phase 9
  "personal-only mod: no installer, no player docs" descope - the user asked for a one-zip
  release a player extracts straight into the game folder. `scripts/package.proj` (run via
  `package.ps1`, or `dotnet msbuild scripts\package.proj`) stages the vendored BepInEx zip
  (its extracted core doubles as the build's BepInEx reference path, so packaging needs only
  the game's Managed dlls, not BepInEx installed in the game), the Release plugin + lang/,
  prism.dll, player docs at the archive root (package-readme.txt as BranteAccess-README.txt,
  KEYS.md as BranteAccess-KEYS.txt), and redistribution licenses (LGPL-2.1 text for BepInEx,
  prism's LICENSES + NOTICE, the mod's MIT) into `artifacts/BranteAccess-<version>.zip`.
  Version is read from Plugin.cs's BepInPlugin constant - the single source. BepInEx's
  changelog.txt is dropped from the package (a bare changelog.txt in the game root would
  read as the game's or the mod's).
- **Intro cutscene made unskippable; skip promise removed** (2026-07-20, user directive after
  hitting the dead-air skip live: "just not allow for or announce the possibility of skipping
  a cutscene"): CutsceneIntro.Update joins the GameInputPatches suppression list and
  cutscene.intro drops its "any key skips" clause in all 14 languages. Ground truth from the
  decompile: the game's intro "skip" only stops the voiceover AudioSource - nothing in the
  assembly touches the PlayableDirector timeline (zero references), the advance is an
  asset-side animation event at the transition's natural end, so a "skipping" player waits
  the full visual length in silence anyway; worse, the handler marks
  GameManager.OpenedSceneName as shown, which the game's own Intro-scene preload has already
  repointed at the Intro event, so skipping also ate the At The End of Time scene (observed
  live: cutscene straight to chapter select; the 2026-07-18 no-skip run played it).
  Scoped to the INTRO cutscene only, not blanket: ChapterCutscene's Enter/Space/Escape read
  is the only code path that ever ends a chapter cutscene (LoadScene is called from that
  branch alone - suppressing it would trap the player permanently) and its skip is the real
  end-of-cutscene handler, verified advancing immediately at chapter V. Side effect
  accepted: the boot-time Cutscene_1 (before the main menu) is likewise unskippable now -
  same audio-only skip there, so the wait was always full-length; the narration simply
  keeps playing. Per user, shipped compile-checked without a live repro (game closed);
  first new-game flow next session confirms.
- **Offline rework batch: conversion book flat, CaseOfYear popup dedicated** (2026-07-20, user
  directive: "fix whatever you can without being able to confirm live... just fix offline and
  commit", reload allowed, driving forbidden to protect the live save): the audit's two real
  gaps built without live verification. The chapter-transition conversion book
  (ParametersConvertationPanelComponent) gets the chapter books' flat-list treatment inside
  InterludeScreen's post-close phase - every era page's folded parameter rows behind per-page
  regions, the visible page following focus through the game's own pager clicks, arrows
  dropped, the panel's ARRIVAL delivered once (page flips no longer re-deliver the whole
  panel). Where the conversion book's Continue lives is unknown offline, so both paths are
  built: buttons on a pager page activate after landing their page, buttons outside the pager
  sweep visible-gated. CaseOfYearPopupController gets a dedicated transcript screen (twin of
  the interlude main phase): under the generic sweep a multi-page case would page silently;
  its NextPage is UNGUARDED past the last block (decompile ground truth), so the newest row
  withholds Enter on the last block and the close row is the way on. The interlude's verified
  settle logic is extracted to UI/SettledDelivery (same behavior, now shared with the case
  screen). Live verify pends the next chapter transition / case beat.
- **Death book reworked to the transcript pattern** (2026-07-20, user directive on a live
  lesser death: "down arrow shouldn't progress you through it... press enter on the last line
  to continue" - the game-wide scene UX, not the flat book): delivered pages accumulate as
  rows, Enter on the newest turns via the game's RightButton_Click, the arrow stops are gone.
  Past rows are a model read, not a cache: the game itself re-renders any page from
  _textBlock[i].SpeechByKey through the pager's own InsertCharacterName on a backward turn,
  so rows re-derive the same way at speech time; only the NEWEST row reads the live TMP,
  because the fourth-death resolve rewrites it after Start (the settle/rewrite delivery
  machinery is unchanged). Page ids are qualified by pager instance for the fourth-death
  trial's same-frame scene reloads.
- **Click scrubs uGUI selection instead of restoring the previous one** (2026-07-21, space
  phantom-click regression from the full-pointer-sequence fix): Selectable.OnPointerDown
  selects the pressed button in the EventSystem, and the game ships Unity's default input
  axes where Submit maps to enter AND space (verified in globalgamemanagers), so every mod
  activation left a standing Submit target and the tooltip key pressed the last activated
  control (live evidence: HUD.Relation stuck selected in the user's session). Restore-to-
  previous was rejected: it would preserve a stale selection forever, including the one the
  buggy build already planted in any running session. Instead UiWidgets.Click clears any
  post-click selection unless the selected object is a text input (TMP_InputField/InputField
  receive typing through selection - name entry). This also removes Enter double-fire and
  arrows silently driving uGUI selection behind the mod's focus. Mouse-made selections during
  sighted co-pilot use are scrubbed on the next keyboard activation, accepted: the mod owns
  keyboard focus and uGUI selection navigation is unused by the game's own keyboard.
