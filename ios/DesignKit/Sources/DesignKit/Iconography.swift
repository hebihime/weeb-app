// ios/DesignKit/Sources/DesignKit/Iconography.swift — SLICE_S7_CONTRACT.md §9a, Correction 2.
//
// ONE Iconography layer. Call sites never write a glyph literal (no `Image(systemName:)`, no raw FA
// class string) — they ask for a `Glyph` in a `Tokens.Register` and this layer decides what actually
// renders. Font Awesome Pro is the ratified icon set (DESIGN.md Iconography). The Pro .otf are a
// licensed asset that is NOT committed (public repo — see .gitignore + the Fonts/ pointer READMEs), so
// they are ABSENT on CI. The layer therefore resolves BOTH backends behind the same seam:
//
//   • FontAwesomeIconography — maps every Glyph to its canonical FA codepoint from the bundled
//     fa-glyph-map.json (DATA, always committed). It ALWAYS resolves; it does NOT gate on font presence.
//   • BundledFallbackIconography — SF Symbols (a system framework: zero license, zero egress, always
//     present). The fallback when the FA Pro font files are not registered.
//
// `IconographyProvider.current` picks between them ONCE at load, by runtime font-presence detection
// (`faFontIsAvailable()`). Fonts present locally -> FA path active. Fonts absent on CI -> SF fallback,
// build stays green. Swapping the backend is a DI decision in this one file, never a call-site change.

import SwiftUI
import CoreText

#if canImport(UIKit)
import UIKit
#elseif canImport(AppKit)
import AppKit
#endif

/// Every glyph this slice's shipped screens need. Adding a glyph the kit doesn't render yet is a
/// compile error at the call site (no stringly-typed lookup), which is the point of the seam.
public enum Glyph: Sendable, CaseIterable {
    case tabConnect
    case tabExplore
    case tabCrews
    case tabInbox
    case tabProfile
    case emptyDeck
    case emptyExplore
    case emptyCrews
    case emptyInbox
    case emptyProfile
    case gatePending
    case presenceFallback
    case battlePause
    case connectivityOffline
    case contractMismatch
    case dignityShield
    case preFirstCon
    case signupHandle
    case signupEmail
    case signupBirthdate
    case signupAvatar
    case signupFandom
    case limitReached
    case problemGeneric
    case couldNotSend
    case chevronForward
    case checkmark
}

/// The register controls FA style selection (Solid for playful, Regular for neutral, Duotone for empty
/// states + celebrations — DESIGN.md Iconography). The bundled SF Symbol fallback approximates this with
/// rendering mode rather than a distinct glyph, since SF Symbols doesn't ship Solid/Regular/Duotone as
/// separate assets the way FA Pro does. Duotone (empty/celebration) needs a two-layer render and is
/// deferred; until then empty/celebration uses the Solid face at the mapped codepoint.
public enum IconStyle: Sendable {
    case playful
    case neutral
    case emptyOrCelebration
}

/// What a source resolved a glyph to. Either an SF Symbol name (the always-present fallback) or a Font
/// Awesome text glyph: the PostScript font name to render in plus the Unicode scalar to draw.
public enum ResolvedGlyph: Sendable, Equatable {
    case sfSymbol(String)
    case faText(postScriptName: String, scalar: UInt32)
}

/// The buy seam: a source maps a `Glyph` + `IconStyle` to a `ResolvedGlyph`. Two implementations ship
/// (SF Symbols + Font Awesome Pro); `IconographyProvider` picks which is live by font presence.
public protocol IconographySource: Sendable {
    func resolve(_ glyph: Glyph, style: IconStyle) -> ResolvedGlyph
}

// MARK: - SF Symbols fallback (always present)

public struct BundledFallbackIconography: IconographySource {
    public init() {}

    public func resolve(_ glyph: Glyph, style: IconStyle) -> ResolvedGlyph {
        .sfSymbol(symbolName(for: glyph))
    }

    private func symbolName(for glyph: Glyph) -> String {
        switch glyph {
        case .tabConnect: return "person.2.fill"
        case .tabExplore: return "safari.fill"
        case .tabCrews: return "person.3.fill"
        case .tabInbox: return "tray.fill"
        case .tabProfile: return "person.crop.circle.fill"
        case .emptyDeck: return "rectangle.stack.badge.person.crop"
        case .emptyExplore: return "binoculars.fill"
        case .emptyCrews: return "person.3.sequence.fill"
        case .emptyInbox: return "tray"
        case .emptyProfile: return "person.crop.circle.badge.questionmark"
        case .gatePending: return "clock.badge.checkmark"
        case .presenceFallback: return "wifi.exclamationmark"
        case .battlePause: return "pause.circle.fill"
        case .connectivityOffline: return "wifi.slash"
        case .contractMismatch: return "arrow.triangle.2.circlepath"
        case .dignityShield: return "hand.raised.fill"
        case .preFirstCon: return "ticket.fill"
        case .signupHandle: return "at"
        case .signupEmail: return "envelope.fill"
        case .signupBirthdate: return "calendar"
        case .signupAvatar: return "photo.badge.plus"
        case .signupFandom: return "tag.fill"
        case .limitReached: return "hourglass"
        case .problemGeneric: return "exclamationmark.triangle.fill"
        case .couldNotSend: return "paperplane.slash"
        case .chevronForward: return "chevron.forward"
        case .checkmark: return "checkmark.circle.fill"
        }
    }
}

// MARK: - Font Awesome Pro (codepoint table from bundled fa-glyph-map.json)

/// The canonical FA codepoint map, parsed ONCE from the bundled `fa-glyph-map.json` resource. Immutable
/// after parse, so it is trivially concurrency-safe as a `static let`. This source ALWAYS maps a
/// codepoint (the JSON is DATA and always committed); it does NOT gate on whether the .otf are present —
/// that gate is `IconographyProvider`'s job via `faFontIsAvailable()`.
public struct FontAwesomeIconography: IconographySource {
    public init() {}

    struct Table: Sendable {
        let playfulPostScriptName: String
        let neutralPostScriptName: String
        let scalars: [String: UInt32] // camelCase Glyph key -> Unicode scalar
    }

    private struct RawMap: Decodable {
        struct Face: Decodable { let postScriptName: String }
        struct Faces: Decodable { let playful: Face; let neutral: Face }
        struct Entry: Decodable { let fa: String; let unicode: String }
        let faces: Faces
        let glyphs: [String: Entry]
    }

    static let table: Table = {
        guard let url = Bundle.module.url(forResource: "fa-glyph-map", withExtension: "json"),
              let data = try? Data(contentsOf: url) else {
            fatalError("fa-glyph-map.json missing from Bundle.module — it is committed DATA and must always be present")
        }
        let raw: RawMap
        do {
            raw = try JSONDecoder().decode(RawMap.self, from: data)
        } catch {
            fatalError("fa-glyph-map.json failed to decode: \(error)")
        }
        var scalars: [String: UInt32] = [:]
        for (key, entry) in raw.glyphs {
            guard let scalar = UInt32(entry.unicode, radix: 16) else {
                fatalError("fa-glyph-map.json glyph \(key) has non-hex unicode '\(entry.unicode)'")
            }
            scalars[key] = scalar
        }
        return Table(
            playfulPostScriptName: raw.faces.playful.postScriptName,
            neutralPostScriptName: raw.faces.neutral.postScriptName,
            scalars: scalars
        )
    }()

    /// The camelCase key this glyph uses in fa-glyph-map.json (matches the enum case name exactly).
    static func key(for glyph: Glyph) -> String { String(describing: glyph) }

    public func resolve(_ glyph: Glyph, style: IconStyle) -> ResolvedGlyph {
        let table = Self.table
        // Duotone deferred: empty/celebration renders in the Solid (playful) face for now.
        let postScriptName: String
        switch style {
        case .playful, .emptyOrCelebration:
            postScriptName = table.playfulPostScriptName
        case .neutral:
            postScriptName = table.neutralPostScriptName
        }
        guard let scalar = table.scalars[Self.key(for: glyph)] else {
            // fa-map-lint keeps the JSON keys in exact sync with Glyph, so this is unreachable in a
            // correct build; a missing key is a lint/build failure, not a silent no-op.
            fatalError("fa-glyph-map.json has no codepoint for glyph \(glyph)")
        }
        return .faText(postScriptName: postScriptName, scalar: scalar)
    }
}

// MARK: - Font registration + runtime presence detection

/// Registers the bundled FA Pro .otf with Core Text EXACTLY ONCE. Registering an absent file simply
/// fails (returns false), which is the intended CI behavior — the fonts are gitignored, so the fallback
/// path takes over. The static `let` closure runs at most once and is concurrency-safe.
enum FAFontRegistrar {
    static let didAttemptRegistration: Bool = {
        for resource in ["FontAwesome7Pro-Solid-900", "FontAwesome7Pro-Regular-400"] {
            if let url = Bundle.module.url(forResource: resource, withExtension: "otf") {
                CTFontManagerRegisterFontsForURL(url as CFURL, .process, nil)
            }
        }
        return true
    }()
}

/// True only if BOTH FA Pro faces are registered and resolvable by PostScript name. Attempts
/// registration first (once). Returns false on CI where the .otf are absent — the caller then chooses
/// the SF Symbols fallback.
public func faFontIsAvailable() -> Bool {
    _ = FAFontRegistrar.didAttemptRegistration
    return fontIsResolvable("FontAwesome7Pro-Solid") && fontIsResolvable("FontAwesome7Pro-Regular")
}

private func fontIsResolvable(_ postScriptName: String) -> Bool {
    #if canImport(UIKit)
    return UIFont(name: postScriptName, size: 1) != nil
    #elseif canImport(AppKit)
    return NSFont(name: postScriptName, size: 1) != nil
    #else
    return false
    #endif
}

// MARK: - Provider selection

public enum IconographyProvider {
    /// Pure, testable chooser: FA when the font is available, SF Symbols otherwise. Injecting the bool
    /// keeps tests environment-independent (fonts present locally, absent on CI).
    public static func chooseSource(faAvailable: Bool) -> any IconographySource {
        faAvailable ? FontAwesomeIconography() : BundledFallbackIconography()
    }

    /// The live source, decided ONCE at first access by runtime font-presence detection.
    public static let current: any IconographySource = chooseSource(faAvailable: faFontIsAvailable())
}

// MARK: - View

public struct IconView: View {
    private let glyph: Glyph
    private let style: IconStyle
    private let size: CGFloat
    private let source: any IconographySource

    public init(
        _ glyph: Glyph,
        style: IconStyle = .neutral,
        size: CGFloat = 20,
        source: any IconographySource = IconographyProvider.current
    ) {
        self.glyph = glyph
        self.style = style
        self.size = size
        self.source = source
    }

    public var body: some View {
        content
            .accessibilityHidden(true) // decorative; the surrounding control carries the a11y label
            .frame(minWidth: Tokens.iconMinTouchTarget, minHeight: Tokens.iconMinTouchTarget)
    }

    @ViewBuilder
    private var content: some View {
        switch source.resolve(glyph, style: style) {
        case let .sfSymbol(name):
            Image(systemName: name)
                .font(.system(size: size, weight: style == .playful ? .bold : .regular))
                .symbolRenderingMode(style == .emptyOrCelebration ? .hierarchical : .monochrome)
        case let .faText(postScriptName, scalar):
            // `.custom(_:fixedSize:)` pins the glyph to the icon point size (no unexpected Dynamic Type
            // scaling). Foreground color is inherited from context (DESIGN.md: icons take the text color).
            Text(String(UnicodeScalar(scalar)!))
                .font(.custom(postScriptName, fixedSize: size))
        }
    }
}

extension Tokens {
    /// DESIGN.md Iconography: 44×44pt minimum touch target.
    public static let iconMinTouchTarget: CGFloat = 44
    public static let iconSizes: [CGFloat] = [16, 20, 24]
}
