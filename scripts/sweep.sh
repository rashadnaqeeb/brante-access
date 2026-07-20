#!/usr/bin/env bash
# Regression sweep (ROADMAP Phase 8, R1) - drives the live game through the dev server and
# asserts on spoken output. Run after every ~4 verified items; keep it green.
#
# Precondition: game running with the mod, sitting on the MAIN MENU, dev server up.
# Scope grows with the mod - each verified surface adds a numbered section here.
# (Bash, not ps1: the agent's shell tool cannot invoke powershell.exe - see DECISIONS.md.)

set -u
PORT="${BRANTE_DEV_PORT:-8772}"
BASE="http://127.0.0.1:$PORT"
PASS=0
FAIL=0

check() { # name, expected substring, haystack
  if [[ "$3" == *"$2"* ]]; then
    PASS=$((PASS + 1)); echo "PASS $1"
  else
    FAIL=$((FAIL + 1)); echo "FAIL $1 - wanted '$2' in: $3"
  fi
}

check_nonempty() { # name, haystack (content varies with save state; presence is the assertion)
  if [[ -n "$2" ]]; then
    PASS=$((PASS + 1)); echo "PASS $1"
  else
    FAIL=$((FAIL + 1)); echo "FAIL $1 - empty"
  fi
}

cursor()  { curl -s -m 5 "$BASE/health" | sed -E 's/.*speechCursor=([0-9]+).*/\1/'; }
# The trailer line "next: N" is protocol, not speech - stripped so an empty capture is
# actually empty (check_nonempty on raw output can never fail).
speech()  { curl -s -m 5 "$BASE/speech?since=$1" | grep -v '^next: '; }
press()   { curl -s -m 5 -X POST "$BASE/input" -d "$1" > /dev/null; sleep 0.6; }
evalcs()  { curl -s -m 10 -X POST "$BASE/eval" -d "$1"; }

# --- 1. server + module alive ---
H=$(curl -s -m 5 "$BASE/health")
check "health" "ok Brante Access" "$H"
LOGC=$(echo "$H" | sed -E 's/.*logCursor=([0-9]+).*/\1/')

# --- 2. main menu screen + graph ---
NAV=$(curl -s -m 5 "$BASE/nav")
check "stack has mainmenu" "mainmenu(0)" "$NAV"
check "mainmenu graph built" "graph (7 nodes)" "$NAV"
check "ui category live" "categories: UI, Global" "$NAV"

# --- 3. navigator moves speak (End then Home guarantees at least one real move each way) ---
press ui.home
C=$(cursor)
press ui.end
check "End speaks Quit" "Quit, button, 5 of 5" "$(speech "$C")"
C=$(cursor)
press ui.home
check "Home speaks Continue" "Continue, button" "$(speech "$C")"

# --- 3b. Tab-stop cycling: extras stop and back ---
C=$(cursor)
press ui.next
check "Tab reaches extras stop" "Discord, button" "$(speech "$C")"
C=$(cursor)
press ui.prev
check "Shift+Tab restores stop 1" "Continue, button" "$(speech "$C")"

# --- 4. tooltip fallback ---
C=$(cursor)
press ui.tooltip
check "no-tooltip fallback" "no tooltip" "$(speech "$C")"

# --- 5. Settings: activation, form graph, slider adjust, Escape-close (game's back path) ---
press ui.down   # New game
press ui.down   # Settings
C=$(cursor)
press ui.activate
sleep 1.5
check "settings announced" "Settings" "$(speech "$C")"
NAV=$(curl -s -m 5 "$BASE/nav")
check "settings focused" "settings(10)*" "$NAV"
check "settings graph built" "graph (9 nodes)" "$NAV"
press ui.down; press ui.down; press ui.down   # language -> ... -> Music slider
C=$(cursor)
press ui.left
check "slider adjust speaks percent" "percent" "$(speech "$C")"
press ui.right  # restore original volume
C=$(cursor)
press ui.back
sleep 1.5
S=$(speech "$C")
check "mainmenu refocus announced" "main menu" "$S"
check "focus restored to Settings button" "Settings, button" "$S"

# --- 5b. Save/load: load window, delete confirm (cancel only - never deletes), chapter select ---
press ui.home   # back to Continue
C=$(cursor)
press ui.activate
sleep 1.2
check "load window announced" "Continue Game" "$(speech "$C")"
C=$(cursor)
press ui.secondary
sleep 1
check "delete confirm announced" "Delete Save Slot" "$(speech "$C")"
C=$(cursor)
press ui.back
sleep 1
check "delete cancel restores load window" "Continue Game" "$(speech "$C")"
C=$(cursor)
press ui.activate
sleep 3
S=$(speech "$C")
check "chapter select announced" "chapter select" "$S"
check "chapter select starts on Continue" "Continue," "$S"
C=$(cursor)
press ui.end
press ui.activate
sleep 2.5
check "quit to main menu returns" "main menu" "$(speech "$C")"

# --- 5c. Event scene: transcript rows, safe re-reads, choices/continue reachable ---
press ui.activate   # Continue -> load window
sleep 1
press ui.activate   # first slot -> chapter select
sleep 2
press ui.activate   # Continue -> into the game
sleep 4
NAV=$(curl -s -m 5 "$BASE/nav")
check "scene screen focused" "scene(0)*" "$NAV"
check "transcript row focused" "scene:page:" "$NAV"
C=$(cursor)
press ui.home
check_nonempty "transcript re-read speaks" "$(speech "$C")"
# Re-reading must not touch game state: the newest delivered page is unchanged.
NAV2=$(curl -s -m 5 "$BASE/nav")
LASTPAGE=$(echo "$NAV" | grep -o 'scene:page:[0-9]*' | sort -t: -k3 -n | tail -1)
LASTPAGE2=$(echo "$NAV2" | grep -o 'scene:page:[0-9]*' | sort -t: -k3 -n | tail -1)
check "re-read left pager alone" "$LASTPAGE" "$LASTPAGE2"
# --- 5c2. HUD windows: each covered window opens by its real HUD button, announces the
# game's own term for it, and Escape returns to the scene. Buttons the current chapter has
# not unlocked are skipped (the save decides). The click eval mirrors the game's button path
# (UiWidgets.Click on the real GameObject) so ShowBackButtonEvent wiring stays exercised.
# Runs BEFORE the scene-advance loop: on a chapter-ending save the loop finishes the scene
# and lands on the chapter final screen, where HUD windows do not open. ---
for PAIR in \
  "WindowCharacterButton_Click|HUD.Character" \
  "WindowFamilyButton_Click|HUD.Family" \
  "WindowDestinyButton_Click|HUD.Destiny" \
  "WindowHomeButton_Click|HUD.Home" \
  "WindowRelationsButton_Click|HUD.Relation" \
  "WindowEmpireButton_Click|HUD.Empire" \
  "WindowMapButton_Click|HUD.Map" \
; do
  HANDLER="${PAIR%%|*}"; TERM_KEY="${PAIR##*|}"
  EXPECTED=$(evalcs "I2.Loc.LocalizationManager.GetTranslation(\"$TERM_KEY\")" | sed -E 's/^=> //; s/^"//; s/"$//')
  C=$(cursor)
  CLICK=$(evalcs "var hud = UnityEngine.Object.FindObjectOfType<_Scripts.AMVCC.Views.HudController>(); var result = \"notfound\"; foreach (var btn in hud.GetComponentsInChildren<UnityEngine.UI.Button>(true)) { for (int i = 0; i < btn.onClick.GetPersistentEventCount(); i++) if (btn.onClick.GetPersistentMethodName(i) == \"$HANDLER\") { if (!btn.gameObject.activeInHierarchy || !btn.interactable) { result = \"inactive\"; } else { BranteAccess.Module.Game.UiWidgets.Click(btn.gameObject); result = \"clicked\"; } } } result;")
  if [[ "$CLICK" == *inactive* ]]; then
    echo "SKIP window $TERM_KEY (button not unlocked in this chapter)"
    continue
  fi
  check "window $TERM_KEY button clicked" "clicked" "$CLICK"
  sleep 1.5
  check "window $TERM_KEY announced" "$EXPECTED" "$(speech "$C")"
  press ui.back
  sleep 1.2
  check "window $TERM_KEY escape returns to scene" "scene(0)*" "$(curl -s -m 5 "$BASE/nav")"
done

# --- 5c3. Scene advance: the universal driver (End + Enter on the newest row, gated on the
# game's own next-button) until the scene offers choices, a consequence Continue, or ends
# outright (a chapter-ending save transitions to the chapter final screen instead). The loop
# inspects the graph BEFORE each press, so it can never activate a choice (which would alter
# the save). ---
FOUND=""
for i in $(seq 1 20); do
  NAV=$(curl -s -m 5 "$BASE/nav")
  if [[ "$NAV" == *"scene:choice:"* || "$NAV" == *"scene:continue"* ]]; then FOUND=choices; break; fi
  if [[ "$NAV" != *"scene(0)*"* ]]; then FOUND=scene-ended; break; fi
  press ui.end
  press ui.activate
  sleep 0.8
done
check_nonempty "choices, continue, or scene end reached" "$FOUND"
echo "     (advance loop outcome: ${FOUND:-none})"
if [[ "$NAV" == *"scene:choice:"* ]]; then
  C=$(cursor)
  press ui.end   # last node is a choice; choices are a positioned list
  check "choice speaks with position" " of " "$(speech "$C")"
fi

# --- 5d. Pause menu in-game + exit confirmation (ends back at the main menu) ---
C=$(cursor)
evalcs '_Scripts.Managers.UIManager.Initiate.ShowPauseMenu(); "opened"' > /dev/null
sleep 1.2
check "pause menu announced" "Music, slider" "$(speech "$C")"
C=$(cursor)
press ui.back
sleep 1
PSTATE=$(evalcs '"pauseOpen=" + _Scripts.Managers.UIManager.Initiate.PauseWindow.activeInHierarchy')
check "escape resumes game" "pauseOpen=False" "$PSTATE"
evalcs '_Scripts.Managers.UIManager.Initiate.ShowPauseMenu(); "opened"' > /dev/null
sleep 1.2
press ui.end        # Resume
press ui.up         # Quit to Main Menu
C=$(cursor)
press ui.activate
sleep 1
check "exit confirm announced" "QUIT GAME" "$(speech "$C")"
press ui.down       # Quit button
C=$(cursor)
press ui.activate
sleep 3
check "quit confirm returns to main menu" "main menu" "$(speech "$C")"

# --- 5e. Credits roll: text rows in reading order, Escape skips back to the menu ---
press ui.end
press ui.up      # Credits button
C=$(cursor)
press ui.activate
sleep 2.5
S=$(speech "$C")
check "credits announced" "credits" "$S"
check "credits first row is block 1" "Game Concept and Setting" "$S"
C=$(cursor)
press ui.end
check "credits last row" "22 of 22" "$(speech "$C")"
C=$(cursor)
press ui.back
sleep 2.5
check "credits skip returns to main menu" "main menu" "$(speech "$C")"

# --- 6. no mod errors logged DURING this sweep (older ring-buffer content is not this run's) ---
ERRS=$(curl -s -m 5 "$BASE/log?since=$LOGC&grep=Error%3ABrante" | grep -c "Brante" || true)
check "no mod error log lines" "0" "$ERRS"

echo
echo "sweep: $PASS passed, $FAIL failed"
exit $((FAIL > 0 ? 1 : 0))
