#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

dotnet build -c Release

echo
echo "Build complete:"
echo "  $(pwd)/bin/Release/HideDebugLine.dll"
echo
echo "Deploy: copy this DLL into the game's BepInEx/plugins/ folder."