#!/usr/bin/env bash
# Packs every SDK to ./artifacts. Usage: ./build.sh [version]
set -euo pipefail
cd "$(dirname "$0")"

VERSION="${1:-0.0.1-local}"

rm -rf artifacts
# Purge cached copies so local iteration always picks up the fresh pack.
for cache in "${NUGET_PACKAGES:-$HOME/.nuget/packages}"/rocket.surgery.sdk*; do
    [ -d "$cache" ] && rm -rf "$cache"
done

for project in src/*.csproj; do
    dotnet pack "$project" -o artifacts --nologo -v quiet -p:Version="$VERSION"
done

ls artifacts/*.nupkg
