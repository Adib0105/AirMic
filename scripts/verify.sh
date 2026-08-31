#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
python3 "$project_dir/scripts/protocol_smoke_test.py"
python3 - <<'PY' "$project_dir"
import json, pathlib, plistlib, sys, xml.etree.ElementTree as ET
root = pathlib.Path(sys.argv[1])
json.loads((root / "shared/test-vectors/audio-packet-v1.json").read_text())
with (root / "ios/AirMic/Resources/Info.plist").open("rb") as stream:
    plistlib.load(stream)
for path in root.rglob("*.csproj"):
    ET.parse(path)
for path in root.rglob("*.xaml"):
    ET.parse(path)
print("JSON, plist, csproj, and XAML syntax: OK")
PY

if command -v dotnet >/dev/null 2>&1; then
  dotnet test "$project_dir/windows/AirMic.sln" --configuration Release
else
  echo "dotnet unavailable: native Windows build gate not run"
fi

if command -v xcodegen >/dev/null 2>&1 && command -v xcodebuild >/dev/null 2>&1; then
  (cd "$project_dir/ios" && xcodegen generate && xcodebuild -project AirMic.xcodeproj -scheme AirMic -sdk iphonesimulator -destination 'platform=iOS Simulator,name=iPhone 16' test)
else
  echo "Xcode unavailable: native iOS build gate not run"
fi
