# SLICE S6 CONTRACT — anime-engine (LOCKED)

**Gate:** G0 · **Actor journey FINISHED:** "user: takes short form, resumes the full 200 in chapters" · **Kills:** B11 (backend half; S9 kills the web half) · **Ledger outcome:** short form scores; full instrument resumable (BUILD.md §7 S6 row) · **Authoritative rulings:** founder ruling 2026-07-10 (200 items = 140 OCEAN + 60 domain-P preference items; P = ZERO OCEAN weight, pure matching signal, never dropped, never surfaced at S6, ALWAYS in the statutory export), profilemodel §5/§5b/§12, questsystem earn table (deferred, §12.7), engreview item 25 (D17 + N7), superprompt §5.15/§5.26/§9.11, design/05 ("Chapter 7 of 12").

Synthesized from three proposals by the design judge. Per-conflict adoptions and the one ruling contradiction are recorded in §12. Two open questions remain (§11); everything else is resolved with stated reasoning. **Every source fact below was re-verified by the judge by script against `/Users/jp/Repos/weebtest` — nothing is inherited latently from the proposals.**

**Governing theses (grafted):**

- **From P1 (simplicity):** the instrument is ONE versioned data artifact plus ONE pure scoring library, and the user has ONE progressive answer canvas per version — the short form is a subset of the same 200; the full run literally resumes it. Six self-scoped routes; no client-supplied resource id exists anywhere in the module.
- **From P2 (extraction-grade seams):** the crown jewel is the versioned, content-hashed scoring definition under `contracts/anime/`, and the golden vectors extracted from the ORIGINAL TypeScript are the cross-implementation contract: an implementation (C# now, S9's web scorer later) is correct iff it reproduces them byte-for-byte. Instrument data never lives in a desk-editable place.
- **From P3 (determinism / port fidelity / privacy):** the server always rescores from raw answers (the source API's trust-the-client posture is a flagged defect, not a behavior to port); the rounding trap is real and reachable only in partial scoring; P-invariance is proven in both directions, not asserted; zero LLM is structural — `Svac.Anime.*` cannot reference `Svac.AimlRouter*`, arch-asserted with a red fixture.

**Verified source facts (judge re-ran the scripts, 2026-07-11):**

1. `src/app/data/questions.ts`: **200 items**, ids `000`–`199`, unique, contiguous. Domains **N=28, E=28, O=29, A=26, C=29 (=140 OCEAN) + P=60**. Keying: 100 plus / 40 minus (all OCEAN) / 60 neutral (all P, all `facet:0`). Per-domain facet counts: N 4/4/5/4/6/5 · E 5/4/5/4/5/5 · O 6/4/5/4/4/6 · A 5/4/5/5/3/4 · C 6/4/5/5/5/4.
2. `validateQuestions()` (questions.ts:361) is stale twice over: asserts `length === 140` (line 384) AND expects per-domain N29/E28/O28/A28/C27 — wrong on N, O, A and C even for a 140-item view; P is never counted. The correct 200-item array fails its own validator. **The port fixes the validator to the true structure; truncation to 140 is banned.**
3. `getOptimalQuestionOrder()` (questions.ts:282) is stale dead code: max id in the array is **159** — P items 160–199 are missing entirely. The live flow never calls it (`test-state.ts` shuffles all 200 at init), so no score was ever affected. Not ported (§12.9).
4. Scoring (`test-state.ts` `calculateScores`/`calculateFacets`): value ∈ 1..5; minus-keyed → `6 − value`; neutral (all P) → raw value, accumulated ONLY into `internalScores.P`, structurally never into an OCEAN domain; **domain score = Math.round(sum / (count_answered × 5) × 100)** (integer percent, floor 20 on all-1s, correct on partial answer sets by construction); facet score = same formula per (domain, facet); an unanswered facet renders **50**; the facet loop skips P via its empty facet-name lookup. JS `Math.round` is half-up.
5. **Rounding trap (reachable, load-bearing):** full-set denominators (26/28/29 per domain, 3–6 per facet) cannot produce exact `.5`, but PARTIAL sets can (e.g. 8 answered in a domain, odd adjusted sum: 21/40 → 52.5 → JS 53, .NET banker's 52). The port pins **`MidpointRounding.AwayFromZero`** and a golden vector pins exactly this case.
6. Archetypes (`api.ts:24–111`): ordered table of **8** range-rule archetypes (`anxious_archivist{N:[70,100],C:[70,100]}` → `nakama_evangelist` → `elitist_hermit` → `festival_wanderer` → `methodical_purist` → `gentle_guide` → `shrine_keeper` → `chaos_catalyst`), inclusive bounds, **first match in table order wins** (the order IS the tie-break), fallback **`seasonal_drifter`** = 9 total. The copy in `weebtest-api/src/results/result.service.ts` is the same table; no drift.
7. `weebtest-api` posture: `create()` accepts CLIENT-computed `scores`/`facets` verbatim, validates only `answers.length >= 200` (which itself confirms the 200-item truth), and recomputes only the archetype. Nothing else to port; the trust-the-client posture is rejected (§12, parity finding a). No answer-range validation exists anywhere in the source (finding b).
8. i18n: `public/assets/i18n/{en,es,pt,zh-CN}.json` each carry all **200** `question.NNN` texts + all 9 archetype blocks (verified by count). Our ×4 set is a data port (zh-CN → zh-Hans), not a translation task.

---

## 0. Scope ruling

**S6 owns** `backend/modules/Anime/**` (module `Svac.Anime`): assemblies `Svac.Anime.Contracts`, `Svac.Anime.Scoring` (pure), `Svac.Anime`; schema `anime` via `AnimeDbContext`; the canonical instrument artifacts `contracts/anime/instrument.v1.json` + `contracts/anime/golden-vectors.v1.json`; the golden-vector extraction tool `tools/anime-goldens/` (Node, zero deps, executes the ORIGINAL TS scoring, provenance header pins the weebtest commit); the FIXED validator; the deterministic scoring core + archetype mapper; short-form selection (20 items, pinned in the version); the 12-chapter presentation order; the facet-grain quest-unlock map (the seam, not the economy); `test_version` pinning + immutability; the one-canvas resumable run flow (start / save partial / resume / per-scope submit, idempotent under race); result storage + caller-owned reads; the 60 preference answers gathered/stored/export-registered with zero OCEAN weight; the D17 validation hook; the OpenAPI delta; the message-key delta (~268 keys ×4); module 4A/9A/10A/13A registrations; its own gate-test project.

S6 does NOT:

- build any web or native UI: the web funnel's client-side minor scoring, share cards and waitlist are S9 (S6 DEFINES the scoring S9 replicates, via the published artifact — §8); reveal/animation surfaces are client slices;
- accrue `anime_internal`/`anime_delta` or any behavioral score (S16 — it initializes later from `anime.result_created` events already flowing; those fields exist nowhere in this module);
- consume results for matching (S14 — S6 publishes the read contracts S14 will call; preference-signal weighting is S14 design work);
- build the quest/points economy or append Fan Point earns (S33 — S6 ships the facet-unlock seam dark and guarantees earns are deterministically backfillable; §12.7);
- draft character vectors (S25, the only latent ANIME surface anywhere — not here);
- run the D17 analysis (S6 makes its dataset structurally inevitable: per-kind version-pinned result pairs, retained; the task is a later query, not new instrumentation);
- add any latent surface.

**The one structural law S6 makes true:** *for a given `test_version`, the same answer set produces the same scores and archetype in every conformant implementation, forever — proven by golden vectors extracted from the original TS, enforced by a build-failing parity gate on both sides of the extraction seam, and no code path from answers to scores can reach IO, another module, or a model* (`Svac.Anime.Scoring` has zero IO/EF/HTTP/wall-clock/`Svac.AimlRouter` references, all arch-asserted with red fixtures). Corollary, founder-locked and made structural: the 60 P items are incapable of entering an OCEAN score — the scorer's domain accumulation is typed over `{O,C,E,A,N}`; P answers flow only to storage, export, and the S14 read seam.

## 1. Module API surface + OpenAPI delta

### 1a. Assemblies (S2 template)

- **`Svac.Anime.Contracts`** (public) — the only assembly other modules reference. `IAnimeResultsReader`, `IAnimePreferenceSignals`, `IAnimeFacetUnlock` (§1c), result DTO records, opaque id types (`arun_`, `ares_` ULIDs). References only `Svac.DomainCore.Contracts`.
- **`Svac.Anime.Scoring`** (pure; referenced by `Svac.Anime` and the test/tooling projects, never by other modules) — the port: `InstrumentDefinition` (parsed artifact), `ValidateInstrument(defn) → ValidationReport` (the FIXED validator), `Score(defn, answers) → ScoreSet` (domains {O,C,E,A,N} + all 30 facets, faithful to the source incl. floor-20 and render-50 semantics), `MapArchetype(defn, domainScores) → archetypeKey`, `ItemsForScope(defn, scope)`, `Completeness(defn, answeredIds, scope) → missing[]`, `ShortFormAgreement(pairs) → per-domain stats` (the D17 math). Zero IO, zero wall clock, zero EF/HTTP/`Svac.AimlRouter` references — arch-tested with red fixtures (S1 `Deterministic` discipline).
- **`Svac.Anime`** (internal; `InternalsVisibleTo` tests) — `AnimeDbContext` (schema `anime`), run/submit services, endpoint handlers on the public host, artifact loader (embeds `contracts/anime/instrument.v1.json`, drift-gated byte-identical to the contract file; boot runs `ValidateInstrument` + sha check against `anime.instrument_versions` and REFUSES a corrupt or tampered artifact).
- **`backend/tests/Svac.Tests.Anime`** — gate lane, deterministic, <2s: golden-vector parity, P-invariance, validator red fixtures, races, purge, DTO trust scan.
- Evals: **N/A with reason** (S1 §11 precedent) — zero latent surface exists in this slice; ANIME scoring is deterministic space by CLAUDE.md's own rule, and the golden-vector parity suite is the entire quality story. Stated, not skipped.

### 1b. The versioned scoring definition (the crown-jewel artifact)

`contracts/anime/instrument.v1.json` — canonical, checked in, content-hashed, **immutable once ratified; a new version is a NEW FILE, never an edit**:

```
{ "test_version": "anime.v1",
  "content_hash": "<sha256 over canonical serialization>",
  "scale": { "min": 1, "max": 5, "reverse": "6_minus_v", "neutral_keyed_as": "raw" },
  "scoring": { "algorithm": "sum_pct_v1",              // round(sum/(n_answered*5)*100)
               "rounding": "half_away_from_zero",       // JS Math.round parity; NEVER banker's
               "partial_sets": "count_answered",
               "unanswered_facet_render": 50 },         // display parity for partials; never stored (§2)
  "items": [ {"id":"000","domain":"N","facet":1,"keyed":"plus","text_key":"anime.question.000"} ×200 ],
  "domain_counts": {"N":28,"E":28,"O":29,"A":26,"C":29,"P":60},   // the validator's truth, in-band
  "presentation_order": [ ×200 ids ],                   // regenerated (source order artifact is 160-entry
                                                        // stale dead code, §12.9); fixed + versioned, which
                                                        // is what makes cross-device resume and chapters possible
  "chapters": [ {"n":1,"name_key":"anime.chapter.01","item_ids":[…]} ×12 ],   // partition of the order;
                                                        // 12 per design/05 "Chapter 7 of 12" (verified)
  "short_form": { "item_ids": [ 15 OCEAN + 5 P = 20 ] },  // §1e; curated data, version-bound (§11 OQ-2)
  "unlock_map": { "O1": [item ids], … ×30 facets },     // facet grain, derived deterministically from items
  "archetype_map": { "rule": "ordered_first_match", "fallback": "seasonal_drifter",
                     "rules": [ ×8, ported VERBATIM from api.ts ] } }
```

**No question texts in the artifact** — the definition is the MATH; the copy lives in message-key catalogs ×4 (clients and the S9 web bundle resolve `text_key`s from their own catalogs). This keeps the artifact locale-free and byte-stable.

`contracts/anime/golden-vectors.v1.json` — produced by `tools/anime-goldens/extract.mjs`, which executes the ORIGINAL TypeScript `calculateScores`/`calculateFacets`/`computeArchetype` over a fixed corpus: all-1s (the floor-20 case) / all-3s / all-5s; per-domain extremes; minus-key flip exercisers; 25 seeded-PRNG full sets; short-form-shaped partials; per-chapter and per-facet-unlock-shaped partials; **the count=8 / odd-sum midpoint-rounding case**; unanswered-facet-50 cases; one probe per archetype rule at its range edges ±1; the rule-overlap set proving table-order tie-break; the all-mid fallback. Each vector: `answers → {domain_scores, facets[30], archetype_key}`. **This file is the conformance suite for every implementation** — S6's C# lib and S9's web scorer run the SAME file in their gates; divergence is a red build, not a drifted score.

**The fixed validator (the N7 deliverable, spelled out):** `ValidateInstrument` asserts total == 200; exactly 140 OCEAN with per-domain **{N:28, E:28, O:29, A:26, C:29}**; per-facet counts exactly the verified table; 60 P items, all `keyed:"neutral"`, `facet:0`; ids unique, zero-padded, contiguous 000–199; OCEAN keying ∈ {plus, minus}; presentation_order covers all 200 exactly once; chapters partition the order; short_form ⊆ items with length ∈ [15,20] and ≥15 OCEAN; unlock_map covers all 30 facets exactly once. Red fixtures: a 140-item truncation fails; the source's stale expected counts (N29/E28/O28/A28/C27, total 140) fail; a P-item-with-facet fails; a duplicate id fails. Runs at build (gate) and at boot (host refuses). The old validator's numbers are preserved in a code comment as the record of why it was wrong. **Never truncate to 140.**

**test_version semantics (tension 3, decided):** a version = items + keying + presentation order + chapters + short-form set + unlock map + scoring params + archetype map — ONE string, ONE sha256. ANY change to ANY of it = a new artifact file (`anime.v2`) + new golden vectors + a new `anime.instrument_versions` row; old files are never edited. Runs pin `test_version` at start; results pin it at submit; **old results are NEVER rescored** (A2: "retake/upgrade, never silently changed") — no code path that rescoring could take exists (arch scan: only the submit service references the scorer alongside a results write). A version activation is the audited founder 9A flip of `anime.current_test_version` (§4); in-flight runs finish under their pinned version; retake/upgrade is an explicit user act producing new rows that supersede, never overwrite. Archetype-map-only changes still bump the whole version — one pin, no partial-version matrix.

### 1c. Module contract (Contracts assembly; consumer seams — none consumed at S6)

```
IAnimeResultsReader {                       // S10 profile render, S14 matching, S16 init read HERE, never tables
  GetCurrent(ActorRef user): AnimeResultSnapshot?
  // { testVersion, kind: Short|Full, ocean {O,C,E,A,N}, facets: (facetKey→int) completed-only,
  //   archetypeKey, fullInstrumentCompletedAt? }       ← fullInstrumentCompletedAt is THE B11 gate read
}
IAnimePreferenceSignals {                   // S14's matching-signal seam (founder-locked purpose of the 60 P)
  GetPreferenceAnswers(ActorRef user): (itemId→int)?    // decrypts per read; NEVER serialized into any
}                                                        // user-bound DTO (trust-DTO arch scan, §8)
IAnimeFacetUnlock {                         // S33 seam; DARK at S6 — no HTTP route, no caller, no UI affordance
  Unlock(ActorRef user, string facetKey, RequestContext ctx)   // facetKey ∈ unlock_map; 4A anime.facet.unlock
}
```

### 1d. Routes (public host, bearer; ALL self-scoped — the actor's own run/result; no client-supplied resource id exists in this module)

```
GET  /v1/anime/instrument            → AnimeInstrument   (current version; ETag/immutable-cacheable; items carry
                                                          id + text_key + chapter placement ONLY — no domain/
                                                          keying/facet: app clients render, the server scores;
                                                          the scoring definition travels only via contracts/anime/)
POST /v1/anime/run                   → AnimeRunState     (idempotent: returns the active run if one exists;
                                                          POST after a completed run = explicit retake →
                                                          supersedes, quota-gated §5)
GET  /v1/anime/run                   → AnimeRunState     (resume: testVersion, chapters[12]{key,nameKey,total,
                                                          answered,complete}, answeredIds, shortForm progress;
                                                          derived from answers — no progress table to drift)
PUT  /v1/anime/run/answers           → AnimeAnswerAck    (batch upsert {itemId, value 1..5}; idempotent by PK,
                                                          last-write-wins pre-submit; per-item validate — accept
                                                          valid, reject invalid with item detail, never
                                                          all-or-nothing (L23); ack returns progress +
                                                          newlyCompletedFacets[{facetKey,score}] — the progressive
                                                          facet_scores[30] fill, computed by the pure lib, no table)
POST /v1/anime/run/submit {scope}    → AnimeResult       (scope short|full; short requires its pinned 20; full
                                                          requires ALL 200 — a 140-answer full submit REFUSES:
                                                          the founder ruling in executable form. Server RESCORES
                                                          from stored answers in the tx; idempotent under race §2)
GET  /v1/anime/result                → AnimeResultSet    ({short?, full?}: archetypeKey + message keys, domain
                                                          scores {O,C,E,A,N}, facet scores for COMPLETED facets
                                                          only (short form ⇒ none — honestly absent, never faked
                                                          at 50), testVersion; absence when none)
```

WHY this shape: six operations finish the whole journey. One active run per user (§2) makes `/run` and `/result` singular and actor-derived, so the Auth-F3 client-supplied-id IDOR surface **structurally does not exist here** (shared note with S3, §3); "takes short form, RESUMES the full 200" is literal — the full run continues the same canvas, the 20 short-form answers carry by construction (same run, same `run_answers` rows). Preference answers are just items to the PUT; they are stored, never in any result DTO, always in the export.

### 1e. Short form + chapters + unlock selection (tension 4, decided)

- **Fixed curated subset, version-bound** — deterministic sampling REJECTED by all three architects: per-user sampling breaks cross-user comparability, D17 same-form validation, and share-card consistency.
- **Composition: 20 items = 15 OCEAN (3 per domain, 3 distinct facets each, ≥1 minus-keyed per domain — acquiescence control) + 5 P items** chosen as the highest-salience preference hooks. The 5 P items ARE the match seeding the ledger row names ("coarse OCEAN **+ match seeding**") — a P-free short form (P1) delivers zero seeds at signup, so P2/P3's composition wins (§12.3). The 20 concrete ids ship as v1 artifact data under this rule (§11 OQ-2).
- **Short-form scoring needs no second scorer:** the ported algorithm normalizes by answered count, so the same pure function over the 20-item subset yields the coarse OCEAN. One algorithm, two scopes.
- **Chapters: 12 named chapters** partitioning the versioned presentation order (design/05 shows "Chapter 7 of 12" — verified; P1's 30-chapters-as-facets contradicted the design surface, §12.2). Order rotates domains and spaces similar items (≥20 apart), P distributed throughout; chapter completion emits the behavioral event and the client's micro-celebration.
- **Quest-unlock selection = the artifact's `unlock_map`, facet grain (30 facets)** — matches profilemodel `facet_scores[30]` and the questsystem "unlock an additional test facet" earn row. "+30 pts: unlock your Openness [facet]" reveals that facet's item ids (minus already-answered). `IAnimeFacetUnlock.Unlock` records the grant; the economy that PAYS for it is S33's. Deterministic, versioned, dark.
- **D17 hook:** at full submit, when a short result exists under the same test_version, append `anime.validation_pair_recorded` (behavioral; payload = run/result id POINTERS, never score vectors — §2) + `ShortFormAgreement()` lives in the pure lib. The retention floor (§4) protects the pair dataset. The later D17 task is a query + desk tile, not new instrumentation.

### 1f. OpenAPI + message-keys delta (real)

`contracts/openapi.v0.json` gains the six paths + components `AnimeInstrument`, `AnimeChapter`, `AnimeRunState`, `AnimeAnswerBatch`, `AnimeAnswerAck`, `AnimeSubmitRequest`, `AnimeResult`, `AnimeResultSet` (ids ride `OpaqueId`; errors ride the existing `Problem`/`LimitReached` — zero new error keys). No trust-shaped field in any schema; contract-lint patterns extended with `preference*`, `anime_internal*`, `anime_delta*`, `answer_value*`.

`contracts/message-keys.json` gains ≈268 keys: `anime.question.000`–`.199` (200, ported ×4 from the source catalogs by a deterministic script with provenance header; zh-CN → zh-Hans), `anime.chapter.01`–`.12` (12, new short strings, translated in-slice), `anime.facet.<key>` ×30, `anime.domain.{O,C,E,A,N}` ×5, `anime.archetype.<key>.name`/`.tagline` ×9×2, `anime.result.*` ~3. i18n-lint ×4 goes hot on a 200-key block — the lint IS the proof the port is complete.

## 2. Schema DDL (schema `anime`, owned solely by AnimeDbContext; no cross-module joins; `user_ref` is an opaque `usr_` string, never an FK)

**Deliberate non-table:** items, scoring params, short-form sets, unlock maps and archetype rules are NOT database rows — the instrument is checked-in, content-hashed contract data; a DB copy of the definition is a drift door and a desk-edit temptation that would break version pinning (§12.4). The DB holds only the version PIN and user state.

```sql
CREATE SCHEMA anime;

CREATE TABLE anime.instrument_versions (      -- thin pin; zero personal data
  test_version      text PRIMARY KEY,         -- 'anime.v1'
  definition_sha256 text NOT NULL,            -- pins contracts/anime/instrument.<v>.json; boot verifies
  created_at        timestamptz NOT NULL,
  activated_at      timestamptz               -- set when anime.current_test_version points here
);  -- append-only: BEFORE UPDATE OR DELETE trigger RAISES (S1 pattern), red-fixture-tested

CREATE TABLE anime.runs (                     -- the one progressive canvas
  id                 text PRIMARY KEY,        -- arun_ ULID
  user_ref           text NOT NULL,
  test_version       text NOT NULL REFERENCES anime.instrument_versions(test_version),
  started_at         timestamptz NOT NULL,
  short_submitted_at timestamptz,
  full_submitted_at  timestamptz,
  superseded_at      timestamptz,             -- retake flips this; NEVER an in-band delete
  region             text NOT NULL, lawful_basis text NOT NULL
);
CREATE UNIQUE INDEX ux_anime_active_run ON anime.runs(user_ref) WHERE superseded_at IS NULL;
  -- ONE active run per user; short→full is the SAME canvas (answers carry); start-vs-start race
  -- resolves by unique-violation catch → re-read winner → return it

CREATE TABLE anime.run_answers (
  run_id       text NOT NULL REFERENCES anime.runs(id) ON DELETE CASCADE,
  item_id      text NOT NULL,                 -- '000'..'199'; validated against the pinned artifact, not a FK
  value_enc    text NOT NULL,                 -- IFieldEncryptor purpose=anime_answers (posture below)
  answered_at  timestamptz NOT NULL,
  region       text NOT NULL, lawful_basis text NOT NULL,
  PRIMARY KEY (run_id, item_id)               -- idempotent upsert anchor; holds ALL 200 incl. the 60 P:
);                                            -- one store, one purge path, one export path

CREATE TABLE anime.results (
  id            text PRIMARY KEY,             -- ares_ ULID
  run_id        text NOT NULL REFERENCES anime.runs(id) ON DELETE CASCADE,
  user_ref      text NOT NULL,
  kind          text NOT NULL CHECK (kind IN ('short','full')),
  test_version  text NOT NULL REFERENCES anime.instrument_versions(test_version),
  domain_scores jsonb NOT NULL,               -- {O,C,E,A,N} ints; server-computed ONLY; NEVER a P key
  facet_scores  jsonb NOT NULL,               -- {facetKey:int} for COMPLETED facets only (render-50 is
                                              --   display parity inside the lib, never persisted)
  archetype_key text NOT NULL,
  created_at    timestamptz NOT NULL,
  superseded_at timestamptz,                  -- retake supersession flip; audit event carries provenance
  region        text NOT NULL, lawful_basis text NOT NULL,
  UNIQUE (run_id, kind)                       -- THE idempotent-under-race submit anchor: short + full
);                                            -- coexist on one run = the D17 pair, structurally
CREATE INDEX ix_anime_results_current ON anime.results(user_ref, kind) WHERE superseded_at IS NULL;

CREATE TABLE anime.facet_unlocks (            -- S33 seam landing spot; written only via IAnimeFacetUnlock; dark
  user_ref text NOT NULL, facet_key text NOT NULL,
  source text NOT NULL,                        -- 'quest' | 'admin'
  unlocked_at timestamptz NOT NULL,
  region text NOT NULL, lawful_basis text NOT NULL,
  PRIMARY KEY (user_ref, facet_key)
);
```

**No progress table** — progress IS `count(run_answers)` against the version's chapter map, derived per read; a second copy would drift. **No facet_scores table** — the answer ack computes newly-completed facet scores through the pure lib (≤200 rows, sub-millisecond); persistence happens once, at submit, in `results.facet_scores`. **No stored P aggregate** — the source computes an `InternalScores.P` percentage no surface consumes; it is dropped as dead data (§12, finding e), consistent with "never scored, never surfaced".

**Idempotent submit under race:** submit = one tx: validate scope completeness (short: its 20; full: all 200) → decrypt answers → score in `Svac.Anime.Scoring` → INSERT result → stamp `runs.<scope>_submitted_at` → append behavioral events. A concurrent duplicate hits `UNIQUE(run_id, kind)` → catch, re-read the winner, return it (identical body either way — determinism makes the race benign). Race test committed.

**Encryption posture (tension 6, DECIDED — P1+P2 over P3, §12.5):** raw item answers — including all 60 preference answers — are field-encrypted (`IFieldEncryptor`, purpose **`anime_answers`**: one additive, versioned change to S1's closed purpose enum, coordinated with the S1 owner as a contract change per CLAUDE.md). Raw instrument responses are psychographic source data read on exactly three cold paths (submit-time scoring, statutory export, S14's per-user preference read); encryption costs one decrypt per rare read and permanently closes the answers-leak-in-a-dump class. **Derived scores (`domain_scores`, `facet_scores`, `archetype_key`) stay plaintext with stated reason:** they are the S14 hot-read path and the data the user themself publishes. Not Art. 9 special category (an anime-flavored OCEAN instrument reveals no health/orientation/beliefs by design); the conservative-global lawful-basis variant applies. Value bounds (1..5) validated at the DTO and in the pure lib — a CHECK is impossible on ciphertext; the golden suite covers range behavior. Crypto-shredding rides the purpose per 13A. **Named residual risk (honest, carried):** if S14's matcher needs bulk SQL over preference answers rather than per-candidate reads through `IAnimePreferenceSignals`, the posture is revisited then as an explicit versioned change — named, bounded, not a rewrite.

**3A events in the mutation tx (the substrate is the outbox):**
- Behavioral: `anime.run_started`, `anime.chapter_completed {chapter, test_version}`, `anime.result_created {kind, test_version, archetype_key, result_id}` (funnel metric + S16's future init read + D17 pairing), `anime.validation_pair_recorded {run_id, short_result_id, full_result_id, test_version}` — **pointers only, never score vectors** (minimal PII on streams; the retained result rows are the data).
- Audit: `anime.run_superseded` (retake provenance). Version activation is audited by 9A's own config-change event — no duplicate row.

**Export (statutory, profilemodel §12):** the module registers as an S3 export contributor: ALL raw answers (decrypted via the purpose key — **always including the 60 preference answers**; "internal only" constrains product surfaces, never the export pipeline), all results (current + superseded, each pinned to its test_version), run timestamps. Gate test: the export payload for a seeded full run contains all 60 P answers.

## 3. 4A policy entries

| action | actor kinds | axes | denyMode | requires_reason |
|---|---|---|---|---|
| `anime.run.start` | user | — | DenyStandard | false |
| `anime.run.answer` | user | — | DenyStandard | false |
| `anime.run.submit` | user | — | DenyStandard | false |
| `anime.run.read` | user (self-scoped; target derived from ActorRef) | — | DenyAsAbsence | false |
| `anime.result.read` | user (self-scoped; target derived from ActorRef) | — | DenyAsAbsence (DenyStandard banned on consumer reads, S1 lint) | false |
| `anime.facet.unlock` | system (S33 quest pipeline); staff: SuperAdmin | role | DenyStandard | true |

**Auth-F3 note (shared with S3):** every S6 route is self-scoped — the target is always derived from the authenticated `ActorRef`; no route or body carries a resource id, so the client-supplied-id IDOR surface Auth-F3 describes structurally does not arise. The Auth-F3 `TargetRef` redesign continues to carry to the first slice with a client-supplied resource-scoped id (likely S10). The S6 security phase confirms the absence and red-fixtures both directions (S2 Correction-2 pattern). `GET /v1/anime/instrument` is an unauthenticated read of non-personal versioned data: no policy row, asserted with reason. The 4A middleware's refuse-unmapped-mutation law covers all mutation routes from commit one; the generated matrix suite covers all six rows.

## 4. 9A config entries

Additive manifest `backend/modules/Anime/config/anime.config.json` (S1 union-merge format). Every entry has a real S6 consumer; the dead-tunable lint holds:

| key | scope | type | v0 | bounds | requires_reason | consumer |
|---|---|---|---|---|---|---|
| `anime.current_test_version` | founder | string | `anime.v1` | must exist in `anime.instrument_versions` with matching sha256 (set-time bounds check; an unknown or tampered version CANNOT be saved) | true | run start (pins it); instrument endpoint. Bumping IS the version-migration act — audited on 3A, never silent; existing runs/results stay pinned |
| `anime.retake_weekly_cap` | ops | int | `1` | [0, 7] | false | quota `anime.retake.weekly` |
| `anime.submit_daily_cap` | ops | int | `10` | [1, 100] | false | quota `anime.submit.daily` |
| `anime.superseded_retention_days` | ops | int | `400` | [90, 1460] | true | retention_expiry purge of superseded runs/results; the 90-day floor protects the D17 pair dataset from an accidental short purge |
| `anime.empty_run_gc_days` | ops | int | `30` | [7, 365] | false | retention_expiry GC of ZERO-answer abandoned runs only. **Runs with answers are never GC'd** — "resumable anytime" (5.26) is a product promise; progress survives until retake or account deletion |

**Deliberately NOT 9A entries (unanimous across proposals; the reasoned refusal):** `anime.shortform_length`, the short-form item list, the archetype map ref, the unlock table, and the chapter shapes are **version-artifact data, not tunables**. A desk edit to any of them would change scoring/selection behavior WITHOUT a test_version bump — exactly the silent-rescore class tension 3 exists to kill; it would also desynchronize server scoring from S9's client artifact. They change only by shipping `anime.v2` and flipping `anime.current_test_version`. Also refused: `anime.facet_unlock_enabled` (the unlock verb is 4A-system-only with no caller until S33 — absence is structural; a bool no one flips is a dead tunable), `anime.answer_batch_max` (a DTO contract constant, ≤200), `anime.retake_cooldown_days` (a second limiter duplicating the retake quota — one deny shape, one mechanism).

## 5. 10A quota keys

Registered in the S1 additive key format (`<domain>.<action>.<window>`), caps resolved from 9A, one `LimitReached` deny shape:

- **`anime.retake.weekly`** — user-local weekly window; consumed ONLY on the retake branch of `POST /run` (a first run is never quota'd; a resume is not a start). Prevents rescore-roulette against archetype boundaries.
- **`anime.submit.daily`** — user-local daily; anti-abuse ceiling on the scoring path. Honest users submit 1–2 times (short + full).

Answer saves are deliberately un-quota'd (unanimous): bounded naturally by the 200-item space × idempotent upsert; a deny mid-chapter is hostile UX at the couch-ritual moment for zero abuse value. Asserted with reason.

## 6. 13A store registrations

| store | account_deletion | statutory_erasure | minor_purge | consent_revocation | retention_expiry |
|---|---|---|---|---|---|
| `anime.runs` | Delete (cascade root) | Delete | Delete (registered despite T10-A — no under-18 server flow exists by construction; an unregistered store is the B2 scar, defense in depth costs one row) | NotApplicable (instrument participation is not a ledger consent; IRL-consent revocation does not erase test data — reason recorded) | superseded runs after `anime.superseded_retention_days`; zero-answer runs after `anime.empty_run_gc_days` |
| `anime.run_answers` | Delete via cascade + CryptoShred available (purpose `anime_answers`) | Delete | Delete | NotApplicable (same) | rides its run |
| `anime.results` | Delete via cascade | Delete | Delete | NotApplicable | superseded rows only; the LATEST result per kind is never retention-purged |
| `anime.facet_unlocks` | Delete | Delete | Delete | NotApplicable | n/a |
| `anime.instrument_versions` | NotApplicable, all classes — zero personal data by construction (registered with reason, never silently exempt) | | | | |

**Retake supersedes, never silently changes:** retake = new run + `superseded_at` flips on the old run and its results + `anime.run_superseded` audit event — a state flip now, purged later by the ONE retention pipeline. Short and full results of one run are different KINDS and never supersede each other; the pair IS the D17 dataset. Account deletion cascades everything. Purge-completeness suite extends with all five stores (seed → run every class → zero residue or asserted state + purge_run row).

## 7. Notification taxonomy rows

**Zero — asserted with the closure reasoning, not assumed.** Scoring is synchronous pure math: the result returns in the submit response; the reveal is in-session; chapter micro-celebrations are client-local render moments. No state change ever happens outside an active session, so no "result ready" row exists to close. Three candidate rows are RECORDED for their future owners so S4's closure lint sees intent, not a gap: (a) "instrument updated — retake available" joins the taxonomy when `anime.v2` first ships (a real consumer-visible state change); (b) resume nudge ("Chapter 7 is waiting") — S4/S19, keyed off `anime.chapter_completed` events already flowing; (c) quest facet-unlock reveal — S33's row.

## 8. BUILD.md §9 seams made concrete

| Seam | S6 concrete form |
|---|---|
| **Deterministic math in pure libs + golden vectors (THE seam of this slice)** | `Svac.Anime.Scoring`: zero IO/EF/HTTP/wall-clock refs AND zero `Svac.AimlRouter` reference — all arch-tested with red fixtures; an LLM structurally cannot enter the scoring path. Golden vectors EXTRACTED FROM THE ORIGINAL TS by `tools/anime-goldens/extract.mjs` (provenance header pins the weebtest commit); C# parity over the full corpus is a gate test; **rounding law ported explicitly** (JS half-up ⇒ `MidpointRounding.AwayFromZero`; the reachable partial-set midpoint case is IN the vectors — proven, not assumed) |
| **Client-side scoring seam (T10-A / S9 — THE extraction seam)** | `contracts/anime/instrument.v1.json` + `golden-vectors.v1.json` are the published, versioned scoring definition; S9's web scorer is a second interpreter of the SAME artifact, proven identical by running the SAME vector file in its CI; backend embeds the artifact (byte-drift-gated) and serves it at `GET /v1/anime/instrument`; minors score with zero server round-trip and zero server storage, by construction |
| test_version pinning | one pin binds the whole artifact; sha stored in `instrument_versions`, verified at boot; new version = new immutable file + audited founder 9A flip; in-flight runs finish pinned; no rescore code path exists |
| D17 validation hook (eng item 25) | `anime.validation_pair_recorded` pointer event at full submit + `ShortFormAgreement()` in the pure lib + the retention floor protecting pairs — the future task is a query + desk tile |
| Quest-unlock seam | `unlock_map` (30 facets) in the artifact; `IAnimeFacetUnlock` behind 4A system-only; `anime.facet_unlocks` table; dark, no endpoint, no UI affordance until S33 (absence law) |
| S14/S16 read seam | `IAnimeResultsReader` (OCEAN + completed facets + archetype + `fullInstrumentCompletedAt` — THE B11 gate read) and `IAnimePreferenceSignals` (the 60 matching signals) in the Contracts assembly; consumers never join `anime.*` |
| 1A boundary isolation | Contracts/Scoring/internal split per S2 template; schema `anime` owned solely by AnimeDbContext; opaque `arun_`/`ares_` ids; `user_ref` opaque, no cross-module FK or join |
| Region-first PII (L21) | region + lawful_basis NOT NULL on runs, run_answers, results, facet_unlocks; stamped from RequestContext; ddl-lint pii-patterns extended `anime.*` |
| Server-authoritative trust (L20) | scores computed ONLY server-side from stored answers (the source API's client-trust defect fixed, flagged); no score/archetype field on any request DTO (type-level); trust-DTO scan extended: `preference*`, `anime_internal*`, `anime_delta*`, `answer_value*` never serialize into user-bound DTOs; item DTOs omit domain/keying/facet |
| Transactional outbox (3A) | submit = score + result insert + events, ONE tx; supersession event same-tx as the flip; E2E reads events BACK and asserts watermarks advanced (written AND received) |
| Idempotent-under-race | answers PK upsert; submit `UNIQUE(run_id, kind)` catch-and-re-read; start `ux_anime_active_run` partial-unique same pattern; all three race-tested |
| i18n ×4 (DR-7.7) | ≈268 keys; the 200 questions + 9 archetypes ported as DATA from the source catalogs (verified present in en/es/pt/zh-CN); question text never stored in rows — keys only; i18n-lint ×4 parity gates |
| Real-or-honestly-dark | facet bars render only from genuinely completed facets (short-form result: domains + archetype, facets honestly absent — the source's render-50 quirk is lib-level display parity, never persisted, never faked); unlock verb real but dark; D17 math real, dark until data exists |
| Bulk paths survive messy input (L23) | answer batch: per-item validate, accept valid / reject invalid with item-level detail — offline replay of 200 answers never all-or-nothing |
| Foreign-event skip (§8 cl.7) | anime consumes NO foreign streams, registers zero projections — asserted by test (N/A-with-note); future consumers (S4 nudge, S16) inherit the skip-test template |

## 9. Dependency classification

| Dependency | Class | Handling |
|---|---|---|
| weebtest client repo (`questions.ts`, `test-state.ts`, `api.ts`, i18n catalogs) | **port-now** | the authoritative algorithm + data + ×4 strings; golden vectors extracted from the ORIGINAL code BEFORE any C# is written; quirks ported faithfully, defects flagged (§11), never silently improved |
| weebtest-api | read, **not ported** | stores client-computed scores, recomputes only the archetype (table verified identical to api.ts); its `>= 200` validation confirms the 200-item truth; its client-trust posture is rejected as a flagged defect |
| weebtest Sui/NFT projects (weebtest-nft/free/pro) | **out of scope** | crypto MVP; zero code, zero concepts ported |
| S1 substrate (3A/4A/9A/10A/13A, RequestContext, IFieldEncryptor, DevSeams, arch harness) | built | consumed as-is; ONE additive coordinated contract change: purpose `anime_answers` joins the closed encryption-purpose enum |
| S3 identity (actors, export/deletion pipeline) | seam | bearer semantics are S3's; DevSeams fixture principals in E2E until S3 lands — never a fake-auth stub in prod DI; export contributor + deletion cascade registered via 13A |
| S9 web funnel (client-side minor scoring, share cards, waitlist) | **seam-now (THE extraction seam)** | published artifact + golden vectors ARE the interface; S9 builds a conforming TS scorer against them, zero server round-trip; B11's web half dies there |
| S14 matching (OCEAN vector + 60 preference signals) | **not-read** | `IAnimeResultsReader`/`IAnimePreferenceSignals` exist and are tested; weighting is S14 design; S6 reads nothing of matching |
| S16 anime_internal | **not-read** | initializes later from `anime.result_created` events + the reader; internal scores exist nowhere in this module |
| S33 quest economy | **seam-now** | `IAnimeFacetUnlock` + 4A row + `unlock_map` + dark table; earn wiring deferred with guaranteed deterministic backfill (§12.7) |
| D17 validation task | **seam-now** | pair events + retained version-pinned pairs + `ShortFormAgreement` math flowing from day one |
| S4 notifications | not-read | zero rows; three candidates recorded (§7) |
| `Svac.AimlRouter` (S2) | **deliberately not-read** | zero latent surface; the arch test proving `Svac.Anime.*` never references router contracts is itself a deliverable — the anti-seam is enforced |
| MUST-BUILD-FIRST blocking S6 | **none** | S1 landed; S6 starts now |

## 10. Outcome moved + evidence at sign-off (HARDENED GATE)

Ledger row: **"short form scores; full instrument resumable"** — B11's backend half dies (the completed-200 gate becomes a checkable read: `fullInstrumentCompletedAt`).

1. **Golden-vector scoring parity (the headline gate):** every vector in `golden-vectors.v1.json` — extracted from the ORIGINAL TS — reproduced exactly by `Svac.Anime.Scoring`: domains, all 30 facets, archetype keys, the tie-break-order probe, the partial-set midpoint-rounding case, the floor-20 case. Red fixtures prove non-vacuity: a banker's-rounding scorer fails; a deliberately mis-keyed item fails.
2. **P-invariance proven in BOTH directions (the founder ruling, executable):** property-style gate test — adding/removing/flipping ANY P answer never moves any OCEAN domain, any facet, or the archetype; AND a full-scope submit with 140/200 answers REFUSES (P items required for completion, never dropped). Plus: export-contains-all-60-P; result DTOs carry no P key (type-level + scan).
3. **Fixed-validator proof:** green on the v1 artifact (200 = N28/E28/O29/A26/C29 + 60 P, keying/facet/order/chapter/short-form/unlock integrity); red fixtures — the 140-truncation fails, the source's stale counts fail, P-with-facet fails, duplicate id fails; boot refuses a corrupt or sha-mismatched artifact.
4. **Version immutability:** UPDATE/DELETE on `instrument_versions` raises (trigger test); results pinned to a superseded version read back byte-identical after `anime.current_test_version` bumps mid-suite; arch scan shows no rescore path.
5. **Deterministic gate suite (<2s):** double-submit race (one result row, identical bodies), double-start race, answer-upsert replay convergence, value-bounds rejection with per-item detail, scope-incomplete refusal, retake supersession chain, quota deny → the single `LimitReached`, 4A matrix over all six rows, config set-time bounds (unknown/tampered `current_test_version` refused), encryption round-trip + shred, artifact/embedded-copy drift gate, purge-completeness over five stores × all classes, DTO trust scan, i18n ×4 parity.
6. **Live E2E** (`backend/e2e/anime.e2e.mjs` against compose: real endpoints, real Postgres, real 3A, DevSeams principal — no stub scoring, no SQL shortcut; the only scoring path IS the pure lib): start run → answer the 20 short-form items → submit `short` → coarse OCEAN + archetype pinned `anime.v1` → fresh-session resume (`GET /run` shows 20/200, chapters intact — the journey's verb proven cross-device) → answer the remaining 180 in chapter order (`chapter_completed` events observed) → submit `full` at 200/200 → full result (30 facets, archetype, `fullInstrumentCompletedAt`); both results readable side by side (the D17 pair exists); `run_started` / `chapter_completed` / `result_created` ×2 / `validation_pair_recorded` read BACK off the streams, region+lawful_basis stamped, watermarks advanced; double-submit replay returns the identical body; retake drill (old run superseded, old result still pinned + readable, quota consumed); purge drill (account_deletion → zero residue); fresh-boot clause (`down -v` → `up`, zero startup errors); zero-exception log sweep on all instances; suite run twice back-to-back, two identical greens.
7. **Drift gates:** OpenAPI delta committed + contract-lint green; `contracts/anime/*` committed and the extraction script re-run reproduces the vectors byte-identically in CI; message-keys ×4 lint green over the 200-question block; ef-gate shows exactly the `anime` schema delta.
8. **Renders:** short-form completion funnel from behavioral events (Actions/retro posture until S5 tiles; S9's ≥60% G1 metric reads this same stream).

## 11. Open questions for Julien

**OQ-1 — Archetype COUNT / NAMES / PALETTES (genuinely yours per superprompt §9.11 "design the system").** The MECHANISM is decided and not open: deterministic ordered-first-match over inclusive OCEAN ranges, explicit fallback, table order as the tie-break, pinned inside the test_version, boundary-golden-vectored. **Recommendation: ratify the source's existing system verbatim as `anime.v1` — 8 rules + `seasonal_drifter` fallback = 9 archetypes** (anxious_archivist, nakama_evangelist, elitist_hermit, festival_wanderer, methodical_purist, gentle_guide, shrine_keeper, chaos_catalyst, seasonal_drifter). Reasons: live on weebtest today (share-continuity), name/description translations already exist in all 4 target locales, 9 distinct hue families fit the §9.11 candy range, and per-archetype palettes/FA glyphs are a design deliverable layered on the KEYS later without a version bump (visuals are not scoring). One honest caveat with data attached: the rule bands are strict (70+/35−, on scores floored at 20), so mid-range users concentrate in the fallback; `anime.result_created` events give you the real prevalence distribution within weeks of S9 traffic, and any re-map is an explicit version bump, never a silent rescore. A different count/names is a v1 artifact edit BEFORE first ship; after ship it costs `anime.v2`.

**OQ-2 — Short-form item LIST ratification (data, not mechanism).** Decided and not open: fixed curated subset inside the artifact (sampling rejected — breaks comparability, D17, and share-card consistency); composition rule 15 OCEAN (3/domain, 3 distinct facets, ≥1 minus-keyed) + 5 P for match seeding. **Recommendation: ratify the rule and delegate the initial 20 ids to the build slice** (they ship as reviewable v1 artifact data; D17 later validates them empirically and a better-validated set becomes v2). Or hand-pick the 20 yourself before first ship — swap-cost is zero now, a version bump after.

**Scoring-parity findings (flagged per tension 1 — none changes an honest score, none blocks; recorded, faithful-port posture applied):** (a) the source API trusts client-computed scores — S6 rescores server-side from raw answers (posture fix); (b) the source never validates answer range 1..5 — S6 validates at DTO + lib (posture fix); (c) normalization is `sum/(n×5)` so every score floors at 20, not 0 — ported faithfully; a "corrected" `(v−1)/4` normalization would change every user's numbers and is recorded only as a candidate `anime.v2` change; (d) unanswered-facet-50 is lib-level display parity, never persisted; (e) `getOptimalQuestionOrder()` is dead code covering only ids ≤159 (P 160–199 missing) — not ported; v1 ships a regenerated fixed 200-item order in 12 chapters (presentation is score-neutral, provable from the algorithm; the runtime shuffle it replaces is also presentation-only); (f) the source's unconsumed P aggregate percentage is dropped as dead data. If you want (a)/(b) treated as versioned changes rather than posture fixes, say so — the judge's read is they change no honest user's score and belong in v1.

## 12. Judge's synthesis record (per-conflict adoptions + the one ruling contradiction)

1. **Run model — P1 adopted, P2/P3 rejected.** One progressive canvas per user (`ux_anime_active_run` on user_ref alone), per-scope submits on the SAME run, `UNIQUE(run_id, kind)` holding the short+full pair. P2's per-kind sessions make "resumes the full 200" a second object, not a resume; P3's `UNIQUE(session_id)` single-result anchor is internally inconsistent with its own carry-over rule (one carried session cannot hold both the short and full results it requires for D17). P1's shape is also what makes the D17 pair structural.
2. **Chapters — P2/P3 (12) adopted, P1 (30 ≡ facets) rejected.** design/05 renders "Chapter 7 of 12" (verified by the judge); CLAUDE.md makes DESIGN.md-family artifacts binding on surface structure. P1's unification was elegant but contradicts the ratified surface. Facet grain (30) survives where it belongs: the unlock_map and the progressive facet fill.
3. **Short-form composition — P2/P3 (15 OCEAN + 5 P) adopted, P1 (20 OCEAN) rejected.** The ledger row says "coarse OCEAN **+ match seeding**"; match seeding is the P items' founder-locked purpose; a P-free short form yields zero seeds at signup. All three agreed on fixed-curated over sampling — locked unanimously.
4. **Instrument residence — P1's thin-pin hybrid adopted; P3's jsonb-in-DB rejected (two copies of truth, a drift door); P2's no-table rejected (loses FK pinning integrity + boot sha verification).** The checked-in artifact is canonical; `anime.instrument_versions` holds only `{test_version, sha256, timestamps}`, append-only-triggered.
5. **Encryption — P1+P2 (encrypt raw answers, purpose `anime_answers`) adopted over P3 (plaintext).** 2-of-3 and the conservative posture win; P3's queryability objection mostly dissolves because D17 compares SCORES (plaintext in all proposals) and S14's hot path is the plaintext derived-score jsonb. The genuine residue (bulk SQL over preference answers, if S14 ever needs it) is carried as a named risk in §2, revisited as an explicit change, not silently pre-solved.
6. **Route shape — P1's six self-scoped routes adopted over P2/P3's `{sessionId}`-scoped routes.** With one active run per user the id is derivable from the actor; removing the client-supplied id removes the entire Auth-F3 IDOR class from the module instead of defending it. **The one ruling contradiction found:** P3 assigned `DenyStandard` to foreign-principal READS of caller-owned resources — S1's consumer-DenyStandard-on-read lint (contract-lint invariant, PolicyDecision closed union) bans that; the correct modes are DenyAsAbsence/DenySilentAs404. Auto-loss recorded; moot under the adopted self-scoped shape, where the foreign-read case cannot be expressed.
7. **Ledger earns — P2's in-tx +50/+100 appends REJECTED; deferral (P1/P3) adopted.** The slice tasking's scope boundary is explicit ("S6 exposes the item-unlock seam, not the economy"), and the questsystem earn table is labeled "draft defaults for founder/eng calibration, required before Online Mode launch" — a G3 gate, not G0. Earn amounts are uncalibrated economy data; wiring them now would freeze them into S6's contract. Deferral loses nothing: earns are once-only per kind, so whichever slice wires them grants retroactively from `anime.results` rows (first non-superseded result per kind) — a deterministic, idempotent backfill, guaranteed by this contract's retention rules.
8. **Rounding — P3's reachability analysis adopted verbatim** (midpoint unreachable on full sets, reachable on partials; `MidpointRounding.AwayFromZero` pinned; the case is IN the golden vectors). P2's "search-the-divisor-space or attest unreachable" extractor behavior adopted as the implementation of that vector.
9. **Presentation order — P1/P2 adopted:** the source's per-user runtime shuffle AND its dead 160-entry `getOptimalQuestionOrder` are both replaced by ONE fixed, versioned, regenerated 200-item order (domain rotation, similar-item spacing, P distributed) partitioned into the 12 chapters. Documented divergence; scoring-neutral by construction; fixed order is what makes cross-device resume and chapters possible.
10. **Derived-state tables — P3/P1 adopted, P2's `facet_scores` table rejected.** Progress and pre-submit facet scores are derived through the pure lib per read/ack (≤200 rows); persistence happens once at submit. A stored copy of derivable state is drift surface. P2's `newlyCompletedFacets` ack payload survives — computed, not stored.
11. **Stored P aggregate — P3 adopted, P2's `p_internal` column rejected:** no source surface consumed it; storing an unconsumed derived P value sits badly next to "never scored, never surfaced" and is dead data.
12. **Config refusals — unanimous core adopted** (no shortform_length / archetype_map_ref / unlock-table knobs — version data, not tunables); P3's `facet_unlock_enabled`, `answer_batch_max`, `retake_cooldown_days` additionally dropped as dead or duplicate tunables; P2's `empty_run_gc_days` adopted (real purge consumer, protects the resumable-anytime promise by GC-ing only zero-answer runs).
13. **Evals — P1/P2's N/A-with-reason adopted** over P3's eval-lane framing: the cross-implementation conformance re-run is a CI drift gate on both sides of the seam, not a paid periodic eval; calling it an eval would blur the two-lane budget rule.
14. **Founder-ruling compliance:** no proposal contradicted the 140+60 ruling; all three verified it by script and the judge re-verified independently (counts, keying, validator staleness, order staleness, archetype table, i18n coverage — §"Verified source facts"). The stale order array measures exactly 160 unique entries, ids 0–159, with precisely the 40 P items 160–199 missing — P2's finding confirmed digit-for-digit.

---

## 13. RATIFICATION (orchestrator, 2026-07-11 — Julien's in-absence pre-approval)

Contract **RATIFIED**. Julien authorized starting the S6 panel this session, then stepped away; in-absence ratification of a current slice's contract is pre-approved. The two OQs are reversible-before-first-ship with recommendations I endorse.

**Scope + the six tension resolutions ratified as written.** One pure `Svac.Anime.Scoring` lib (zero IO/wall-clock/`Svac.AimlRouter` refs, arch-tested with red fixtures — no LLM path exists) + the canonical `contracts/anime/instrument.v1.json` + `golden-vectors.v1.json` extracted from the ORIGINAL TS by a committed provenance-pinned script; validator fixed to the verified truth (200 = N28/E28/O29/A26/C29 + 60 P) with red fixtures for both stale failure modes; `MidpointRounding.AwayFromZero` pinned with the reachable partial-set midpoint case IN the vectors; one progressive canvas per user (short = subset of the same run, full resumes it, `UNIQUE(run_id,kind)` makes the D17 pair structural); six self-scoped routes; raw answers field-encrypted (new purpose `anime_answers`), derived scores plaintext; 12 chapters (design/05 verified); test_version immutability (no rescore code path exists).

**Founder ruling — made structural and ratified:** all 200 kept; the 60 domain-P items have ZERO OCEAN weight (the scorer's domain accumulation is typed over `{O,C,E,A,N}` — P cannot enter), are stored/encrypted/always-exported/never-in-DTOs, never surfaced at S6; P-invariance is property-tested both directions; export-contains-all-60-P is a gate test.

**OQ-1 — Archetype count/names/palettes: adopted the ported 9 as the WORKING v1 default, flagged as YOURS to finalize before first ship (S9).** Per superprompt §9.11 this is a founder-reserved "design the system" call, so I am NOT exercising design authority — I am seeding v1 with the system already live on weebtest (8 range rules + `seasonal_drifter` fallback = 9; names translated ×4 already; fits the §9.11 candy range) so the build has a concrete instrument to score against. It is reversible as a FREE artifact edit until S9 first ships; after ship it costs `anime.v2`. Honest caveat carried: the bands are strict, so mid-range users concentrate in the fallback — `anime.result_created` events give real prevalence within weeks of S9 traffic, and any re-map is an explicit version bump, never a silent rescore. **Your call before S9: keep the 9, or redesign count/names/palettes.**

**OQ-2 — Short-form 20-id list: rule ratified, initial ids delegated to the build as reviewable v1 artifact data** (15 OCEAN [3/domain, 3 distinct facets, ≥1 minus-keyed] + 5 P for match seeding; sampling rejected). Swap-cost is zero before ship, a version bump after. You can hand-pick the 20 before S9 if you'd rather.

**Scoring-parity findings (a)–(f): faithful-port posture ratified** — (a) server rescores from raw answers (fixes the source's trust-the-client defect) and (b) 1..5 range validation added are posture fixes that change no honest score; (c) floor-20 normalization ported FAITHFULLY (a "corrected" (v−1)/4 is recorded only as a candidate v2, never silently applied); (d) unanswered-facet-50 is lib-level display parity, never persisted; (e) dead `getOptimalQuestionOrder` (missing P 160–199) not ported, replaced by a fixed versioned 200-item order in 12 chapters (score-neutral); (f) the unconsumed P aggregate dropped as dead data. Endorsed: port faithfully, flag defects, never silently "improve" a score.

**RECONCILIATION with S3 + S5 (both LOCKED) — S6's domain-core footprint is MINIMAL:** exactly ONE additive coordinated change — encryption purpose `anime_answers` joins S1's closed `IFieldEncryptor` purpose enum (same pattern as S3's `birthdate`). S6 adds NO policy-engine surface: all routes are self-scoped, so it uses S3's `SelfAccount` target binding (not `OwnedResource`), consumes S3's `IBearerAuthenticator` session auth (DevSeams fixture principal in the E2E until S3's module lands), and contributes its six 4A rows as an `AnimePolicyTableSource` via S5's `IPolicyTableSource` union. Its scoring lib is its own module assembly, not domain-core. **Auth-F3 is RETIRED at S3** (S3 has the client-supplied `{sessionId}`/`{deviceId}`/`{exportId}` routes + `OwnedResource` resolvers + un-skips S1's lens test); S6's §3 "carries to S10" note is superseded — S6 simply doesn't need the mechanism.

**BUILD-ORDERING:** S6's `anime_answers` enum addition folds into the ONE combined domain-core Phase-2a substrate mutation (with S3's + S5's additive deltas), landed with byte-identical-behavior proof on S1/S2 before any feature builder fans out. Then the S6 anime module builds on the shared substrate.

Ratified. Proceeds to Phases 1–3 through THE HARDENED GATE after Julien's go on starting the build wave; STOPS at DONE for /compact (stop-after-slice).
