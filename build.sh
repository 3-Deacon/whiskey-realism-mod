#!/bin/bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
PROJ="$ROOT/src/WhiskeyRealism/WhiskeyRealism.csproj"
OUT="$ROOT/dist"

dotnet restore "$PROJ"
dotnet build "$PROJ" -c Release -o "$OUT"

echo
echo "Built plugin: $OUT/WhiskeyRealism.dll"
echo
echo "To install: copy that DLL to <GTCW install>/BepInEx/plugins/"
echo "  Linux/WSL path: /mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
