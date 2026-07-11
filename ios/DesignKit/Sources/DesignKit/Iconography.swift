// ios/DesignKit/Sources/DesignKit/Iconography.swift — SLICE_S7_CONTRACT.md §9a, Correction 2.
//
// ONE Iconography layer. Call sites never write a glyph literal (no `Image(systemName:)`, no raw FA
// class string) — they ask for a `Glyph` in a `Tokens.Register` and this layer decides what actually
// renders. Font Awesome Pro is the ratified icon set (DESIGN.md Iconography) but it is a licensed asset
// that may not be in-repo; Correction 2 requires the kit to ship a BUNDLED FALLBACK glyph source so
// `xcodebuild test` / Maestro go green without the Pro kit. SF Symbols (an iOS system framework, zero
// license, zero egress, always present) is that bundled fallback — the buy seam means swapping in real
// FA Pro assets later is a resource+DI change in this one file, never a call-site change.

import SwiftUI

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
/// separate assets the way FA Pro does; swapping to real FA Pro restores the exact style split.
public enum IconStyle: Sendable {
    case playful
    case neutral
    case emptyOrCelebration
}

/// The buy seam: swap this implementation (or inject a different one) to move from the bundled SF
/// Symbol fallback to the real Font Awesome Pro kit once Julien drops the license files in. No other
/// file in the app needs to change.
public protocol IconographySource: Sendable {
    func systemName(for glyph: Glyph) -> String
}

public struct BundledFallbackIconography: IconographySource {
    public init() {}

    public func systemName(for glyph: Glyph) -> String {
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

public struct IconView: View {
    private let glyph: Glyph
    private let style: IconStyle
    private let size: CGFloat
    private let source: any IconographySource

    public init(
        _ glyph: Glyph,
        style: IconStyle = .neutral,
        size: CGFloat = 20,
        source: any IconographySource = BundledFallbackIconography()
    ) {
        self.glyph = glyph
        self.style = style
        self.size = size
        self.source = source
    }

    public var body: some View {
        Image(systemName: source.systemName(for: glyph))
            .font(.system(size: size, weight: style == .playful ? .bold : .regular))
            .symbolRenderingMode(style == .emptyOrCelebration ? .hierarchical : .monochrome)
            .accessibilityHidden(true) // decorative; the surrounding control carries the a11y label
            .frame(minWidth: Tokens.iconMinTouchTarget, minHeight: Tokens.iconMinTouchTarget)
    }
}

extension Tokens {
    /// DESIGN.md Iconography: 44×44pt minimum touch target.
    public static let iconMinTouchTarget: CGFloat = 44
    public static let iconSizes: [CGFloat] = [16, 20, 24]
}
