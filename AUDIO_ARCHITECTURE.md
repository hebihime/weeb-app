# AUDIO_ARCHITECTURE.md — music + SFX

Status: **engine architecture researched and recommended (2026-07-11). NOT yet ratified.** The design-system half (defaults, accessibility, brand delta) is a `/design-consultation`-class founder ruling and is still open — see §7. This doc is the engineering half: what to build once that ruling lands.

Scope decision (founder, 2026-07-11): **full audio** — the app carries weebtest's music + SFX experience, built for tight trigger control, gapless looping, and low battery. This doc is the "best way to do it" answer to that mandate, from three parallel platform research lanes (iOS / Android / web), library choices vetted on live GitHub + community data per CLAUDE.md.

## 0. The one line

One semantic **sound seam** (a sound-enum "buy seam", same pattern as the icon seam), three vanilla native engines behind it, one shared **AAC/MP4 asset set** built by a deterministic script with committed loop points and loudness normalization. No audio framework on any platform — the workload (6 looping beds, 2 rapid SFX, one bed at a time) needs no synthesis or DSP, so vanilla platform APIs win outright.

## 1. Per-platform engine (all three converge on "vanilla, split the two paths")

The universal shape: **beds** = decode/stream once, native gapless loop, crossfade via gain ramp; **SFX** = pre-decoded PCM resident in memory, fired with no I/O. Separate paths, never one mechanism for both.

| Platform | Beds (gapless loop + crossfade) | SFX (rapid, overlapping) | Framework? |
|---|---|---|---|
| **iOS** (`ios/`) | `AVAudioEngine` + `AVAudioPlayerNode`, buffer scheduled `options: .loops` on pre-decoded PCM; crossfade = two nodes ramped into `mainMixerNode` | round-robin pool of 2-4 `AVAudioPlayerNode`s, resident PCM buffers, `scheduleBuffer` per hit | none (AudioKit rejected, §6) |
| **Android** (`android/`) | AndroidX **Media3 ExoPlayer**, `REPEAT_MODE_ONE` (reads gapless metadata + trims encoder delay); crossfade = two players ramped by one `ValueAnimator` | **SoundPool**, `maxStreams` 4-8, clips pre-loaded → PCM | none (Oboe reserved for a future sub-10ms need, §6) |
| **Web** (`web/`) | raw **Web Audio API**, `AudioBufferSourceNode.loop` + `loopStart`/`loopEnd` set to sample-exact offsets that skip codec padding; crossfade = paired `GainNode`s on the sample clock | same context, tiny SFX buffers decoded once, resident | none (Howler + Tone rejected, §6) |

iOS uses ONE engine graph for both paths; Android splits across two APIs (SoundPool has no gapless loop; ExoPlayer is heavy for a keystroke click); web uses one `AudioContext` for both. weebtest's raw-Web-Audio choice was correct and is kept.

## 2. The shared seam (the "buy seam")

A semantic contract both design and every client import; no client hard-codes a filename or a raw engine call.

- **Sound enum**, two kinds: `MusicBed { actionTheme, about, loading, compute, results1, results2 }` and `Sfx { keystroke, tic }` (the exact weebtest inventory).
- **Verbs**: `playBed(bed, loop)`, `crossfadeTo(bed, ms)`, `duck(toGain, ms)` / `unduck(ms)`, `fireSfx(sfx)`, `setMuted(bool)`. Mute is a master-gain gate, never an engine teardown.
- **Asset manifest** — a shared committed artifact, `contracts/audio/soundbank.v1.json`: for each sound, its file ref(s), `loopStartSample` / `loopEndSample`, and measured integrated loudness. Deterministic: same enum → same asset, same loop point, every platform. This is deterministic-space work (a table the build script emits), never a runtime judgment.

Each platform ships its own gate tests + eval against this contract (bounded-unit rule): a client change can't require another client's suite.

## 3. Asset pipeline (deterministic build script + gate)

- **Format: one universal AAC-LC in an MP4/`.m4a` container**, for web, iOS, and Android. AAC is hardware-decoded on all three and plays on every iOS version the funnel meets (Opus is out for web — iOS only got Opus-in-OGG at 18.4 and still refuses Opus-in-MP4). One encode, one loudness pass, identical masters everywhere.
- **The gapless catch, solved deterministically:** lossy codecs inject encoder priming/padding (~2112 samples for AAC), so a naive `loop` clicks. Fix is not a runtime crossfade hack — the build script computes exact `loopStart`/`loopEnd` sample offsets (authored at zero-crossings) and writes them to the manifest. Web sets them on the source node; iOS loops the trimmed PCM buffer; Android ExoPlayer trims via container metadata. **Per-bed escape hatch:** if a bed still fails the loop-seam eval on any platform, promote *that bed* to lossless (FLAC bundled on native / WAV-decoded on web) — padding-free by construction. Escape is per-bed, not per-platform, so the universal-AAC simplicity holds for the rest.
- **SFX:** ship as short 16-bit mono WAV/PCM (KB-scale); preload to resident PCM on all platforms.
- **Loudness normalization** (so crossfades don't jump): `ffmpeg loudnorm` / EBU R128 to **−16 LUFS**, true-peak −1 dBTP, applied at encode time. Gate asserts each output is within ±0.5 LUFS of target — a mis-normalized bed can't ship.
- The whole thing is a committed build script producing versioned assets + manifest; re-runnable, gated, no hand-tuning.

## 4. Delivery

- **Native (iOS + Android): bundle in the app binary.** ~12 MB AAC (or tens of MB if a bed goes FLAC) is trivial against store budgets and gives zero-latency first play + offline. No CDN on the latency-sensitive path.
- **Web: Azure Blob + CDN** (already provisioned, ruling 2A) — **this is the fix to the "CDN-hosted" doubt.** weebtest served from DigitalOcean Spaces; a generic object-store CDN is functionally fine but the wrong home for this project. Move the assets onto Azure Blob+CDN and verify: byte-range requests (`Accept-Ranges: bytes`), `Cache-Control: public, max-age=31536000, immutable` with content-hashed filenames, HTTP/2, and CORS `Access-Control-Allow-Origin` for the funnel origin (`decodeAudioData` is a cross-origin read). Nothing specialized needed for 8 static files.

## 5. Battery discipline (the hard constraint — universal checklist)

- **Start muted; lazy-init the engine on first unmute.** If the user never unmutes, never create the engine at all.
- **Release on background, recreate on foreground.** No paused bed player kept alive across backgrounding. No background-playback service — this is UI ambiance, not a media feature.
- **Never hold a persistent low-latency output stream for SFX.** (Android Oboe / AAudio trap: it pins the audio DSP awake between keystrokes. SoundPool's shared mixer idles cheaply. Same principle everywhere.)
- **Pre-decode once, keep resident:** SFX always; beds only current + next (during a crossfade). Never decode all six (web: ~250 MB decoded = mobile-fatal; iOS: store beds as Int16 PCM to halve RAM).
- **No high-frequency timers.** Drive ramps off the audio clock / one bounded animator, not a polling loop.
- **Platform specifics:**
  - iOS: `AVAudioSession` category **`.ambient`** — respects the hardware silent switch for free, mixes with other apps, silenced on lock. Never `.playback`. Keep default IO buffer (~20 ms); don't force a tiny one.
  - Android: let ExoPlayer own audio focus (`handleAudioFocus = true`); skip audio offload (breaks crossfade volume automation); cap SoundPool `maxStreams`; no wakelocks. Google's own guidance: for foreground screen-on audio, power impact is small — getting the lifecycle right is 90% of it.
  - Web: `AudioContext.suspend()` on idle; **Page Visibility API** `suspend`/`resume` on tab hide/show (handle iOS Safari's `interrupted` state explicitly, re-arm on next gesture).
- **Interruptions / routes:** iOS handle `interruptionNotification` (re-schedule the loop on `.shouldResume`) + `routeChangeNotification` (pause on headphone unplug); Android audio focus covers calls/other apps; web covers via visibility + context state.

## 6. Library vetting (live data, per CLAUDE.md)

- **iOS — AudioKit: REJECTED.** 11.4k stars, ~10 yrs, maintained but slow cadence. It *wraps the same AVAudioEngine* we'd use directly, so it adds a dependency and its unused Soundpipe/DSP surface for zero gain on this workload. Earns its place only for synthesis/sequencing/FFT. → vanilla AVAudioEngine.
- **Android — Media3/ExoPlayer: ADOPT** (Jetpack official, v1.10.1 May 2026, engine behind YouTube Music; star count understates adoption since it migrated from the frozen `google/ExoPlayer`). **Oboe: excellent but not now** (v1.10.0 Sep 2025, Google official) — reserve for a future sample-accurate need; its latency win costs battery here. **SoundPool: ADOPT** (platform, zero-dep, the standard SFX choice).
- **Web — Howler.js: REJECTED** (25.2k stars but last real release Sep 2023, effectively stalled; its own seamless-loop bug open for years). **Tone.js: REJECTED** (14.7k stars, healthy, but a music-production framework — wrong tool, bundle + abstraction we'd fight). → ~150 lines of vanilla TS over the raw Web Audio API; the only path that fully meets all three constraints. Layer-3 "the wrapper doesn't fit" call, reason documented.

## 7. OPEN — design-system ruling (founder, `/design-consultation`-class) — blocks build

Adding a sensory dimension to the LOCKED DESIGN.md is a founder/design call, not an engineering default. Before the seam is built, DESIGN.md needs a **Sound** section deciding:
- **Default state**: start muted (recommended, matches weebtest + autoplay policy) vs opt-in prompt.
- **Accessibility**: the audio-equivalent of the existing reduced-motion fallback (respect silent switch / an in-app toggle / OS "reduce sound" if surfaced).
- **Brand delta**: does Friki share the beds/SFX or get its own, mirroring the Bubblegum→Tangerine palette split.
- **Sound identity**: are weebtest's beds the final palette, or does the Candy Sticker Pop register call for a fresh set.
- **Licensing**: confirm we own/licensed weebtest's music + SFX before shipping (same discipline as the Font Awesome Pro call).

## 8. Where it lands + how it gets ratified (not the S3/S5/S6 backend wave)

Client-side only. Web seam ships with **S9** (web funnel); native seams with the iOS/Android test surfaces. Nothing here touches the S6 anime *scoring* engine or the backend. Does not block or gate the current S3→S5→S6 backend wave.

**Ratification path (founder, 2026-07-11): fold the engine architecture into the S9 slice Phase-0 panel, NOT a standalone plan-eng-review.** Rationale: this is within-client architecture — it sets no backend module boundary, touches no OpenAPI contract, and the Azure Blob+CDN delivery is already inside ruling 2A. It clears none of the bars that make a decision eng-review-class, so the normal SLICE_PLAYBOOK Phase-0 panel + founder checkpoint (the machinery that ratified S3/S5/S6) is the right forum. This doc is that panel's input. The native-test-surface slices inherit the same architecture the same way.
