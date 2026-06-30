#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TARKOV_DIR="${1:-$(cd "$SCRIPT_DIR/../.." && pwd)}"

# Verify Tarkov directory
if [ ! -d "$TARKOV_DIR/EscapeFromTarkov_Data/Managed" ]; then
    echo "ERROR: Tarkov managed directory not found at: $TARKOV_DIR/EscapeFromTarkov_Data/Managed"
    echo "Usage: $0 [TARKOV_DIR]"
    exit 1
fi

# Clean previous tools
rm -rf "$SCRIPT_DIR/tools"

# Create target directories
mkdir -p "$SCRIPT_DIR/tools/managed"
mkdir -p "$SCRIPT_DIR/tools/bepinex-core"
mkdir -p "$SCRIPT_DIR/tools/spt-plugins"
mkdir -p "$SCRIPT_DIR/tools/bepinex-patchers"

# Copy DLLs
echo "Copying managed DLLs..."
cp "$TARKOV_DIR/EscapeFromTarkov_Data/Managed/"*.dll "$SCRIPT_DIR/tools/managed/"

echo "Copying BepInEx core DLLs..."
cp "$TARKOV_DIR/BepInEx/core/"*.dll "$SCRIPT_DIR/tools/bepinex-core/"

echo "Copying SPT plugin DLLs..."
cp "$TARKOV_DIR/BepInEx/plugins/spt/"*.dll "$SCRIPT_DIR/tools/spt-plugins/"

echo "Copying BepInEx patcher DLLs..."
if [ -d "$TARKOV_DIR/BepInEx/patchers" ] && compgen -G "$TARKOV_DIR/BepInEx/patchers/*.dll" > /dev/null; then
    cp "$TARKOV_DIR/BepInEx/patchers/"*.dll "$SCRIPT_DIR/tools/bepinex-patchers/"
else
    echo "  (no patcher DLLs found, skipping)"
fi

# Pack
echo "Packing NuGet package..."
dotnet pack "$SCRIPT_DIR/NarcoNet.GameReferences.csproj" -o "$SCRIPT_DIR/bin/"

echo "Done! Package created in $SCRIPT_DIR/bin/"
