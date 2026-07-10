# Weeb App / Friki App

Dual-brand social product for anime/manga fandom communities. Native iOS (Swift) + native Android
(Kotlin) + a web funnel, backed by one C#/ASP.NET Core modular monolith. See `BUILD.md` for the full
build guide, `DESIGN.md` for the design system, and `SLICE_PLAYBOOK.md` for how every vertical slice
ships.

## First-time setup

```bash
git config core.hooksPath .githooks
```

This wires `.githooks/pre-commit` as your local pre-commit hook — the gate-test lane (deterministic,
free, <2s). It runs on every commit; never bypass it with `--no-verify` (CLAUDE.md Safety). Each of its
blocks activates itself once the directory it checks exists, so it is a no-op today and a real gate as
soon as `backend/tests/Svac.Tests.Architecture` (S1) or `web/` (S9) land.

## Repo layout (S0 baseline — see SLICE_S0_CONTRACT.md for what's real vs guarded)

| Path | What |
|---|---|
| `backend/` | `Svac.sln` (empty solution) + `Directory.Build.props`. The C#/ASP.NET Core modular monolith lands starting S1. |
| `backend/e2e/` | Versioned live-E2E scripts (`edge-guard.mjs` today; API journeys land per-slice). |
| `brands/` | Canonical build-time brand facts (`weeb.json`, `friki.json`) — the single source every platform flavor file must match. Never a runtime registry entry (7A). |
| `i18n/` | `locales.json` — the canonical ×4 locale set (`en`, `es`, `pt`, `zh-Hans`). |
| `contracts/` | `README.md` documents the OpenAPI v0 invariants now; `openapi.v0.json` itself arrives at S1. |
| `infra/` | Bicep IaC: one module per Azure service under `modules/`, `main.bicep` composition root, per-environment `.bicepparam` files, `edge-guard.bicep` (L17). |
| `tools/` | Deterministic lint tools with golden-vector tests: `contract-lint/`, `ddl-lint/`, `i18n-lint/`. |
| `build/scripts/` | `brand-gate.mjs`, `ef-gate.sh`, `destructive-verb-check.mjs`, `compose-smoke.sh` — all golden-vector or fixture tested. |
| `.github/workflows/` | Per-unit CI: `backend.yml`, `ios.yml`, `android.yml`, `web.yml`, `infra.yml`, `lints.yml`, `release.yml`. Every leg is directory/file-guarded so the pipeline is green on this empty-unit repo today. |
| `.githooks/pre-commit` | The local gate-test lane. Enable with the command above. |
| `docker-compose.yml` | Dev stack: Postgres+PostGIS, Redis, Azurite, Mailpit, all with healthchecks. Backend hosts (with in-process SignalR) join at S1. |
| `ios/`, `android/`, `web/` | Do not exist yet by design (S7, S7, S9 respectively) — see SLICE_S0_CONTRACT.md §0 for why creating them early is the wrong move, not an oversight. |

## Local dev stack

```bash
docker compose up -d --wait   # Postgres (localhost:5433), Redis (6379), Azurite (10000-10002), Mailpit (1025 SMTP / 8025 UI)
docker compose down -v        # tear down, including volumes
```

Postgres is remapped to host port **5433** (not the default 5432) because this dev machine runs other
projects' compose stacks concurrently that already claim 5432 — see `docker-compose.yml`'s inline note.
Record any further local port/path facts in `BUILD.md` §1 ("Local facts") as they're settled.

## Running the deterministic tool test suites

Every tool under `tools/` and `build/scripts/` ships Node's built-in test runner (`node:test`), zero
external dependencies:

```bash
node --test tools/contract-lint/contract-lint.test.mjs
node --test tools/ddl-lint/ddl-lint.test.mjs
node --test tools/i18n-lint/i18n-lint.test.mjs
node --test build/scripts/brand-gate.test.mjs
node --test build/scripts/destructive-verb-check.test.mjs
node --test infra/edge-guard.test.mjs infra/residency.test.mjs
```

## Backend

```bash
dotnet build backend/Svac.sln     # empty solution today; the first real project lands at S1
```
