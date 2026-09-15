# Shared settings for the testbed scripts. Sourced, never run directly.
#
# The testbed lives off the NVMe, alongside the other Vintage Story testbeds on
# this machine rather than on its own. Both overridable, so a machine that keeps
# things elsewhere is not forced into this layout.

TESTBED="${SMITHINGPLUS_TESTBED:-/mnt/media/testbed/smithingplus}"
GAME="${VINTAGE_STORY:-$HOME/.local/share/vintagestory}"

SERVER_DATA="$TESTBED/server"
CLIENT_DATA="$TESTBED/client"

# The real install, read from only, to seed the testbed client's settings once.
REAL_DATA="${VINTAGE_STORY_DATA:-$HOME/.config/VintagestoryData}"

# The testbed's port. The default 42420 is held by the real server, 42450 by the
# Underrealm testbed, 42451 by Sated's and 42470 by Haft's; a testbed sharing any
# of them dies at socket bind with "Address already in use" -- after a startup log
# long enough to look like it got further than it did. Defined here rather than in
# either script so the server that binds it and the client that dials it cannot
# drift apart.
#
# Takes effect for the server only when serverconfig.json is written, which is
# on first run or after wipe-all. An existing testbed carries its old port until
# then, so testbed-server.sh corrects the file in place on every start.
TESTBED_PORT="${SMITHINGPLUS_PORT:-42471}"

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# The game version the mod is compiled against. The csproj resolves this itself,
# but the build is run from here, so the same fallback chain is asserted for it.
export VINTAGE_STORY

die() { printf 'error: %s\n' "$1" >&2; exit 1; }

require_game() {
	[ -f "$GAME/VintagestoryServer.dll" ] ||
		die "Vintage Story not found at '$GAME'. Set VINTAGE_STORY to the folder holding VintagestoryServer.dll."
}

# The packaged zip's name carries BOTH the modid and the version from
# modinfo.json, so it moves on a version bump and on a rename. Read both rather
# than hardcoding either, or the next change leaves the scripts copying a file
# that is no longer produced.
mod_id() {
	python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["modid"])' \
		"$REPO/SmithingPlus/modinfo.json" || die "could not read the modid from SmithingPlus/modinfo.json"
}

mod_zip() {
	version=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["version"])' \
		"$REPO/SmithingPlus/modinfo.json") || die "could not read the version from SmithingPlus/modinfo.json"
	printf '%s/Releases/%s_%s.zip' "$REPO" "$(mod_id)" "$version"
}

# Mods installed alongside ours in the testbed, by filename, taken from the real
# install.
#
# smithingplusmaterialcache is deliberately NOT among them. This fork carries that
# mod's fixes in its own code, and the standalone mod patches the very methods the
# fork has changed: loading both would have it wrap an implementation that no
# longer matches what it expects.
TESTBED_EXTRA_MODS="${SMITHINGPLUS_EXTRA_MODS:-}"

# Toolsmith is built from its own checkout rather than copied from the real
# install, because the interaction under test is with Toolsmith's CURRENT source:
# it takes tool damage over entirely (PreventDefault in OnDamageItem) and calls
# DestroyItem itself only when the head breaks, which is the path this fork's
# broken-head drop now hangs off. The released zip in the real install lags that
# source, so testing against it would test the wrong code.
#
# Setting SMITHINGPLUS_TOOLSMITH_REPO to an empty string leaves Toolsmith out of
# the testbed entirely, which is how the no-Toolsmith case is tested.
#
# Expanded with ${VAR-default} rather than ${VAR:-default}: the colon form treats
# an empty value as unset and would substitute the default right back, so
# "SMITHINGPLUS_TOOLSMITH_REPO= testbed-server.sh" would silently install
# Toolsmith anyway -- the run would look like a no-Toolsmith run and not be one.
TOOLSMITH_REPO="${SMITHINGPLUS_TOOLSMITH_REPO-$HOME/Development/Toolsmith}"

# Copies the extra mods in if they are not already there. Kept separate from
# install_mod so a wipe restores them without a rebuild being involved.
install_extra_mods() {
	target="$1"
	mkdir -p "$target/Mods"

	for name in $TESTBED_EXTRA_MODS; do
		[ -f "$target/Mods/$name" ] && continue
		if [ -f "$REAL_DATA/Mods/$name" ]; then
			cp "$REAL_DATA/Mods/$name" "$target/Mods/$name"
			printf 'extra:  %s\n' "$name"
		else
			printf 'extra:  %s not found in %s/Mods, skipping\n' "$name" "$REAL_DATA"
		fi
	done
}

# Builds Toolsmith from its checkout and installs the zip it produces.
#
# The zip is globbed rather than named: its filename carries Toolsmith's version,
# so pinning one here would leave this copying a file that is no longer produced
# the next time that version moves -- the same trap mod_zip() reads modinfo.json
# to stay out of.
install_toolsmith() {
	target="$1"

	# Unset means testing WITHOUT Toolsmith, so an installed copy has to go: left
	# in place it would still load, and the run would quietly be a Toolsmith run.
	if [ -z "$TOOLSMITH_REPO" ]; then
		for existing in "$target"/Mods/toolsmith_*.zip; do
			[ -e "$existing" ] || continue
			rm -f "$existing" &&
				printf 'extra:  removed %s (Toolsmith disabled)\n' "$(basename "$existing")"
		done
		return 0
	fi

	if [ ! -f "$TOOLSMITH_REPO/build.sh" ]; then
		printf 'extra:  Toolsmith checkout not found at %s, skipping\n' "$TOOLSMITH_REPO"
		return 0
	fi

	mkdir -p "$target/Mods"

	# build.sh is not executable in a fresh checkout, so it is run through sh.
	( cd "$TOOLSMITH_REPO" && sh build.sh >/dev/null 2>&1 ) ||
		die "Toolsmith build failed (run 'sh build.sh' in $TOOLSMITH_REPO to see why)"

	zip=""
	for candidate in "$TOOLSMITH_REPO"/Releases/toolsmith_*.zip; do
		[ -e "$candidate" ] || continue
		# Newest wins, so a stale zip from an earlier version is never the one picked.
		if [ -z "$zip" ] || [ "$candidate" -nt "$zip" ]; then
			zip="$candidate"
		fi
	done
	[ -n "$zip" ] || die "the Toolsmith build did not produce a zip in $TOOLSMITH_REPO/Releases"

	# Any previously installed Toolsmith goes first: after a version bump its zip
	# has a different name and would otherwise load alongside this one.
	for existing in "$target"/Mods/toolsmith_*.zip; do
		[ -e "$existing" ] || continue
		[ "$(basename "$existing")" = "$(basename "$zip")" ] && continue
		rm -f "$existing" &&
			printf 'extra:  removed stale %s\n' "$(basename "$existing")"
	done

	cp "$zip" "$target/Mods/$(basename "$zip")" ||
		die "could not copy Toolsmith into $target/Mods"
	printf 'extra:  %s (built from %s)\n' "$(basename "$zip")" "$TOOLSMITH_REPO"
}

# Rebuilds the mod and drops it into one data dir's Mods folder. Both scripts do
# this on every launch so there is no separate build step to forget.
#
# build.sh runs the Cake build, which validates the JSON assets, publishes, and
# packages Releases/<modid>_<version>.zip -- the same zip a player installs.
# Testing that artifact rather than an unpacked bin/ folder is the point: an
# asset missing from the package is invisible until something loads the zip.
install_mod() {
	target="$1"
	mkdir -p "$target/Mods"

	( cd "$REPO" && ./build.sh >/dev/null ) ||
		die "mod build failed (run ./build.sh to see why)"

	zip=$(mod_zip)
	[ -f "$zip" ] || die "the build did not produce $zip"

	# Installed under the zip's own name rather than a fixed one. A stale copy
	# under the previous name would otherwise sit alongside this build and load
	# beside it, which is exactly what happens after a version bump.
	installed="$target/Mods/$(basename "$zip")"
	remove_stale_mod_copies "$target" "$(basename "$zip")"

	cp "$zip" "$installed" || die "could not copy the mod into $target/Mods"
	printf 'mod:    %s -> %s\n' "$(basename "$zip")" "$installed"
}

# Deletes any previously installed copy of OUR mod from the testbed, matching on
# the modid recorded inside each zip rather than on its filename, so a copy left
# behind by an older version or dropped in by a mod manager is caught too. The zip
# being installed now is skipped, and the other mods listed in TESTBED_EXTRA_MODS
# are never considered.
#
# smithingplusmaterialcache is removed on sight for the reason given above: it
# patches methods this fork has rewritten.
remove_stale_mod_copies() {
	target="$1"
	keep="$2"
	ours=$(mod_id)

	for existing in "$target"/Mods/*.zip; do
		[ -e "$existing" ] || continue
		[ "$(basename "$existing")" = "$keep" ] && continue

		existing_id=$(unzip -p "$existing" modinfo.json 2>/dev/null |
			python3 -c 'import sys,re; m=re.search(r"\"modid\"\s*:\s*\"([^\"]+)\"", sys.stdin.read(), re.I); print(m.group(1) if m else "")' 2>/dev/null)

		case "$existing_id" in
			"$ours"|smithingplusmaterialcache)
				rm -f "$existing" &&
					printf 'mod:    removed stale %s (modid %s)\n' "$(basename "$existing")" "$existing_id"
				;;
		esac
	done
}
