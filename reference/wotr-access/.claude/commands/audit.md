Perform a full codebase audit of the WrathAccess mod.

## Your Role
You are performing a comprehensive audit of the entire codebase, not just a diff. Focus on architectural health, abstraction quality, adherence to the project's invariants, and long-term maintainability.

## Instructions

1. Read CLAUDE.md for project context and invariants.
2. Scan all .cs files under `src/` using Glob and Grep, and the locale JSON under `assets/locale/enGB/`.
3. Analyze each area below and report findings.

## Audit Areas

### Architecture & Abstractions

**Missed abstractions:**
- Are there patterns repeated across 3+ files that should be a shared base class or utility?
- Are there switch statements or type checks that should be polymorphism?
- Are there string-based lookups that should be typed (enums, constants, generics)?

**Over-abstraction:**
- Are there base classes or interfaces used by only one implementation?
- Are there layers of indirection that don't add value?
- Are there generics or patterns that make the code harder to follow without benefit?

**Inconsistent abstractions:**
- Do similar subsystems use different patterns for the same problem? (e.g., some screens poll in OnUpdate, others subscribe via EventBus; some content rebuilds in place, others destructively)
- Are there older files that haven't been updated to use newer patterns? Known convention: **FlowSheet is the grid primitive; Table is retained only for the Spells picker** — flag any new Table usage or dead Table-handling code.
- Screen lifecycle compliance: screens build their tree in OnPush, expose ScreenName, and let the Navigator own input. Flag screens that mutate focus directly or duplicate navigator logic.

### Game-Model Access Rules (the project's View-layer equivalents)

These are the invariants that keep us correct against the game's MVVM layer. Each violation is a bug waiting to surface:

**Read live, not cached:**
- Proxies must read live computed model values (e.g. `ContextMenuCollectionEntity.IsEnabled`) at announce time — never stash the game's reactive values or our own copies at build time. Scan proxies for fields capturing VM state that can change.
- Tooltip-carrying APIs take a `Func` factory resolved on each drill-in. Scan for cached built tooltip templates.

**Activate via the VM contract:**
- Activation must invoke the VM method the game's click handler is wired to (`Execute`, `SetSelectedFromView`, `ChangeValue`, …) — compile-checked. Flag any Unity scene-hierarchy hunting for a control's OnClick.
- BUT: check the PC view's click handler for side effects (UI sounds, view-owned state like view modes) the VM call alone won't reproduce. Flag activations that skip a sound or guard the real handler has.
- Mirror the game's real PC view/prefab/handler flow including its guards and order.

**Surface only what's visible:**
- Mirror the game's on-screen content; don't invent indicators from hidden VM data (the feat "Source:" line is a deliberate, commented exception — not a precedent).
- Mirror view-side filters/sorts: view `IsVisible`/`EntityComparer` rules (e.g. voice "None" always shown) must be replicated, not bypassed by iterating the raw VM collection.

**Reflection use:**
- Prefer public API; reflecting protected/private game members is acceptable when there's no clean public path. Flag contortions made just to avoid reflection, AND flag reflection where a public path exists. List all reflection sites with what they touch.
- The ~100 generic types missing from the decompile (UnitEntityData etc.) have real APIs in the typedump — flag any code whose comments suggest the API was reconstructed from call sites.

### Localization & Message Usage

**The hard rule:** every string the mod speaks or displays must come from the locale tables — `Loc.T(key[, args])` / `Message.Localized` + `assets/locale/enGB/ui.json`, settings labels via `localizationKey` + `settings.json`. Sole exception: debug-only tooling (Player.log output, dev hotkeys). Game content (names, log lines, tooltips) passes through — never re-translate.

- Scan for hardcoded English in `Tts.Speak(...)`, `Speak(...)`, `new TextElement("...")`, proxy label arguments (`new ProxyActionButton("Back", ...)`), and screen/Container labels.
- Scan for `Message.Raw("...")` with English literals (vs. legitimate `Message.Raw(setting.Label)` / game-provided text).
- Scan for resolve-then-compose freezing: `.Resolve()` fed into `string.Join` / `+` / interpolation and re-wrapped — language switches can't re-translate the frozen pieces. Compose with `Message.Join` instead. `.Resolve()` is legal only at output boundaries (handing text to Tts, a log line, a `{var}` template substitution).
- Helpers that build their result from `Message.*`/`Loc.T` calls but return `string` mid-stack should return `Message` so callers keep composing.
- Cross-check the locale JSON: keys referenced in code but missing from enGB (silent fallback), and dead keys no longer referenced.
- Live language swaps are POLLED (LocalizationManager.Tick) — flag anything caching a resolved string across a possible language change.

### Code Reuse

**Duplicated logic across files:**
- Search for similar blocks in `src/UI/Proxies/`, `src/Screens/`, `src/Exploration/`, `src/Exploration/Overlays/`.
- Focus on: announcement assembly, VM unwrapping (`.Value` chains), EventBus subscribe/unsubscribe patterns, distance/bearing math, settings tree building.

**Underused utilities:**
- Are there helper methods in one class that should be shared (e.g., label stripping, position math, VM lookups duplicated between Scanner/WorldModel/overlay systems)?
- Is announcement-part logic duplicated between proxies that `AnnouncementComposer` or a base proxy should own?

### Complexity Hotspots

**Files that are too large or do too much:**
- Identify files over ~300 lines. Should they be split? (Screens that grew region-building, ModMenuScreen tab builders, etc.)
- Identify methods over 50 lines. Should they be decomposed?
- Identify classes with mixed responsibilities.

**Overly complex methods:**
- Methods with deep nesting (3+ levels).
- Methods with many parameters (5+).
- Methods doing reflection, try/catch, and business logic in the same block.

### Screen, Input & Speech Organization

**Screen coverage:**
- List the game's major UI surfaces (RootUIContext-driven: dialogue, loot, service windows pages, vendor, book events, global map, crusade, …) and whether the mod has a Screen for each. Distinguish "deliberately deferred" (note it) from "missed".
- Are there Screen classes doing too much that should delegate (content builders, per-phase classes)?

**Help system coverage:**
- List all screens and whether they implement `GetHelpMessages()`. Are help strings localized?

**Input:**
- All user-facing actions must be registered `InputAction`s (rebindable, persisted via BindingSetting) — flag raw `Input.GetKeyDown` polling outside the sanctioned spots (capture screens, typeahead char feed) since it bypasses rebinding.
- Two-tier rule: global hotkeys vs nav keys; flag actions gated to the wrong tier or missing screen gating.

**Speech:**
- Prism is primary; Tolk is RETIRED — flag any Tolk leftovers (code, csproj items, deploy steps, docs).
- The never-interrupt-by-default preference: flag `Speak(..., interrupt: true)` outside genuine focus-move/rapid-refinement paths.
- Unity Mono has NO managed COM — flag any new System.Speech / Type.GetTypeFromProgID usage (must go through ComDispatch).

### Long-term Concerns

**Fragility:**
- The game is patch-frozen (final update shipped) — prefab dumps and hardcoded asset keys are acceptable. Still list reflection-based accesses and per-instance prefab wiring assumptions so they're known.
- What breaks if a screen's VM is null mid-transition? Scan `.Value` chains for missing null guards on screens that can close under us.

**Native-mod constraints:**
- `Assemblies\` must contain managed dlls only; every file there is `Assembly.LoadFrom`'d. Flag build/deploy changes that could drop a native dll in there.
- Harmony stays within the game's bundled 2.0.x API — flag 2.1+ API usage.
- Per-frame work hangs off our `Ticker`; flag new MonoBehaviours or coroutines created outside it.

**Compiler warnings:**
- Run `dotnet build` and check for warnings. The build must produce 0 warnings.
- Do NOT suppress warnings with `#pragma`, `!`, or `[SuppressMessage]`. Fix the underlying issue.

**Technical debt:**
- List TODO comments and known workarounds.
- Leftover temporary `[tag]` debug logging that should have been stripped after diagnosis.
- Commented-out code blocks that should be removed or restored.
- English glue literals awaiting domain locale tables (known pending work) — inventory them.

## Output Format

### Summary
- **Critical issues**: [count] (architectural problems or invariant violations that will cause bugs/maintenance pain)
- **Improvements**: [count] (refactoring opportunities)
- **Notes**: [count] (minor observations)

### Findings (grouped by severity)
For each finding:
- **Area**: Which audit area it falls under
- **File(s)**: Affected files
- **Description**: What the issue is
- **Impact**: Why it matters for long-term health
- **Suggestion**: Concrete recommendation
