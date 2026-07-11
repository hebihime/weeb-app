# TODOS — weeb-app

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
- **What:** Decide whether the app carries the weebtest audio experience, then (if yes) add a Sound section to DESIGN.md and build a cross-platform audio seam.
- **Original asset:** `~/Repos/weebtest/src/app/services/sound-manager.ts` (Web Audio) ships 6 music beds (`action_theme`, `about`, `loading`, `compute`/bom, `results_1`, `results_2`) with loop/play/play-then-loop modes + track-completion events, and 2 SFX (`keystroke`, `tic`), volume 0.6, always-start-muted. Assets hosted on the weebtest DigitalOcean Spaces CDN (`weebtest.sgp1.cdn.digitaloceanspaces.com`), not committed.
- **Gap:** our app has ZERO audio code (ios/android/web) and DESIGN.md is silent on sound (Motion section + one reduced-motion haptic fallback only). So this is a design-system extension, not just missing code.
- **Why it's a founder call:** adding a sensory dimension to the LOCKED, founder-ratified DESIGN.md is a `/design-consultation`-class ruling — defaults (muted vs opt-in), hardware silent-switch + accessibility (audio-equivalent of reduced-motion), weeb/friki brand delta.
- **Then engineering:** a semantic sound-enum "buy seam" (same pattern as the icon seam) behind `AVAudioEngine` (iOS) / ExoPlayer|SoundPool (Android) / Web Audio (web). CLIENT work — lands in S9 (web funnel) + the native test surfaces, NOT the S6 backend scoring engine.
- **Licensing:** confirm we own/licensed the music + SFX (weebtest is a prior JP project, so plausibly yes) before shipping — same discipline as the Font Awesome Pro call.
- **Scope OPEN (asked 2026-07-11, unanswered — JP away):** full audio (design ruling first) vs SFX-only-for-now vs defer-to-post-v1. Does NOT block S3/S5/S6. Recommendation on file: full audio via a design pass; it fits the playful Candy Sticker Pop register and is part of the original's charm.
