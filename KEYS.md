# Brante Access - Key Reference

Generated from the mod's actual input registrations (ModModule.RegisterActions) and
cross-checked against the live bindings dump. One navigation set; the mod owns the
keyboard whenever it has a screen for the current surface, and stands down to the
game's stock keys everywhere else (cutscene skips, text entry).

## Navigation (any screen)

- Up and Down arrows: previous and next item. Held keys repeat at your OS keyboard rate.
- Left and Right arrows: same as Up and Down where a row layout calls for it
  (the HUD bar's buttons, slider adjustment, spinner cycling).
- Tab and Shift plus Tab: next and previous control group, for example from the scene
  transcript to the HUD bar and back.
- Enter (main or keypad): activate the focused item through the game's own click path.
  Unavailable items refuse and speak the reason instead.
- Escape: back. Closes the open window through the game's back handler; in a scene it
  opens the pause menu; in a popup it cancels where the game allows cancelling.
- Space or F1: read details for the focused item - choice conditions and consequences,
  parameter scales, character and objective descriptions. Where the item has no detail
  of its own, Space reads the window's help text if the game provides one.
- Backspace: secondary action where one exists. Currently only the load window uses it:
  Backspace on a save slot opens the game's delete confirmation.
- Home and End: first and last item. Pressed again at the edge, they re-read the
  current item in full.
- Ctrl plus Up and Ctrl plus Down: previous and next region on screens that group
  their rows.

## Type-ahead search

Typing letters starts a search within the current control group.
Space is allowed once a search has begun. Up and Down move through the matches, and
focus follows each match as it speaks, so Enter activates it as usual. Escape clears
the search (the next Escape is Back as usual).

## Text entry

In the game's text fields (the hero name request), the mod stands down and keys go
straight to the game's edit field; typed characters echo from the field itself.
