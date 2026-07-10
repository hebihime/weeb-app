#!/usr/bin/env bash
# build/scripts/emit-purge-registry.sh — SLICE_S1_CONTRACT.md §6 drift gate.
#
# Regenerates backend/domain-core/purge-registry.json via Svac.PublicApi's `--emit-purge-registry` CLI
# mode (pure in-memory data, no DB) and fails if the regenerated file differs from what is committed —
# same "CI regenerates + diff" shape as build/scripts/emit-openapi-contract.sh. The completeness check
# itself (every EF table registered) lives in Svac.Tests.Architecture's PurgeRegistryGateTests; this
# script's job is keeping the committed artifact honest, not re-deriving that proof.
#
# Usage: build/scripts/emit-purge-registry.sh

set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

fail() { echo "emit-purge-registry BLOCKED: $1" >&2; exit 1; }

OUT="backend/domain-core/purge-registry.json"
BEFORE_HASH="$( [ -f "$OUT" ] && shasum -a 256 "$OUT" | awk '{print $1}' || echo "MISSING" )"

echo "emit-purge-registry: regenerating $OUT"
dotnet run --project backend/public-host/Svac.PublicApi --configuration Release -- --emit-purge-registry "$(pwd)/$OUT"

AFTER_HASH="$(shasum -a 256 "$OUT" | awk '{print $1}')"

if [ "$BEFORE_HASH" != "$AFTER_HASH" ]; then
  fail "$OUT drifted from its regenerated form — commit the regenerated file (git diff \"$OUT\" for details)"
fi

echo "emit-purge-registry OK: $OUT matches its regenerated form"
