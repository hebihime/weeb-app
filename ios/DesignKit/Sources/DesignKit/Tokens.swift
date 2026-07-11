// ios/DesignKit/Sources/DesignKit/Tokens.swift — SLICE_S7_CONTRACT.md §9a.
//
// Hand-written mirror of design/tokens.v1.json (itself the machine-readable mirror of DESIGN.md, which
// stays the authoritative prose). VALUES ONLY — no derivation, no color math, no brand hexes (brand
// colors are a build-flavor concern: Config/Brand-*.xcconfig + each target's Assets.xcassets color set,
// never a literal here or anywhere else — brand-gate's leak scan allowlists ONLY this file for palette
// hex literals, and only the manifest's palette, not the brand-delta values).
//
// tools/token-lint verifies this file against design/tokens.v1.json value-for-value, both directions:
// every manifest palette hex must appear here, and no hex may appear here that isn't in the manifest.

import SwiftUI

public enum Tokens {
    // ---------- palette (design/tokens.v1.json → palette.candy / .semantic / .light / .dark) ----------

    /// The mark's world. Never reachable from the neutral register (law 2) except where a token below
    /// explicitly says otherwise.
    public enum Candy {
        public static let bubblegum = "#F7568F"
        public static let sky = "#38BDF2"
        public static let mikan = "#FF9838"
        /// Earned material ONLY (law 1: foil never ranks people) — moments, crews, authored cards.
        public static let foil = "#C99A2E"
        /// Light-mode ink + outlines; dark-mode ground. The mark's linework color.
        public static let choco = "#1E1410"
    }

    public enum Semantic {
        public static let good = "#3FB950"
        public static let warn = "#F5A623"
        /// Lives almost exclusively in the neutral register — the playful world never brandishes red.
        public static let danger = "#ED4245"
    }

    public enum Light {
        public static let ground = "#FFFFFF"
        public static let surface = "#FFFFFF"
        public static let surface2 = "#F7F5F2"
        public static let hairline = "#E8E3DD"
        public static let text = "#26170F"
        public static let dim = "#8A7C72"
        public static let outline = "#2B1B12"
    }

    /// Carried from the start per DR-6.3 but rendered NOWHERE until its G3 slice — apps force light mode
    /// at S7 (see `ColorScheme.forced` in AppShell). These values still must round-trip token-lint.
    public enum Dark {
        public static let ground = "#1E1410"
        public static let surface = "#2A1D16"
        public static let surface2 = "#35251D"
        public static let line = "#48362B"
        public static let text = "#FBF3EC"
        public static let dim = "#B3A093"
        public static let outline = "#F5EBE2"
    }

    // ---------- type scale (design/tokens.v1.json → type_scale) ----------

    public struct TypeRole: Sendable {
        public let size: CGFloat
        public let lineHeight: CGFloat
        public let weight: Font.Weight
    }

    public enum TypeScale {
        public static let displayXL = TypeRole(size: 34, lineHeight: 40, weight: .black)
        public static let display = TypeRole(size: 28, lineHeight: 34, weight: .black)
        public static let title = TypeRole(size: 22, lineHeight: 28, weight: .heavy)
        public static let heading = TypeRole(size: 17, lineHeight: 24, weight: .heavy)
        public static let body = TypeRole(size: 15, lineHeight: 22, weight: .medium)
        public static let caption = TypeRole(size: 13, lineHeight: 18, weight: .medium)
        /// Uppercase, +0.08em tracking — applied by the caller (SwiftUI has no built-in tracking modifier
        /// that reads a token, so `Components` apply `.tracking(1.1)` at call sites using this role).
        public static let microLabel = TypeRole(size: 11, lineHeight: 14, weight: .bold)
    }

    /// Neutral register: 400/500/700 only — Black 900 never appears on safety surfaces (DESIGN.md Voice).
    public enum TypeWeights {
        public static let display: Font.Weight = .black
        public static let heading: Font.Weight = .heavy
        public static let body: Font.Weight = .medium
        public static let longform: Font.Weight = .regular
        public static let neutralAllowed: Set<Font.Weight> = [.regular, .medium, .bold]
    }

    // ---------- spacing / radii / motion (design/tokens.v1.json) ----------

    public enum Spacing {
        public static let base: CGFloat = 4
        public static let scale: [CGFloat] = [4, 8, 12, 16, 24, 32, 48, 64]
        public static let xs: CGFloat = 4
        public static let sm: CGFloat = 8
        public static let md: CGFloat = 12
        public static let lg: CGFloat = 16
        public static let xl: CGFloat = 24
        public static let xxl: CGFloat = 32
        public static let xxxl: CGFloat = 48
        public static let huge: CGFloat = 64
    }

    public enum Radii {
        public static let card: CGFloat = 24
        public static let modal: CGFloat = 20
        public static let sheet: CGFloat = 20
        public static let input: CGFloat = 16
        public static let pill: CGFloat = 999
        public static let neutralMax: CGFloat = 16
    }

    public enum Outline {
        public static let playfulMin: CGFloat = 2
        public static let playfulMax: CGFloat = 3
        public static let personCard: CGFloat = 3
        public static let chip: CGFloat = 2
        public static let crewCard: CGFloat = 3
        public static let neutral: CGFloat = 0
    }

    public enum Motion {
        public static let microMs: ClosedRange<Double> = 80...120
        public static let shortMs: ClosedRange<Double> = 150...250
        public static let mediumMs: ClosedRange<Double> = 250...400
        public static let celebrationMs: ClosedRange<Double> = 400...700
    }

    // ---------- registers (design/tokens.v1.json → registers) ----------

    /// The register is a DATA switch, never a second component tree — DESIGN.md: "decoration-zero by
    /// subtraction... never a separate 'serious mode'." Every Component reads its palette through this.
    public enum Register: Sendable, CaseIterable {
        case playful
        case neutral

        public var candyAllowed: Bool { self == .playful }
        public var chocoOutlineAllowed: Bool { self == .playful }
        public var black900Allowed: Bool { self == .playful }
        public var dangerAllowed: Bool { self == .neutral }
        public var radiusMax: CGFloat { self == .playful ? Radii.card : Radii.neutralMax }
    }

    // Forbidden token groups (law 3 & 6) are enforced by ABSENCE — see StateCatalog.swift and
    // DesignKitTests/NoDisabledStateTests.swift: there is no `case disabled` anywhere in this package,
    // which is the deterministic proof, not a runtime flag that could be flipped back on.
}

// MARK: - SwiftUI conveniences (values only; still traceable back to a single hex string above)

extension Color {
    /// `Color(hex:)` for our own token strings only — never call this with a literal outside `Tokens`.
    init(tokenHex hex: String) {
        var s = hex.trimmingCharacters(in: .whitespacesAndNewlines)
        s.removeAll { $0 == "#" }
        var value: UInt64 = 0
        Scanner(string: s).scanHexInt64(&value)
        let r = Double((value & 0xFF0000) >> 16) / 255.0
        let g = Double((value & 0x00FF00) >> 8) / 255.0
        let b = Double(value & 0x0000FF) / 255.0
        self = Color(red: r, green: g, blue: b)
    }
}

extension Tokens.Candy {
    public static var bubblegumColor: Color { Color(tokenHex: bubblegum) }
    public static var skyColor: Color { Color(tokenHex: sky) }
    public static var mikanColor: Color { Color(tokenHex: mikan) }
    public static var foilColor: Color { Color(tokenHex: foil) }
    public static var chocoColor: Color { Color(tokenHex: choco) }
}

extension Tokens.Semantic {
    public static var goodColor: Color { Color(tokenHex: good) }
    public static var warnColor: Color { Color(tokenHex: warn) }
    public static var dangerColor: Color { Color(tokenHex: danger) }
}

extension Tokens.Light {
    public static var groundColor: Color { Color(tokenHex: ground) }
    public static var surfaceColor: Color { Color(tokenHex: surface) }
    public static var surface2Color: Color { Color(tokenHex: surface2) }
    public static var hairlineColor: Color { Color(tokenHex: hairline) }
    public static var textColor: Color { Color(tokenHex: text) }
    public static var dimColor: Color { Color(tokenHex: dim) }
    public static var outlineColor: Color { Color(tokenHex: outline) }
}

extension Font.Weight {
    /// M PLUS Rounded 1c ships weight-named static files (Thin/Light/Regular/Medium/Bold/ExtraBold/Black).
    fileprivate var mplusRoundedSuffix: String {
        switch self {
        case .black: return "Black"
        case .heavy: return "ExtraBold"
        case .bold: return "Bold"
        case .semibold, .medium: return "Medium"
        case .regular: return "Regular"
        case .light: return "Light"
        default: return "Regular"
        }
    }
}

extension Font {
    /// M PLUS Rounded 1c (OFL) is the app's one type family (DESIGN.md Typography — rounded terminals
    /// echo the bubble-letter mark; the display face IS the body face so kanji stay clean at every
    /// size). The actual font binary is a Julien asset-drop task (§11 dependency classification); until
    /// it lands, `Font.custom` gracefully substitutes the system font when the named face can't be
    /// found, so this compiles and renders today and upgrades automatically once the OFL files are
    /// bundled and declared in each target's Info.plist `UIAppFonts`.
    public static func token(_ role: Tokens.TypeRole) -> Font {
        .custom("MPLUSRounded1c-\(role.weight.mplusRoundedSuffix)", size: role.size)
    }
}
