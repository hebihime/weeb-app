# iOS bundled fonts — drop points + license status

Two font families back the iOS client. Neither binary is committed to this repo.

## Font Awesome Pro 7.3.0 (icons) — LICENSED, NOT COMMITTED

- **Drop location (local only):** `ios/DesignKit/Sources/DesignKit/Resources/`
  - `FontAwesome7Pro-Solid-900.otf` — playful register (DESIGN.md Iconography: Solid), PostScript `FontAwesome7Pro-Solid`
  - `FontAwesome7Pro-Regular-400.otf` — neutral register + list rows (Regular), PostScript `FontAwesome7Pro-Regular`
  - declared as a DesignKit SPM resource (`resources: [.process("Resources")]`), so they are bundled into `Bundle.module` when present and simply absent (fallback) when not — the committed `fa-glyph-map.json` beside them keeps the resource dir non-empty on CI.
- **Why not committed:** FA's Pro license forbids putting Pro files in a public repository, and this repo is public. The `.otf` files are hard-ignored in the root `.gitignore` (`**/FontAwesome*.otf`). Placing them here locally is safe; committing them is not.
- **Glyph → codepoint mapping (committed, it is data not a font):** `design/fa-glyph-map.json` — the 27-glyph `Glyph` vocabulary → FA icon name + Unicode PUA codepoint, generated deterministically from the licensed FA metadata. Both faces (Solid 900 / Regular 400) render the same codepoint.
- **Wiring status (S7): WIRED with runtime presence-detection.** `Iconography.swift` registers the
  bundled OTF via CoreText and, if both faces register, `IconographyProvider.current` resolves
  `FontAwesomeIconography` (renders the bundled `fa-glyph-map.json` codepoints); if the fonts are absent
  (public CI, clean clone) it falls back to `BundledFallbackIconography` (SF Symbols) so the build stays
  green. No call site changes. Real FA renders automatically the moment the licensed OTF are present in
  the build (i.e. after the repo goes private + the fonts are committed via LFS).

## M PLUS Rounded 1c (type) — OFL, separate asset-drop, NOT in this package

`Info-*.plist` already declares `UIAppFonts` = `MPLUSRounded1c-*.ttf`. Those OFL files are freely redistributable (NOT matched by the FA ignore rule) and are their own asset-drop task; `Font.token(_:)` in `Tokens.swift` substitutes the system font until they land.
