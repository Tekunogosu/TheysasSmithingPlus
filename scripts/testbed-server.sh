#!/usr/bin/env bash
# Runs the SmithingPlus testbed server against its own data directory, rebuilding
# and installing the mod first. Runs in the foreground: Ctrl+C stops it.
#
#   testbed-server.sh [start]   build, install, launch  (default)
#   testbed-server.sh wipe      delete the world, keep config and mods, relaunch
#   testbed-server.sh wipe-all  also delete configs and player data, relaunch
#   testbed-server.sh stop      stop one running in another terminal
#   testbed-server.sh logs      tail the last server log
#   testbed-server.sh status    is a testbed server running?

set -euo pipefail
. "$(dirname "$(readlink -f "$0")")/common.sh"

WORLD="$SERVER_DATA/Saves/smithingplus.vcdbs"
CONFIG="$SERVER_DATA/serverconfig.json"

# The server settings a testbed wants, as opposed to a public server's. Written
# once, on first run; after that the file is the user's to edit.
#
# Nothing here shapes the world: SmithingPlus changes what can be worked on an
# anvil, not what the world looks like, so the testbed generates an ordinary
# survival world.
seed_config() {
	[ -f "$CONFIG" ] && return 0

	require_game
	mkdir -p "$SERVER_DATA"
	printf 'config: generating %s\n' "$CONFIG"

	( cd "$GAME" && dotnet VintagestoryServer.dll --dataPath "$SERVER_DATA" --genconfig ) >/dev/null

	python3 - "$CONFIG" "$WORLD" "$TESTBED_PORT" <<-'PY'
	import json, sys
	path, world, port = sys.argv[1], sys.argv[2], int(sys.argv[3])
	with open(path) as fh:
	    cfg = json.load(fh)

	if not cfg.get("WorldConfig"):
	    cfg["WorldConfig"] = {}
	wc = cfg["WorldConfig"]
	wc["SaveFileLocation"] = world
	wc["WorldName"] = "smithingplus"

	# A testbed is for one person walking around in it, so the gates that exist
	# for public servers only get in the way here.
	cfg["ServerName"] = "SmithingPlus testbed"
	cfg["Port"] = port
	cfg["WhitelistMode"] = "off"
	cfg["AdvertiseServer"] = False
	cfg["Upnp"] = False
	cfg["PassTimeWhenEmpty"] = False

	with open(path, "w") as fh:
	    json.dump(cfg, fh, indent=1)
	PY
}

# The port lives in serverconfig.json, which is written once and then belongs to
# the user. A testbed created before the port moved would keep dialling the old
# one forever, so the value is reasserted on every start -- and only rewritten
# when it actually differs, leaving every other edit to the file untouched.
enforce_port() {
	[ -f "$CONFIG" ] || return 0

	python3 - "$CONFIG" "$TESTBED_PORT" <<-'PY' || die "could not check the port in $CONFIG"
	import json, sys
	path, port = sys.argv[1], int(sys.argv[2])
	with open(path) as fh:
	    cfg = json.load(fh)
	if cfg.get("Port") == port:
	    sys.exit(0)
	was = cfg.get("Port")
	cfg["Port"] = port
	with open(path, "w") as fh:
	    json.dump(cfg, fh, indent=1)
	print(f"port:   serverconfig.json {was} -> {port}")
	PY
}

# Stopping first matters for the wipe commands: deleting Saves out from under a
# running server leaves it writing into files that no longer exist, and the
# world it then saves on shutdown can land back on disk after the wipe.
stop_running() {
	pids=$(pgrep -f "VintagestoryServer.dll --dataPath $SERVER_DATA" || true)
	[ -z "$pids" ] && return 0

	printf 'note:   stopping testbed server already running (pid %s)\n' "$(echo $pids | tr '\n' ' ')"
	kill $pids 2>/dev/null || true
	for _ in $(seq 1 30); do
		pgrep -f "VintagestoryServer.dll --dataPath $SERVER_DATA" >/dev/null || return 0
		sleep 1
	done
	die "the previous testbed server would not stop; kill it by hand and retry"
}

case "${1:-start}" in
	start) ;;
	wipe|wipe-all) stop_running ;;&
	wipe)
		# The world only. Everything that is expensive to put back -- the Mods
		# folder, serverconfig.json -- is left alone, because regenerating
		# terrain is the common case and none of that is part of the terrain.
		rm -rf "$SERVER_DATA/Saves" "$SERVER_DATA/Cache" "$SERVER_DATA/Logs"
		printf 'wipe:   world deleted, config and mods kept\n'
		;;
	wipe-all)
		# Also the configs. SmithingPlus regenerates ModConfig/SmithingPlus.json
		# at boot, so this is how to get back to the shipped defaults after
		# editing the live one by hand.
		rm -rf "$SERVER_DATA/Saves" "$SERVER_DATA/Cache" "$SERVER_DATA/Logs" \
		       "$SERVER_DATA/ModConfig" "$SERVER_DATA/Playerdata" \
		       "$SERVER_DATA/serverconfig.json" "$SERVER_DATA/servermagicnumbers.json"
		printf 'wipe-all: world, configs and player data deleted, mods kept\n'
		;;
	logs)
		exec tail -n 200 -f "$SERVER_DATA/Logs/server-main.log"
		;;
	stop)
		if pids=$(pgrep -f "VintagestoryServer.dll --dataPath $SERVER_DATA"); then
			kill $pids 2>/dev/null || true
			printf 'stop:   signalled %s\n' "$(echo $pids | tr '\n' ' ')"
		else
			printf 'stop:   nothing running\n'
		fi
		exit 0
		;;
	status)
		if pgrep -f "VintagestoryServer.dll --dataPath $SERVER_DATA" >/dev/null; then
			echo "running"
		else
			echo "not running"
		fi
		exit 0
		;;
	*)
		die "usage: $(basename "$0") {start|stop|wipe|wipe-all|logs|status}"
		;;
esac

require_game

# "Address already in use" is otherwise the whole error: a server left running
# from an earlier session holds the port and the new one dies at socket bind,
# after a startup log long enough to look like it got further than it did.
stop_running

seed_config
enforce_port
install_mod "$SERVER_DATA"
install_extra_mods "$SERVER_DATA"
install_toolsmith "$SERVER_DATA"

printf 'data:   %s\n' "$SERVER_DATA"
printf 'world:  %s\n' "$WORLD"
printf 'port:   %s\n' "$TESTBED_PORT"
printf 'start:  Ctrl+C to stop\n\n'

cd "$GAME"
exec dotnet VintagestoryServer.dll --dataPath "$SERVER_DATA"
