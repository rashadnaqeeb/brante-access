# Brante Access - Accessibility Mod for The Life and Suffering of Sir Brante

Screen-reader accessibility mod for blind players. Goal: the ENTIRE game playable with
speech and keyboard only - no mouse interaction required anywhere. Speech is the sole
interface: a silent failure or stale announcement is invisible to the player, so log
loudly and verify by driving the live game, never by assuming.

Reference projects (read these before inventing anything - most problems here are already
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

**BepInEx 5 (x64, Mono)** - the game has no native mod system. Plugin targets `net472`
(Unity 2018.3's .NET 4.x Mono profile; drop to net471/net46 only if an API turns out missing at
runtime - record it here if so), references game assemblies from `<Game>\..._Data\Managed`
and BepInEx's bundled Harmony (HarmonyX, use 2.x API). Prism (`prism.dll`) deploys next to the
game exe. Both dependencies are vendored in-repo so no run ever needs the network:
`third_party/bepinex/BepInEx_win_x64_5.4.23.5.zip` and `third_party/prism/prism.dll` (with its
licenses).

Prefer Messenger subscriptions and reading live MonoBehaviour state over Harmony patches;
patch only where no event/state exists. Activation = invoking the game's own handlers
(`Button_Click`, `UIManager.ShowWindow`, `AnswerVM`-equivalents) so game logic and sounds run.

## Build & deploy

Solution `BranteAccess.slnx`, three projects (hot-reload split ported from Non-Visual Calculus):
- `src/BranteAccess.Core` - contracts only (IModModule, IModHost+ISpeech, IDevDriver, TextUtil).
  Pure BCL, no Unity/game refs. Loaded once; stable type identity across module reloads.
- `src/BranteAccess` - the permanent HOST plugin: BepInEx entry, speech backends (Prism/SAPI/
  clipboard), ModuleLoader, HostPump, dev server (Debug-only, `#if DEBUG`). Changing it (or Core)
  needs a game restart - their dlls are file-locked while the game runs.
- `src/BranteAccess.Module` - ALL feature code; the reloadable dll. Loaded from BYTES so the file
  is never locked: `dotnet build src/BranteAccess.Module/BranteAccess.Module.csproj` with the
  game RUNNING, then `curl -X POST http://127.0.0.1:8772/reload` (or F6 in-game) - new behavior
  is live in ~2s, no restart. A failed reload keeps the old module running and speaks + logs
  the failure.

`dotnet build` from the repo root builds everything and (Debug only) deploys to
`<Game>\BepInEx\plugins\BranteAccess\` (host+Core+Mono.CSharp; module under `module\`) +
`prism.dll` beside the exe. **Close the game first for host/Core builds** or the copy fails on
locked DLLs - then you test a stale build. Module-only builds deploy fine with the game running.
`-c Release` compiles without deploying.

Game control (game window does NOT need focus once the dev server forces runInBackground).
Steam **appid 1272160** (verified from appmanifest). Carried gotcha from the Disco project: do
NOT invoke `powershell.exe` from the Bash tool (the auto-mode classifier blocks it) and
`steam://` URLs silently no-op - use these exact forms:
- Launch: `"C:/Program Files (x86)/Steam/steam.exe" -applaunch 1272160`
- Kill: `MSYS_NO_PATHCONV=1 taskkill.exe /F /IM "The Life and Suffering of Sir Brante.exe"`
  (plain `taskkill` from Bash mangles `/F` into a path)
- If direct exe launch works without Steam DRM complaints, prefer it and record that here.
- Mute for unattended runs: `curl -s -X POST --data 'AudioListener.volume = 0f' .../eval`
  (global Unity master volume, session-only - never touches the user's saved settings; the
  game's SoundManager only fades individual music sources). Re-mute after every game restart.
- Mute SPEECH whenever driving the game (user directive - dev speech must never reach the
  user's screen reader): `curl -s -X POST --data 'on' http://127.0.0.1:8772/mute`. The dev tap
  keeps capturing, so /speech verification is unaffected; `--data 'off'` restores speech and
  MUST be sent before handing the game back to the user. State is in /health (`muted=`), off
  by default, reset by a game restart - re-mute speech AND audio after every restart.
- Steam `-applaunch` against an ALREADY RUNNING game is a silent no-op - you keep testing the
  old process with stale plugins. Kill first, check LogOutput.log's mtime is fresh if in doubt.

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

## Hard rules (migrated from wotr-access and Non-Visual Calculus - binding here)

**Speech & announcements**
- **Speech never interrupts by default.** Queued is the house style; interrupt only where an
  action supersedes prior speech (focus moves under key repeat). All speech goes through the
  Tts facade / speech pipeline - never call a backend (Prism/SAPI) directly.
- **Speak on delivery, driven by model state** - never off our own input. Find the same signal
  the game's view uses for "this is visible now" (game state, Messenger events, SceneStateMachine
  phase) and key announcements off that, once per new content.
- **Visibility gates**: hidden is not closed. Never let Enter activate through a window the game
  has hidden (animations, popup transitions, `IsButtonsBlocked`). Gate activation on the game's
  own shown state.
- **Strip TMP rich text at the speech boundary**; keep raw text where markup matters upstream.
- **Announcement style** (users are expert screen-reader users - strip fluff, never information):
  distinguishing word first ("anchored cursor", not "cursor anchored"); no navigation hints
  except for unusual controls; no redundant context or obvious type suffixes; include ALL
  gameplay-relevant detail - concise means no fluff, not less information; no emdashes or fancy
  punctuation (readers voice them); "selected" is the selected-state word, never invent
  per-screen synonyms; expand on-screen abbreviations to full words using the game's own
  localized long forms; label readouts with the game's own header string where one exists; fold
  an item's detail onto the item itself rather than a separate stop (saves a keypress, makes it
  type-ahead searchable); read detail from the model, not a tooltip that lags focus.
- **Transcript pattern for the event scene**: passage rows + choices in one stop, no position
  chatter on transcript lines, silent re-home on new content, the delivery announcement is the
  speech.
- **Unavailable choices announce as unavailable with the failed requirement** (the per-check
  data is on `ParameterButtonChanger`), never hidden or silently dead.

**State & strings**
- **Never cache game state.** No copying game data into mod-side collections or strings for
  later; re-query the game at speech time. The only acceptable "cache" is a reference to a live
  component read at speech time. A blind player trusts speech absolutely - stale data is worse
  than no data. Centralize shared reads in one adapter class per surface.
- **Reuse game data; never hardcode game text.** The game already localizes: fetch through
  I2 `LocalizationManager.GetTranslation` or read live components; reuse the game's own
  placeholder substitution helpers (hero name, `<el>`). Before authoring any string, check
  whether a game string exists; author only when none does.
- **No inline user-facing string literals.** Every mod-authored spoken/displayed word goes
  through the mod's central strings/localization table (port wotr's `Loc.T(key)` layer; enGB
  files are the complete manifest; other languages are a dropped-in folder). Word order lives in
  `{0}` templates - never concatenate English grammar around a value in code. Punctuation and
  log/debug text are exempt. Sole exception: debug-only tooling.

**Engineering**
- **No silent failures.** Pumps, patches, and event handlers fail invisibly unless logged: every
  catch logs what failed and where via the mod logger (never Unity `Debug.Log` directly). No
  empty catches, no catch-and-return-default without logging. A logged failure is actionable; a
  silent one is invisible to a player who cannot see the screen.
- **No defensive null handling.** Null-check only where null is legitimate and expected
  (FirstOrDefault, API boundaries). Let code crash otherwise - a crash is visible and logged, a
  swallowed null is not. Trust private callers.
- **Comments state what is, not what isn't** - no change history, no "removed X", no documenting
  absences. Prescriptive rules and what-happens facts that justify an instruction are fine.
- **No throwaway dev hacks** - never force-enable a gated feature in the tree (`|| true`); set
  the real config and rebuild/reload. Gates stay honest.
- If a piece of code decides what words the user hears, it must be reachable by tests or the
  dev server; nothing user-facing gets verified by eyeballing code alone. Keep
  composition/wording logic free of Unity types so it stays unit-testable.

## Gotchas (seeded from the reference mods; append new ones as found)

- Do not invoke `powershell.exe` from the Bash tool - the auto-mode classifier blocks it. Use
  `dotnet build`, `taskkill.exe` (with `MSYS_NO_PATHCONV=1`), and `steam.exe -applaunch`
  directly (see Game control above).
- Stale DLLs survive in bin and deploy dirs: `dotnet build` never removes a file it no longer
  produces. After renaming/removing an assembly, delete `src/*/bin` and the deployed plugin
  folder once.
- Close the game before building, or the deploy copy fails on locked DLLs and you test a stale
  build. Treat "file in use" build warnings as a failed deploy, never ignorable.
- Sweep BOTH `TMP_Text`/`TextMeshProUGUI` and legacy `UnityEngine.UI.Text` when reading a
  screen - don't assume all labels are TMP until verified per screen.
- This Mono (Unity 2018) dedupes `Assembly.Load(byte[])` by SIMPLE assembly name: loading fresh
  bytes under an already-loaded name silently returns the OLD image (differing version and MVID
  do not help - verified live). Hence the module csproj stamps a unique AssemblyName per Debug
  build (`BranteAccess.Module.g<utc-stamp>`); the deployed FILE name stays fixed. The REPL
  collapses generations to the newest (CSharpEvaluator). Never "simplify" either side away.
- In /eval, Mono.CSharp's InteractiveBase shadows `Time` (helper method) and Assembly-CSharp
  defines its own `Console` - write `UnityEngine.Time.*` and `System.Console.*` fully qualified,
  or build a result string and return it instead of printing.

## Autonomous run protocol

State lives in the repo, not in conversation memory: **ROADMAP.md** is the work queue and the
single source of truth for progress. Each work cycle: read ROADMAP.md, take the next unfinished
item, implement, verify via the dev server, flip its status (todo → built → verified, with a
one-line evidence note), commit ("checkpoint: <item> - <what was verified>"). Judgment calls are
logged in **DECISIONS.md** (follow reference-mod conventions by default) instead of stopping to
ask. Every few items, run the regression sweep item in ROADMAP.md. When compacting, always
preserve: the current ROADMAP item and its status, the dev-server port, and any game-restart
state (was the game running, which save was loaded).
