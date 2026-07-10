#!/usr/bin/env bash
# build/scripts/compose-smoke.sh — BUILD.md §8 clause 2 fresh-boot health discipline (L13), the
# "trivial container test" clause of the S0 gate.
#
# up -d --wait -> health asserts -> down -v -> fresh up -> zero-error/zero-restart log sweep.
# Two oracles: assertions (docker compose ps / inspect) AND logs (docker compose logs grep).
#
# Usage: build/scripts/compose-smoke.sh

set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

fail() { echo "compose-smoke BLOCKED: $1" >&2; exit 1; }

echo "compose-smoke: first up -d --wait"
docker compose up -d --wait

echo "compose-smoke: asserting every service is healthy"
UNHEALTHY="$(docker compose ps --format '{{.Name}} {{.Health}}' | grep -v 'healthy$' || true)"
if [ -n "$UNHEALTHY" ]; then
  fail "service(s) not healthy after first up: $UNHEALTHY"
fi

echo "compose-smoke: down -v (fresh-boot discipline)"
docker compose down -v

echo "compose-smoke: fresh up -d --wait"
docker compose up -d --wait

echo "compose-smoke: asserting every service is healthy (fresh boot)"
UNHEALTHY="$(docker compose ps --format '{{.Name}} {{.Health}}' | grep -v 'healthy$' || true)"
if [ -n "$UNHEALTHY" ]; then
  fail "service(s) not healthy after fresh boot: $UNHEALTHY"
fi

echo "compose-smoke: asserting zero restarts on every container"
for c in $(docker compose ps -q); do
  COUNT="$(docker inspect --format '{{.RestartCount}}' "$c")"
  NAME="$(docker inspect --format '{{.Name}}' "$c")"
  if [ "$COUNT" != "0" ]; then
    fail "$NAME restarted $COUNT time(s) on fresh boot — a self-healing race passes health while erroring; read the logs"
  fi
done

echo "compose-smoke: log sweep for zero unhandled errors across all services"
# EF Core's Npgsql provider probes "does __EFMigrationsHistory exist" via a SELECT it expects might
# fail, and handles that failure client-side to mean "no, create it" — but Postgres still logs the
# failed SELECT server-side as an ERROR line on every truly-fresh database, by design, every time
# (tracked upstream: npgsql/efcore.pg — this is not this app's bug, it fires for ANY EF Core app on
# its very first migration against Postgres, S1's first real reproduction of it). Excluded by exact
# message text so a real error containing "error" is never masked by a loose pattern.
LOG_HITS="$(docker compose logs --no-color 2>&1 \
  | grep -iE 'error|fatal|exception|panic' \
  | grep -v 'relation "__EFMigrationsHistory" does not exist' \
  || true)"
if [ -n "$LOG_HITS" ]; then
  fail "log sweep found error-shaped lines:
$LOG_HITS"
fi

echo "compose-smoke OK: fresh-boot health clean, zero restarts, zero error-shaped log lines"
