# Design System — Weeb App / Friki

Commissioned by design ruling DR-5.1 (G0-class). This is the token layer both native codebases and all web surfaces consume. The Claude Design superprompt carries this file as its final chapter; screens are briefed there, identity is defined here.

**The memorable thing (founder, 2026-07-09): "Nakama forever."** Found-family permanence. Every token serves it: time only adds value — origin lines are permanent, crew cards earn foil, counters only go up, nothing decays, nothing resets.

**The mark is the north star.** The brand mark is a kawaii chibi mascot in candy-sticker style: saturated candy colors, thick chocolate outlines, glossy bubble lettering (reference: `~/.gstack/projects/weeb-app/designs/design-system-20260709/logo-reference.png`). Brand posture: **"Weeb App," like "Cash App"** — mainstream-consumer confidence. The UI is clean and bold so the sticker energy pops instead of drowning: pure white light mode, one loud color, bold rounded type, generous whitespace.

---

## Product Context
- **What this is:** Dual-brand con-social product. Con Mode: a trading-card deck of real verified attendees with synergy, quests, battles, crew founding. Online Mode: default deck + galleries. Plus the ANIME instrument, a partner web portal, and an admin portal.
- **Who it's for:** Anime convention attendees, 18+ trust-gated features; worldwide (Weeb App) and Iberia + LatAm (Friki).
- **Project type:** Two native mobile apps (iOS/Android) + web funnel + two desktop web portals (partner, admin).
- **Registers:** playful-otaku (consumer), neutral-plain (safety/consent/reporting), neutral-professional (partner/admin portals).

## Aesthetic Direction
- **Direction:** Candy Sticker Pop — the mark's world (candy color, chocolate ink, sticker objects) on Cash-App-clean grounds.
- **Decoration level:** intentional. Sticker treatment (chocolate outline, big radius) on key playful objects only — deck cards, chips, badges. Everything else is clean. The neutral register is decoration-zero **by subtraction**: same tokens with candy, outlines, and Black weight removed — never a separate "serious mode."
- **Mood:** warm, loud in one place at a time, zero irony, zero menace. Every screen passes one test: *does this feel like something your crew made for you, or something a company optimized at you?*
- **Reference landscape (2026 research):** Hinge (editorial anti-app posture), Duolingo (gamification vocabulary), Crunchyroll rebrand (owns orange + manpu glyphs — we deliberately do not compete on orange or glyph systems).

## Typography
One family, all three languages, weight does the talking. No novelty faces, no pixel/retro faces (founder-vetoed 2026-07-09).

- **Family:** **M PLUS Rounded 1c** (OFL) — native Japanese, full Latin with Spanish diacritics. Rounded terminals echo the bubble-letter mark; kanji stay clean at every size because the display face IS the body face.
- **Display:** Black 900. Heroes, celebration moments, card names.
- **Headings:** 800.
- **Body/UI:** 500 (400 for long-form reading).
- **Stats/numerals:** 800, colored (Sky for synergy, Foil for $SVAC). `font-variant-numeric: tabular-nums` wherever digits align.
- **Neutral register:** 400/500/700 only — Black 900 never appears on safety surfaces.
- **Loading:** bundle OFL files in both native apps; Google Fonts (or self-host) on web.

**Scale (mobile):**
| Role | Size/Line | Weight |
|---|---|---|
| display-xl | 34/40 | 900 |
| display | 28/34 | 900 |
| title | 22/28 | 800 |
| heading | 17/24 | 800 |
| body | 15/22 | 500 |
| caption | 13/18 | 500 |
| micro-label | 11/14 | 700, uppercase, +0.08em |

Web portals shift body to 16/24 and add a `data` row style (14/20, tabular-nums).

## Color
Every color is sampled from the mark's world. **Approach:** balanced — one loud color at a time; large areas stay ground-colored.

| Token | Hex | Role |
|---|---|---|
| **Bubblegum** | `#F7568F` | Primary. The wordmark pink. Primary actions, brand moments, romantic intent accents. |
| **Sky** | `#38BDF2` | Secondary. Her hair. Synergy, links, platonic/battle accents, portal primary. |
| **Mikan** | `#FF9838` | Celebration accent. The moon. Quests, streaks, reward toasts. |
| **Foil** | `#C99A2E` | Earned material ONLY — milestones, crew tenure, authored-card frames. See token-layer laws. |
| **Choco** | `#1E1410` | Light-mode ink + outlines; dark-mode ground. The mark's linework color. |
| good | `#3FB950` | success |
| warn | `#F5A623` | warnings |
| danger | `#ED4245` | errors/destructive. Lives almost exclusively in the neutral register — the playful world never brandishes red. |

**Light mode (default):** ground `#FFFFFF` (pure white — founder-mandated), surface `#FFFFFF` with `#E8E3DD` hairlines, surface-2 `#F7F5F2`, text `#26170F`, dim `#8A7C72`, outline `#2B1B12`.

**Dark mode ("Choco"):** ground `#1E1410`, surface `#2A1D16`, surface-2 `#35251D`, line `#48362B`, text `#FBF3EC`, dim `#B3A093`, outline flips to `#F5EBE2`. Candy hexes stay identical (they hold on chocolate); saturation is controlled by using them on smaller areas, not by desaturating tokens.

**Contrast:** body text and semantic-on-ground pairs meet WCAG AA in both modes; Bubblegum/Sky on white are button-fill-with-white-text only, never small text on white.

## Iconography
- **Set:** **Font Awesome Pro** (licensed).
- **Playful register:** Solid style — chunky fills match Black 900 type and the sticker look.
- **Neutral register + list rows:** Regular style.
- **Duotone:** reserved for empty states and celebration illustrations support. Thin/Sharp: never.
- **Sizes:** 16 / 20 / 24; minimum touch target 44×44pt. Icons take the text color of their context; never a third color inside one component.

## Spacing
- **Base unit:** 4px. **Density:** comfortable.
- **Scale:** 4 · 8 · 12 · 16 · 24 · 32 · 48 · 64.
- Cards get generous internal padding (16–24). The neutral register gets *more* whitespace, not less.

## Layout
- **Approach:** card-first inside a grid-disciplined shell; hybrid creative-editorial for the web funnel; dense-but-calm data grids for portals.
- **Max content width:** 680px reading, 1180px portal shells.
- **Border radius:** card 24 · modal/sheet 20 · input 16 · button/chip/badge pill (999). Neutral register: 16 max, no chocolate outlines.
- **Sticker outline:** 2–3px Choco outline appears ONLY on playful-register key objects (deck card, chips, badges, synergy pill).

## Component Anatomy
- **Person card (deck):** full-bleed verified photo · 3px Choco outline, radius 24 · synergy pill top-right (white surface, Choco outline, Sky numeral) · status chip top-left ("Cosplaying today") · bottom scrim with name (Black 900), meta line, intent + interest chips. An illustration in the photo slot IS the AI-character disclosure — no extra badge.
- **Crew card:** Foil 3px border + 9% foil-tint gradient · label micro-caps in Foil · name Black 900 · tenure line in Foil 800 · permanent origin line (italic, foil left-rule) · patina badge row (pill badges; gold for earned).
- **Authored/event card:** person-card anatomy with brand/partner frame color; never foil unless earned status applies.
- **Modal:** radius 20, 2px hairline, title 800, body 500, stacked full-width pill buttons, quiet action last. Neutral modals: no outline color, no Bubblegum.
- **Sheet:** radius 20 top corners, grab handle, same button rules.
- **Chip:** pill, 2px outline (Choco in playful, hairline in neutral), 700 text. Selected = filled candy.
- **Badge:** pill, micro-label type; Foil variant for earned only.
- **Row:** 56px min, leading icon (FA Regular) optional, title 500 + caption, trailing chevron/value; portals add a `data` row with tabular-nums.

## Motion
- **Approach:** intentional. Personality lives in card physics; everything else is functional.
- **Easing:** enter ease-out · exit ease-in · move ease-in-out · springy overshoot allowed only in the playful register (card settle, badge pop).
- **Duration:** micro 80–120ms · short 150–250ms · medium 250–400ms · celebration 400–700ms.
- **Choreography budget:** full choreography is reserved for the crew founding ritual, hidden-quest reveal, and first-connection card reveal. One per screen, max.
- **The pass gesture returns the card to the binder** — an ease-in-out slide back, never a fling into a void. Rejection is never animated as disposal.
- **Neutral register:** functional transitions only. `prefers-reduced-motion` honored everywhere; celebrations degrade to a static frame + haptic.

## Weeb ↔ Friki Brand Delta
The delta is a token namespace swap — **nothing structural** (per settled spec: wordmark, palette, illustration, icon accents, strings only).

| Token | Weeb App | Friki (RATIFIED 2026-07-09) |
|---|---|---|
| `brand.wordmark` | Weeb App mark | Friki mark |
| `brand.primary` | Bubblegum `#F7568F` | Tangerine `#FF7A3D` |
| `brand.celebration` | Mikan `#FF9838` | Bubblegum `#F7568F` |
| `brand.illustration` | Weeb mascot set | Friki mascot set (same style spec) |
| `brand.strings` | en/ja-led string pack | es/pt-led string pack |
Everything else (grounds, Choco, Sky, Foil, semantics, type, spacing, anatomy, motion) is shared. Same sticker, different ink.

## Locale Layout Rules (DR-6.2)
- Design every text container with **+30% width headroom** (Spanish expansion); test the ES string first.
- Japanese body line-height 1.7; never justify JP text; no text baked into images — all imagery text-free.
- Chips wrap, never truncate mid-word; names/handles get one-line ellipsis with full value on tap.
- Handle-primary in Online Mode; display-name-primary in IRL modes (DR-7.5) — a type-role decision, not a new style.
- Dates/numbers localize; $SVAC always renders with the `$SVAC` prefix in every locale.

## Archetype Visual System (framework — DR-4.4)
Archetype **count and names are OPEN by design** — Claude Design proposes them. The visual framework they must land in:
- Each archetype = one hue family (drawn from candy range, never Choco/danger) + one FA Pro Solid glyph.
- Rendered as a **sticker badge**: 32px pill, archetype hue fill, white glyph, 2px Choco outline in playful register.
- Archetype hue is an accent on profile/results surfaces — never recolors the whole screen, never appears on safety surfaces.
- Web-funnel result card: archetype name in Black 900 + facet bars in Sky — the shareable artifact.

## Voice (two registers + portal)
- **Playful-otaku:** second person, warm, in-jokes allowed, exclamation allowed, quest/RPG vocabulary ("the hall knows your name"). Never at anyone's expense; never FOMO or guilt ("you both wanted Eat" — context, not pressure).
- **Neutral-plain (safety/consent/reporting):** short declaratives. No exclamation, no mascots, no candy. States exactly what happens and who sees what ("Our safety team will see the messages between you and this person."). The deny-silence guarantee is stated in plain words wherever it applies.
- **Neutral-professional (portals):** business plain. No otaku vocabulary, no mascot, Sky as action color, data-forward.

## Token-Layer Laws (enforced in tokens, not per-screen judgment)
1. **Foil never ranks people.** Foil/patina/rarity apply to moments, crews, and authored cards — never to a person's card (never-exposed-reputation + anti-humiliation rulings).
2. **Danger stays out of the playful register.** Errors there use the one generic could-not-send pattern in neutral styling.
3. **Absence, not disablement.** No locked/grayed decorative variants exist in the component set (sole exception: create-crew Premium secondary CTA). Below a gate, the affordance is absent.
4. **One limit-reached surface.** Freemium caps and reputation-scaled caps render identically.
5. **Lock-screen privacy.** Notifications render in neutral register with Con-Mode content rules (anti-humiliation).
6. **Time only adds value.** No token exists for decay, expiry shame, or streak-loss states; expiries render as neutral information (verification) — never as loss theater.

## Decisions Log
| Date | Decision | Rationale |
|---|---|---|
| 2026-07-09 | Initial system created via /design-consultation | Memorable thing: "Nakama forever"; two design voices + 2026 landscape research |
| 2026-07-09 | Risograph/zine direction dropped; rebuilt as Candy Sticker Pop | Founder: system must derive from the actual mark (kawaii chibi sticker) + "Weeb App like Cash App" posture |
| 2026-07-09 | Dela Gothic One rejected | Founder: display kanji too thick — ink blobs at heavy weight |
| 2026-07-09 | Pixel/retro numeral faces (Departure Mono / DotGothic16) rejected | Founder: corny. Stats = same family, 800 + color |
| 2026-07-09 | Light mode = pure white `#FFFFFF` | Founder mandate |
| 2026-07-09 | M PLUS Rounded 1c, one family EN/ES/JP | Kanji-safe display + body in one family; rounded echoes bubble mark |
| 2026-07-09 | Font Awesome Pro as icon set | Founder: Pro license available. Solid (playful) / Regular (neutral) |
| 2026-07-09 | Friki delta values RATIFIED (Tangerine `#FF7A3D` primary; Bubblegum demoted to celebration) | Founder ratified after side-by-side ink-run preview |
