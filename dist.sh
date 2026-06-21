#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$repo_root"

project="league_tablecloth.csproj"
assembly="league_tablecloth"

echo "Building $project (Release)"
dotnet build "$project" -c Release "$@"

version="$(dotnet msbuild "$project" -getProperty:Version)"
echo "Version: $version"

stage="dist/$assembly"
rm -rf "$stage"
mkdir -p "$stage"

cp "bin/Release/$assembly.dll" "$stage/"
cp -r assets "$stage/assets"

zip_name="$assembly-$version.zip"
echo "Packaging dist/$zip_name"
( cd dist && rm -f "$zip_name" && zip -r "$zip_name" "$assembly" >/dev/null )

echo "Done: dist/$zip_name"
