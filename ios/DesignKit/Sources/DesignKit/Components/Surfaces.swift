// ios/DesignKit/Sources/DesignKit/Components/Surfaces.swift — SLICE_S7_CONTRACT.md §9a.
//
// Modal, Sheet, PersonCardFrame — DESIGN.md Component Anatomy. Fixture-driven only at S7 (no deck/DM
// surface ships this slice — §0 scope ruling); these anatomy shells are what S14/S19 mount real data
// into later without shell surgery.

import SwiftUI
import Strings

/// Radius 20, 2px hairline, title 800, body 500, stacked full-width pill buttons, quiet action last.
/// Neutral modals: no outline color, no Bubblegum (law 2).
public struct Modal<Actions: View>: View {
    private let titleKey: String
    private let bodyKey: String
    private let register: Tokens.Register
    private let actions: Actions

    public init(titleKey: String, bodyKey: String, register: Tokens.Register = .neutral, @ViewBuilder actions: () -> Actions) {
        self.titleKey = titleKey
        self.bodyKey = bodyKey
        self.register = register
        self.actions = actions()
    }

    public var body: some View {
        VStack(alignment: .leading, spacing: Tokens.Spacing.lg) {
            Text(L10n.string(titleKey))
                .font(.token(Tokens.TypeScale.title))
                .foregroundStyle(Tokens.Light.textColor)
            Text(L10n.string(bodyKey))
                .font(.token(Tokens.TypeScale.body))
                .foregroundStyle(Tokens.Light.dimColor)
            VStack(spacing: Tokens.Spacing.sm) {
                actions
            }
        }
        .padding(Tokens.Spacing.xl)
        .background(Tokens.Light.surfaceColor)
        .clipShape(RoundedRectangle(cornerRadius: Tokens.Radii.modal))
        .overlay(
            RoundedRectangle(cornerRadius: Tokens.Radii.modal)
                .strokeBorder(Tokens.Light.hairlineColor, lineWidth: 2)
        )
    }
}

/// Radius 20 top corners, grab handle, same button rules as Modal.
public struct Sheet<Content: View>: View {
    private let content: Content

    public init(@ViewBuilder content: () -> Content) {
        self.content = content()
    }

    public var body: some View {
        VStack(spacing: Tokens.Spacing.lg) {
            Capsule()
                .fill(Tokens.Light.hairlineColor)
                .frame(width: 36, height: 4)
                .padding(.top, Tokens.Spacing.sm)
                .accessibilityHidden(true)
            content
        }
        .padding(.horizontal, Tokens.Spacing.xl)
        .padding(.bottom, Tokens.Spacing.xl)
        .background(Tokens.Light.surfaceColor)
        .clipShape(
            UnevenRoundedRectangle(
                topLeadingRadius: Tokens.Radii.sheet,
                bottomLeadingRadius: 0,
                bottomTrailingRadius: 0,
                topTrailingRadius: Tokens.Radii.sheet
            )
        )
    }
}

/// Person-card frame anatomy ONLY (fixture-driven; no live deck data at S7 — §0). Full-bleed photo slot,
/// 3px Choco outline, radius 24, synergy pill top-right, status chip top-left, bottom scrim with name.
/// Gesture-equivalent button slots reserved per DR-6.1 but unwired — S14/S19's obligation.
public struct PersonCardFrame<Photo: View>: View {
    private let name: String
    private let metaLine: String?
    private let synergy: Int?
    private let statusChipLabel: String?
    private let photo: Photo

    public init(
        name: String,
        metaLine: String? = nil,
        synergy: Int? = nil,
        statusChipLabel: String? = nil,
        @ViewBuilder photo: () -> Photo
    ) {
        self.name = name
        self.metaLine = metaLine
        self.synergy = synergy
        self.statusChipLabel = statusChipLabel
        self.photo = photo()
    }

    public var body: some View {
        ZStack(alignment: .bottom) {
            photo
                .aspectRatio(3.0 / 4.0, contentMode: .fill)

            LinearGradient(
                colors: [.clear, Tokens.Candy.chocoColor.opacity(0.85)],
                startPoint: .center,
                endPoint: .bottom
            )

            VStack(alignment: .leading, spacing: 2) {
                Text(name)
                    .font(.token(Tokens.TypeScale.heading).weight(.black))
                    .foregroundStyle(.white)
                if let metaLine {
                    Text(metaLine)
                        .font(.token(Tokens.TypeScale.caption))
                        .foregroundStyle(.white.opacity(0.85))
                }
            }
            .padding(Tokens.Spacing.lg)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .overlay(alignment: .topTrailing) {
            if let synergy {
                SynergyPill(value: synergy).padding(Tokens.Spacing.sm)
            }
        }
        .overlay(alignment: .topLeading) {
            if let statusChipLabel {
                Chip(label: statusChipLabel, register: .playful).padding(Tokens.Spacing.sm)
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: Tokens.Radii.card))
        .overlay(
            RoundedRectangle(cornerRadius: Tokens.Radii.card)
                .strokeBorder(Tokens.Candy.chocoColor, lineWidth: Tokens.Outline.personCard)
        )
    }
}

/// Crew card anatomy: Foil 3px border + 9% foil-tint gradient, permanent, never expires (law 6: "time
/// only adds value" — there is no decay/expiry rendering path anywhere in this type).
public struct CrewCardFrame: View {
    private let name: String
    private let tenureLine: String
    private let originLine: String

    public init(name: String, tenureLine: String, originLine: String) {
        self.name = name
        self.tenureLine = tenureLine
        self.originLine = originLine
    }

    public var body: some View {
        VStack(alignment: .leading, spacing: Tokens.Spacing.sm) {
            Text(name)
                .font(.token(Tokens.TypeScale.heading).weight(.black))
                .foregroundStyle(Tokens.Light.textColor)
            Text(tenureLine)
                .font(.token(Tokens.TypeScale.caption).weight(.heavy))
                .foregroundStyle(Tokens.Candy.foilColor)
            Text(originLine)
                .font(.token(Tokens.TypeScale.caption))
                .italic()
                .foregroundStyle(Tokens.Light.dimColor)
                .padding(.leading, Tokens.Spacing.sm)
                .overlay(alignment: .leading) {
                    Rectangle().fill(Tokens.Candy.foilColor).frame(width: 2)
                }
        }
        .padding(Tokens.Spacing.lg)
        .background(
            LinearGradient(colors: [Tokens.Candy.foilColor.opacity(0.09), .clear], startPoint: .top, endPoint: .bottom)
        )
        .background(Tokens.Light.surfaceColor)
        .clipShape(RoundedRectangle(cornerRadius: Tokens.Radii.card))
        .overlay(
            RoundedRectangle(cornerRadius: Tokens.Radii.card)
                .strokeBorder(Tokens.Candy.foilColor, lineWidth: Tokens.Outline.crewCard)
        )
    }
}
