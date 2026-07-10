# CLAUDE DESIGN SUPERPROMPT — Weeb App (weeb.app) / Friki App (friki.app)

> **How to use:** upload this file to Claude Design (or paste as the prompt) and ask it to produce the full screen set. Every screen brief below is self-contained: purpose, hierarchy (what the user sees 1st/2nd/3rd), states, and copy notes. All decisions herein are founder-ratified (design review 2026-07-09) — treat them as constraints, not suggestions. Visual identity is now RATIFIED (§9 — the DESIGN.md token system from /design-consultation, 2026-07-09): consume it as law. Creative license remains only where §9 marks something OPEN (archetype count/names/palettes — design the system).

---

## 1. WHAT THIS PRODUCT IS

A social matching app for anime convention attendees, 18+, **platonic-first** with opt-in romantic intent. Two brands, one product: **Weeb App** (worldwide) and **Friki App** (Iberia + Latin America) — identical structure, different skin. Native iOS + Android, phone-only v1.

Three modes, presented as **context, never as places the user navigates to**:
- **Online Mode** (default, anywhere): contests (TikTok-linked), personality test, waitlists, profiles with art avatars.
- **Convention Mode** (auto-offered inside a con's geofence during its dates): the swipe deck, battles, quests, Explore map, schedule.
- **Nakama Mode** (later release, off-con IRL): platonic-only local deck.

The founding user: a shy 19–24-year-old at their first big con, alone, who cannot cold-approach. Every design choice serves them.

**The dead-time law (core design law):** the app never competes with con programming. It attaches to already-spent moments — lines, waits, evenings. Any screen that demands attention during a panel is wrong.

**The trust laws:**
- Every human profile photo in IRL modes is a **verified real photograph**. Illustrated/anime-art avatars are banned for humans in IRL contexts (allowed as Online Mode avatars). Therefore: an illustration in a deck card's photo slot IS the disclosure that this is an AI character — no label needed.
- Rejection is always **silent**: a denied romantic superlike, a declined battle — the other side never learns. Design must never leak these.
- Reputation exists internally but is **never shown to anyone**. Limit screens render identically whether the cap is freemium or reputation-scaled.
- **Absence, not disablement:** unavailable actions simply don't render. No grayed-out buttons, no locked icons (the one exception: the no-crew tab may show create-as-Premium as a secondary option).

## 2. DESIGN DIRECTION (identity RATIFIED 2026-07-09 — §9 is the token system; these bounds still apply)

- **Identity is settled: Candy Sticker Pop (§9).** The mark is a kawaii chibi mascot in candy-sticker style (thick chocolate outlines, bubble lettering); brand posture is **"Weeb App," like "Cash App"** — mainstream-consumer confidence. Light mode is pure white; dark mode is Choco (`#1E1410`). Both palettes ship; deck signal rows stay big and glanceable at arm's length in a dim panel hall and on a bright show floor.
- **One loud color at a time** (Weeb: Bubblegum; Friki delta per §9 — brands differ by wordmark, palette, illustration set, app icon: NOTHING structural).
- Energetic anime-con culture WITHOUT any licensed IP — original iconography only (Font Awesome Pro per §9 + original illustration). No franchise names, no character likenesses.
- Not a Tinder clone, not a generic SaaS look. Banned: purple-blue gradients, icon-in-circle feature grids, centered-everything, decorative blobs, emoji as UI, cookie-cutter card grids. (Pill buttons/chips and sticker radii are §9 system choices, not the banned "uniform bubble radii" — the radius scale in §9 is hierarchical.)
- Typography per §9: M PLUS Rounded 1c, one family EN/ES/JP, weight-differentiated. Contrast ≥4.5:1; no default system stacks.
- **Three copy registers (§9 voice guide):** playful-otaku for product surfaces; neutral-plain for anything safety/account (verification, moderation, lockouts); neutral-professional for portals. Never mix registers on one screen.
- Localization-proof: EN/ES/PT/zh-Hans day one. ES/PT run ~30% longer — badges, chips, and modal buttons need expansion headroom; never truncate a safety button. (Full locale rules in §9.)
- Accessibility is mandatory: every gesture has an on-card button equivalent; both forced modals fully screen-reader navigable; dynamic type safe; reduced-motion honored.
- Motion per §9 budget: celebration moments (match, level-up, hidden-quest discovery, crew founding) get real motion design; everything else stays calm. The pass gesture returns the card to the binder — never a fling into a void.

## 3. THE GESTURE GRAMMAR (canonical, all decks)

Left = pass · Right = platonic connect · **Up = Romantic Intent superlike** (at cons/online; in Nakama Mode up = Nakama platonic superlike) · Down = Actions Menu · Tap = detail view.
- Drag-direction glyph overlays: each direction has a color + icon that fades in as the card drags.
- First-deck interactive tutorial (one card per gesture) + per-mode legend in the deck header.
- If a person has romantic-receiving off: the up-swipe affordance/overlay/button **does not exist** on their card.
- On-card button equivalents for all four gestures (accessibility).

## 4. NAVIGATION SKELETON

Fixed bottom tab bar: **Connect · Explore · Crews · Inbox · Profile**.
Mode re-skins Connect and Explore; a mode chip in the header shows the current con when con-present. Entering a con geofence triggers a "Which con are you at?" confirmation — that prompt is the only mode switch. Quests live inside Explore and Crews.

## 5. SCREEN BRIEFS (produce one design per numbered screen)

### 5.1 Con Mode deck card (the product's core screen)
- Hierarchy: (1) verified photo fills the card; (2) identity zone bottom-left overlay: display name, age, pronouns; (3) signal row: synergy pill top-right, intent badge ("Platonic / Non-Romantic"), distance + zone ("< 1 mi · Artist Alley"); max 3 fandom tag chips (rest in detail view); "Cosplaying today" chip top-left when set.
- Synergy pill: band-colored, range 55–99. Below 55: the pill simply doesn't render (never an empty pill or dash).
- Gesture legend visible for new users, fades with experience.
- States: loading = card-shaped skeletons; offline = one banner + "N cards left"; exhausted = see 5.13.

### 5.2 AI character card (same template as 5.1, fully dressed)
- Identical skeleton; illustration full-bleed instead of a photo (this IS the disclosure — design nothing else to mark it).
- All slots real, from character data: name, age (canonical birthdate), pronouns (bio), distance (actual GPS), fandom-flavored tags, and a REAL synergy pill (same math as humans).
- Characters are unique persistent inhabitants of the merged world (IRLNFTRPG) — design them as people, not ads.

### 5.3 Sponsored venue card (deck ad)
- Persistent top-band label: "Sponsored · [venue name]" — body-size text, ≥4.5:1 contrast, never over busy imagery.
- Card leads with venue imagery + the fixed quest-reward preview ("Complete: +50 pts"). No synergy pill, no human social signals.

### 5.4 Detail view (tap-through from any card)
- Order: gallery → "why you two" block ABOVE the fold (mutual fandoms + schedule overlap) → passport (cons attended) + pledges → crew flag + permanent origin line. Sticky action bar bottom.

### 5.5 Romantic Intent response modal (forced, app-locking — behavior is law)
- Appears on app open when someone sent a Romantic Intent superlike; must be answered; multiple queue oldest-first with a preamble screen ("3 people sent you Romantic Intent").
- Tone: a flattering, unhurried reveal — "Someone's into you." Being liked should feel good; forced-ness reads as "this deserves your attention," never detention. Warm color/motion.
- Hierarchy: (1) sender's FULL deck card, tap-through to their complete detail view; (2) headline; (3) response sheet: **Connect romantically** (primary) / **Connect as friends instead** (secondary — with its guarantee inline: "They'll see a normal friend match — never that this was a downgrade") / **No thanks** (quiet; "they won't be told"); (4) footer row: Block · Report · Stop receiving Romantic Intent.
- Neutral-plain register on the footer; playful register above.

### 5.6 Battle challenge modal (forced when foregrounded — behavior is law)
- Hierarchy: (1) challenger's card — who is this person; (2) stakes as play: "If they win, you're connected. If they lose, you choose. Win or lose it takes a minute — you pick the game."; (3) game picker AS the accept mechanism (Janken first — 10 seconds, then Trivia duel, Emoji charades, Drawing); (4) Decline — quiet but always visible, never below the fold ("silent, no consequence").

### 5.7 Match moment (full-screen celebration)
- Both cards + the formation context as the hero: "You're both going to [panel] at 3pm" / "Won by janken" / synergy band / mutual fandoms.
- ONE primary action: open the thread (pre-seeded — see 5.8). Real motion design here; respect reduced-motion.

### 5.8 DM thread
- Opens with a formation-context system card (the "why you two" from 5.7) so the first message is a reply, never a cold open.
- When both are con-present: a "meet at [dead-time spot]" suggestion chip from shared schedule/zone data.
- Pledged pairs post-con: a standing "[N] days until [next con]" context strip.
- **Media (settled — eng re-open rulings ER-1…ER-17, 2026-07-09):** in matched threads where BOTH people are past the trust gate (18+ assured both ends + a speakable milestone + a silent floor), an attachment affordance renders (photos; video only when enabled — see below). Below the gate the affordance **does not exist** — no locked state, no teaser, no hint that media is a feature. This is the absence law, applied here with extra force: the silent leg of the gate must never be inferable from the UI.
- Incoming media bubble: EVERY received image arrives **blurred with a tap-to-reveal**, regardless of sender or content. Reveal is per-recipient and permanent for that item. Design: blurred state (no thumbnail leakage), revealed state, load-failure state.
- Video is **async**: the sender sees an in-thread processing state ("uploading → processing → delivered"); design that progression. If video is disabled by config, the affordance is photos-only — never a disabled video option.
- Send failure (including a scan rejection) renders as the ONE standard could-not-send treatment. Never design a distinct "blocked for content" state or error code surface.
- Cold-DM Requests (5.9) and crew/party chats (5.10) are **text-only permanently** — no attachment affordance there, ever.
- Translation: auto-translated messages show tap-for-original.

### 5.9 Inbox
- Three areas: Matches (primary list) · **Requests** (Premium cold DMs: sender's full card + Accept / Ignore / Block; accepted join Matches; ignored never re-notify) · Notifications (chronological, category icons, per-tab unread badges; safety/account items pinned and undismissable).

### 5.10 Crew home
- Chat-first: the crew group chat is the main tab; Log/Album is the second tab (the memory object: photos, quest completions, milestones); roster + flag + permanent origin line as the header.
- Founding ritual (crew creation): ceremony, not form-filling — name it, raise a flag (composer: background + emblem + palette layers from unlockable sets), write the origin line with "this appears on your crew page forever" gravity. Bar: it should feel like founding a One Piece pirate crew (original theming only).
- No-crew state: "get invited" as the hero (how crews find you); create-as-Premium secondary.

### 5.11 Explore (con-present)
- Map-first; partner markers carry photo/video chip + open/event state; filter bar: Events · Quests · Schedule · For You; list toggle.
- Schedule surface: day-grid with source-layer chips (official/user/sponsor/venue), one-tap marking, "people going" teaser (count only — never a browsable attendee list with approach signals).

### 5.12 Convention tab, pre-first-con (the app for months for most users)
- Spine: countdown to your pledged/waitlisted con + "N people pre-registered."
- The con-ready checklist: verify your photo · IRL consent · **complete your full ANIME profile (200 items, resumable in chapters, also on web)** — required before any IRL mode unlocks; framed as the ritual that makes the con work, done on the couch.
- Below: waitlistable cons from the registry.

### 5.13 Empty & edge states (design each)
- Deck exhausted: "You've met the whole hall" + dead-time redirects (who's in line, QR battle, Explore) + "passes reset each con day."
- Matches empty: points to deck, "matches appear when it's mutual."
- Nakama empty: "No nakama nearby right now — you'll be first in their decks when someone joins."
- Verification pending at the con gate: expected wait + retry + interim access (schedule, Explore read-only). Never a bricked app at peak intent.
- Presence not detected: "Not detecting the con?" → wifi/QR fallback walkthrough; never blames the user.
- Age-estimation lockout: dignity screen — exactly what still works (all of Online Mode), the voluntary appeal explained without accusation, "declining is allowed." Neutral-plain register.
- Find-my-cosplay zero results: ONE neutral state ("cosplayers appear when they set today's cosplay") — used identically whether truly empty or filtered.
- Battle connectivity pause: resume countdown + plain forfeit warning.
- Counterpart protection notice: full-screen explainer (what happened, you're not in trouble, what was removed, one-tap support).

### 5.14 Onboarding flow
- Signup minimum: handle, verified email, birthdate attest, avatar-or-skip, one fandom tag.
- Then: short-form ANIME test prompt (15–20 items, skippable, "sharpen your matches").
- Notifications asked AFTER first value moment, never at first launch. All other permissions just-in-time with designed pre-permission screens (location at first con entry, camera at verification).
- Romantic setup appears ONLY at first romantic-feature touch — the word "romantic" does not appear in signup.

### 5.15 Web funnel (weeb.app / friki.app — the only under-18-visible surface)
- Marketing page → ANIME test (no account) → result: full-screen archetype reveal (archetype name as the shareable hook + OCEAN bars) → share card → "see who you'd match with at [con]" waitlist → app install. One primary CTA per scroll position.
- Archetype share cards: static per-archetype OG images, distinct visual identity per archetype (names/colors/illustration OPEN — design the system).
- Post-account web: the full 200-item test, resumable (counts toward the IRL gate).

### 5.16 Limit-reached surface (one pattern, everywhere)
- "You've reached today's [battles/invites/...]" + reset time. A Premium upsell line appears ONLY for action types that Premium actually extends — never keyed to the individual's numbers. Identical rendering for everyone; never penalty-flavored.

### 5.17 Actions Menu (down-swipe)
- Bottom sheet, fixed order: Invite · Battle · Gift · Trade · DM. Context-absent items are removed, not grayed. Tapping a capped item shows 5.16.

### 5.18 Post-con recap
- Meetups made, people met, quest/battle stats, crew moments; closing CTA: per-match "See you at [next edition]?" pledge prompts. The co-signed meetup artifact (con, date, optional photo prompt) lives in both users' threads.

### 5.19 QR mutual check-in
- Symmetric (either scans either); shared success screen both phones show simultaneously (+30 pts animation); produces the meetup artifact. The screen doubles as the icebreaker — it gives both hands something to do; lean into that.

### 5.20 Public gallery (Online-Mode profile feature — all users)
- A first-class grid on every profile: own view (manage/delete posts, post CTA) and visitor view (browse, per-post report action). NOT gated by the DM trust gate — this ships for everyone.
- Post flow: pick → caption → a visible "publishing…" state (posts pass moderation before going live) → live. A rejected post gets the ONE generic could-not-post treatment; no content-specific rejection state.
- Daily post cap uses the 5.16 limit pattern. Blocked users see no trace of each other's galleries (total severance).
- Gallery images are full-view on tap within the profile context; no blur-until-tap here (public, pre-moderated) — the blur law is DM-only.

### 5.21 Media-unlock milestone surface
- The trust gate has one SPEAKABLE leg — the milestone (a co-signed IRL meetup, or 30 days of active clean tenure) — and one SILENT leg (an internal floor) that must never surface. Design ONLY the milestone.
- Pre-unlock: a quiet milestone tracker (e.g. in profile/settings context): the two routes to the milestone, progress framed warmly ("meet someone at a con, or just keep being here"). Playful register.
- Unlock moment: a small celebration when the FULL gate passes ("Photos unlocked in your matches"). Critical law: someone who completed the milestone but sits below the silent floor sees the milestone as **not yet complete** — identical rendering to a genuine not-yet. Never design a "milestone done but locked" state; it cannot exist.
- 18+ note: threads where either side lacks 18+ assurance simply never show the affordance (absence, no explanation surface).

### 5.22 "Media won't send" appeal entry
- A help-center entry (neutral-plain register): plain description ("Trouble sending photos?"), a request-review action that files to human review, a confirmation state ("We'll take a look — you'll hear back here").
- Explains nothing about scanning, thresholds, or reasons. Outcome notification is generic (resolved / no change). This is the only surface acknowledging that media sending can fail for review reasons, and it lives in help — never inline in the thread.

### 5.23 Online Mode deck & card (Connect, the default mode)
- The everyday deck, pre-con and between cons. Card hierarchy: art avatar OR photo full-bleed (art is allowed here — this is the one mode where illustration ≠ AI character, because no IRL trust rides on it); **handle primary** (Online is the handle world); contest wins as a trophy row (the card's substance); entries collapsed to a count; level corner badge; crew name + flag line; fandom tags.
- Same gesture grammar (up = Romantic Intent — romantic decks exist online too; absent affordance when the person's romantic-receiving is off). Synergy pill same rules (55–99, sub-55 absent).
- Battles do NOT exist here — no Battle in the Actions Menu online (absence, per the menu law).

### 5.24 Deck categories & filters
- Connect decks are **per-category**: 11 action categories (Meet, Eat, Quest, Team, etc. — Drink is cut) as a chip bar above the deck. Selecting a chip = that category's deck.
- **Degraded-mode merge:** below a density floor a category chip routes into the general deck — the chip stays, the user's category intent is preserved and later surfaces as match context ("you both wanted Eat"). Never an empty category deck, never a "not enough people" apology.
- **Find-my-cosplay filter** (Con Mode): cosplayer toggle + canonical character picker + zone filter. Zero-results uses the ONE neutral state (5.13). Filters narrow honestly; no filter ever explains who it removed.

### 5.25 Own profile (view & edit)
- Preview-first: a mode toggle shows "how you appear" as the Online card vs the Con card (the two cards from 5.23/5.1) — editing happens behind the preview.
- Sections: avatar + con photos (changing the primary con photo triggers re-verification — warn inline, friction stated plainly); fandom/interest tags (canonical tags marked as "counts for matching," free-text as display-only); cosplay attributes + a prominent con-day "Cosplaying today" setter; pronouns; passport (cons attended) + pledges; contest history; level + points; gallery management (5.20 own view).
- Handle change with cooldown note ("once per 30 days"). Display name, birthdate never editable-casually (age shows as years only).

### 5.26 The ANIME test experience (a core product surface, not a form)
- **Short form** (15–20 items, post-signup, skippable): fast, one item per screen, progress dots, "sharpen your matches" framing; archetype result moment in-app (the same reveal quality as web 5.15).
- **Full 200-item instrument** — the couch ritual that unlocks ALL IRL modes (part of the con-ready checklist 5.12): resumable **facet-sized chapters** with named chapter breaks, chapter-complete micro-celebrations, progress persistent across app and web (design both). Never framed as a wall; framed as "what makes the con work."
- Facet unlocks via quests ("+30 pts: unlock your Openness profile") get a reveal moment. Retakes/upgrades allowed; results are never silently changed.

### 5.27 Verification flows (neutral-plain register throughout)
- **Photo verification:** designed pre-permission camera screen → hand-off into the vendor's liveness flow (frame the transition honestly: "verified by [vendor], we never keep the images") → pending / verified / failed+retry states (pending-at-con-gate is 5.13's interim-access state).
- **Expiry warning:** "you're about to lose Convention and Nakama access" — plain, dated, one action.
- **Voluntary ID appeal** (age-estimation lockout, extends 5.13's dignity screen): the appeal explained without accusation, entirely optional, "declining is allowed"; jurisdiction-mandated variant states the legal reason plainly. Verify-and-discard promise stated: "we keep a yes/no and birth year, never the document."

### 5.28 Settings & consent center
- **Five consents as first-class cards**, each with its plain-language trade statement, status, and one-tap revoke: IRL Access (the big one — revoking warns "Convention and Nakama turn off; you will leave your crew" with the captain-disband variant), background location ("help local businesses become weeb-friendly third spaces", off by default), identity fields, verification record, marketing.
- **Consent-version bump screen**: "consent terms updated — action required" → re-consent or the standard revocation path. Full-screen, neutral-plain.
- **Romantic settings** (also the first-touch setup flow — the word "romantic" appears only here): enable (→ verification), receive-Romantic-Intent toggle, seek preferences. Unreachable while a romantic modal is pending (the modal precedes settings).
- **Identity & filters:** pronouns/gender identity/orientation with display vs filter-only disclosure clearly separated ("displaying also enrolls it in filtering — here's why"); the **identity-exclusion filter** framed as "who you want to meet" — preset groups only, no free text, identical UI weight in both directions, with the plain note "this doesn't affect people you're already connected with — use block for that."
- Also: challenge-receive toggle, per-category notification consents + quiet hours (account & safety category visibly locked on), blocked-users list, data export, account deletion (export offered first).

### 5.29 Report, block & standing (neutral-plain register)
- **Report:** two-step (category → optional detail) with the disclosure "the conversation is attached automatically"; confirmation sets the SLA expectation ("reviewed within minutes during con hours"). Photo-subject path: "I'm in this photo" pulls the image pending review.
- **Block:** confirmation states total severance; blocking a crew/party co-member gets the carve-out warning ("shared crew spaces stay visible — leave the crew to fully separate") + one-tap leave.
- **Standing & appeals:** suspension screens use "account standing" language, never scores; the appeal path is one plain form → human review → generic outcome. Counterpart-visible chat purge renders as "this conversation was removed for a safety reason" in-thread.
- Counterpart protection full-screen: already 5.13 — link these visually (same family).

### 5.30 Premium (G3)
- One paywall: what Premium actually is — unlimited swipes, cold DM requests, create a crew, form quest parties, gifting, larger battle/invite budgets, extra Romantic Intent. What it is NOT (never say it, show it by omission): no ranking boost, no visibility, no reputation effect.
- Grace/lapsed: quiet banner + resubscribe; **formation-gated lapse** stated plainly ("your crew and parties are untouched; creating new ones needs Premium").
- Purchases via store IAP sheets; restore-purchases path.

### 5.31 Contests (Online Mode's engine, G2)
- Browse: current contests by category (7 incl. Cosplay/Art/Edits), each with lane labels — **Human Only** vs **Open** — and rules.
- Entry: TikTok link paste (manual fallback), lane choice with the Human-Only attestation (Art requires timelapse evidence — an upload step); my-entries states: submitted / accepted / rejected (rejected visible to self only).
- Voting: app-only, free, rate-limited; vote confirmation must feel like recognition, not consumption.
- Results: winner announcement moment (recognition-only — no cash anywhere); contest gallery is also web-browsable (5.15).

### 5.32 Battle gameplay set (G3, Con Mode only)
- **Sender flow:** pick opponent (deck/Actions Menu/QR scan of the person next to you) → declare stakes (platonic or romantic intent) → send → the ONLY bounded waiting state in the product (≤90s, designed as a light moment, never a spinner) → accepted (game starts) / "not available right now" (expiry — NOT a rejection, style it neutrally).
- **The four games**, each under 5 minutes with a built-in sudden-death tiebreaker (draws don't exist): **janken** (10 seconds, the default), **trivia duel**, **emoji charades**, **drawing game**. Each needs its own tiny game-feel identity within one battle visual system.
- **Resolution:** challenger wins → connection celebration (feeds 5.7); challenger loses → recipient's choose screen (connect / decline — decline is quiet, no consequence). Forfeit warning is plain ("leaving counts as a loss"). Connectivity pause = 5.13's resume state.

### 5.33 Quests (G4 build; design now — founder directive)
- **Quest card** (Explore + deck placements) and **quest detail**: type, steps, deadline, reward preview, venue/geofence map snippet.
- **Chain progress:** "3 of 5 before the deadline" — partial credit is always visible, never silently discarded; killed/expired states keep-credit messaging.
- **Photo submission:** capture flow with the envelope indicator (location+time captured), the willing-participants attestation, receipt crop/redact guidance when the evidence is a receipt; **offline capture** shows a queued-will-submit state.
- **Hidden quest discovery:** the top celebration tier (with crew-log echo "your crew found a secret quest" that never reveals the trigger).
- **Quest party:** recruitment card (author-filtered like all authored cards), party chat + shared evidence space, captain powers (invite/kick/close/submit), the 30-day post-quest lock notice, and the post-completion "form a crew with these people" one-tap prompt.

### 5.34 Gifts, trades & inventory (G4)
- Inventory grid (items with acquired-via provenance); gift send flow (pick item → recipient → note); trade: offer → both-confirm → completed, with a reversed state ("this trade was reversed" — ledger honesty).
- $SVAC balance + the sink shop (crew-flag cosmetics, quest-hint consumables). "Earned, never bought" is the design language: no price tags in currency people recognize, no store-feel.
- New-account cooldowns render as absence (no trade affordance), per the absence law.

### 5.35 Nakama Mode (G4)
- Deck variant: up-swipe = **Nakama platonic superlike** (romantic simply doesn't exist here — no affordance, no explanation); received-nakama = sender's card pinned atop your next deck session with the nakama mark.
- Distance floor raised: coarsest bucket "< 5 km"; no zones; empty state per 5.13.
- Explore off-con = the venue map (partner markers, venue quests). "Nakama eligibility attained" is a quiet celebratory notice (fires once, all gates met).

### 5.36 Crew depth (G3.5a)
- **Log/album** (the memory object): photo posting (tagging notifies — a consent surface), quest completions auto-post, milestones; **branded album export** (the shareable recap artifact — design the export's look).
- Roster + captain tools: invite (budgeted), kick, captaincy transfer; disband flow with the 30-day grace explanation (thread goes read-only, history stays readable).
- Collective next-con pledges on the crew page.

### 5.37 Events, invitations & authored cards (G3+)
- **Event detail:** venue-hosted (host named, venue verified badge-less — the perimeter is invisible trust), datetime, capacity, 18+ flag; RSVP; change/cancellation notices.
- **Authored deck cards** — open invitations, polls, announcements: card anatomy distinct from profiles (no synergy, no photo-slot ambiguity), author identity + intent up top.
- **Attendee-context profile view:** the enumerated-fields-only view (photo, name, pronouns, tags, passport, crew) with NO action buttons, no distance, no synergy, no intent badges — RSVPing to the same thing reveals nothing approachable. "People going" is a count, never a browsable list with signals.
- Invite flow (Actions Menu → event/quest picker) draws on the combined budget → 5.16 when capped.

### 5.38 Explore off-con, For You & video (G3.5b)
- Online-Mode Explore: contests/content rail. Con-Mode Explore gains the **For You** feed at G3.5b: content cards + hosted video (async pipeline — poster, duration, buffering states), sponsored items rationed and labeled per 5.3's rules.
- Partner map markers: photo/video chip, open/event state (already 5.11) — the marker-tap sheet is partner-supplied media + actions; design its frame.

### 5.39 Reward moments & progression
- The pattern library (design-system deliverable, but brief it here): **toast** for small earns (+5, +10), **full-screen moment** for completions, level-ups, and hidden-quest discoveries; all within the motion budget, reduced-motion honored.
- Points/XP/level surface on the profile; **ledger history** view ("your balance changed and why" — including reversals stated plainly: "a quest submission was reversed").

### 5.40 Mode transitions
- **"Which con are you at?" confirmation sheet** — the product's ONLY mode switch; design it as a welcome moment, not a dialog. Decline path exists for residents/workers (no explanation demanded).
- Mode chip in the header when non-Online; Nakama sessions visibly survive con entry (suspend, never destroy — returning users find everything intact).

### 5.41 The two surveys (tiny, load-bearing)
- **D17 post-meetup 1-tap** ("would you hang out again?") the evening after a mutual check-in — one tap, in-notification where the platform allows; never visible to the rated person.
- Next-day ground-truth survey (D14): equally minimal. These feed the entire matching calibration loop — design them frictionless enough to actually get answered.

### 5.42 Notification & email anatomy
- Push: every notification deep-links; **lock-screen privacy in Con Mode** — sender + "new message," content hidden (anti-humiliation extends to shoulder-surfers).
- In-app inbox rows per category with icons (5.9); category-8 items pinned, undismissable.
- Transactional email set (the fallback channel): verification results, gate-critical notices, re-verification deadlines, data-export ready, waitlist emails — neutral-plain register, both brands.

## 5B. PARTNER PORTAL BRIEFS (external web, desktop-first, responsive)

Separate product for venues/vendors/sponsors — same brand family, **neutral-professional register** (partners are businesses, not otaku; zero playful copy). Aggregate-only data posture is a selling point: design it visibly ("no individual data, ever" as a product virtue, not fine print).

### B1 Registration wizard (G3.5b)
Business identity → location(s) with geo pin → category + venue-nature flags (18+ / 21+-alcohol-primary, with the honest consequence stated: alcohol-primary = map presence only for now) → safety attestations → primary photo/video (this becomes their map marker — say so) → submit → states: in review / approved / rejected / needs-more. MFA setup mandatory in the flow.

### B2 Dashboard
First-party counts (their own quest completions, RSVPs, impressions) display from the first event — instant gratification; cohort/breakdown views are k-floored and render "insufficient data" below floor, never a small number. Rotation share shown, never purchasable ("your rotation share" as fact, no upsell).

### B3 Campaign builder (sponsored quests + deck ads)
Template-driven: type (location/photo) → copy (own-IP attestation) → dates + geofence → **fixed reward preview, not editable** (the no-multipliers rule enforced by the UI's shape) → submit for review. Campaign states: draft · submitted · approved · live · paused · completed · suspended. Their deck-ad preview renders as the actual 5.3 card.

### B4 Event creation
Datetime, capacity, 18+ flag, named host staff — per-event pre-publish review status visible; rejection reasons plain.

### B5 Heatmap analytics (the paid tier)
Cell map × time buckets × cohort filters; below-floor cells auto-widen or render "insufficient data"; off-con cells labeled **beta/preview** until density; an explicit, well-designed "query budget reached — resets [period]" state (never silent). Rendered maps only — no export affordance exists.

### B6 Consultant (G4 upsell) + billing
Consultant recommendations as cited cards (every claim links its report-pack artifact); one-click quest drafting drops into B3 flagged as AI-drafted, with audience notes surfaced ("party quests require a Premium member"). Billing: invoice history, dunning warnings (one suspension state covers campaigns + analytics together), verification-lapse warnings with the resume path.

## 5C. ADMIN PORTAL BRIEFS (internal, desktop-first; Safety Desk must be one-thumb mobile-usable)

Blazor Server internal product. Information-design-first: queues, clocks, evidence, audit. Every user-impacting action takes a mandatory reason; high-impact actions get a confirm-with-reason interstitial. Reputation appears as **tiers only** (trusted/neutral/watch/restricted) — the number does not exist in this UI.

### C1 Safety Desk
Unified triage queue (human slice of the AI-first pipeline): rows tagged by surface, **SLA countdown per row** (15-min clock during con hours), minor-reports pinned to top; AI-decision rationale visible on every auto-handled item. Evidence view: transcript (original + rendered translation), images, context incl. crew-carve-out notes. Actions: warn/mute/suspend/ban/device-ban + reason. Kill switches (per-con, per-quest, per-venue — "one kill, total" with a blast-radius preview). Appeals inbox; law-enforcement request workflow; counterpart-protection executor.
**Mobile variant** (con-floor triage, standing up): SLA countdown dominant, evidence scrollable middle, one-thumb uphold/dismiss/escalate fixed at bottom.

### C2 Verification Desk
Queues: photo-verification manual review (cosplay edge cases), voluntary-ID appeals, jurisdiction-mandated checks, L3 behavioral re-verification, L4 restoration. Verify-and-discard posture reflected in the UI (no document imagery ever displayed post-decision).

### C3 Content Desk
Deck-content review stream; fandom/character tag approval queue; pronoun display-preset list management (locale-aware); translation-quality escalations; content-ANIME vector view/correct (reason required); contest admin (Human-Only lane checks, timelapse evidence, winner selection); DMCA workflow. **Character governance queue (pre-G3):** AI-character vector drafts (Claude-assisted) review/approve with the adversarial-lore breadth check surfaced, sponsor lore intake, character presence authoring (home GPS + per-con zone/day entries), and the sponsored-character audience-concentration (Gini) monitor.

### C4 Venue & Con Desk
Venue registration review (per-location approval), re-verification queue, per-event pre-publish approval, partner media review, schedule submission approvals, **con registry CRUD** (series/editions, geofence + zone drawing, timezone, kill/suspend), quest location vetoes, post-con partner report workflow.

### C5 Quest & Economy Desk
Quest CMS (author/schedule/geofence, hidden-content authoring, sponsored campaign review with AI-drafted flags), photo spot-check queue with one-tap fraud→ledger-reversal, anti-inflation dashboard (earn/sink ratio), item catalog, trade/RMT fraud desk, billing (invoice issuance, manual payment recording, dunning states).

### C6 Metrics & Ops Desk
Every gate metric as a live dashboard; category-density monitor (drives degraded-mode); **config registry editor** — typed entries with scope badges (founder-only vs ops), bounds, required-reason field, and the confirm-with-reason interstitial on the dangerous ones (lowering the age fail-line); push/email delivery health; data-export/deletion queue with statutory clocks; partner + consultant query-audit views.

## 6. IDENTITY PRESENTATION RULES

- Online Mode: **handle** primary (art-avatar world). IRL modes: **display name** primary, handle secondary. Detail view + DM header show both.
- Age displays on all Con deck cards. Distance unit follows device region (mi/km, never mixed on one surface).
- Pronouns displayable on every profile surface, every mode.

## 7. WHAT NOT TO DESIGN

- No visible reputation anywhere, ever (admin portal included: tiers only, never a number). No "pending" indicator for sent romantic superlikes, ever. No read receipts spec'd. No age-verification demands (estimation-first; ID is a voluntary appeal only). No mode switcher UI. No tablet layouts. No licensed characters/names/trade dress.
- Media (per eng re-open, 2026-07-09): no locked/teaser media affordance below the trust gate — below-gate threads look like media was never a feature. No distinct "blocked for content" send-error state anywhere. No unblurred default for incoming DM media. No attachment affordance in cold-DM Requests or crew/party chats. No DM media with AI characters (companion chat is text-only; out of scope).
- No penalty-flavored anything: reduced limits, reputation scaling, and freemium caps all render through the ONE limit surface (5.16). No grayed-out/locked affordances anywhere (absence law) — the sole exception remains 5.10's create-as-Premium secondary.
- No browsable attendee lists with approach signals; no public itineraries; no loser boards for battles; no raw location or sub-bucket distance ever; no phone numbers displayed anywhere.
- No cash prizes, price-tag store feel, or fiat currency framing in the points/$SVAC economy.
- **OPEN — do not design without a founder ruling:** any user-facing screenshot-detected notice (detection exists internally; whether the counterpart sees anything is unruled). Archetype count/names/palettes are OPEN by design — design the SYSTEM (per §2 and 5.15).

## 8. DELIVERY

- **§5 (consumer app, 5.1–5.42):** every numbered screen as a phone-frame design (9:19.5), both light and dark where the surface ships both (deck, inbox, threads), Weeb skin first (Friki = palette/wordmark swap, same structure). 5.15's web funnel screens are responsive web, mobile-first.
- **§5B (partner portal):** desktop web frames (1440), responsive notes; neutral-professional register, brand-family adjacent but visually distinct from the consumer app (a business tool, not the app in a browser).
- **§5C (admin portal):** desktop frames; C1's mobile variant additionally as a phone frame. Utilitarian information design — density, clocks, and state color take priority over brand expression.
- Gate labels on briefs (G2/G3/G3.5/G4) are phasing info, not permission to skip — the founder directive is design-everything-now, build wave-gated.
- Where copy is quoted above it is canonical; otherwise write in the register rules of §2 (playful-otaku product / neutral-plain safety; partner surfaces neutral-professional).
- Every frame consumes the §9 token system. Do not invent alternate palettes, faces, or radii.

## 9. VISUAL IDENTITY — RATIFIED DESIGN SYSTEM (DESIGN.md, /design-consultation 2026-07-09)

**The memorable thing (founder): "Nakama forever."** Found-family permanence. Every token serves it: time only adds value — origin lines are permanent, crew cards earn foil, counters only go up, nothing decays, nothing resets.

**The mark is the north star.** The brand mark is a kawaii chibi mascot in candy-sticker style: saturated candy colors, thick chocolate outlines, glossy bubble lettering (blue-haired cat-eared chibi + bubble wordmark; an attached reference image accompanies this package where supported). Brand posture: **"Weeb App," like "Cash App"** — mainstream-consumer confidence. The UI is clean and bold so the sticker energy pops instead of drowning: pure white light mode, one loud color, bold rounded type, generous whitespace.

### 9.1 Aesthetic
- **Direction:** Candy Sticker Pop — the mark's world (candy color, chocolate ink, sticker objects) on Cash-App-clean grounds.
- **Decoration:** intentional. Sticker treatment (chocolate outline, big radius) on key playful objects only — deck cards, chips, badges. Everything else clean. The neutral register is decoration-zero **by subtraction**: same tokens with candy, outlines, and Black weight removed — never a separate "serious mode."
- **Mood test for every screen:** does this feel like something your crew made for you, or something a company optimized at you? Ship only the first.

### 9.2 Typography
One family, all three languages, weight does the talking. No novelty faces, no pixel/retro faces (founder-vetoed).
- **Family: M PLUS Rounded 1c** (OFL; native JP, full Latin + Spanish diacritics). Rounded terminals echo the bubble-letter mark; kanji stay clean because the display face IS the body face.
- Display Black 900 (heroes, celebrations, card names) · headings 800 · body/UI 500 (400 long-form) · stats 800 + color (Sky for synergy, Foil for $SVAC, tabular-nums) · neutral register uses 400/500/700 only — Black 900 never appears on safety surfaces.
- **Scale (mobile):** display-xl 34/40·900, display 28/34·900, title 22/28·800, heading 17/24·800, body 15/22·500, caption 13/18·500, micro-label 11/14·700 uppercase +0.08em. Web portals: body 16/24 + data row 14/20 tabular-nums.

### 9.3 Color
| Token | Hex | Role |
|---|---|---|
| Bubblegum | `#F7568F` | Primary (Weeb). Wordmark pink. Primary actions, brand moments, romantic accents. |
| Sky | `#38BDF2` | Secondary. Synergy, links, platonic/battle accents, portal primary action color. |
| Mikan | `#FF9838` | Celebration accent. Quests, streaks, reward toasts. |
| Foil | `#C99A2E` | Earned material ONLY (see 9.9 laws). |
| Choco | `#1E1410` | Light-mode ink + outlines; dark-mode ground. The mark's linework color. |
| good / warn / danger | `#3FB950` / `#F5A623` / `#ED4245` | Danger lives almost exclusively in the neutral register — the playful world never brandishes red. |

- **Light mode (default):** ground `#FFFFFF` (pure white — founder-mandated), surface white + `#E8E3DD` hairlines, surface-2 `#F7F5F2`, text `#26170F`, dim `#8A7C72`, outline `#2B1B12`.
- **Dark mode ("Choco"):** ground `#1E1410`, surface `#2A1D16`, surface-2 `#35251D`, line `#48362B`, text `#FBF3EC`, dim `#B3A093`, outline flips to `#F5EBE2`. Candy hexes stay identical; control saturation by area, not by desaturating tokens.
- Contrast AA both modes; Bubblegum/Sky on white are button-fills with white text, never small text on white.

### 9.4 Iconography
**Font Awesome Pro** (licensed). Solid style on playful surfaces (chunky fills match Black 900); Regular on neutral register + list rows; Duotone reserved for empty states; Thin/Sharp never. Sizes 16/20/24; 44×44pt touch targets; icons take their context's text color — never a third color inside one component.

### 9.5 Spacing, Layout, Radius
- 4px base, comfortable density; scale 4·8·12·16·24·32·48·64. Cards 16–24 internal padding; neutral register gets MORE whitespace.
- Card-first inside a grid-disciplined shell; hybrid editorial for web funnel; dense-but-calm grids for portals. Reading width 680px; portal shells 1180px.
- Radius: card 24 · modal/sheet 20 · input 16 · button/chip/badge pill (999). Neutral register: 16 max, no chocolate outlines. **Sticker outline (2–3px Choco) appears ONLY on playful key objects** — deck card, chips, badges, synergy pill.

### 9.6 Component Anatomy
- **Person card (deck):** full-bleed verified photo · 3px Choco outline, radius 24 · synergy pill top-right (white surface, Choco outline, Sky numeral) · status chip top-left · bottom scrim with name (Black 900), meta, intent + interest chips. An illustration in the photo slot IS the AI-character disclosure — no extra badge.
- **Crew card:** Foil 3px border + 9% foil-tint gradient · micro-caps label in Foil · name Black 900 · tenure line Foil 800 · permanent origin line (italic, foil left-rule) · patina badge row (gold pills for earned).
- **Authored/event card:** person-card anatomy with brand/partner frame color; never foil unless earned.
- **Modal:** radius 20, hairline border, title 800, stacked full-width pill buttons, quiet action last. Neutral modals: no outline color, no Bubblegum. **Sheet:** radius 20 top, grab handle, same button rules. **Chip:** pill, 2px outline (Choco playful / hairline neutral), 700 text; selected = filled candy. **Badge:** pill, micro-label; Foil variant earned-only. **Row:** 56px min, FA Regular leading icon optional, title 500 + caption, trailing chevron/value.

### 9.7 Motion
- Easing: enter ease-out · exit ease-in · move ease-in-out · springy overshoot only in playful register (card settle, badge pop).
- Duration: micro 80–120ms · short 150–250ms · medium 250–400ms · celebration 400–700ms.
- **Choreography budget:** full choreography reserved for crew founding ritual, hidden-quest reveal, first-connection card reveal. One per screen, max.
- **The pass gesture returns the card to the binder** — never a fling into a void. Rejection is never animated as disposal.
- Neutral register: functional only. Reduced-motion honored everywhere; celebrations degrade to static frame + haptic.

### 9.8 Weeb ↔ Friki Brand Delta (token namespace swap — nothing structural; values RATIFIED 2026-07-09)
| Token | Weeb App | Friki |
|---|---|---|
| brand.wordmark | Weeb App mark | Friki mark |
| brand.primary | Bubblegum `#F7568F` | Tangerine `#FF7A3D` |
| brand.celebration | Mikan `#FF9838` | Bubblegum `#F7568F` |
| brand.illustration | Weeb mascot set | Friki mascot set (same style spec) |
| brand.strings | en/ja-led pack | es/pt-led pack |
Everything else (grounds, Choco, Sky, Foil, semantics, type, spacing, anatomy, motion) is shared. Same sticker, different ink.

### 9.9 Token-Layer Laws (enforced in tokens, not per-screen judgment)
1. **Foil never ranks people.** Foil/patina/rarity apply to moments, crews, and authored cards — never a person's card (never-exposed-reputation + anti-humiliation).
2. **Danger stays out of the playful register.** Errors there use the one generic could-not-send pattern in neutral styling.
3. **Absence, not disablement.** No locked/grayed decorative variants exist (sole exception: 5.10 create-as-Premium secondary).
4. **One limit-reached surface.** Freemium caps and reputation-scaled caps render identically (5.16).
5. **Lock-screen privacy.** Notifications render in neutral register with Con-Mode content rules.
6. **Time only adds value.** No token exists for decay, expiry shame, or streak-loss; expiries render as neutral information, never loss theater.

### 9.10 Locale Layout Rules
+30% width headroom (design against the ES string first); JP body line-height 1.7, never justified; no text baked into images; chips wrap, never truncate mid-word; one-line ellipsis on names/handles with full value on tap; handle-primary Online / display-name-primary IRL (§6); dates/numbers localize; $SVAC always renders with its prefix.

### 9.11 Archetype Visual Framework (names/count OPEN — design the system)
Each archetype = one hue family (candy range, never Choco/danger) + one FA Pro Solid glyph, rendered as a sticker badge (32px pill, archetype hue fill, white glyph, 2px Choco outline in playful register). Archetype hue is an accent on profile/results surfaces — never recolors a whole screen, never appears on safety surfaces. Web-funnel result card: archetype name in Black 900 + facet bars in Sky — the shareable artifact.

### 9.12 Voice
- **Playful-otaku:** second person, warm, in-jokes and exclamation allowed, quest/RPG vocabulary ("the hall knows your name"). Never at anyone's expense; never FOMO or guilt.
- **Neutral-plain:** short declaratives; no exclamation, mascots, or candy; states exactly what happens and who sees what ("Our safety team will see the messages between you and this person."). The deny-silence guarantee is stated plainly wherever it applies.
- **Neutral-professional (portals):** business plain; no otaku vocabulary or mascot; Sky as action color; data-forward.
