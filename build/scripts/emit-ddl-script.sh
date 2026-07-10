#!/usr/bin/env bash
# build/scripts/emit-ddl-script.sh — SLICE_S1_CONTRACT.md §2 drift gate.
#
# Regenerates the idempotent SQL script EF actually applies (via `dotnet ef migrations script
# --idempotent`) and fails if it differs from what is committed. This is the file tools/ddl-lint/
# ddl-lint.mjs actually lints (lints.yml's ddl-lint job globs backend/**/Migrations/*.sql) — without
# this drift gate, the linted SQL and the SQL EF really applies at startup could silently diverge the
# next time someone adds a migration and forgets to regenerate the script.
#
# Usage: build/scripts/emit-ddl-script.sh

set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

fail() { echo "emit-ddl-script BLOCKED: $1" >&2; exit 1; }

OUT="backend/domain-core/Svac.DomainCore/Persistence/Migrations/InitialCore.sql"
BEFORE_HASH="$( [ -f "$OUT" ] && shasum -a 256 "$OUT" | awk '{print $1}' || echo "MISSING" )"

# dotnet-ef MUST match the EF Core package pin (Directory.Packages.props: 10.0.9). A v9 tool on a v10
# project regenerates the script differently than v10 tooling, so a byte-diff drift gate is only
# deterministic when local and CI use the SAME tool version — pin it exactly, never a loose major.
command -v dotnet-ef >/dev/null 2>&1 || dotnet tool install --global dotnet-ef --version 10.0.9 >/dev/null 2>&1 || true
export PATH="$PATH:$HOME/.dotnet/tools"

echo "emit-ddl-script: regenerating $OUT"
dotnet ef migrations script --idempotent --project backend/domain-core/Svac.DomainCore --output "$OUT"

AFTER_HASH="$(shasum -a 256 "$OUT" | awk '{print $1}')"

if [ "$BEFORE_HASH" != "$AFTER_HASH" ]; then
  fail "$OUT drifted from its regenerated form — commit the regenerated file (git diff \"$OUT\" for details)"
fi

echo "emit-ddl-script OK: $OUT matches its regenerated form"
