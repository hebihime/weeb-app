# TODOS — weeb-app

## S3 build wave — S3 MERGED to master; S5 IN PROGRESS
- **S3 identity: DONE 2026-07-12, MERGED to master (PR #1, `795209c`).** THE HARDENED GATE green
  (`signup→verified→delete` E2E live twice); Phase-3 security done (1 CRITICAL + 7 HIGH + 6 MEDIUM
  remediated, `SECURITY_REVIEW_S3.md`). The combined Phase-2a substrate (S3+S5+S6 deltas) landed with it.
- **S5 admin desk: IN PROGRESS on branch `wave/s5-admin-desk` (off master).** Contract
  `SLICE_S5_CONTRACT.md` RATIFIED (§13). Greenlit by Julien 2026-07-12. Phases 1→4 to THE HARDENED GATE,
  stop at DONE for /compact.
- **RULED 2026-07-12 (founder) — heatmap retention (SECURITY_REVIEW_S3 PII-4): anonymize-at-write.**
  **Keep the analytics signal, sever the subject.** The cell/density/pattern (the actionable data) is
  retained; only "whose signal is this" is dropped, so deleting an account keeps its contributions on the
  map, just unattributed. Lawful under GDPR Recital 26 (anonymous data out of scope; kept despite the
  originating user's erasure wish). `NotExportable` + `NotApplicable`-on-deletion dispositions stay.
  **ACCEPTANCE BAR for S9/S14 Phase-0 — two-sided:** (1) *genuinely anonymous*, not pseudonymous — strip
  the subject link either at write or by crypto-shredding it at deletion; a held-elsewhere salt keeps it
  personal data and Art.17 still bites; (2) *no singling-out* — coarsen/bucket coordinate+time+rare-attr
  so no retained cell re-identifies one person via the mosaic effect, while staying granular enough to
  stay actionable. That coarsening tension is the design work. Verify irreversibility AND non-singling-out
  before the first `events_heatmap_provenance` row.
- **PRE-PROD REQUIREMENT:** set `SVAC_ACA_INGRESS_CIDRS` to the real Azure Container Apps ingress subnet
  before any non-Development deploy — the anonymous rate limiter is inert-but-safe until then (OPS-1).
- **NEXT (not started, awaiting greenlight):** S5 (admin desk) then S6 (anime test) — each its own
  slice (Phase 1→4, stop at DONE). Both rebase their domain-core needs onto the landed Phase-2a surgery.

## Azure SignalR self-host cost review
- **What:** Re-evaluate managed Azure SignalR Service vs self-hosted SignalR + Redis backplane.
- **Why:** Managed is right at launch scale; at multi-con scale self-hosting can be meaningfully cheaper. Switching is a connection-string/config change, not a rewrite.
- **Trigger:** Sustained concurrent connections >10k, or SignalR line-item >10% of infra spend.
- **Context:** Ruling 2A (eng review 2026-07-08) chose Azure partly for managed SignalR; this captures the exit ramp so the choice never becomes silent lock-in.
- **Depends on:** post-G3 usage data.

## Plan-board artifact refresh
- **What:** Regenerate the Claude artifact plan board (linked in ceo-plans/2026-07-08-weeb-app-v1.md).
- **Why:** Stale at "session5-complete" — missing coherence rulings R1–R10, the entire 2026-07-08 eng review, and now the 2026-07-09 design review (25 rulings incl. IRLNFTRPG, full-200 IRL gate, T3-C supersession).
- **Context:** A stale shareable board actively misleads; refresh once, after final state lands. Now also missing the 2026-07-09 eng re-open (ER-1…ER-17: trust-gated DM media with the public-route DROPPED, gallery as first-class feature, character registry + synergy mechanism).
- **Depends on / blocked by:** UNBLOCKED — the eng-review spec-amendment pass (DONE) and the targeted eng re-open (DONE 2026-07-09) have both landed. Runnable next.

## ~~Targeted /plan-eng-review re-open: DM media + character-synergy pipeline~~ — DONE 2026-07-09
- **Resolved:** Session ran 2026-07-09; both docket items closed with 17 founder-ratified rulings (ER-1…ER-17). Headlines: DR-7.6's public-route half DROPPED (ER-11 — correlation oracle + forced-publication consent; founder-final: below the trust gate = no DM media, the pathway is rising above it); DM media is 18+ both ends (ER-12); compound gate = speakable milestone (meetup artifact or active clean tenure) + silent tier floor (ER-13); gallery ships as an independent first-class Online-Mode feature at G2 (ER-2); character registry + renormalizing scoring adapter, 18+-gated synergy metric, authored presence (ER-6/ER-7/ER-8/ER-17). Record: `~/.gstack/projects/weeb-app/jp-unknown-design-20260709-engreopen.md`; build tasks R1–R10 in tasks-eng-review-reopen JSONL.

## ~~/design-consultation → DESIGN.md (token system + brand delta)~~ — DONE 2026-07-09
- **Resolved:** Session ran 2026-07-09. DESIGN.md written to repo root (Candy Sticker Pop system: memorable thing "Nakama forever"; mark-derived palette — Bubblegum/Sky/Mikan/Foil/Choco, pure-white light mode (founder mandate), Choco dark mode; M PLUS Rounded 1c one-family EN/ES/JP; Font Awesome Pro icons; sticker component anatomy; motion budget; Weeb↔Friki brand delta with Friki values pending founder ratification; token-layer laws incl. foil-never-ranks-people). Rejected along the way: risograph/zine direction, Dela Gothic One (kanji too thick), pixel/retro numeral fonts (corny). DESIGN.md folded into the superprompt as §9; §2 rewritten from "identity OPEN / dark-base" to "identity RATIFIED / white light + Choco dark". Preview artifact: `~/.gstack/projects/weeb-app/designs/design-system-20260709/preview.html` (+ logo-reference.png).
- **Friki delta values RATIFIED 2026-07-09** (Tangerine `#FF7A3D` primary, Bubblegum → celebration; side-by-side ink-run preview: `designs/design-system-20260709/preview-friki.html`). **The superprompt is upload-ready** — only remaining nicety: attach the logo reference image alongside it where Claude Design supports attachments.

## Audio: music + SFX system (design ruling + client seam)
- **Scope RESOLVED 2026-07-11 (founder): FULL audio.** The app carries weebtest's music + SFX experience.
- **Engine architecture RESEARCHED + RECOMMENDED 2026-07-11** → `AUDIO_ARCHITECTURE.md` (three parallel platform lanes; library choices vetted on live data). Summary: one semantic sound seam → vanilla native engines (iOS `AVAudioEngine`, Android Media3 ExoPlayer + SoundPool split, web raw Web Audio — NO framework on any platform); one universal AAC/MP4 asset set built by a deterministic script with committed loop-point offsets (kills encoder-padding click) + −16 LUFS normalization gate; native bundles the assets, web serves from **Azure Blob+CDN** (move off weebtest's DO Spaces). Battery discipline: start muted, lazy-init on unmute, release on background, never hold a persistent SFX stream, only current+next bed resident.
- **STILL OPEN — design-system ruling (blocks build):** `AUDIO_ARCHITECTURE.md` §7 — a `/design-consultation`-class founder ruling for a DESIGN.md **Sound** section: default state (muted rec.), accessibility (audio-equiv of reduced-motion), Weeb/Friki brand delta, whether weebtest's beds are the final sound palette, and licensing confirmation (we own/licensed the assets — same discipline as Font Awesome Pro).
- **Where it lands:** CLIENT-side only. Web seam ships with S9 (web funnel); native seams with the iOS/Android test surfaces. Does NOT touch S6 backend scoring or block the S3→S5→S6 backend wave.
