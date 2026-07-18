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

cursor()  { curl -s -m 5 "$BASE/health" | sed -E 's/.*speechCursor=([0-9]+).*/\1/'; }
speech()  { curl -s -m 5 "$BASE/speech?since=$1"; }
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

# --- 5c. Pause menu in-game + exit confirmation (ends back at the main menu) ---
press ui.activate   # Continue -> load window
sleep 1
press ui.activate   # first slot -> chapter select
sleep 2
press ui.activate   # Continue -> into the game
sleep 4
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

# --- 6. focus mode toggles both ways ---
C=$(cursor)
press focusmode
check "focus mode off spoken" "focus mode off" "$(speech "$C")"
C=$(cursor)
press focusmode
check "focus mode on spoken" "focus mode on" "$(speech "$C")"

# --- 7. no mod errors logged DURING this sweep (older ring-buffer content is not this run's) ---
ERRS=$(curl -s -m 5 "$BASE/log?since=$LOGC&grep=Error%3ABrante" | grep -c "Brante" || true)
check "no mod error log lines" "0" "$ERRS"

echo
echo "sweep: $PASS passed, $FAIL failed"
exit $((FAIL > 0 ? 1 : 0))
