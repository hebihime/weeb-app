#!/usr/bin/env bash
# ios/scripts/bootstrap.sh — SLICE_S7_CONTRACT.md §1a/§1d, HARD CONTRACT item 6.
#
# (a) copies contracts/openapi.v0.json into ApiKit's expected swift-openapi-generator input location
#     (the repo file is canonical per contracts/README; this copy is gitignored, never committed —
#     there is no codegen-drift class because nothing generated is ever checked in).
# (b) runs `xcodegen generate`, producing ios/WeebApp.xcodeproj (also gitignored — ios/.gitignore).
#
# Idempotent: safe to run repeatedly (CI runs it before every build/test/codegen job).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
IOS_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$IOS_DIR/.." && pwd)"

CONTRACT_SRC="$REPO_ROOT/contracts/openapi.v0.json"
CONTRACT_DST="$IOS_DIR/ApiKit/Sources/ApiKit/openapi.json"

if [ ! -f "$CONTRACT_SRC" ]; then
  echo "bootstrap.sh: FATAL — canonical contract not found at $CONTRACT_SRC" >&2
  exit 1
fi

echo "bootstrap.sh: copying canonical contract -> $CONTRACT_DST"
cp "$CONTRACT_SRC" "$CONTRACT_DST"

if ! command -v xcodegen >/dev/null 2>&1; then
  echo "bootstrap.sh: FATAL — xcodegen not found on PATH. Install with: brew install xcodegen" >&2
  exit 1
fi

echo "bootstrap.sh: running xcodegen generate (ios/project.yml -> ios/WeebApp.xcodeproj)"
(cd "$IOS_DIR" && xcodegen generate)

echo "bootstrap.sh: done."
