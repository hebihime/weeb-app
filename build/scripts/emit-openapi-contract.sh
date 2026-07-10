#!/usr/bin/env bash
# build/scripts/emit-openapi-contract.sh — SLICE_S1_CONTRACT.md §1c drift gate.
#
# Regenerates contracts/openapi.v0.json via Svac.PublicApi's `--emit-openapi` CLI mode (a minimal,
# DB-free instance of the real host on an ephemeral loopback port — see Program.cs /
# OpenApiContractEmitter.cs) and fails if the regenerated file differs from what is committed. This is
# the "CI regenerates + git diff --exit-code" half of the guarded-activation contract §1c promises.
#
# Usage: build/scripts/emit-openapi-contract.sh

set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

fail() { echo "emit-openapi-contract BLOCKED: $1" >&2; exit 1; }

OUT="contracts/openapi.v0.json"
BEFORE_HASH="$( [ -f "$OUT" ] && shasum -a 256 "$OUT" | awk '{print $1}' || echo "MISSING" )"

echo "emit-openapi-contract: regenerating $OUT"
dotnet run --project backend/public-host/Svac.PublicApi --configuration Release -- --emit-openapi "$(pwd)/$OUT"

AFTER_HASH="$(shasum -a 256 "$OUT" | awk '{print $1}')"

if [ "$BEFORE_HASH" != "$AFTER_HASH" ]; then
  fail "$OUT drifted from its regenerated form — commit the regenerated file (git diff \"$OUT\" for details)"
fi

echo "emit-openapi-contract OK: $OUT matches its regenerated form"
