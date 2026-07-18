# Combat

Combat is where Pathfinder's rules come together, and it's the part newcomers most often find opaque.
This chapter explains what's actually happening in a fight and how to run one well. It builds on
[The Pathfinder Rules](rules.md), so skim that first if a term here is unfamiliar.

## The two modes

Wrath of the Righteous offers two ways to play combat, and you can switch between them at any time —
even mid-fight (the mod binds this to **Ctrl+T**):

- **Real-time with pause (RTWP)** — everyone acts simultaneously in flowing real time. You pause
  whenever you want, issue orders, and unpause to watch them play out. The game can auto-pause on
  triggers (a new enemy spotted, a spell about to land, an ally bloodied). It's fast and a little
  chaotic by design.
- **Turn-based** — combat proceeds one creature at a time in a fixed order, exactly like the tabletop
  game. You see and control each turn deliberately.

The underlying rules are identical; only the presentation differs. **Turn-based is strongly
recommended while you're learning** — it makes the action economy visible and gives you time to think.
Many veterans use turn-based for hard fights and RTWP to blow through easy ones.

## How a fight starts

Combat begins when a hostile creature notices you (or you it) — usually when someone gets within an
enemy's **aggro range**, which varies by enemy. Good positioning lets you pull a small group instead
of a whole room, so move carefully when you know enemies are near. The moment combat starts, everyone
rolls **initiative** (a d20 plus Dexterity), and that sets the turn order for the whole fight.

## Rounds and turns

Combat is measured in **rounds** of six seconds. In a round, every creature gets one **turn**. On your
turn you spend a small budget of actions — this **action economy** is the single most important combat
concept in Pathfinder.

### The action economy

Each turn you normally get:

- One **standard action** — the big thing: a single attack, casting most spells, using most
  abilities.
- One **move action** — moving up to your speed, drawing a weapon, standing up, and similar.
- One **swift action** — a quick extra, like a swift-action spell or a class ability. One per turn.
- Any number of **free actions** — trivial things.

You can trade your standard action down for a second move action (so you can move twice). Or you can
give up moving and your standard action together to take a **full-round action** — most importantly a
**full attack**, which uses all your iterative attacks (the extra swings high-BAB characters get). The
key tension: **standing still and full-attacking does far more damage than moving and making one
attack.** Positioning before the swing matters enormously.

One small mercy: a **five-foot step** lets you shift a single five-foot square *without* using your
move action and without provoking (see below), so you can reposition slightly and still full-attack.
The mod's combat-status readout (**R**) tells you whose turn it is and how much action and movement
you have left.

## Commanding your party

Issuing orders in combat works the same way it does while exploring — combat just makes it matter
more. First **choose who you're commanding** (Ctrl+1 through Ctrl+6 for one character, Ctrl+A for the
whole party), then give the order:

- **Move** — put the movement cursor where you want them and send the selection there (Backspace). A
  single character walks over; the whole party moves as a group.
- **Attack** — put the movement cursor on an enemy and press Enter, or pick the enemy with the review
  cursor (Period cycles enemies) and press I. Your character closes to reach and attacks.
- **Use an ability or spell** — Tab to the action bar, choose it, and the game enters targeting; aim
  with the movement cursor (Enter) or the review cursor (I).

How those orders play out depends on the mode.

### Commanding in real time with pause

Pause (Space), select a character, and queue their action — a move, an attack, a spell — then do the
same for the next character, and unpause to watch it all resolve. Anyone you didn't give a specific
order to acts on the game's AI, which is usually fine for routine attacks. **Pause again whenever you
want control** — at the start of a fight, when a new enemy appears, or any time things change — and
lean on the game's auto-pause settings to stop the action at key moments for you.

Press **R** to hear what your selected character(s) are currently doing — casting, attacking, moving,
or idle (it reads all of them when several are selected). And the scanner's per-unit line shows the
same for *any* unit you land on, so you can check an enemy's action without selecting it.

### Commanding in turn-based

Combat proceeds one character at a time in initiative order, and on your character's turn your orders
**happen immediately** — there's nothing to queue. Spend the turn within the action economy: a move
action to reposition (cursor + Backspace), then a standard action to attack (cursor + Enter, or review
cursor + I) or to cast from the action bar — or, if you don't move, a full attack to use all your
swings. Press **R** at any point in the turn to hear the active character's **action economy** — the
standard, move, and swift actions still available and how much movement is left — so you know what you
can still do before committing. When you're finished, **end your turn** (Space skips/ends the turn in
turn-based) and play passes to the next creature. Tab to the extra **turn panel** for the initiative order and turn-based
actions such as ending your turn or taking a five-foot step.

## Attacking

To attack, you roll a d20, add your attack bonus, and compare to the target's **Armor Class**. Meet or
beat it and you hit, then roll damage. A **natural 20** always hits and threatens a **critical hit** —
roll again to confirm, and if it lands you multiply the damage. Some weapons crit more often or harder.

Defenses cut the other way: **Damage Reduction (DR)** subtracts from each hit unless you use the right
weapon type (silver, cold iron, magic), and **energy resistance** blunts fire, cold, and the like.
Against demons especially, the right damage type matters.

### Attacks of opportunity and flanking

Two positioning rules shape every melee:

- **Attacks of opportunity (AoO)** — moving out of a square an enemy threatens, or doing something
  careless like casting next to them, gives that enemy a **free attack**. This is why you don't just
  run past enemies, and why casters want room. A five-foot step never provokes; the **Mobility** skill
  helps you avoid AoOs when you must move.
- **Flanking** — unlike the tabletop rules, the video game doesn't care about being on opposite
  sides: you flank an enemy whenever **two of your characters are in melee range of it and able to
  act** (not flat-footed). Flanking gives both attackers **+2 to hit**, and a rogue flanking an enemy
  adds **sneak attack** damage. Ganging up in melee is good; letting enemies gang up on you is bad.

## Spellcasting in combat

Spellcasters are powerful and fiddly:

- **Prepared casters** (wizard, cleric, druid) choose their spells when they rest; **spontaneous
  casters** (sorcerer, bard, oracle) know a fixed list and cast any of it freely. Either way you have
  a limited number of **spell slots per level per day**, refreshed by resting — so spells are a
  resource to spend wisely.
- Many spells let the target make a **saving throw** to resist or halve the effect, against a DC set
  by your casting stat. Aim mind-affecting spells at enemies with weak Will, area damage at weak
  Reflex, and so on.
- Some spells make a **touch attack** (against the low Touch AC) instead of asking for a save.
- Tough enemies have **spell resistance**, which your spells must beat to take effect.
- Casting usually **provokes an attack of opportunity**, and taking damage mid-cast forces a
  **concentration** check or the spell is lost — so casters should step back and get clear before
  casting.

## Buffs, debuffs, and control

The biggest jump in effectiveness for new players is learning to **buff and debuff**:

- **Pre-buff** before a known hard fight — cast your protective and enhancement spells (haste, bless,
  defensive buffs) while paused or before stepping into aggro range, since many last minutes.
- **Debuff and control** the enemy — spells and abilities that frighten, blind, slow, knock prone, or
  paralyze can swing a fight harder than raw damage, by taking enemy turns away entirely.
- Watch the **conditions** on your party and the enemy; that list is your real-time picture of who has
  the upper hand.

## Running combat well

Putting it together:

- **Pause (or play turn-based) and think.** Combat is a puzzle, not a reflex test. In RTWP, pause
  constantly to issue orders, then unpause to let them resolve.
- **Queue your opening.** While paused at the start, line up each character's first action — a buff, a
  charge, a spell — then unpause to set it all in motion. In turn-based, you simply act in initiative
  order.
- **Let the AI handle the trivial stuff.** If you don't give a character a specific order, they'll act
  on their own — fine for routine attacks. Step in when a fight needs real decisions.
- **Position deliberately.** Keep squishy casters and archers back, put durable characters between the
  enemy and your backline, and look for flanks. Use the five-foot step to stay in full-attack range.
- **Target the right enemy.** Focus dangerous casters and ranged attackers first; a downed enemy deals
  no damage. The mod's scanner and review cursor let you pick out and target a specific enemy without
  hunting for it, and the combat-status key (**R**) keeps you oriented in the turn order.
- **Spend resources to win, then rest.** Daily spells and abilities exist to be used; a hoarded
  fireball never killed anything. When you're spent, find a safe spot and rest.

When something goes wrong, the answer is usually positioning, a missing buff, or a save you can
shore up — not a lack of damage. And if a fight is simply beyond you, remember the difficulty is
yours to adjust.
