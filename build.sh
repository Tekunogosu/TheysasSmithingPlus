#!/usr/bin/env bash
# Runs the Cake build: validates the JSON assets, publishes, and packages
# Releases/<modid>_<version>.zip -- the same zip a player installs.
set -euo pipefail
cd "$(dirname "$(readlink -f "$0")")"
exec dotnet run --project ./CakeBuild/CakeBuild.csproj -- "$@"
