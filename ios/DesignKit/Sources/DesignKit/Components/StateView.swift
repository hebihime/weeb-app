// ios/DesignKit/Sources/DesignKit/Components/StateView.swift — SLICE_S7_CONTRACT.md §9d.
//
// ONE component renders every one of the 23 (see StateCatalog.swift's Phase-1 checkpoint note) states:
// illustration slot [FA duotone / bundled fallback], title 800, body 500, single CTA slot. No per-state
// bespoke view exists to drift from DESIGN.md — only data (a StateSpec) varies.

import SwiftUI
import Strings

public struct StateView: View {
    private let spec: StateSpec
    private let accessibilityID: String?
    private let locale: Locale?
    private let ctaAction: (() -> Void)?

    /// `locale: nil` (the default — how the app mounts every state) lets L10n resolve against its
    /// effective default locale, so a DEBUG harness locale override reaches these strings too. The
    /// snapshot suite passes an explicit locale to force each of the four.
    public init(
        spec: StateSpec,
        accessibilityID: String? = nil,
        locale: Locale? = nil,
        ctaAction: (() -> Void)? = nil
    ) {
        self.spec = spec
        self.accessibilityID = accessibilityID
        self.locale = locale
        self.ctaAction = ctaAction
    }

    public var body: some View {
        VStack(spacing: Tokens.Spacing.lg) {
            IconView(spec.glyph, style: .emptyOrCelebration, size: 48)
                .foregroundStyle(spec.register.candyAllowed ? Tokens.Candy.skyColor : Tokens.Light.dimColor)

            // The state's accessibilityIdentifier lives on the TITLE (a single leaf element), NOT on the
            // enclosing VStack. Applying it to the container propagated it down and OVERRODE every child's
            // own identifier — the CTA button ended up with id "state.crews.empty" instead of
            // "crews.create.premium.cta", so Maestro's "scroll until crews.create.premium.cta visible"
            // could never find it (confirmed via an XCUITest accessibility-hierarchy dump). Putting the
            // id on one leaf keeps `state.<name>.empty` present-and-visible for the shell assertions while
            // leaving the CTA button free to carry its own distinct id.
            Text(L10n.string(spec.titleKey, locale: locale))
                .font(.token(Tokens.TypeScale.heading))
                .foregroundStyle(spec.register.black900Allowed ? Tokens.Light.textColor : Tokens.Light.textColor)
                .multilineTextAlignment(.center)
                .modifier(AccessibilityIdentifierIfPresent(id: accessibilityID))

            Text(L10n.string(spec.bodyKey, locale: locale))
                .font(.token(Tokens.TypeScale.body))
                .foregroundStyle(Tokens.Light.dimColor)
                .multilineTextAlignment(.center)
                .frame(maxWidth: 320) // DR-6.2: +30% ES headroom lives in the container, not a fixed cap

            if let ctaKey = spec.ctaKey {
                Button(action: { ctaAction?() }) {
                    Text(L10n.string(ctaKey, locale: locale))
                        .font(.token(Tokens.TypeScale.heading))
                }
                .buttonStyle(PillButtonStyle(register: spec.register))
                .accessibilityIdentifier(ctaKey == "crews.create.premium.cta" ? "crews.create.premium.cta" : accessibilityID.map { "\($0).cta" } ?? "")
            }
        }
        .padding(Tokens.Spacing.xl)
        .frame(maxWidth: .infinity)
    }
}

/// SwiftUI has no conditional-modifier-with-no-op-else that keeps the view identity stable across an
/// `Optional` without this wrapper; keeping it out of call sites keeps StateView itself readable.
private struct AccessibilityIdentifierIfPresent: ViewModifier {
    let id: String?
    func body(content: Content) -> some View {
        if let id {
            content.accessibilityIdentifier(id)
        } else {
            content
        }
    }
}

/// Pill button, stacked full-width in modals per DESIGN.md; candy fill in playful, quiet neutral outline
/// in neutral register (law 2: danger/candy never mix). Absence, not disablement (law 3) — this style has
/// no disabled variant; a button that shouldn't act yet simply isn't rendered.
public struct PillButtonStyle: ButtonStyle {
    let register: Tokens.Register

    public init(register: Tokens.Register) {
        self.register = register
    }

    public func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .padding(.vertical, Tokens.Spacing.md)
            .padding(.horizontal, Tokens.Spacing.xl)
            .frame(minHeight: Tokens.iconMinTouchTarget)
            .background(fillColor.opacity(configuration.isPressed ? 0.85 : 1))
            .foregroundStyle(foregroundColor)
            .clipShape(RoundedRectangle(cornerRadius: Tokens.Radii.pill))
            .overlay(
                RoundedRectangle(cornerRadius: Tokens.Radii.pill)
                    .strokeBorder(register.chocoOutlineAllowed ? Tokens.Candy.chocoColor : .clear, lineWidth: Tokens.Outline.chip)
            )
    }

    private var fillColor: Color {
        // Primary actions render in the flavor's brand.primary (§1c) — Weeb's pink, Friki's tangerine —
        // never the fixed Bubblegum candy token, which is shared/unbranded (BrandColor.swift's header).
        register == .playful ? BrandColor.primary : Tokens.Light.surface2Color
    }

    private var foregroundColor: Color {
        register == .playful ? .white : Tokens.Light.textColor
    }
}
