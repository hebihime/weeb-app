# Android bundled fonts — drop points + license status

Two font families back the Android client. Neither binary is committed to this repo.

## Font Awesome Pro 7.3.0 (icons) — LICENSED, NOT COMMITTED

- **Drop location (local only):** `android/designkit/src/main/assets/fonts/`
  - `fontawesome7pro_solid_900.otf` — playful register (DESIGN.md Iconography: Solid)
  - `fontawesome7pro_regular_400.otf` — neutral register + list rows (Regular)
- **Why not committed:** FA's Pro license forbids putting Pro files in a public repository, and this repo is public. The `.otf` files are hard-ignored in the root `.gitignore` (`**/fontawesome*.otf`). They live under `assets/` (not `res/font/`) on purpose: `res/font` requires the file at compile time, so a gitignored `res/font` would break CI; an `assets/` font is loaded at runtime and its absence simply falls back.
- **Glyph → codepoint mapping (committed, it is data not a font):** `design/fa-glyph-map.json` — the 27-entry `Glyph` vocabulary → FA icon name + Unicode PUA codepoint, generated deterministically from the licensed FA metadata. Both faces (Solid 900 / Regular 400) render the same codepoint.
- **Wiring status (S7):** the `GlyphSource` seam in `src/main/kotlin/app/client/designkit/Iconography.kt` currently resolves `FallbackGlyphSource` (Compose Material Icons — zero license, always present). Swapping in an FA-Pro-backed source that loads the asset OTF at runtime and renders `design/fa-glyph-map.json` codepoints is a DI swap of `Iconography.source`; no call site changes. Held pending the repo-publish decision (how the fonts reach CI / shipped builds — see SECURITY_REVIEW_S7.md).

## M PLUS Rounded 1c (type) — OFL, separate asset-drop

`Typography.kt`'s `weebFontFamily` is `FontFamily.Default` until the OFL `.ttf` files land under `designkit/src/main/res/font/`. Those files are freely redistributable (NOT matched by the FA ignore rule) and are their own asset-drop task.
