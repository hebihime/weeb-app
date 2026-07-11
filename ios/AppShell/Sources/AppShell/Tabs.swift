// ios/AppShell/Sources/AppShell/Tabs.swift — SLICE_S7_CONTRACT.md §9b, maestro/README.md's a11y contract.
//
// Fixed five tabs: Connect · Explore · Crews · Inbox · Profile. Quests tab does NOT exist (pre-G4
// absence — no "coming soon" placeholder, no disabled tab item, nothing). Each tab renders its designed
// honest zero-data state (L6: never fabricated data) — the accessibility identifiers below are the exact
// strings maestro/flows/brand-smoke/subflows/assert-shell.yaml asserts against.

import SwiftUI
import DesignKit
import Strings

public enum AppTab: String, CaseIterable, Identifiable, Sendable {
    case connect, explore, crews, inbox, profile

    public var id: String { rawValue }

    var accessibilityID: String { "tab.\(rawValue)" }

    var labelKey: String {
        switch self {
        case .connect: return "tab.connect.label"
        case .explore: return "tab.explore.label"
        case .crews: return "tab.crews.label"
        case .inbox: return "tab.inbox.label"
        case .profile: return "tab.profile.label"
        }
    }

    var glyph: Glyph {
        switch self {
        case .connect: return .tabConnect
        case .explore: return .tabExplore
        case .crews: return .tabCrews
        case .inbox: return .tabInbox
        case .profile: return .tabProfile
        }
    }

    /// The catalog id of this tab's honest zero-data state (StateCatalog.swift's `tabEmpty` family).
    var emptyStateID: String { "\(rawValue).empty" }

    /// The exact a11y identifier maestro/README.md's contract mandates for this tab's empty state —
    /// distinct from the DesignKit-internal catalog id above.
    var emptyStateAccessibilityID: String { "state.\(rawValue).empty" }
}

/// One tab's content: the honest empty state, full stop. No live data renders in any tab at S7 (§0).
public struct TabContentView: View {
    private let tab: AppTab

    public init(tab: AppTab) {
        self.tab = tab
    }

    public var body: some View {
        NavigationStack {
            ScrollView {
                if let spec = StateCatalog.spec(id: tab.emptyStateID) {
                    StateView(spec: spec, accessibilityID: tab.emptyStateAccessibilityID)
                        .padding(.top, Tokens.Spacing.xxl)
                }
                #if DEBUG
                if tab == .profile {
                    // Debug-only entry points, never compiled into release (§1d/§9d). Deliberately NOT a
                    // sixth tab — the fixed five-tab law (§9b) stays visually true in debug builds too;
                    // these are just extra links inside the one tab that already exists.
                    VStack(spacing: Tokens.Spacing.md) {
                        NavigationLink("Debug Diagnostics") { DebugDiagnosticsView() }
                        NavigationLink("Debug State Gallery") { DebugStateGalleryView() }
                    }
                    .padding(.top, Tokens.Spacing.xxl)
                }
                #endif
            }
            .navigationTitle(L10n.string(tab.labelKey))
            .background(Tokens.Light.groundColor)
        }
    }
}

/// The five-tab shell + the brand mark chrome + the mode chip (never visible at S7 — ModeContext.boot()
/// only ever returns `.online`). This is the ONE shared shell layout the trunk test runs against on
/// every screen (§9b).
///
/// A CUSTOM bottom tab bar (an HStack of five Buttons), NOT SwiftUI's `TabView`. SwiftUI does not
/// propagate a `TabView` content view's `.accessibilityIdentifier` onto the system-drawn tab-bar button,
/// and only the selected tab's content is in the accessibility hierarchy — so a `TabView` exposes at
/// most one `tab.*` id at a time, which breaks maestro/flows/brand-smoke/subflows/assert-shell.yaml's
/// requirement that all five `tab.*` ids be visible SIMULTANEOUSLY and each be tappable. With a custom
/// bar every `tab.<name>` button is always in the tree; tapping one flips `selectedTab`, and the content
/// area shows exactly that tab's `state.<name>.empty`. (Fixes the CI Maestro failure
/// "id: tab.explore is visible failed".)
public struct RootView: View {
    @State private var selectedTab: AppTab = .connect
    @State private var isSignupPresented = false
    private let mode: ModeContext
    private let wordmarkDisplayName: String

    public init(mode: ModeContext = .boot(), wordmarkDisplayName: String) {
        self.mode = mode
        self.wordmarkDisplayName = wordmarkDisplayName
    }

    public var body: some View {
        // The chrome (BrandBar) and the persistent tab bar are attached as safe-area INSETS, not VStack
        // siblings. That is load-bearing: a plain VStack sibling abuts the content visually but leaves the
        // inner ScrollView's content inset unchanged, so the last scrollable item (the crews
        // `crews.create.premium.cta`) can never scroll fully clear of the bar — it stays partly obscured,
        // which failed the Maestro "scroll until 100% visible" assertion. `.safeAreaInset` reserves the
        // space AND propagates a matching bottom content inset down into the ScrollView, so every tab's
        // content — the crews secondary CTA included — scrolls to 100% visible above the bar.
        // `.id(selectedTab)` gives each tab its own NavigationStack identity so switching is a clean swap.
        TabContentView(tab: selectedTab)
            .id(selectedTab)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .safeAreaInset(edge: .top, spacing: 0) {
                BrandBar(wordmarkDisplayName: wordmarkDisplayName, mode: mode, isSignupPresented: $isSignupPresented)
            }
            .safeAreaInset(edge: .bottom, spacing: 0) {
                AppTabBar(selectedTab: $selectedTab)
            }
            .background(Tokens.Light.groundColor)
            // Force light mode at S7 (DR-6.3: Choco dark tokens carried but rendered nowhere yet).
            .preferredColorScheme(.light)
            .signupPresentation(isPresented: $isSignupPresented) {
                SignupHost(isPresented: $isSignupPresented)
            }
    }
}

/// The custom bottom tab bar. Every `tab.<name>` button is ALWAYS present and tappable (the property the
/// Maestro shell smoke depends on) — Quests is simply not one of `AppTab.allCases`, so `tab.quests`
/// never exists (the five-tab law by absence, §9b). Each button carries its `tab.<name>` id AND its
/// localized label as the a11y label (VoiceOver/TalkBack baseline, DR-6.1).
struct AppTabBar: View {
    @Binding var selectedTab: AppTab

    var body: some View {
        HStack(spacing: 0) {
            ForEach(AppTab.allCases) { tab in
                Button {
                    selectedTab = tab
                } label: {
                    VStack(spacing: 2) {
                        IconView(tab.glyph, style: .playful, size: 20)
                        Text(L10n.string(tab.labelKey))
                            .font(.token(Tokens.TypeScale.microLabel))
                    }
                    .frame(maxWidth: .infinity, minHeight: Tokens.iconMinTouchTarget)
                    .foregroundStyle(selectedTab == tab ? BrandColor.primary : Tokens.Light.dimColor)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityIdentifier(tab.accessibilityID)
                .accessibilityLabel(L10n.string(tab.labelKey))
                .accessibilityAddTraits(selectedTab == tab ? [.isButton, .isSelected] : .isButton)
            }
        }
        .padding(.vertical, Tokens.Spacing.sm)
        .background(Tokens.Light.surfaceColor)
        .overlay(alignment: .top) {
            Rectangle()
                .fill(Tokens.Light.hairlineColor)
                .frame(height: 1)
        }
    }
}

extension View {
    /// `fullScreenCover` doesn't exist on macOS — this package declares macOS as a platform only so
    /// `swift test` runs locally (see Package.swift); the shipping target is iOS-only, where the real
    /// full-screen cover applies.
    @ViewBuilder
    fileprivate func signupPresentation<Content: View>(
        isPresented: Binding<Bool>,
        @ViewBuilder content: @escaping () -> Content
    ) -> some View {
        #if os(iOS)
        self.fullScreenCover(isPresented: isPresented, content: content)
        #else
        self.sheet(isPresented: isPresented, content: content)
        #endif
    }
}

/// Brand mark + active-tab chrome (always visible, every tab — the §9b trunk test) + the mode chip
/// (never visible at S7 — `mode.chip` must NOT exist as a visible element per maestro/README.md; this
/// view only renders it when `mode.showsModeChip` is true, which `.boot()` never produces) + the
/// `signup.start` entry point. It lives here, not inside any one tab's content, precisely because it
/// must be reachable from whichever tab the trunk-test walk left the app on (assert-shell.yaml ends on
/// Profile before signup-walk.yaml taps `signup.start`).
struct BrandBar: View {
    let wordmarkDisplayName: String
    let mode: ModeContext
    @Binding var isSignupPresented: Bool

    var body: some View {
        HStack {
            Text(wordmarkDisplayName)
                .font(.token(Tokens.TypeScale.heading).weight(.black))
                .foregroundStyle(BrandColor.primary)
                .accessibilityIdentifier("brand.wordmark")
                .accessibilityLabel(wordmarkDisplayName)
            Spacer()
            if mode.showsModeChip {
                ModeChip(mode: mode)
            }
            Button(action: { isSignupPresented = true }) {
                Text(Strings.L10n.string("signup.start.cta"))
                    .font(.token(Tokens.TypeScale.caption).weight(.bold))
            }
            .buttonStyle(PillButtonStyle(register: .playful))
            .accessibilityIdentifier("signup.start")
        }
        .padding(.horizontal, Tokens.Spacing.lg)
        .padding(.vertical, Tokens.Spacing.sm)
        .background(Tokens.Light.groundColor)
    }
}

struct ModeChip: View {
    let mode: ModeContext

    var body: some View {
        // Unreachable at S7 (ModeContext.boot() only returns .online), kept for S19/S34 to fill in real
        // copy without a nav rewrite. Never assign `mode.chip` as an accessibilityIdentifier anywhere
        // else in this file — this is its one and only home.
        Chip(label: label, register: .neutral)
            .accessibilityIdentifier("mode.chip")
    }

    private var label: String {
        switch mode {
        case .online: return "" // unreachable — showsModeChip is false for .online
        case .conPresent: return "Con"
        case .nakama: return "Nakama"
        }
    }
}
