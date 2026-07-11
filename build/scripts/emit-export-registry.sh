#!/usr/bin/env bash
# build/scripts/emit-export-registry.sh — SLICE_S3_CONTRACT.md §6b drift gate.
#
# Regenerates backend/domain-core/export-registry.json via Svac.PublicApi's `--emit-export-registry` CLI
# mode (pure in-memory data, no DB) and fails if the regenerated file differs from what is committed —
# same "CI regenerates + diff" shape as build/scripts/emit-purge-registry.sh. The completeness check
# itself (the export⋈purge cross-gate) lives in Svac.Tests.Architecture; this script's job is keeping the
# committed artifact honest, not re-deriving that proof.
#
# Usage: build/scripts/emit-export-registry.sh

set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

fail() { echo "emit-export-registry BLOCKED: $1" >&2; exit 1; }

OUT="backend/domain-core/export-registry.json"
BEFORE_HASH="$( [ -f "$OUT" ] && shasum -a 256 "$OUT" | awk '{print $1}' || echo "MISSING" )"

echo "emit-export-registry: regenerating $OUT"
dotnet run --project backend/public-host/Svac.PublicApi --configuration Release -- --emit-export-registry "$(pwd)/$OUT"

AFTER_HASH="$(shasum -a 256 "$OUT" | awk '{print $1}')"

if [ "$BEFORE_HASH" != "$AFTER_HASH" ]; then
  fail "$OUT drifted from its regenerated form — commit the regenerated file (git diff \"$OUT\" for details)"
fi

echo "emit-export-registry OK: $OUT matches its regenerated form"
