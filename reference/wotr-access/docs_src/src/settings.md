# Settings

Press **Ctrl+M** anywhere to open the **mod menu**. It holds the mod's own settings, lets you re-run
the **setup wizard**, opens **Help** (which has **Read documentation** — opening this book in your
browser), and links to the Discord and Patreon. Settings are organised into tabs; you navigate them
with the usual arrows, Tab, and Enter, and every category has a **Reset to defaults** that affects
only that category.

## The setup wizard

The first launch runs a setup wizard automatically, and you can re-run it from the mod menu at any
time. It walks you through the most important choices as a series of steps with a roadmap header you
can jump around in:

- **Speech** — which speech engine to use (your screen reader via Prism, or Windows SAPI) and its
  rate / volume.
- **Movement** — continuous vs. tiled cursor movement, speed, and tile size.
- **Wall tones** and **sonar** — whether they play, and which kinds of things the sonar pings.
- **Event feedback** — whether combat and world events (damage, healing, spellcasting, …) are spoken
  aloud, and how, including distinct voices for enemies, allies, neutrals, and sourceless events.

The wizard's presets configure many settings at once; anything it sets can still be fine-tuned
afterward under the matching settings tab.

## Settings tabs

- **Speech** — the speech engine and per-engine tuning (rate, volume, voice). Includes the distinct
  voices used for positional event feedback.
- **Audio & overlays** — the spatial-audio overlays (sonar, wall tones, and others): per-system play
  mode (off / when moving / continuous) and volume, plus the shared cursor and movement behaviour.
- **Scanner** — what the scanner includes and how it's grouped, and the taxonomy used for both the
  scanner and the sonar sounds.
- **Events** — one entry per event the mod can announce (damage, healing, buffs, spellcasting, turn
  changes, and so on), with per-source settings (party / enemy / neutral / sourceless) and the speech
  configuration each uses.
- **Input** — every action in the mod as a rebindable binding. Capture a new combination (with a
  clash warning) or clear one. These are the mod's own bindings, persisted separately from the game's
  keybindings.
- **Announcements / UI** — verbosity toggles the mod consults live; turning one off stops that piece
  of information being spoken.

## Notes

- Your Wrath Access settings are stored separately from the game's, so **updating the mod never
  resets them**, and you can reset any single category back to its defaults without touching the
  rest.
- The first-launch flag is independent of the settings, so a full "reset all" won't re-trigger the
  setup wizard.
