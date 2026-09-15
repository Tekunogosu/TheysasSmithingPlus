#!/usr/bin/env bash
# Runs the real Vintage Story client against the testbed's data directory.
#
# The client is the one already installed and configured; only where it reads
# and writes is changed. Settings are re-synced from the real install on every
# launch, so the credentials, hotkeys and video preferences here are always the
# ones set up over there and the testbed never has to be logged into by hand.
#
#   testbed-client.sh [connect]   build, install, launch, connect to localhost
#   testbed-client.sh solo        launch and open a local world instead
#   testbed-client.sh menu        launch and stop at the main menu

set -euo pipefail
. "$(dirname "$(readlink -f "$0")")/common.sh"

HOST="${SMITHINGPLUS_HOST:-localhost:$TESTBED_PORT}"
MODE="${1:-connect}"

# Takes the real install's settings, credentials included, and points the copy
# at the testbed's own folders.
#
# Copied on every launch rather than once, because the login lives in this file:
# a copy taken once drifts, and the moment the real install's session key is
# renewed the testbed is asking to log in again -- which is the whole reason
# this is not a separate profile. Anything changed in the testbed's own options
# is overwritten on the next launch by design; the real install is where
# settings are meant to be edited.
#
# modPaths is the part that cannot be left alone, and only its second entry.
# Neither entry follows --dataPath, but they are not the same kind of thing:
#
#   "Mods"  is relative, and resolves against the install directory rather than
#           the data path. That is where VSSurvivalMod and VSEssentials live --
#           the base game's own code mods -- so it has to stay. Dropping it
#           leaves every mod's game@ and survival@ dependencies unresolvable,
#           and this mod is then disabled before it ever loads.
#
#   the second entry is written out as an absolute path to the real data
#           folder, and is the one that would pull the entire installed mod
#           collection into the testbed. It is what gets replaced.
#
# So the list becomes the install's own Mods folder plus the testbed's, and
# nothing of the real data folder. (--addModPath cannot express this: it only
# ever adds to what the settings already name, and what needs doing here is a
# removal.) This matters more than usual here: the real install carries
# smithingplusmaterialcache, whose fixes this fork now contains, and letting it
# load beside the fork would have it patch code that has been rewritten.
#
# disabledMods is cleared for the same reason in reverse: it travels with the
# copy, so a mod switched off in the real client would be switched off here too,
# and the failure that produces -- the mod under test silently not loading --
# looks like a bug in the mod rather than a setting inherited from elsewhere.
# The testbed installs exactly the mods it means to run, so none of them should
# ever start disabled.
sync_settings() {
	src="$REAL_DATA/clientsettings.json"
	dst="$CLIENT_DATA/clientsettings.json"

	[ -f "$src" ] || die "no client settings at $src -- run the real client once first, so there is a login to borrow"

	python3 - "$src" "$dst" "$CLIENT_DATA/Mods" <<-'PY' || die "could not sync settings from $src"
	import json, sys
	src, dst, mods = sys.argv[1], sys.argv[2], sys.argv[3]
	with open(src) as fh:
	    cfg = json.load(fh)
	settings = cfg.setdefault("stringListSettings", {})
	settings["modPaths"] = ["Mods", mods]
	settings["disabledMods"] = []
	with open(dst, "w") as fh:
	    json.dump(cfg, fh, indent=1)
	PY

	# The source is credential-bearing and so is this copy.
	chmod 600 "$dst"
	printf 'settings: synced from %s\n' "$src"
}

require_game
[ -x "$GAME/Vintagestory" ] || die "the client binary is not at '$GAME/Vintagestory' (a server-only install has no client)"

case "$MODE" in
	connect) args=(--connect "$HOST") ;;
	solo)    args=(--openWorld smithingplus) ;;
	menu)    args=() ;;
	*)       die "usage: $(basename "$0") {connect|solo|menu}" ;;
esac

mkdir -p "$CLIENT_DATA"
sync_settings
install_mod "$CLIENT_DATA"
install_extra_mods "$CLIENT_DATA"
install_toolsmith "$CLIENT_DATA"

case "$MODE" in
	connect) printf 'connect: %s\n' "$HOST" ;;
	solo)    printf 'world:  local "smithingplus"\n' ;;
	menu)    printf 'start:  main menu\n' ;;
esac

printf 'data:   %s\n\n' "$CLIENT_DATA"

# The game loads its own fonts through this and will not start without it.
export FONTCONFIG_FILE="$GAME/fonts.conf"

# Exported rather than set inline on the command: exec takes no assignments.
export mesa_glthread=true

cd "$GAME"
exec ./Vintagestory --dataPath "$CLIENT_DATA" "${args[@]}"
