# Android bundled fonts — drop points + license status

Two font families back the Android client. Neither binary is committed to this repo.

## Font Awesome Pro 7.3.0 (icons) — LICENSED, NOT COMMITTED

- **Drop location (local only):** `android/designkit/src/main/assets/fonts/`
  - `fontawesome7pro_solid_900.otf` — playful register (DESIGN.md Iconography: Solid)
  - `fontawesome7pro_regular_400.otf` — neutral register + list rows (Regular)
- **Why not committed:** FA's Pro license forbids putting Pro files in a public repository, and this repo is public. The `.otf` files are hard-ignored in the root `.gitignore` (`**/fontawesome*.otf`). They live under `assets/` (not `res/font/`) on purpose: `res/font` requires the file at compile time, so a gitignored `res/font` would break CI; an `assets/` font is loaded at runtime and its absence simply falls back.
- **Glyph → codepoint mapping (committed, it is data not a font):** `design/fa-glyph-map.json` — the 27-entry `Glyph` vocabulary → FA icon name + Unicode PUA codepoint, generated deterministically from the licensed FA metadata. Both faces (Solid 900 / Regular 400) render the same codepoint.
- **Wiring status (S7): WIRED with runtime presence-detection.** `Iconography.kt` tries to load the
  asset OTF (via `AssetManager`) + parse the bundled `fa-glyph-map.json`; if the fonts are present it uses
  a `FontAwesomeGlyphSource` (renders the codepoints), else it falls back to `FallbackGlyphSource`
  (Compose Material Icons) so the build stays green when the fonts are absent (public CI, clean clone).
  No call site changes. Real FA renders automatically the moment the licensed OTF are present in the
  build (i.e. after the repo goes private + the fonts are committed via LFS).

## M PLUS Rounded 1c (type) — OFL, separate asset-drop

`Typography.kt`'s `weebFontFamily` is `FontFamily.Default` until the OFL `.ttf` files land under `designkit/src/main/res/font/`. Those files are freely redistributable (NOT matched by the FA ignore rule) and are their own asset-drop task.
