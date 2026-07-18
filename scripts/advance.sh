#!/usr/bin/env bash
# Story-advance driver (ROADMAP Phase 8, save-jump harness) - pushes the live game forward
# through the mod's own navigator, verifying speech after every action (act, then listen).
# Reaching a chapter makes the game write that chapter's save, which is the jump point for
# later-chapter window verification.
#
# Per step: probe the mod's screen stack via /eval, then either activate a choice (rotating
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
evalcs() { curl -s -m 15 -X POST "$BASE/eval" --data-binary "$1"; }

PROBE='
var cur = BranteAccess.Module.Screens.ScreenManager.Current;
string key = cur == null ? "none" : cur.Key;
string line;
if (key != "scene") { line = "screen " + key; }
else
{
    var sc = UnityEngine.Object.FindObjectOfType<_Scripts.AMVCC.Controllers.SceneController>();
    string title = sc.Title.GetComponent<TMPro.TextMeshProUGUI>().text.Replace("|", " ");
    var avail = new System.Collections.Generic.List<string>();
    if (!sc.GetButtonClickedState())
    {
        var pbcs = new System.Collections.Generic.List<_Scripts.AMVCC.Views.ParameterButtonChanger>(
            UnityEngine.Object.FindObjectsOfType<_Scripts.AMVCC.Views.ParameterButtonChanger>());
        pbcs.Sort((a, c) => a.ButtonIndex.CompareTo(c.ButtonIndex));
        foreach (var p in pbcs)
            if (p.IsButtonInteractable && BranteAccess.Module.Game.UiWidgets.Visible(p.gameObject))
                avail.Add(p.ButtonIndex.ToString());
    }
    line = (avail.Count > 0 ? "choices " + string.Join(",", avail.ToArray()) : "advance") + "|" + title;
}
line'

STEP=0
SILENT=0
NONE_COUNT=0
LAST_TITLE=""
TITLE_COUNT=0
LAST_SPOKEN=""
LOGC=$(logcur)

while [ "$STEP" -lt "$MAX" ]; do
  STEP=$((STEP + 1))

  ERRS=$(curl -s -m 5 "$BASE/log?since=$LOGC&grep=Error" | grep -v '^next:' || true)
  if [ -n "$ERRS" ]; then
    echo "ABORT step $STEP: mod errors in /log:"; echo "$ERRS"; exit 1
  fi
  LOGC=$(logcur)

  RAW=$(evalcs "$PROBE")
  DIRECTIVE=$(echo "$RAW" | head -1 | sed 's/^=> //')
  KIND=${DIRECTIVE%%|*}
  TITLE=${DIRECTIVE#*|}
  if [ "$KIND" != "screen none" ]; then NONE_COUNT=0; fi

  # Stuck means same scene AND no new speech: long scenes legitimately run past 40 pages
  # (The Girl from Your Past is 41+), so the counter resets on any fresh spoken line below.
  if [ "$TITLE" = "$LAST_TITLE" ]; then TITLE_COUNT=$((TITLE_COUNT + 1)); else TITLE_COUNT=0; LAST_TITLE="$TITLE"; fi
  if [ "$TITLE_COUNT" -gt 40 ]; then
    echo "ABORT step $STEP: stuck in scene '$TITLE' for $TITLE_COUNT steps with no new speech"; exit 1
  fi

  case "$KIND" in
    "screen chapterstart")
      echo "STOP step $STEP: chapter start window reached (manual verification item)"; exit 0 ;;
    "screen chapterfinal")
      echo "STOP step $STEP: chapter final window reached (chapter save written)"; exit 0 ;;
    "screen mainmenu")
      echo "STOP step $STEP: main menu"; exit 0 ;;
    "screen death"|"screen interlude"|"screen popup"|"screen chapterpicture")
      ACTION="endenter" ;;
    "screen chapterselect")
      # Between-chapters loading screen: Continue is the start node (End would land on a
      # locked chapter station and refuse).
      ACTION="homeenter" ;;
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
      echo "ABORT step $STEP: unparseable probe result: $RAW"; exit 1 ;;
  esac

  C=$(cursor)
  if [ "$ACTION" = "endenter" ]; then
    press ui.end; sleep 0.3; press ui.activate
  elif [ "$ACTION" = "homeenter" ]; then
    press ui.home; sleep 0.3; press ui.activate
  else
    PICKED=${ACTION#choice }
    evalcs "BranteAccess.Module.UI.Navigation.FocusNode(BranteAccess.Module.UI.Graph.ControlId.Structural(\"scene:choice:$PICKED\"), false); \"focused\"" > /dev/null
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
  if [ "$SPOKEN" != "$LAST_SPOKEN" ]; then TITLE_COUNT=0; LAST_SPOKEN="$SPOKEN"; fi
  echo "step $STEP [$KIND -> $ACTION] $(echo "$SPOKEN" | head -2 | cut -c1-140)"
done
echo "STOP: max steps ($MAX) reached"
exit 0
