#!/usr/bin/env bash
# build/scripts/ef-gate.sh — SLICE_S0_CONTRACT.md §2, EF-migration CI gate (7A, ratified).
# Union of all three checks (§12.10 — no conflict, pure graft):
#   (a) `dotnet ef migrations has-pending-model-changes` per DbContext — model drift without a
#       migration fails the PR.
#   (b) Idempotency: apply the full chain to a fresh throwaway Postgres+PostGIS container, apply
#       again, assert no-op.
#   (c) Destructive-verb grep (DROP TABLE / DROP COLUMN / TRUNCATE) fails unless the migration
#       carries an explicit "-- destructive: <reason>" marker.
#
# Guarded activation: this whole gate is a no-op (exit 0) until a DbContext exists (S1). That guard is
# the point — S1 cannot land its first store ungated, but S0 stays green on a repo with zero DbContexts.
#
# Usage: build/scripts/ef-gate.sh [path-to-backend-dir]  (defaults to ./backend)

set -euo pipefail

BACKEND_DIR="${1:-backend}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

fail() { echo "ef-gate BLOCKED: $1" >&2; exit 1; }

if [ ! -d "$BACKEND_DIR" ]; then
  echo "ef-gate SKIP: $BACKEND_DIR does not exist"
  exit 0
fi

# Guarded activation: find any C# file that declares a DbContext subclass.
CONTEXT_FILES="$(grep -rl --include='*.cs' -E 'class\s+\w+\s*:\s*.*DbContext' "$BACKEND_DIR" 2>/dev/null || true)"

if [ -z "$CONTEXT_FILES" ]; then
  echo "ef-gate SKIP: no DbContext found under $BACKEND_DIR yet (guarded — S1 is the first consumer)"
  exit 0
fi

echo "ef-gate: found DbContext(s):"
echo "$CONTEXT_FILES" | sed 's/^/  - /'

# --- (c) destructive-verb grep — deterministic, runs first because it is cheapest and fails fast ---
MIGRATIONS_DIR_LIST="$(find "$BACKEND_DIR" -type d -iname 'Migrations' 2>/dev/null || true)"
if [ -n "$MIGRATIONS_DIR_LIST" ]; then
  echo "ef-gate: running destructive-verb check over migration directories"
  # shellcheck disable=SC2086
  node "$SCRIPT_DIR/destructive-verb-check.mjs" $MIGRATIONS_DIR_LIST \
    || fail "destructive-verb check failed — mark the migration or split the drop into its own reviewed change"
else
  echo "ef-gate: no Migrations/ directories yet, skipping destructive-verb check"
fi

# --- (a) pending-model-changes per context ---
FOUND_PROJECTS="$(find "$BACKEND_DIR" -name '*.csproj' -exec grep -l 'Microsoft.EntityFrameworkCore' {} \; 2>/dev/null || true)"
if [ -z "$FOUND_PROJECTS" ]; then
  fail "DbContext found but no .csproj references Microsoft.EntityFrameworkCore — cannot run pending-model-changes"
fi
for proj in $FOUND_PROJECTS; do
  echo "ef-gate: dotnet ef migrations has-pending-model-changes for $proj"
  ( cd "$(dirname "$proj")" && dotnet ef migrations has-pending-model-changes ) \
    || fail "model drift without a migration in $proj — run 'dotnet ef migrations add' before merging"
done

# --- (b) idempotent reapply against a fresh throwaway Postgres+PostGIS container ---
CONTAINER_NAME="ef-gate-throwaway-$$"
echo "ef-gate: starting throwaway Postgres+PostGIS container ($CONTAINER_NAME)"
docker run -d --rm --name "$CONTAINER_NAME" \
  -e POSTGRES_PASSWORD=ef-gate-throwaway \
  -e POSTGRES_DB=ef_gate_throwaway \
  -p 0:5432 \
  postgis/postgis:16-3.4 >/dev/null

cleanup() { docker stop "$CONTAINER_NAME" >/dev/null 2>&1 || true; }
trap cleanup EXIT

HOST_PORT="$(docker inspect -f '{{ (index (index .NetworkSettings.Ports "5432/tcp") 0).HostPort }}' "$CONTAINER_NAME")"
CONN_STRING="Host=localhost;Port=$HOST_PORT;Database=ef_gate_throwaway;Username=postgres;Password=ef-gate-throwaway"

echo "ef-gate: waiting for Postgres to accept connections"
for i in $(seq 1 30); do
  if docker exec "$CONTAINER_NAME" pg_isready -U postgres >/dev/null 2>&1; then break; fi
  sleep 1
  [ "$i" -eq 30 ] && fail "throwaway Postgres never became ready"
done

# The official postgres/postgis entrypoint restarts the server once after initdb completes
# (init-then-restart). `pg_isready` can observe the FIRST (pre-restart) instance as ready and a
# connection landing in that narrow window gets reset when the restart happens underneath it
# ("Connection reset by peer" mid-SSL-negotiation, reproduced on this gate's first real DbContext).
# A short settle + a bounded retry on the first real connection attempt (not on pg_isready, which
# already passed) absorbs that restart without masking a genuinely broken migration.
sleep 2

for proj in $FOUND_PROJECTS; do
  PROJ_DIR="$(dirname "$proj")"
  echo "ef-gate: applying full migration chain (pass 1) for $proj"
  APPLY_OK=0
  for attempt in 1 2 3; do
    if ( cd "$PROJ_DIR" && dotnet ef database update --connection "$CONN_STRING" ); then
      APPLY_OK=1
      break
    fi
    echo "ef-gate: pass-1 apply attempt $attempt failed (possible entrypoint-restart race), retrying in 2s"
    sleep 2
  done
  [ "$APPLY_OK" -eq 1 ] || fail "first migration apply failed for $proj after 3 attempts"
  echo "ef-gate: re-applying full migration chain (pass 2, must be a no-op) for $proj"
  APPLY_OUTPUT="$(cd "$PROJ_DIR" && dotnet ef database update --connection "$CONN_STRING" 2>&1)"
  if echo "$APPLY_OUTPUT" | grep -qiE 'applying migration'; then
    fail "second apply for $proj was NOT a no-op — migration chain is not idempotent: $APPLY_OUTPUT"
  fi
done

echo "ef-gate OK: pending-model-changes clean, destructive-verb check clean, migration chain idempotent"
