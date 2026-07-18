# Walkthrough: The Caves Beneath Kenabres

This is a hands-on tour of the opening of the game, to show how it all fits together in practice.
It contains slight spoilers for the first area — nothing very specific, but if you'd rather discover
it yourself, you're well equipped to experiment after reading [Getting Started](getting-started.md).
Don't be afraid to tinker with mod settings either; you can reset any category to its defaults at any
time.

## How to play

You play by commanding your party members to move and interact, much as in a real-time strategy
game. Take the introduction as an example: after the intro dialogue, you're given a little time to
explore the Kenabres festival.

The game starts **unpaused**. Press **Space** to pause and unpause (or use the corresponding menu
item). Use pause often to assign commands — this is a feature of the game and the genre, and the
game is designed around it.

Use the movement cursor (**W, A, S, D**) to explore. Depending on your sonar settings you'll hear
sounds around you for NPCs, interactables, and so on; when the cursor hovers over something you'll
hear a sound for it. **Enter** commands your character to walk to that target and interact when they
arrive. With wall tones on, you'll hear sounds as you approach walls and obstacles. Your characters
pathfind to their commanded destination automatically — that's how the game works, not a mod feature.
Alternatively, use the scanner or the quick keys (**Period**, **Comma**, and so on) to find what you
want and press **I**.

Press **B** to cycle the area's points of interest. These aren't interactable objects — they're map
markers — but pressing **Slash** on one jumps your cursor to it; then find the object nearby and
command your party to move or interact.

To complete the introduction, interact with 2 of the 3 festival attractions (the drinking contest,
the dart-throwing competition, or the training dummy). After that you're thrown into the first real
dungeon.

## Into the caves

You start in a small room in the northern part of the area. Press **C** to make sure the cursor is at
your character's location (after an area transition it can land in odd spots; this is being worked
on). Use **W, A, S, D** to look around — notice how the wall tones change volume as you near a wall.

This room isn't a neat rectangle. Pathfinder is a 3-D game, so rooms are realistically modelled,
curves and all. It's simplified by the camera angle, but worth keeping in mind.

When the cursor moves far enough you'll hear a higher-pitched "bing": you've entered fog of war — any
area your characters can't currently see. Press **Ctrl+A** to select the whole party, then
**Backspace** to move everyone closer, revealing the hidden areas. If they aren't moving, make sure
the game is unpaused.

This is a good moment to try **L** and **Shift+L**, which cycle *unexplored space*: the openings
where walkable ground you haven't revealed yet borders ground you have — in other words, the places
you could go to push the map back. Each press names the opening (with its room, when known) and how
far away it is. Press **Slash** to jump the cursor there, then **Backspace** to send the party.
Whenever you're unsure where to explore next, L is the answer.

There's nothing hidden in the starting room and only one way forward — head south. The room curves
southwest, and once your characters pass a certain point a scripted rockfall occurs.

Use the scanner, or press **N** to cycle neutral units, to find Anevia, trapped under fallen debris.
Press **I** to interact with her. A conversation follows; choose whatever you like. You can fail the
initial skill check with no negative consequence — the game is being nice to you, for now. (Pressing
**Space** on dialogue or an option lets you view any links in its text.)

Afterwards, Seela joins your party and Anevia follows as a neutral unit. Seela can be commanded
directly; Anevia is fully AI-controlled — she follows but won't fight.

You can have up to 6 party members. That limit counts only specific characters; summons and animal
companions don't count toward it (which makes animal companions very strong).

Command everyone at once with **Ctrl+A**, or a specific member with **Ctrl+1**–**Ctrl+6**. Moving the
whole party keeps them in a formation, which you can rearrange in the accessible formation window
(the HUD menu's Formation button).

Select the whole party and move the cursor up to the next fog of war. This hallway curves west and
northwest. Keep approaching and you'll eventually hear a new sonar sound for a revealed container (if
enabled); Seela comments on it too.

Interact with the container to loot it — Tab to "loot all", or press **Enter** on each item.
**Space** on an item reviews its information first. Continue northwest to find another lootable item,
Terendelev's Scale (you want these). Further northwest leads to a cutscene and another party member,
Camellia.

Before continuing, it's worth reviewing your characters. Press **Ctrl+C** for the character sheet (or
use the Windows menu). It has tabs that change which parts are shown; if you're unsure what changed,
check the bottom sections. You can also level up here. Then open the inventory (**Ctrl+I**) for your
main character — note they have nothing equipped. Equip what makes sense. All items are shared, so
you don't juggle inventories between characters. Seela and Camellia start with some gear; on higher
difficulties it can be worth giving Camellia a ranged weapon, as she tends to go down quickly in
melee. Continue north and west to your first encounter.

## Combat

This encounter is two centipedes. The game auto-pauses when the first becomes available; the second
is hidden in the fog of war to the west.

When a character gets within roughly 25–30 feet of an enemy, combat starts. Aggro ranges vary, so
move carefully near enemies — with good positioning you can often single out smaller groups. That
doesn't apply here, though: there's no avoiding the second centipede.

Pathfinder has two combat modes. **Real-time with pause (RTWP)** has characters act simultaneously in
real time; you can set the game to auto-pause on triggers (a spell cast, a new enemy revealed, …).
RTWP is hectic by design. **Turn-based** works like tabletop Pathfinder: characters act in initiative
order, rolled at the start. Press **Ctrl+T** to switch between the two at any time, even mid-combat.
For newer players, turn-based is recommended — but experiment.

Pause before positioning or preparing. Note that for casts to go off you must unpause and wait out
their duration. Moving one character into range starts the encounter; for an advantage, have everyone
use an ability first. For example, if your main character has Charge and is mounted (it won't apply
to many characters, especially casters): Tab to the action bar and select Charge — the mod says
you're now targeting. Press **Period** to pick the enemy and **I**, or move the cursor over it and
press **Enter**. You've queued Charge for when the game unpauses. (Seela has Charge but can't use it
here — she's fatigued.) Queue up whatever you like, then unpause to begin.

### RTWP

In RTWP the game will most likely auto-pause when the battle starts (the default). Unpause to
continue; queued abilities execute, and every action — movement included — takes a variable amount
of time. Various things auto-pause depending on your settings. If nothing is queued, characters act
on the game's AI, which is usually fine — pause when you *do* want to issue specific commands. When a
character moves west, the second centipede is revealed (probably pausing the game). Handle it however
you like; the AI will likely deal with it, but you could command a ranged character to soften it
first.

### Turn-based

In turn-based mode you can't queue a burst of actions up front — rounds happen in initiative order
regardless. It's much simpler: a command (attack, move, …) executes immediately. The pause key is
**skip/end turn** here. There's also an extra UI panel you can Tab to, holding the turn order and
some turn-based actions.

## What's next?

Keep exploring; the path curves southwest, with several more encounters and a few optional alcoves to
the west and southwest (one has loot). The way forward then leads east and south.

The **rooms** system can help with orientation. The mod marks regions of the map as rooms (you've
probably heard it announce the cursor entering a new room). Press **V** to cycle room exits — doors
get marked as exits later on — and **Z** for "where am I", which includes the room your cursor is
in. **Shift+X** speaks a hand-written description of the room, and **X** one of the scanner object
you're focused on, where they've been authored. And if you've lost the thread of the dungeon
entirely, **L** cycles the unexplored openings that remain.
