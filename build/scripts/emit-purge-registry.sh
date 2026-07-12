#!/usr/bin/env bash
# build/scripts/emit-purge-registry.sh — SLICE_S1_CONTRACT.md §6 drift gate, extended by
# SLICE_S5_CONTRACT.md §6 for the SECOND host emitting into this ONE committed, repo-wide file.
#
# backend/domain-core/purge-registry.json is the union of every registered IPurgeRegistrySource across
# EVERY host — but no single .NET binary may assemble that union directly: Svac.PublicApi (core+
# identity, via its own `--emit-purge-registry` CLI mode) and Svac.AdminHost (admin, via its OWN
# `--emit-purge-registry` CLI mode) are two different deploy units, and the admin trust-boundary rule
# (SLICE_S5_CONTRACT.md §0 law c) forbids Svac.PublicApi from ever referencing Svac.AdminHost* — so this
# script runs BOTH emitters into scratch fragment files and merges them with
# build/scripts/merge-purge-registry.mjs (deterministic, boot-refuses on a duplicate (storeKey,
# purgeClass) cell exactly like PurgeRegistry's own in-process constructor does) before diffing against
# the committed file. Same "CI regenerates + diff" shape as build/scripts/emit-openapi-contract.sh. The
# completeness check itself (every EF table registered) lives in Svac.Tests.Architecture's
# PurgeRegistryGateTests; this script's job is keeping the committed artifact honest, not re-deriving
# that proof.
#
# Usage: build/scripts/emit-purge-registry.sh

set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

fail() { echo "emit-purge-registry BLOCKED: $1" >&2; exit 1; }

OUT="backend/domain-core/purge-registry.json"
BEFORE_HASH="$( [ -f "$OUT" ] && shasum -a 256 "$OUT" | awk '{print $1}' || echo "MISSING" )"

WORKDIR="$(mktemp -d)"
trap 'rm -rf "$WORKDIR"' EXIT

echo "emit-purge-registry: regenerating the public-host fragment (core + identity)"
dotnet run --project backend/public-host/Svac.PublicApi --configuration Release -- --emit-purge-registry "$WORKDIR/public-host.json"

echo "emit-purge-registry: regenerating the admin-host fragment (admin)"
dotnet run --project backend/admin-host/Svac.AdminHost --configuration Release -- --emit-purge-registry "$WORKDIR/admin-host.json"

echo "emit-purge-registry: merging fragments into $OUT"
node build/scripts/merge-purge-registry.mjs "$WORKDIR/public-host.json" "$WORKDIR/admin-host.json" "$(pwd)/$OUT"

AFTER_HASH="$(shasum -a 256 "$OUT" | awk '{print $1}')"

if [ "$BEFORE_HASH" != "$AFTER_HASH" ]; then
  fail "$OUT drifted from its regenerated form — commit the regenerated file (git diff \"$OUT\" for details)"
fi

echo "emit-purge-registry OK: $OUT matches its regenerated (merged) form"
