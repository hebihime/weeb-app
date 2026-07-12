#!/usr/bin/env bash
# build/scripts/emit-ddl-script.sh — SLICE_S1_CONTRACT.md §2 drift gate, extended by SLICE_S3_CONTRACT.md
# item 1 for the SECOND module-owned DbContext (identity, schema `identity`) — same drift-gate shape, not
# a second script: the S1 drift-gate lesson explicitly named here ("the linted SQL must equal what EF
# applies") applies identically to every DbContext this repo ever grows, so this file loops over a table
# of (project, output) pairs rather than special-casing a second hard-coded path.
#
# Regenerates the idempotent SQL script EF actually applies (via `dotnet ef migrations script
# --idempotent`) per DbContext and fails if either differs from what is committed. These are the files
# tools/ddl-lint/ddl-lint.mjs actually lints (lints.yml's ddl-lint job globs backend/**/Migrations/*.sql)
# — without this drift gate, the linted SQL and the SQL EF really applies at startup could silently
# diverge the next time someone adds a migration and forgets to regenerate the script.
#
# Usage: build/scripts/emit-ddl-script.sh

set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

fail() { echo "emit-ddl-script BLOCKED: $1" >&2; exit 1; }

# dotnet-ef MUST match the EF Core package pin (Directory.Packages.props: 10.0.9). A v9 tool on a v10
# project regenerates the script differently than v10 tooling, so a byte-diff drift gate is only
# deterministic when local and CI use the SAME tool version — pin it exactly, never a loose major.
command -v dotnet-ef >/dev/null 2>&1 || dotnet tool install --global dotnet-ef --version 10.0.9 >/dev/null 2>&1 || true
export PATH="$PATH:$HOME/.dotnet/tools"

# (project dir, output .sql path) pairs — one per module-owned DbContext.
PAIRS=(
  "backend/domain-core/Svac.DomainCore|backend/domain-core/Svac.DomainCore/Persistence/Migrations/InitialCore.sql"
  "backend/modules/identity/Svac.Identity|backend/modules/identity/Svac.Identity/Persistence/Migrations/InitialIdentity.sql"
  "backend/admin-host/Svac.AdminHost.Domain|backend/admin-host/Svac.AdminHost.Domain/Persistence/Migrations/InitialAdmin.sql"
)

DRIFTED=()
for pair in "${PAIRS[@]}"; do
  PROJECT="${pair%%|*}"
  OUT="${pair##*|}"
  BEFORE_HASH="$( [ -f "$OUT" ] && shasum -a 256 "$OUT" | awk '{print $1}' || echo "MISSING" )"

  echo "emit-ddl-script: regenerating $OUT"
  dotnet ef migrations script --idempotent --project "$PROJECT" --output "$OUT"

  AFTER_HASH="$(shasum -a 256 "$OUT" | awk '{print $1}')"
  if [ "$BEFORE_HASH" != "$AFTER_HASH" ]; then
    DRIFTED+=("$OUT")
  fi
done

if [ "${#DRIFTED[@]}" -gt 0 ]; then
  fail "the following file(s) drifted from their regenerated form — commit the regenerated file(s) (git diff for details): ${DRIFTED[*]}"
fi

echo "emit-ddl-script OK: ${#PAIRS[@]} DbContext(s) checked, all match their regenerated form"
