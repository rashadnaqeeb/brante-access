#!/usr/bin/env bash
# Story-advance driver (ROADMAP Phase 8, save-jump harness) - pushes the live game forward
# through the mod's own navigator, verifying speech after every action (act, then listen).
# Reaching a chapter makes the game write that chapter's save, which is the jump point for
# later-chapter window verification.
#
# Per step: probe the mod's screen stack via /nav, then either activate a choice (rotating
# among available ones so a lethal first choice cannot loop forever), or End+Enter to page
# text / press the way-forward button. Any action that produces no speech is retried; three
# consecutive silent actions abort. Mod errors in /log abort. Unknown screens abort.
#
# Stops cleanly (exit 0) on: chapterstart screen (a pending manual-verification item),
# main menu, or max steps. Aborts (exit 1) on: silence, unknown screen, mod errors.
#
# Usage: scripts/advance.sh [max_steps]   (default 60)

set -u
PORT="${BRANTE_DEV_PORT:-8772}"
BASE="http://127.0.0.1:$PORT"
MAX="${1:-60}"

cursor() { curl -s -m 5 "$BASE/health" | sed -E 's/.*speechCursor=([0-9]+).*/\1/'; }
logcur() { curl -s -m 5 "$BASE/health" | sed -E 's/.*logCursor=([0-9]+).*/\1/'; }
press()  { curl -s -m 5 -X POST "$BASE/input" -d "$1" > /dev/null; }

# The probe reads /nav (no compilation). It must NOT use /eval: every eval compiles a fresh
# dynamic assembly, and hundreds of them in one session overflowed the Boehm GC's mark stack
# and crashed the game (2026-07-18, "Unexpected mark stack overflow" in the gc log). The
# active screen is the starred stack entry; a scene's available choices are the choice nodes
# not marked unavailable.

STEP=0
SILENT=0
NONE_COUNT=0
LAST_TITLE=""
TITLE_COUNT=0
LAST_SPOKEN=""
LOGC=$(logcur)

while [ "$STEP" -lt "$MAX" ]; do
  STEP=$((STEP + 1))

  # Life support (harness intervention, see DECISIONS.md): the fourth-death trial rewinds
  # the whole chapter, so three runs in a row died before reaching the chapter final. Reset
  # the death counter (trial fires at 4) and keep Will off the floor so the run can finish;
  # standard deaths still occur and traverse in place. One eval per 60 steps (~10 per leg)
  # stays far below the ~400-eval GC crash documented in CLAUDE.md.
  # No declarations in the eval: the REPL keeps state for the whole game session, so a
  # `var` would compile once and fail as a redefinition on every later call - which is
  # exactly what happened (silently, because the output was discarded) the first time a
  # trial fired straight through life support. The result is logged; a non-ok result is
  # loud but does not abort (one missed reset is survivable, a pattern of them is not).
  if [ $((STEP % 60)) -eq 1 ]; then
    LS=$(curl -s -m 10 -X POST "$BASE/eval" --data-binary '_Scripts.Managers.ParametersManager.Instance.Parameters.Death = 0; if (_Scripts.Managers.ParametersManager.Instance.Parameters.Will < 15) _Scripts.Managers.ParametersManager.Instance.Parameters.Will = 15; _Scripts.Managers.ParametersManager.Instance.OnBeforeSerialize(); "ls Death=" + _Scripts.Managers.ParametersManager.Instance.Parameters.Death + " Will=" + _Scripts.Managers.ParametersManager.Instance.Parameters.Will')
    case "$LS" in
      *"ls Death=0"*) echo "life support step $STEP: $LS" ;;
      *) echo "life support step $STEP FAILED: $LS" ;;
    esac
  fi

  # Mod errors abort; the game's own errors (e.g. UIManager.SetAchievementInfo NREs in the
  # fourth-death Continue path, a game bug that also fires on mouse clicks) are noted and
  # driving continues - the game survives them.
  ERRS=$(curl -s -m 5 "$BASE/log?since=$LOGC&grep=Error" | grep -v '^next:' || true)
  MODERRS=$(echo "$ERRS" | grep 'Brante Access' || true)
  if [ -n "$MODERRS" ]; then
    echo "ABORT step $STEP: mod errors in /log:"; echo "$MODERRS"; exit 1
  fi
  if [ -n "$ERRS" ]; then
    echo "note step $STEP: game errors in /log (continuing):"
    echo "$ERRS" | head -3
  fi
  LOGC=$(logcur)

  # Scene loads block the game's main thread past the probe timeout (seen live after the
  # fourth-death trial's final Continue), so one failed probe is not a dead server: retry
  # before giving up.
  NAVTRY=0
  while :; do
    NAVOUT=$(curl -s -m 10 "$BASE/nav")
    echo "$NAVOUT" | grep -q '^stack:' && break
    NAVTRY=$((NAVTRY + 1))
    if [ "$NAVTRY" -ge 6 ]; then
      echo "ABORT step $STEP: no /nav response after $NAVTRY probes (dev server gone or module not loaded)"; exit 1
    fi
    sleep 5
  done
  ACTIVE=$(echo "$NAVOUT" | sed -n 's/^stack:.* \([a-z]*\)([0-9]*)\*.*$/\1/p')
  [ -z "$ACTIVE" ] && ACTIVE=none
  CHOICE_IDS=""
  if [ "$ACTIVE" != "scene" ]; then
    KIND="screen $ACTIVE"
    TITLE="screen $ACTIVE"
  else
    # Graph lines render ids as "ControlId(scene:choice:N, ref=...)"; the focused-node
    # header repeats the id, so only graph lines' leading token counts (the focused graph
    # line starts with "* " instead of two spaces).
    CHOICE_IDS=$(echo "$NAVOUT" | awk '{id=($1=="*")?$2:$1; if (id ~ /^ControlId\(scene:choice:[0-9]+,$/) {gsub(/^ControlId\(scene:choice:|,$/,"",id); print id}}')
    AVAIL=$(echo "$NAVOUT" | grep -v ', unavailable' \
      | awk '{id=($1=="*")?$2:$1; if (id ~ /^ControlId\(scene:choice:[0-9]+,$/) {gsub(/^ControlId\(scene:choice:|,$/,"",id); print id}}' \
      | tr '\n' ',' | sed 's/,$//')
    # Progress fingerprint for the stuck detector: newest transcript page + choice set.
    TITLE="scene:$(echo "$NAVOUT" | grep -o 'scene:page:[0-9]*' | tail -1):$CHOICE_IDS"
    if [ -n "$AVAIL" ]; then KIND="choices $AVAIL"; else KIND=advance; fi
  fi
  if [ "$KIND" != "screen none" ]; then NONE_COUNT=0; fi

  # Stuck means same scene AND no new speech: long scenes legitimately run past 40 pages
  # (The Girl from Your Past is 41+), so the counter resets on any fresh spoken line below.
  if [ "$TITLE" = "$LAST_TITLE" ]; then TITLE_COUNT=$((TITLE_COUNT + 1)); else TITLE_COUNT=0; LAST_TITLE="$TITLE"; fi
  if [ "$TITLE_COUNT" -gt 40 ]; then
    echo "ABORT step $STEP: stuck in scene '$TITLE' for $TITLE_COUNT steps with no new speech"; exit 1
  fi

  case "$KIND" in
    "screen chapterstart")
      # The chapter start book (verified surface): page forward with End+Enter (End lands
      # on the next arrow); the last page reveals Continue before the arrows, so walk to it
      # by position when it appears.
      # Other swept buttons (the New Sections page's section rows) share the
      # chapterstart:btn: prefix, so require the Continue label on the same line.
      # Node positions count only node-START lines (multi-line LeafTexts wrap: counting
      # every line once walked onto the prev arrow and ping-ponged the last two pages).
      TARGET=$(echo "$NAVOUT" | awk '/^graph \(/{g=1;next} /^stack:/{g=0} g && /^(\* |  )ControlId\(/{n++; if ($0 ~ /chapterstart:btn:/ && $0 ~ /"Continue, button/) last=n} END{if (last) print last}')
      if [ -n "$TARGET" ]; then
        ACTION="walk $TARGET"
      else
        ACTION="endenter"
      fi ;;
    "screen chapterfinal")
      echo "STOP step $STEP: chapter final window reached (chapter save written)"; exit 0 ;;
    "screen mainmenu")
      echo "STOP step $STEP: main menu"; exit 0 ;;
    "screen death"|"screen interlude"|"screen popup"|"screen chapterpicture")
      ACTION="endenter" ;;
    "screen chapterselect")
      # Two surfaces share this screen. The between-chapters loading screen: Continue is
      # the start node (End would land on a locked chapter station and refuse), so
      # Home+Enter. The relive selection (after the fourth death): the age text is the
      # start node, chapter stations follow - walk to the LAST available chapter so the
      # run resumes at the furthest reached content.
      TARGET=$(echo "$NAVOUT" | awk '/^graph \(/{g=1;next} /^stack:/{g=0} g && /^(\* |  )ControlId\(/{n++; if ($0 ~ /chapterselect:chapter:/ && $0 !~ /, unavailable/) last=n} END{if (last) print last}')
      if [ -n "$TARGET" ]; then
        ACTION="walk $TARGET"
      else
        ACTION="homeenter"
      fi ;;
    "screen chapterrestart")
      # Rewind confirmation from the relive selection: Confirm is node 2 (End would land
      # on Cancel).
      ACTION="walk 2" ;;
    "screen cutscene")
      echo "note step $STEP: cutscene - waiting"; sleep 5; continue ;;
    "screen none")
      # Transient: the screen stack empties during scene loads. Retry; a surface the mod
      # genuinely does not cover stays "none" and falls to the stuck-title abort.
      NONE_COUNT=$((NONE_COUNT + 1))
      if [ "$NONE_COUNT" -ge 8 ]; then
        echo "ABORT step $STEP: no active screen after $NONE_COUNT probes (uncovered surface)"; exit 1
      fi
      echo "note step $STEP: no active screen - retrying"; sleep 2; continue ;;
    screen*)
      echo "ABORT step $STEP: unhandled screen '$KIND'"; exit 1 ;;
    choices*)
      LIST=${KIND#choices }
      N=$(echo "$LIST" | tr ',' '\n' | wc -l)
      PICK_AT=$((STEP % N + 1))
      PICK=$(echo "$LIST" | tr ',' '\n' | sed -n "${PICK_AT}p")
      ACTION="choice $PICK" ;;
    advance)
      ACTION="endenter" ;;
    *)
      echo "ABORT step $STEP: unparseable probe result: $KIND"; exit 1 ;;
  esac

  C=$(cursor)
  if [ "$ACTION" = "endenter" ]; then
    press ui.end; sleep 0.3; press ui.activate
  elif [ "$ACTION" = "homeenter" ]; then
    press ui.home; sleep 0.3; press ui.activate
  elif [ "${ACTION%% *}" = "walk" ]; then
    # Walk to the Nth graph node by position: Home seats node 1, Down steps forward.
    DOWNS=$(( ${ACTION#walk } - 1 ))
    press ui.home
    WI=0
    while [ "$WI" -lt "$DOWNS" ]; do press ui.down; sleep 0.15; WI=$((WI + 1)); done
    sleep 0.3; press ui.activate
  else
    # Reach the picked choice by keys alone: choices are the last nodes of the transcript
    # stop, so End lands on the last choice and Up walks back to the target (unavailable
    # choices are nodes too, so the walk counts ALL choice nodes, not just available ones).
    PICKED=${ACTION#choice }
    TOTAL=$(echo "$CHOICE_IDS" | grep -c .)
    POS=$(echo "$CHOICE_IDS" | grep -n "^$PICKED\$" | head -1 | cut -d: -f1)
    if [ -z "$POS" ]; then
      echo "ABORT step $STEP: picked choice $PICKED not in choice list"; exit 1
    fi
    UPS=$((TOTAL - POS))
    press ui.end
    UI=0
    while [ "$UI" -lt "$UPS" ]; do press ui.up; sleep 0.15; UI=$((UI + 1)); done
    sleep 0.3; press ui.activate
  fi

  SPOKEN=$(curl -s -m 15 "$BASE/speech?since=$C&wait=9000" | grep -v '^next:' || true)
  if [ -z "$SPOKEN" ]; then
    SILENT=$((SILENT + 1))
    echo "step $STEP [$KIND -> $ACTION] SILENT ($SILENT)"
    if [ "$SILENT" -ge 3 ]; then echo "ABORT: three consecutive silent actions"; exit 1; fi
    sleep 2; continue
  fi
  SILENT=0
  # Ring cursor numbers make identical repeated text look new - strip them before comparing.
  SPOKEN_TEXT=$(echo "$SPOKEN" | sed 's/^[0-9]*: //')
  if [ "$SPOKEN_TEXT" != "$LAST_SPOKEN" ]; then TITLE_COUNT=0; LAST_SPOKEN="$SPOKEN_TEXT"; fi
  echo "step $STEP [$KIND -> $ACTION] $(echo "$SPOKEN" | head -2 | cut -c1-140)"
done
echo "STOP: max steps ($MAX) reached"
exit 0
