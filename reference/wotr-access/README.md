# Wrath Access

A screen-reader accessibility mod for **Pathfinder: Wrath of the Righteous**, for blind and
visually-impaired players. It speaks the game's menus, character creation, dialogue, exploration,
and turn-based combat, and adds custom keyboard navigation over the game's UI, spatial audio for
the world around you, and review "buffers" for inspecting characters in detail.

> **Status: alpha (0.0.1).** A great deal works, but expect rough edges and gaps. Please report
> bugs (see below) - that's what the alpha is for.

> **New to the mod?** Read the [getting started walkthrough](getting-started.md) for a guided tour.

## What works

- **Speech** through your screen reader (NVDA, JAWS, etc.) via Prism, with a Windows SAPI fallback.
- **Custom keyboard navigation** in mouse mode, with key-repeat matching your OS settings.
- **Main menu**, the **New Game** flow, and **character creation / level-up**.
- **Settings** (checkboxes, sliders, dropdowns) and **rebindable key bindings**.
- A **mod menu** (Ctrl+M) with the mod's own settings and a **first-run setup wizard** that walks
  you through speech, movement, wall tones, sonar, and event feedback.
- **Exploration**: a movement cursor, a categorized **scanner** of everything in the area, a
  **sonar** and other spatial-audio overlays, **wall tones**, room/area awareness, and **world-map
  travel**.
- **Dialogue**, book events, tutorial popups, and the in-game log / barks.
- **Turn-based combat** and **targeting** (abilities, resting).
- **Service windows**: inventory, character sheet, spellbook, journal, encyclopedia, local map.
- **Vendors / trade**, looting, resting, party selection, and the group manager.
- **Review buffers** (Alt+arrows) for reading a unit's details line by line - name, HP, AC, and
  every buff / debuff / condition.

The mod follows the game's language setting; English is included.

## Requirements

- Pathfinder: Wrath of the Righteous on **Windows** (Steam).
- A **screen reader** (NVDA, JAWS, ...) or Windows SAPI voices.
- Optionally **Git** - only if you want the pull-and-deploy install path below; the installer doesn't
  need it.
- That's it - this uses the game's **native mod system**, so there is **no Unity Mod Manager** to
  install.

## Install and update

There are two ways to install - use whichever suits you. **Close the game first** either way, and the
setup wizard runs automatically on the first launch. Your Wrath Access settings are stored separately,
so updating never resets them.

### Without Git (installer)

1. Download the installer:
   **[WrathAccessInstaller.exe](https://github.com/bradjrenshaw/wotr-access/releases/latest/download/WrathAccessInstaller.exe)**
2. Run it. Check the game directory is right (it auto-detects your Steam install; use Browse if not),
   then choose **Install alpha** - it downloads the latest build straight from GitHub.
3. Start the game.

To **update**, just run the installer again and choose **Install alpha** - you only download the
installer itself once. It's a small accessible app, with a `--cli` keyboard/console mode if you prefer.

### With Git

1. Clone the repo:
   ```
   git clone https://github.com/bradjrenshaw/wotr-access
   ```
2. In PowerShell, from the repo folder, run `.\deploy.ps1`.
3. Start the game. To **update** later: `git pull`, then `.\deploy.ps1` again.

`deploy.ps1` finds your install automatically (Steam libraries); if it can't, pass the folder
explicitly:
```
.\deploy.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure"
```

## Keys

Press **Ctrl+Shift+A** to toggle accessibility focus mode. The essentials:

| Key | Action |
| --- | --- |
| Ctrl+Shift+A | Toggle focus mode |
| Ctrl+M | Open the mod menu (settings, setup wizard) |
| Arrow keys | Navigate / adjust the focused control; move the cursor while exploring |
| Tab / Shift+Tab | Move between regions / panels |
| Enter | Primary action (activate) |
| Backspace | Secondary action (e.g. clear a key binding; move to the cursor) |
| Escape | Back / close |
| Alt + arrows | Review buffers - Alt+Left/Right pick a buffer, Alt+Up/Down read its lines |
| Ctrl+C / Ctrl+I / Ctrl+B / Ctrl+J | Open character sheet / inventory / spellbook / journal |
| F5 | Quick save |

There are many more keys for the scanner, overlays, party control, and combat. **The complete,
rebindable list is in the mod menu** (Ctrl+M, then Settings, then key bindings), where you can also
change any of them.

## Getting Started
Launch Pathfinder: Wrath of the Righteous. Note that only pc is supported; console mode (aka controller support) is not implemented yet and I don't know if it will be.
If the game has been running for a while and nothing is happening, press enter on the keyboard and the game should then load you into the main menu. The first time you launch the game, you will get a setup wizard that walks you through some of the mod's features and allows you to set up your speech rate, prefered exploration features (sonar, wall tones, etc.)

### UI
Menu navigation works as you would expect. Use the arrow keys to navigate the current panel; tab to navigate between panels. Enter acts as your primary action (usually corresponds to a left-click in game); backspace is your secondary or right click action. Space on an element allows you to view its tooltips or any hyperlinks in the text of that element.
Typeahead is supported; just start typing and the current panel will be filtered for the search text. Escape will clear any typeahead filters.

### in-game
The in-game UI consists of a number of panels you can tab between:
* Game: where you enter most game commands (cursor movement, the scanner, etc.)
* The action bar: An action bar where you can use and target your character's abilities
* Menu: The game's menu bar, allowing you to alter AI behavior and access certain functions, such as rest and formation
*Windows: Your character pane, spellbook, etc

### Cursors and the Scanner
Pathfinder: Wrath of the Righteous is a crpg. As such, every action is performed via the mouse. Instead of directly trying to emulate a 2d mouse cursor, the mod allows you to move a cursor around the world in 3d instead (though due to how the game's point of view and terrain are coded, it is effectively 2d in most cases.) There are two cursors: the movement cursor and the review cursor.

#### The Movement Cursor
Use w, a, s, and d to move the cursor around the environment. If the cursor's movement mode is set to continuous, the cursor will continue to travel at a rate of 15 feet per second (by default) while held. If set to tiled, you can traverse the world in 5-foot increments (as if you were looking at a tile-based tactical map.) The tile size can also be adjusted in settings. Sounds you hear from the game and mod will all be relative to this cursor; treat it like an audio camera in a way.
When the cursor is over an object, release the direction you're holding and you will hear the name and information about what the cursor is hovering over. Pressing enter will perform a left click on this object (attack, loot, open, etc). Pressing backspace (right click equivalent) will move your selected party members to the location of the cursor. Pressing c will teleport the cursor to the position of the party leader. The cursor will follow slopes, stairs, etc automatically, so you never have to adjust the cursor's height manually.
Also note that this game has fog of war. If none of your characters could see a point, you won't know what is there as it is obscured (as it would be for the sighted.) A sound will indicate when the cursor has entered or left fog of war.

#### The Review Cursor
This mod has a scanner that works similarly to other mods. Use page up/down to cycle all things visible to you and where they are relative to you within the current scanner category and subcategory (by default this will be the all category/everything in the area.) Use control page up/down to switch between categories; shift page up/down to cycle through subcategories. The currently selected scanner item is refered to as the review cursor.
The review cursor is a secondary cursor that can only target objects, interactables, etc. If you press i, you will interact with the review item (as if you were left clicking.) Pressing slash will jump the movement cursor to the location of the review cursor. Note that you can use i to target abilities, spells, etc too without having to move the movement cursor to it.
You can also quickly cycle the review cursor through various things around you. Pressing period cycles enemies, comma allies, m interactables, n neutral units, b points of interest, v room exits, l unexplored space (the openings where the map can still be revealed; hold shift on any of these to go backwards). The review cursor allows you to quickly interact with various things while simultaneously moving the movement cursor.

### Buffers
Buffers let you read a character's details line by line without leaving what you're doing. There are two: the selected unit buffer (the character you currently have selected, for example after pressing control 1) and the review unit buffer (whatever the review cursor is on, when it is a unit). Each reads the unit's name, its hit points, its armor class, and then every buff, debuff, and condition affecting it. Use alt left and alt right to switch between buffers (the first time you switch to one it reads its name), then alt up and alt down to move through its lines.

### Spatial audio
Think of the movement cursor as an audio camera: overlays are swappable sets of spatial sounds layered around it, and you cycle the active one with control o. The sonar periodically pings the things near you, each with a sound for its type (enemies, allies, interactables, and so on) placed by distance and direction, so you can build up a picture of your surroundings; you choose which types are included in the setup wizard or settings, and in busy areas (50 or more units) enabling everything can get noisy. Wall tones play a tone for each nearby wall in the four cardinal directions, growing louder as a wall gets closer. In multi-level areas, control comma and control period follow the surface up and down, and keypad 5 re-announces the overlay cursor.

### Where you are
A few keys keep you oriented. Press z for "where am I" (your current area, the room you're standing in, and whether that spot is still unexplored), x for the mod's authored description of the scanner target and shift x for the current room's, k to hear the movement cursor's exact position, and shift k to hear your party.

### Party control and movement
Press control a to select your whole party, or control 1 through control 6 to select a single member (press the same number again to cycle to that member's pet or mount). With the movement cursor over a spot, backspace sends your current selection there: a single selected character walks over, while the whole party moves into formation.

### Combat
Turn-based combat is supported. When it begins you'll hear whose turn it is, and you act with the current character. Press r at any time for a status readout: whose turn it is and how many actions and how much movement remain. Control t switches the whole game between turn-based and real-time-with-pause, and space toggles pause while exploring.

### Targeting abilities and spells
To use an ability or spell, tab to the action bar and choose it; the game then enters targeting mode. Aim with either cursor: move the movement cursor onto a target and press enter, or put the review cursor on a target and press i. Press escape to cancel targeting (when you're not targeting, escape opens the game menu instead).

### Your character windows
You can open the main character screens directly, both in an area and on the world map: control c for the character sheet, control i for inventory, control b for the spellbook, and control j for the journal. Each one toggles, so pressing the key again closes it, and you move around inside with the usual arrows, tab, and enter. The encyclopedia and local map don't have a shortcut yet; open them from the Windows panel of the in-game UI.

### Dialogue
Conversations are presented as a transcript you can read through: what has already been said, the current line, and your answer choices, including any skill-check options. Choose an answer to continue; storybook passages (book events) work the same way. Speech never interrupts itself, so lines won't cut each other off.

### Vendors and trade
A vendor window is a series of labelled panels you tab between: your inventory, the store, your buying cart, your selling cart, the bulk-sell options, the running deal total, and close. Pressing enter on an item moves it the sensible way for the panel it's in (buying from the store, selling from your inventory, or returning something from a cart), and the deal button confirms the trade.

### Looting
Containers and corpses open a loot window where you can take items one at a time or all at once. When you leave an area, the game may ask whether to collect any loot you left behind.

### Resting
You can rest from the in-game menu bar, which behaves like a targeted action and asks you to place your camp, or open the rest screen to manage the camp itself: who cooks, who keeps watch, and so on.

### The world map
Travelling between locations happens on the world map, which has its own cursor, scanner, and sonar. Use page up and page down to move through the points on the map and control page up/down to switch categories, then press i to travel to or enter the selected one. You can also review points quickly: b cycles every point nearest first, m the points connected to where you are, and n the locations you can actually reach (hold shift to go backwards). Locations you can't enter, or that travel is restricted to, are announced as such, and any book events or encounters along the way are accessible.

### Tutorials
The game's tutorial popups (controls, mechanics, and the like) are read out, with options to dismiss the current one or to stop showing them.

### The game log and barks
Ambient lines that characters say (barks) and narrative log messages are spoken as they happen. You can also open the mod's log from the in-game Windows list to review past messages, grouped into channels such as combat and dialogue.

### Settings and the setup wizard
Press control m anywhere to open the mod menu, which holds the mod's settings and lets you re-run the setup wizard. Settings are organised into tabs (speech, audio and overlays, the scanner, events, input, and more), and every key in the mod can be rebound, either by capturing a new combination (with a warning if it clashes) or by clearing one. Combat and world events such as damage, healing, and spellcasting can also be spoken aloud, with separate voices for enemies, allies, neutrals, and sourceless events, as positional speech or through your screen reader; you set this up in the wizard or under the events settings.

## Notes and limitations

- **Alpha**: not everything in the game is accessible yet, and some screens are partial.
- Native mods set the game's "a mod is active" flag, which **disables Steam achievements** while the
  mod is installed. I will add a way to fix this later, but I wouldn't want a buggy alpha feature giving people achievements they shouldn't have.
- **Report bugs** on the [issue tracker](https://github.com/bradjrenshaw/wotr-access/issues) - the
  more detail (where you were, what you pressed, what you heard vs. expected), the better.

## Building (developers)

The mod targets `net48` and builds against the game's own assemblies. With the .NET SDK and the
.NET Framework 4.8 targeting pack:

```
dotnet build
```

A Debug build compiles `WrathAccess.dll` and deploys the full native-mod layout (plus a dev-only
in-process diagnostic server). `dotnet build -c Release` compiles without deploying. See
[`CLAUDE.md`](CLAUDE.md) for the full build and architecture details, and `scripts/stage.ps1` for
refreshing the tester payload under `deploy/`.

## License

Not yet chosen. The bundled `prism.dll` (Prism screen-reader speech) and `NAudio.dll` are
third-party, redistributed under their own licenses.
