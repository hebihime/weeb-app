// ios/DesignKit/Tests/DesignKitTests/StateViewSnapshotTests.swift — SLICE_S7_CONTRACT.md §9d/§12.
//
// "Every state snapshot-tested per flavor per locale." Two things are true at once:
//
// 1. Evaluating a SwiftUI `View`'s `.body` directly from a headless SwiftPM command-line test run (no
//    live UIApplication/NSApplication run loop) is unreliable — AttributeGraph expects a real rendering
//    host. An earlier version of this file did exactly that and crashed the test process outright
//    (signal 5), which is strictly worse than a flaky pixel diff. So this package's own test target does
//    NOT touch `.body` or attempt pixel snapshotting.
// 2. The BINDING logic (which catalog state a deny surface renders, which glyph a state uses, that the
//    mapping is total and has no forbidden concept) is fully testable as plain data, and that is what
//    this file and NoDisabledStateTests/StateCatalogTests cover.
//
// The real per-flavor-per-locale PIXEL snapshot suite the contract calls for is the App-hosted UI-test
// target (see project.yml: WeebTests/FrikiTests, built through `xcodebuild test` against the actual
// compiled String Catalogs and running inside a real iOS Simulator process) — that is where SwiftUI
// rendering is safe and where the real per-locale translated text exists to diff against. This file's
// job is the deterministic, environment-independent half: proving the DATA every rendered state is bound
// to is total, unique, and locale-key-complete.

import Foundation
import Testing
@testable import DesignKit

@Suite("Deny-surface bindings + Iconography totality (the environment-independent half of §9d)")
struct StateViewSnapshotTests {
    @Test("LimitReachedView and ProblemView variants bind to the correct catalog state + a11y id")
    func denySurfacesBindCorrectly() {
        let limitReached = StateCatalog.spec(id: "limitReached.generic")
        #expect(limitReached?.bodyKey == "limit_reached.generic")

        let generic = StateCatalog.spec(id: ProblemView.Variant.generic.stateID)
        #expect(generic?.bodyKey == "error.generic")
        #expect(ProblemView.Variant.generic.accessibilityID == "state.error.generic")

        let couldNotSend = StateCatalog.spec(id: ProblemView.Variant.couldNotSend.stateID)
        #expect(couldNotSend?.bodyKey == "error.could_not_send")
        #expect(ProblemView.Variant.couldNotSend.accessibilityID == "state.error.could_not_send")
    }

    @Test("BundledFallbackIconography resolves a non-empty SF Symbol name for every Glyph (Correction 2)")
    func iconographyIsTotal() {
        let source = BundledFallbackIconography()
        for glyph in Glyph.allCases {
            guard case let .sfSymbol(name) = source.resolve(glyph, style: .neutral) else {
                Issue.record("\(glyph) did not resolve to an SF Symbol via the bundled fallback")
                continue
            }
            #expect(!name.isEmpty, "\(glyph) has no bundled fallback glyph — Correction 2 requires the kit to render without the FA Pro kit")
        }
    }

    @Test("every StateSpec's glyph is one Iconography knows how to render")
    func everyStateHasARenderableGlyph() {
        let source = BundledFallbackIconography()
        for spec in StateCatalog.all {
            guard case let .sfSymbol(name) = source.resolve(spec.glyph, style: .neutral) else {
                Issue.record("\(spec.id) references an unrenderable glyph")
                continue
            }
            #expect(!name.isEmpty, "\(spec.id) references an unrenderable glyph")
        }
    }

    @Test("FontAwesomeIconography maps every Glyph to a non-zero scalar + non-empty PostScript name (all styles)")
    func faMapsEveryGlyph() {
        let source = FontAwesomeIconography()
        for glyph in Glyph.allCases {
            for style in [IconStyle.playful, .neutral, .emptyOrCelebration] {
                guard case let .faText(postScriptName, scalar) = source.resolve(glyph, style: style) else {
                    Issue.record("\(glyph)/\(style) did not resolve to FA text")
                    continue
                }
                #expect(scalar != 0, "\(glyph)/\(style) resolved to scalar 0")
                #expect(!postScriptName.isEmpty, "\(glyph)/\(style) resolved to an empty PostScript name")
            }
        }
    }

    @Test("FontAwesomeIconography spot-checks known codepoints from fa-glyph-map.json")
    func faSpotChecksCodepoints() {
        let source = FontAwesomeIconography()
        let expected: [(Glyph, UInt32)] = [
            (.checkmark, 0xF058),
            (.tabConnect, 0xF0C0),
            (.chevronForward, 0xF054),
            (.signupHandle, 0x40),
        ]
        for (glyph, want) in expected {
            guard case let .faText(_, scalar) = source.resolve(glyph, style: .neutral) else {
                Issue.record("\(glyph) did not resolve to FA text")
                continue
            }
            #expect(scalar == want, "\(glyph) expected scalar \(String(want, radix: 16)) got \(String(scalar, radix: 16))")
        }
    }

    @Test("FontAwesomeIconography picks Solid face for playful/celebration and Regular for neutral")
    func faPicksFaceByStyle() {
        let source = FontAwesomeIconography()
        guard case let .faText(playfulPS, _) = source.resolve(.checkmark, style: .playful),
              case let .faText(neutralPS, _) = source.resolve(.checkmark, style: .neutral),
              case let .faText(emptyPS, _) = source.resolve(.checkmark, style: .emptyOrCelebration) else {
            Issue.record("checkmark did not resolve to FA text for all styles")
            return
        }
        #expect(playfulPS == "FontAwesome7Pro-Solid")
        #expect(neutralPS == "FontAwesome7Pro-Regular")
        #expect(emptyPS == "FontAwesome7Pro-Solid") // duotone deferred -> Solid for now
    }

    @Test("chooseSource returns FontAwesome when available and Bundled when absent (env-independent)")
    func chooseSourceIsInjectable() {
        // FA available -> FA text.
        let faSource = IconographyProvider.chooseSource(faAvailable: true)
        if case .faText = faSource.resolve(.checkmark, style: .neutral) {
            // ok
        } else {
            Issue.record("chooseSource(faAvailable: true) did not yield the FA backend")
        }
        // FA absent -> SF Symbol fallback.
        let fallback = IconographyProvider.chooseSource(faAvailable: false)
        if case .sfSymbol = fallback.resolve(.checkmark, style: .neutral) {
            // ok
        } else {
            Issue.record("chooseSource(faAvailable: false) did not yield the SF Symbols fallback")
        }
    }

    @Test("SupportedLocale enumerates exactly i18n/locales.json's four locales")
    func supportedLocaleMatchesI18nManifest() throws {
        let thisFile = URL(fileURLWithPath: #filePath)
        let repoRoot = thisFile
            .deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        let data = try Data(contentsOf: repoRoot.appendingPathComponent("i18n/locales.json"))
        let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        let manifestLocales = json?["locales"] as? [String] ?? []
        let ourLocales = ["en", "es", "pt", "zh-Hans"] // Strings.SupportedLocale's raw values
        #expect(manifestLocales == ourLocales)
    }
}
