// ios/DesignKit/Sources/DesignKit/Components/Primitives.swift — SLICE_S7_CONTRACT.md §9a.
//
// Chip, Badge, Row — DESIGN.md Component Anatomy. Fixture-driven only at S7 (no live data anywhere in
// the shipped screens — L6); these are exercised by the debug gallery + snapshot tests so later slices
// mount real data into an already-reviewed shape instead of inventing anatomy under deadline.
//
// Absence, not disablement (law 3): none of these types has a `disabled`/`locked`/`grayed` case or
// parameter anywhere. A chip that cannot be selected yet is simply not shown.

import SwiftUI

/// Pill, 2px outline (Choco in playful, hairline in neutral), 700 text. Selected = filled candy.
public struct Chip: View {
    private let label: String
    private let isSelected: Bool
    private let register: Tokens.Register

    public init(label: String, isSelected: Bool = false, register: Tokens.Register = .playful) {
        self.label = label
        self.isSelected = isSelected
        self.register = register
    }

    public var body: some View {
        Text(label)
            .font(.token(Tokens.TypeScale.caption).weight(.bold))
            .padding(.horizontal, Tokens.Spacing.md)
            .padding(.vertical, Tokens.Spacing.sm)
            .frame(minHeight: 32)
            .background(isSelected && register.candyAllowed ? Tokens.Candy.bubblegumColor : Tokens.Light.surfaceColor)
            .foregroundStyle(isSelected && register.candyAllowed ? .white : Tokens.Light.textColor)
            .clipShape(Capsule())
            .overlay(
                Capsule().strokeBorder(
                    register.chocoOutlineAllowed ? Tokens.Candy.chocoColor : Tokens.Light.hairlineColor,
                    lineWidth: register.chocoOutlineAllowed ? Tokens.Outline.chip : 1
                )
            )
            // Chips wrap, never truncate mid-word (DR-6.2) — enforced at the layout call site
            // (`WrappingChipRow`), this view itself never clips its own text.
            .fixedSize(horizontal: false, vertical: true)
    }
}

/// Lays out chips left-to-right, wrapping to a new line rather than truncating (DR-6.2).
public struct WrappingChipRow<Data: RandomAccessCollection, ID: Hashable>: View where Data.Element: Identifiable, Data.Element.ID == ID {
    private let data: Data
    private let content: (Data.Element) -> Chip

    public init(_ data: Data, @ViewBuilder content: @escaping (Data.Element) -> Chip) {
        self.data = data
        self.content = content
    }

    public var body: some View {
        // A simple flow layout via wrapping HStacks in a LazyVGrid-free approach keeps this dependency-free;
        // adaptive grid columns naturally wrap without mid-word truncation.
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 64), spacing: Tokens.Spacing.sm)], alignment: .leading, spacing: Tokens.Spacing.sm) {
            ForEach(data) { item in
                content(item)
            }
        }
    }
}

/// Pill, micro-label type; Foil variant for earned only (law 1: never on a person's card).
public struct Badge: View {
    public enum Style: Sendable {
        case neutral
        case foilEarned
    }

    private let label: String
    private let style: Style

    public init(label: String, style: Style = .neutral) {
        self.label = label
        self.style = style
    }

    public var body: some View {
        Text(label.uppercased())
            .font(.token(Tokens.TypeScale.microLabel))
            .tracking(1.1)
            .padding(.horizontal, Tokens.Spacing.sm)
            .padding(.vertical, 4)
            .background(style == .foilEarned ? Tokens.Candy.foilColor.opacity(0.16) : Tokens.Light.surface2Color)
            .foregroundStyle(style == .foilEarned ? Tokens.Candy.foilColor : Tokens.Light.dimColor)
            .clipShape(Capsule())
    }
}

/// 56px min row: leading icon (FA Regular) optional, title 500 + caption, trailing chevron/value.
public struct Row: View {
    private let glyph: Glyph?
    private let title: String
    private let caption: String?
    private let showsChevron: Bool

    public init(glyph: Glyph? = nil, title: String, caption: String? = nil, showsChevron: Bool = false) {
        self.glyph = glyph
        self.title = title
        self.caption = caption
        self.showsChevron = showsChevron
    }

    public var body: some View {
        HStack(spacing: Tokens.Spacing.md) {
            if let glyph {
                IconView(glyph, style: .neutral, size: 20)
            }
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(.token(Tokens.TypeScale.body))
                    .foregroundStyle(Tokens.Light.textColor)
                if let caption {
                    Text(caption)
                        .font(.token(Tokens.TypeScale.caption))
                        .foregroundStyle(Tokens.Light.dimColor)
                }
            }
            Spacer()
            if showsChevron {
                IconView(.chevronForward, style: .neutral, size: 16)
            }
        }
        .padding(.horizontal, Tokens.Spacing.lg)
        .frame(minHeight: 56)
    }
}

/// The synergy pill — white surface, Choco outline, Sky numeral. Render-absent below the synergy band
/// (law: no token exists to render a "disabled" pill; the caller simply omits this view entirely).
public struct SynergyPill: View {
    private let value: Int

    public init(value: Int) {
        self.value = value
    }

    public var body: some View {
        // A locale-aware number format, not a hardcoded string literal — DESIGN.md's tabular-nums
        // numeral treatment; `Text(_:format:)` also sidesteps the hardcoded-string lint's `Text("...")`
        // pattern entirely, correctly, since this was never translatable text in the first place.
        Text(value, format: .number)
            .font(.token(Tokens.TypeScale.caption).weight(.heavy))
            .monospacedDigit()
            .foregroundStyle(Tokens.Candy.skyColor)
            .padding(.horizontal, Tokens.Spacing.sm)
            .padding(.vertical, 4)
            .background(Tokens.Light.surfaceColor)
            .clipShape(Capsule())
            .overlay(Capsule().strokeBorder(Tokens.Candy.chocoColor, lineWidth: Tokens.Outline.chip))
    }
}
