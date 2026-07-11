# iOS bundled fonts — drop points + license status

Two font families back the iOS client. Neither binary is committed to this repo.

## Font Awesome Pro 7.3.0 (icons) — LICENSED, NOT COMMITTED

- **Drop location (local only):** `ios/DesignKit/Fonts/`
  - `FontAwesome7Pro-Solid-900.otf` — playful register (DESIGN.md Iconography: Solid)
  - `FontAwesome7Pro-Regular-400.otf` — neutral register + list rows (Regular)
- **Why not committed:** FA's Pro license forbids putting Pro files in a public repository, and this repo is public. The `.otf` files are hard-ignored in the root `.gitignore` (`**/FontAwesome*.otf`). Placing them here locally is safe; committing them is not.
- **Glyph → codepoint mapping (committed, it is data not a font):** `design/fa-glyph-map.json` — the 27-glyph `Glyph` vocabulary → FA icon name + Unicode PUA codepoint, generated deterministically from the licensed FA metadata. Both faces (Solid 900 / Regular 400) render the same codepoint.
- **Wiring status (S7):** the `IconographySource` seam in `Sources/DesignKit/Iconography.swift` currently resolves `BundledFallbackIconography` (SF Symbols — zero license, always present). Swapping in an FA-Pro-backed source that renders `design/fa-glyph-map.json` codepoints in the bundled OTF is a change to that ONE file plus font registration; no call site changes. Held pending the repo-publish decision (how the fonts reach CI / shipped builds — see SECURITY_REVIEW_S7.md).

## M PLUS Rounded 1c (type) — OFL, separate asset-drop, NOT in this package

`Info-*.plist` already declares `UIAppFonts` = `MPLUSRounded1c-*.ttf`. Those OFL files are freely redistributable (NOT matched by the FA ignore rule) and are their own asset-drop task; `Font.token(_:)` in `Tokens.swift` substitutes the system font until they land.
