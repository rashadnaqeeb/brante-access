# Brante Access - Accessibility Mod for The Life and Suffering of Sir Brante

Screen-reader accessibility mod for blind players. Goal: the ENTIRE game playable with
speech and keyboard only - no mouse interaction required anywhere. Speech is the sole
interface: a silent failure or stale announcement is invisible to the player, so log
loudly and verify by driving the live game, never by assuming.

Sibling projects (read these before inventing anything - most problems here are already
solved there):
- `C:\Users\rasha\Documents\wotr-access` - Wrath of the Righteous mod. Same runtime family
  (Unity/Mono/net4x). Port from here: speech backends (Prism/SAPI/clipboard), input manager +
  OS-matched key repeat, screen stack (poll-and-diff), graph navigator (KeyGraph/GraphBuilder/
  GraphAnnouncer, two-tier ControlId focus reconciliation), typeahead, localization layer,
  Mono.CSharp dev REPL.
- `C:\Users\rasha\Documents\Disco Elysium` - Non-Visual Calculus (IL2CPP, BepInEx 6). Port the
  dev-server ENDPOINT SET from here (/eval speech-settle capture, /input verbs, /wait per-frame
  predicates, /gui, /log in-band, /screenshot), but implement on the Mono/net48 stack like
  wotr-access (Mono.CSharp REPL, not Roslyn). Its CLAUDE.md documents the act-then-listen
  verification discipline - adopt it.

## Game facts

- **Engine**: Unity **2018.3.0f2**, **Mono** backend, x64 → `Assembly-CSharp.dll` fully
  decompilable and Harmony-patchable. uGUI + TextMeshPro, EventSystem-driven mouse UI.
- **Install**: `C:\Program Files (x86)\Steam\steamapps\common\The Life and Suffering of Sir Brante`
  (exe: `The Life and Suffering of Sir Brante.exe`; Managed dir under `..._Data\Managed`).
- **Content model**: ~478 Unity scenes, one per story event. No dialogue database: each scene's
  prefabs carry content as I2 localization keys. `TextController` pages passage text
  (`{Key}_Text_{n}` / `{Key}_ConsequenceText_{choice}_{n}`); `ParameterButtonChanger` is a choice
  (text key, stat effects, unlock conditions all serialized on the component, per-check results
  readable). `SceneStateMachine` exposes the event phase (show/pregame/setup/resolve/postgame/hide).
- **Wiring**: static `Messenger` string-keyed event bus (subscribe to the same events the game's
  views use: "GoToConsequenceEvent", "ChangeSceneTitleEvent", "WriteAnswerInLog", ...).
  Singletons: `GameManager`, `UIManager` (one opened-window slot + popup/tooltip slots),
  `ParametersManager`, `SoundManager`. Some flow rides Bolt visual-scripting graphs
  ("TalismanFlowMachine") - opaque to decompile; observe at runtime instead.
- **Text access**: read live TMP components, or `I2.Loc.LocalizationManager.GetTranslation(key)`
  with deterministic keys. Hero-name/`<el>` placeholders are substituted by the game's own
  helpers - reuse them, never re-implement.
- **Existing keyboard**: A/D/arrows page text, Escape pause, Space/Enter cutscene skip. All else
  is mouse-only - the mod owns it.

## Decompiled reference

`brante-decompiled/` (gitignored): `Assembly-CSharp/` (507 types) + `Assembly-CSharp-firstpass/`
(Steamworks). Full method bodies (Mono, not IL2CPP stubs) - read logic from here as ground truth.
Regenerate after a game update:
`ilspycmd -p -o brante-decompiled/Assembly-CSharp --nested-directories -r "<Managed>" "<Managed>/Assembly-CSharp.dll"`

Key files: `_Scripts/Managers/UIManager.cs` (window/popup slots), `_Scripts/AMVCC/Controllers/
TextController.cs` (passage pager), `_Scripts/AMVCC/Views/ParameterButtonChanger.cs` (choices),
`Assets/_Scripts/AMVCC/Core/SceneStateMachine/SceneStateMachine.cs`, `_Scripts/AMVCC/Views/
HudController.cs`, `_Scripts/AMVCC/Views/Windows/*` (all windows), `Messenger.cs`.

## Modding route

**BepInEx 5 (x64, Mono)** - the game has no native mod system. Plugin targets `net48`
(needs the .NET Framework 4.8 targeting pack), references game assemblies from `<Game>\..._Data\Managed`
and BepInEx's bundled Harmony (HarmonyX, use 2.x API). Prism (`prism.dll`) deploys next to the
game exe. Install for players = copy files (screen-reader-friendly); no admin, no registry.

Prefer Messenger subscriptions and reading live MonoBehaviour state over Harmony patches;
patch only where no event/state exists. Activation = invoking the game's own handlers
(`Button_Click`, `UIManager.ShowWindow`, `AnswerVM`-equivalents) so game logic and sounds run.

## Build & deploy

`dotnet build` from the repo root builds the plugin and (Debug only) deploys to
`<Game>\BepInEx\plugins\BranteAccess\` + `prism.dll` beside the exe. **Close the game first**
or the copy fails silently on locked DLLs - then you test a stale build. `-c Release`
compiles without deploying. (Exact project layout is created in the bring-up phase; keep this
section updated as it lands.)

Game control (game window does NOT need focus once the dev server forces runInBackground):
- Kill: `powershell -c "Stop-Process -Name 'The Life and Suffering of Sir Brante' -Force -ErrorAction SilentlyContinue"`
- Launch: `powershell -c "Start-Process 'steam://rungameid/1068500'"` (verify appid on first use;
  if direct exe launch works without Steam DRM complaints, prefer it and record that here.)
- Mute game audio via Settings volume sliders once reachable, or the SoundManager from the REPL,
  so unattended runs stay quiet. Record the chosen mechanism here.

## Dev driver (build FIRST, before any features)

In-process loopback HTTP server, Debug builds only, default port **8772** (`BRANTE_DEV_PORT`
overrides; `BRANTE_NO_DEV=1` disables; `BRANTE_NO_SPEECH=1` skips Prism init for headless runs -
spoken lines are still captured). Bring-up: launch game, then
`curl -s --retry 60 --retry-connrefused --retry-delay 1 http://127.0.0.1:8772/health`.

Endpoint set (port of Disco's, on the Mono stack): POST /eval (Mono.CSharp REPL, persistent
state, speech-settle capture appended), POST /input (verbs through OUR navigator), POST /wait
(per-frame bool predicate), GET /speech?since=N (+&wait=), GET /log?since=N (+&grep=),
GET /gui, GET /focus, GET /typeinfo?name=, GET /screenshot, GET /health. Threading: HTTP thread
enqueues; per-frame pump on the Unity main thread executes; /speech + /log read ring buffers
directly. The server forces `Application.runInBackground = true`.

**Verification discipline**: every feature is verified by driving the live game over HTTP and
reading back speech - act, then listen, in one /eval or /input + /speech pair. Read decompiled
source to discover structure; use the live server to confirm runtime values. A Harmony patch is
smoke-tested by calling the patched method from /eval and expecting the announcement.

## Hard rules (carried from the reference mods)

- **Speech never interrupts by default.** Queued speech is the house style; interrupt only for
  focus moves under held-key repeat.
- **Speak on delivery, driven by model state** - never off our own input. Find the same signal
  the game's view uses for "this is visible now" (game state, Messenger events, SceneStateMachine
  phase) and key announcements off that, once per new content.
- **Visibility gates**: hidden is not closed. Never let Enter activate through a window the game
  has hidden (animations, popup transitions). Gate activation on the game's own shown state.
- **Pass game text through** - the game already localizes; never re-translate or hardcode English
  copies of game strings. Mod-authored strings go through the mod's localization table
  (port wotr-access's `Loc`/`Message` layer) with enGB as the complete manifest.
- **Strip TMP rich text at the speech boundary**, keep raw text where links/markup matter.
- **Transcript pattern for the event scene**: passage rows + choices in one stop, no position
  chatter, silent re-home on new content, the delivery announcement is the speech.
- **Unavailable choices announce as unavailable with the failed requirement** (the per-check
  data is on `ParameterButtonChanger`), never hidden or silently dead.
- If a piece of code decides what words the user hears, it must be reachable by tests or the
  dev server; nothing user-facing gets verified by eyeballing code alone.

## Autonomous run protocol

State lives in the repo, not in conversation memory: **ROADMAP.md** is the work queue and the
single source of truth for progress. Each work cycle: read ROADMAP.md, take the next unfinished
item, implement, verify via the dev server, flip its status (todo → built → verified, with a
one-line evidence note), commit ("checkpoint: <item> - <what was verified>"). Judgment calls are
logged in **DECISIONS.md** (follow reference-mod conventions by default) instead of stopping to
ask. Every few items, run the regression sweep item in ROADMAP.md. When compacting, always
preserve: the current ROADMAP item and its status, the dev-server port, and any game-restart
state (was the game running, which save was loaded).
