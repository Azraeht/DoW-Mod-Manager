#!/usr/bin/env bash
# Install the Unification mod into Dawn of War: Definitive Edition (Steam + Proton), build the
# cross-platform mod manager and run it against that install.
#
# Usage: scripts/test-unification-de.sh [all|build|backup|install|verify|run|log|uninstall]...
#   all (default) = build backup install verify run
#   DIRECT=1 makes "run" start the manager with DOWMM_PROTON (direct Proton route, not Steam).
# Every path below can be overridden from the environment.
set -euo pipefail
shopt -s nullglob

REPO="${REPO:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
DL_DIR="${DL_DIR:-$(xdg-user-dir DOWNLOAD 2>/dev/null || echo "$HOME/Downloads")}"
INSTALLER="${INSTALLER:-$DL_DIR/Unification-v7.3.8.exe}"
ARCHIVE="$(dirname "$INSTALLER")/Unification.7z"   # the installer reads it from its own folder
STEAM_CLIENT="${STEAM_CLIENT:-$HOME/snap/steam/common/.local/share/Steam}"
LIBRARY="${LIBRARY:-/mnt/steamssd/steam}"
APPID=3556750
GAME="${GAME:-$LIBRARY/steamapps/common/Dawn of War Definitive Edition}"
COMPAT="$LIBRARY/steamapps/compatdata/$APPID"
USERDATA="$COMPAT/pfx/drive_c/users/steamuser/AppData/Roaming/Relic Entertainment/Dawn of War"
BACKUP_ROOT="${BACKUP_ROOT:-$HOME/dowmm-backup}"
PUBLISH_DIR="$REPO/artifacts/linux-x64"
export PATH="$HOME/.dotnet:$PATH"

log() { printf '\n>> %s\n' "$*"; }
die() { printf 'error: %s\n' "$*" >&2; exit 1; }

preflight() {
    [[ -d "$GAME" ]] || die "game folder not found: $GAME"
    [[ -d "$COMPAT/pfx" ]] || die "no Proton prefix at $COMPAT; launch the game once from Steam"
    ! pgrep -f 'W40k(_gog)?\.exe' >/dev/null || die "the game is running; quit it first"
}

# The Proton build the prefix was made with (compatdata/<appid>/version names a custom tool such
# as GE-Proton), so running the installer does not upgrade the prefix. PROTON overrides it.
resolve_proton() {
    if [[ -n "${PROTON:-}" ]]; then echo "$PROTON"; return; fi
    local name tool fallback
    name="$(head -n1 "$COMPAT/version" 2>/dev/null || true)"
    tool="$STEAM_CLIENT/compatibilitytools.d/$name/proton"
    if [[ -n "$name" && -f "$tool" ]]; then echo "$tool"; return; fi
    fallback="$STEAM_CLIENT/steamapps/common/Proton - Experimental/proton"
    [[ -f "$fallback" ]] || die "no Proton found for prefix '$name'; set PROTON=/path/to/proton"
    printf 'warning: prefix was made with "%s"; using Proton Experimental\n' "$name" >&2
    echo "$fallback"
}

# HOME for Proton. Its Steam bridge (lsteamclient) loads $HOME/.steam/sdk64/steamclient.so; snap
# and Flatpak Steam keep that link in their own home, which Steam passes to Proton as HOME.
# Without it, Proton's steam.exe crashes before starting the program.
steam_home() {
    local base="${STEAM_CLIENT%/.local/share/Steam}"
    if [[ ! -e "$HOME/.steam/sdk64/steamclient.so" && -e "$base/.steam/sdk64/steamclient.so" ]]; then
        echo "$base"
    else
        echo "$HOME"
    fi
}

# Runs a Windows program in the game's compatdata the way Steam does: Proton inside the Steam
# Linux Runtime (sniper) container, which GE-Proton and Proton 9+ require. Started on the host
# instead, 32-bit programs such as NSIS installers miss host libraries (FreeType) and fail.
prefix_run() {
    local proton runtime
    proton="$(resolve_proton)"
    runtime="$STEAM_CLIENT/steamapps/common/SteamLinuxRuntime_sniper/_v2-entry-point"
    [[ -x "$runtime" ]] || die "Steam Linux Runtime (sniper) not found: $runtime; install it from Steam"
    log "Running $(basename "$1") with $proton (in the sniper runtime, HOME=$(steam_home))"
    HOME="$(steam_home)" STEAM_COMPAT_DATA_PATH="$COMPAT" STEAM_COMPAT_CLIENT_INSTALL_PATH="$STEAM_CLIENT" \
        STEAM_COMPAT_MOUNTS="$(dirname "$1")" \
        "$runtime" --verb=waitforexitandrun -- "$proton" waitforexitandrun "$@"
}

cmd_build() {
    command -v dotnet >/dev/null || die "dotnet not found (expected in ~/.dotnet)"
    cd "$REPO"
    log "Tests"
    dotnet test tests/DowModManager.Tests/DowModManager.Tests.csproj --nologo -v quiet
    log "Publish to $PUBLISH_DIR"
    dotnet publish "DoW Mod Manager.CrossPlatform/DowModManager.csproj" -f net10.0 -r linux-x64 \
        --self-contained -p:PublishSingleFile=true -o "$PUBLISH_DIR" --nologo -v quiet
    log "AppImage"
    bash packaging/linux/build-appimage.sh "$PUBLISH_DIR"
}

cmd_backup() {
    preflight
    local dir files
    dir="$BACKUP_ROOT/$(date +%Y%m%d-%H%M%S)"
    files=("$GAME"/*.module "$GAME"/*.ini)
    mkdir -p "$dir"
    log "Backup to $dir"
    (( ${#files[@]} )) && cp -a "${files[@]}" "$dir"/
    if [[ -d "$USERDATA" ]]; then
        tar -C "$(dirname "$USERDATA")" -czf "$dir/userdata.tgz" "$(basename "$USERDATA")"
    fi
    ls -l "$dir"
}

cmd_install() {
    preflight
    [[ -f "$INSTALLER" ]] || die "installer not found: $INSTALLER"
    [[ -f "$ARCHIVE" ]] || die "Unification.7z must sit next to the installer: $ARCHIVE"
    local need free
    need=$(( $(stat -c %s "$ARCHIVE") + 2 * 1024 ** 3 ))   # .sga data barely compresses
    free=$(df --output=avail -B1 "$GAME" | tail -n1)
    (( free > need )) || die "not enough space on $(df --output=target "$GAME" | tail -n1): need ~$(( need / 1024 ** 3 )) GiB"
    cat <<EOF

In the installer:
  - pick the Steam version
  - game folder, if asked:          Z:${GAME//\//\\}
  - Relic Entertainment folder, if asked:
        C:\\users\\steamuser\\AppData\\Roaming\\Relic Entertainment\\Dawn of War
  - leave the Soulstorm folder empty
EOF
    prefix_run "$INSTALLER"
    read -rp "Press Enter once the installer window has closed... "
}

cmd_verify() {
    log "Unification modules"
    local m d found=0
    for m in "$GAME"/Unification*.module "$USERDATA"/Mods/Unification*.module "$USERDATA"/Mods/*/Unification*.module; do
        found=1
        echo "== $m"
        grep -iE '^\s*(RequiredMod|ModFolder|Playable|ModVersion)' "$m" || true
    done
    (( found )) || die "no Unification*.module in $GAME or $USERDATA/Mods"
    log "Data folders"
    for d in "$GAME"/Unification*/ "$USERDATA"/Mods/Unification*/; do du -sh "$d"; done
    log "Manager settings written by the installer"
    grep -H 'SoulstormDir' "$GAME/DoW Mod Manager.ini" || echo "(no SoulstormDir entry)"
}

cmd_run() {
    local app apps
    apps=("$REPO"/artifacts/DoW_Mod_Manager-*-x86_64.AppImage)
    app="$PUBLISH_DIR/DowModManager"
    (( ${#apps[@]} )) && app="$(ls -t "${apps[@]}" | head -n1)"
    [[ -x "$app" ]] || die "manager not built; run: $0 build"
    if [[ "${DIRECT:-0}" == 1 ]]; then
        DOWMM_PROTON="$(resolve_proton)"
        export DOWMM_PROTON
        log "Direct Proton route: DOWMM_PROTON=$DOWMM_PROTON"
    fi
    log "Starting $app (run '$0 log' in another terminal to follow warnings.log)"
    "$app"
}

cmd_log() {
    local f="$USERDATA/warnings.log"
    log "Following $f (Ctrl+C to stop)"
    tail -n0 -F "$f" | grep --line-buffered -E "MOD --|rror|Application closed"
}

cmd_uninstall() {
    preflight
    local un left
    un=("$GAME"/Unification-*-Uninstall.exe)
    (( ${#un[@]} )) || die "no Unification uninstaller in $GAME"
    prefix_run "${un[0]}"
    read -rp "Press Enter once the uninstaller window has closed... "
    left=("$GAME"/Unification*.module "$USERDATA"/Mods/Unification*.module)
    if (( ${#left[@]} )); then printf 'still present: %s\n' "${left[@]}"; else echo "Unification modules removed"; fi
}

main() {
    (( $# )) || set -- all
    local step
    for step in "$@"; do
        case "$step" in
            all) cmd_build; cmd_backup; cmd_install; cmd_verify; cmd_run ;;
            build|backup|install|verify|run|log|uninstall) "cmd_$step" ;;
            -h|--help) sed -n '2,8p' "$0" ;;
            *) die "unknown step '$step' (all|build|backup|install|verify|run|log|uninstall)" ;;
        esac
    done
}

main "$@"
