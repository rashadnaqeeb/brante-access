# Getting Started

Launch Pathfinder: Wrath of the Righteous. Only PC is supported; console mode (controller support)
is not implemented yet. If the game has been running for a while and nothing is happening, press
Enter on the keyboard and it should load you into the main menu. The first time you launch, a setup
wizard walks you through some of the mod's features and lets you set your speech rate and preferred
exploration features (sonar, wall tones, and so on).

This page explains how the mod works. For a hands-on guided tour of the opening dungeon, see
[The Caves Beneath Kenabres](walkthrough.md). For the full key list, see [Controls](controls.md).

## Menus and the UI

Menu navigation works as you would expect. Use the **arrow keys** to navigate the current panel and
**Tab** to move between panels. **Enter** is your primary action (usually a left-click in game);
**Backspace** is your secondary / right-click action. **Space** on an element reads its tooltips or
any hyperlinks in its text.

Typeahead is supported — just start typing and the current panel filters to the search text.
**Escape** clears any typeahead filter.

## The in-game UI

The in-game UI is a set of panels you can Tab between:

- **Game** — where you enter most game commands (cursor movement, the scanner, and so on).
- **Action bar** — use and target your character's abilities.
- **Menu** — the game's menu bar: alter AI behaviour and access functions such as rest and
  formation.
- **Windows** — your character sheet, spellbook, and the rest.

## Cursors and the scanner

Pathfinder is a CRPG; every action is performed with the mouse. Rather than emulate a 2-D mouse
pointer, the mod gives you a cursor you move around the world. There are two cursors: the **movement
cursor** and the **review cursor**.

### The movement cursor

Use **W, A, S, D** to move the cursor around the environment. In **continuous** mode it travels at a
set speed (15 feet/second by default) while held; in **tiled** mode it steps in 5-foot increments,
as if reading a tactical grid. Tile size is adjustable in settings. Every sound from the game and
mod is positioned relative to this cursor — treat it like an audio camera.

When the cursor is over an object, release the direction and you'll hear its name and information.
**Enter** left-clicks it (attack, loot, open, …). **Backspace** moves your selected party members to
the cursor. **C** snaps the cursor to the party leader. The cursor follows slopes and stairs
automatically, so you never adjust its height by hand.

This game has fog of war: if none of your characters can see a spot, you won't know what's there. A
sound marks the cursor entering or leaving fog of war.

### The review cursor

The scanner cycles through everything visible to you. Use **Page Up / Page Down** to move through
items in the current category and subcategory (by default, *Everything* in the area). **Ctrl+Page
Up/Down** switches category; **Shift+Page Up/Down** switches subcategory. The currently selected
scanner item is the **review cursor**.

Press **I** to interact with the review item (a left-click) — handy for targeting abilities and
spells without moving the movement cursor. **Slash** jumps the movement cursor to the review cursor.

You can also cycle the review cursor by kind, nearest first: **Period** for enemies, **Comma** for
allies, **M** for interactables, **N** for neutral units, **B** for points of interest, **V** for
room exits, **L** for unexplored space. Hold **Shift** to go backwards.

Unexplored space (**L**) deserves a note: it cycles the openings where walkable ground you haven't
revealed yet borders ground you have — the places where the map can still grow. If you're ever
unsure where to go next, press **L**, jump the cursor there with **Slash**, and send your party with
**Backspace**.

## Buffers

Buffers let you read a character's details line by line without leaving what you're doing. There are
two: the **selected unit** buffer (the character you currently have selected) and the **review
unit** buffer (whatever the review cursor is on, when it's a unit). Each reads the unit's name, hit
points, armor class, and then every buff, debuff, and condition affecting it. Use **Alt+Left /
Alt+Right** to switch buffers (it reads the buffer name the first time), then **Alt+Up / Alt+Down**
to move through its lines.

## Spatial audio

Think of the movement cursor as an audio camera; **overlays** are swappable sets of spatial sounds
layered around it, and you cycle the active one with **Ctrl+O**. The **sonar** periodically pings
nearby things, each with a sound for its type (enemies, allies, interactables, …) placed by distance
and direction, so you can build a picture of your surroundings. You choose which types are included
in the setup wizard or settings; in busy areas (50+ units) enabling everything can get noisy. **Wall
tones** play a tone for each nearby wall in the four cardinal directions, louder as a wall gets
closer. In multi-level areas, **Ctrl+Comma** and **Ctrl+Period** follow the surface up and down, and
**Keypad 5** re-announces the overlay cursor.

## Where you are

Press **Z** for "where am I": your area, whether you're indoors, the room you're standing in
(noting if that spot is still unexplored), and roughly where you are on the map. Press **X** to hear
the mod's own hand-written visual description of the scanner object you're focused on, and
**Shift+X** for the description of the room your cursor is standing in, where one has been authored.
**K** reads the movement cursor's exact position, and **Shift+K** reads your party.

## Party control and movement

Press **Ctrl+A** to select the whole party, or **Ctrl+1**–**Ctrl+6** to select a single member
(press the same number again to cycle to that member's pet or mount). With the movement cursor over
a spot, **Backspace** sends your selection there: a single character walks over, while the whole
party moves into formation. **H** toggles hold position, **G** stops the selection, **Ctrl+S**
toggles stealth, and **Ctrl+D** toggles AI control for the selection.

## Combat

Turn-based combat is supported. When it begins you'll hear whose turn it is, and you act with the
current character. **R** gives a status readout: in turn-based, the acting character's remaining
actions and movement; in real-time-with-pause, what your selected character(s) are currently doing
(casting, attacking, moving, or idle). **Ctrl+T** switches the whole game between turn-based and
real-time-with-pause, and **Space** toggles pause while exploring.

You can also check any unit without selecting it: when you land on a unit in the scanner, its line
includes what it's doing right now — e.g. "Bob, casting Fireball, HP 23 of 23." That action readout
is its own announcement type, so you can toggle it (and tune it per faction) under **Scan
announcements → Action** in settings.

## Targeting abilities and spells

To use an ability or spell, Tab to the action bar and choose it; the game then enters targeting
mode. Aim with either cursor: move the movement cursor onto a target and press **Enter**, or put the
review cursor on a target and press **I**. **Escape** cancels targeting (when you're not targeting,
Escape opens the game menu instead).

## Your character windows

Open the main character screens directly, both in an area and on the world map: **Ctrl+C** for the
character sheet, **Ctrl+I** for inventory, **Ctrl+B** for the spellbook, and **Ctrl+J** for the
journal. Each toggles, so pressing the key again closes it, and you move around inside with the usual
arrows, Tab, and Enter. The encyclopedia and local map don't have a shortcut yet; open them from the
Windows panel of the in-game UI.

## Dialogue

Conversations are a transcript you read through: what has already been said, the current line, and
your answer choices, including skill-check options. Choose an answer to continue; storybook passages
(book events) work the same way. Speech never interrupts itself, so lines won't cut each other off.

## Vendors and trade

A vendor window is a series of labelled panels you Tab between: your inventory, the store, your
buying cart, your selling cart, the bulk-sell options, the running deal total, and close. **Enter**
on an item moves it the sensible way for its panel (buying from the store, selling from your
inventory, returning from a cart), and the deal button confirms the trade.

## Looting

Containers and corpses open a loot window where you can take items one at a time or all at once. When
you leave an area, the game may ask whether to collect any loot you left behind.

## Resting

Rest from the in-game menu bar — it behaves like a targeted action and asks you to place your camp —
or open the rest screen to manage the camp itself: who cooks, who keeps watch, and so on.

## The world map

Travelling between locations happens on the world map, which has its own cursor, scanner, and sonar.
Use **Page Up / Page Down** to move through points and **Ctrl+Page Up/Down** to switch categories,
then press **I** to travel to or enter the selected one. You can also review points quickly: **B**
cycles every point nearest first, **M** the points connected to where you are, and **N** the
locations you can actually reach (hold **Shift** to go backwards). Locations you can't enter, or that
travel is restricted to, are announced as such, and any book events or encounters along the way are
accessible.

## Tutorials

The game's tutorial popups (controls, mechanics, and the like) are read out, with options to dismiss
the current one or to stop showing them.

## The game log and barks

Ambient lines characters say (barks) and narrative log messages are spoken as they happen. You can
also open the mod's log from the in-game Windows list to review past messages, grouped into channels
such as combat and dialogue.

## Settings and the setup wizard

Press **Ctrl+M** anywhere to open the mod menu, which holds the mod's settings and lets you re-run
the setup wizard. See [Settings](settings.md) for the full breakdown.
