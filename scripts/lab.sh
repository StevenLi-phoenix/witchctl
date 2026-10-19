#!/usr/bin/env bash
# Throwaway copy of the game for development. The Steam install is only ever read.
#   lab.sh clone     APFS-clone the Steam app into .lab (instant, no extra disk)
#   lab.sh install   build + copy the mod into the clone
#   lab.sh start     launch hidden in the background (never takes focus) and wait for the bridge
#   lab.sh stop      stop the clone (only processes whose path is inside .lab)
#   lab.sh reset     wipe the lab copy's own save data + preferences (fresh profile; never touches the real one)
#   lab.sh status    running? bridge reachable?
source "$(dirname "$0")/common.sh"

cmd="${1:-status}"
case "$cmd" in
  clone)
    [ -d "$STEAM_APP" ] || { echo "game not found at: $STEAM_APP" >&2; exit 2; }
    [ -n "$(lab_pids)" ] && { echo "lab game is running; run: lab.sh stop" >&2; exit 2; }
    mkdir -p "$LAB"
    rm -rf "$APP"
    /bin/cp -Rc "$STEAM_APP" "$APP"
    # Own bundle id => own save folder and own preferences: a lab run can never touch the real profile.
    /usr/libexec/PlistBuddy -c "Set :CFBundleIdentifier $LAB_BUNDLE_ID" "$APP/Contents/Info.plist"
    codesign --force --deep --sign - "$APP" >/dev/null 2>&1
    echo "cloned to $APP (bundle id $LAB_BUNDLE_ID)"
    ;;
  install)
    # Overwriting the DLL a running game has loaded and re-signing the running bundle can crash it (and the crash dialog takes focus).
    [ -z "$(lab_pids)" ] || { echo "lab game is running; run: lab.sh stop" >&2; exit 2; }
    "$ROOT/scripts/build.sh"
    [ -d "$APP" ] || { echo "no clone yet; run: lab.sh clone" >&2; exit 2; }
    mkdir -p "$MOD_DIR/Scripts"
    cp "$ROOT/build/WitchCtl.local.dll" "$MOD_DIR/Scripts/Entry.dll"
    cp "$ROOT/mod/ModConfig.json" "$ROOT/mod/Icon.png" "$MOD_DIR/"
    codesign --force --deep --sign - "$APP" >/dev/null 2>&1   # re-seal after adding files
    echo "installed into $MOD_DIR"
    ;;
  start)
    [ -d "$APP" ] || { echo "no clone yet; run: lab.sh clone" >&2; exit 2; }
    [ -n "$(lab_pids)" ] && { echo "already running"; exit 0; }
    : > "$LOG"
    # -g: do not bring to front, -j: launch hidden. env -u: do not leak Claude Code's NO_COLOR/PAGER into the app.
    env -u NO_COLOR -u PAGER -u CLAUDECODE open -g -j --env WITCHCTL_MUTE=1 "$APP" --args -logFile "$LOG"
    # The port opens early, but the main thread keeps loading for a while. Ready = thirty fast /state answers in a row (the game has a multi-second loading hitch shortly after the menu appears).
    streak=0
    for _ in $(seq 1 120); do
      ms="$(curl -fs -m2 -o /dev/null -w '%{time_total}' "http://127.0.0.1:$PORT/state" 2>/dev/null | awk '{printf "%d", $1*1000}')" || ms=""
      if [ -n "$ms" ] && [ "$ms" -lt 100 ]; then streak=$((streak+1)); else streak=0; fi
      if [ "$streak" -ge 30 ]; then echo "bridge ready on :$PORT"; exit 0; fi
      sleep 0.5
    done
    echo "bridge not ready in 90s; see $LOG" >&2; exit 1
    ;;
  stop)
    pids="$(lab_pids)"
    [ -n "$pids" ] || { echo "not running"; exit 0; }
    for pid in $pids; do
      kill "$pid" && echo "stopped $pid"
    done
    # Starting again while LaunchServices still sees the old process fails with -600; wait until it is really gone.
    for _ in $(seq 1 50); do [ -z "$(lab_pids)" ] && break; sleep 0.2; done
    [ -z "$(lab_pids)" ] || { echo "game did not exit" >&2; exit 1; }
    sleep 1
    ;;
  reset)
    [ -z "$(lab_pids)" ] || { echo "lab game is running; run: lab.sh stop" >&2; exit 2; }
    # Guard: only ever delete the folder that belongs to the lab bundle id.
    case "$LAB_DATA" in *"/$LAB_BUNDLE_ID") ;; *) echo "refusing to delete $LAB_DATA" >&2; exit 2 ;; esac
    rm -rf "$LAB_DATA"
    defaults delete "$LAB_BUNDLE_ID" >/dev/null 2>&1 || true
    echo "lab profile reset ($LAB_DATA)"
    ;;
  status)
    if [ -n "$(lab_pids)" ]; then echo "game: running (pid $(lab_pids | tr '\n' ' '))"; else echo "game: not running"; fi
    curl -fs -m1 "http://127.0.0.1:$PORT/ping" && echo || echo "bridge: unreachable on :$PORT"
    ;;
  *) echo "usage: $0 clone|install|start|stop|reset|status" >&2; exit 2 ;;
esac
