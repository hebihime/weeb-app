// ios/DesignKit/Sources/DesignKit/BrandColor.swift — SLICE_S7_CONTRACT.md §1c/§9a.
//
// The brand-delta surface in code is EXACTLY `brand.primary` / `brand.celebration` (DESIGN.md's Brand
// Delta table + §1c) — nothing else varies by brand. Their VALUES live in each app target's own
// Assets.xcassets color sets ("BrandPrimary" / "BrandCelebration"), never a hex literal here or anywhere
// in Swift (brand-gate's leak scan + token-lint's rogue-hex check both cover Swift/Kotlin source, not
// asset-catalog JSON — that split is exactly why the value lives there). Reading `Color("BrandPrimary")`
// resolves against `Bundle.main`, i.e. whichever app target is actually running — Weeb resolves the pink,
// Friki resolves the tangerine, with zero branching code anywhere.
//
// Everything else (Sky, Foil, Choco, semantics, chips, badges — DESIGN.md: "same sticker, different
// ink") stays a shared `Tokens` value regardless of brand. Only reach for `BrandColor` when a surface is
// genuinely "the brand identity color" (primary CTAs, the wordmark, celebration moments) — not for
// generic playful decoration.

import SwiftUI

public enum BrandColor {
    public static var primary: Color { Color("BrandPrimary", bundle: .main) }
    public static var celebration: Color { Color("BrandCelebration", bundle: .main) }
}
