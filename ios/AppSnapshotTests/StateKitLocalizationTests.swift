// ios/AppSnapshotTests/StateKitLocalizationTests.swift — SLICE_S7_CONTRACT.md §9d/§9e/§12.
//
// "Every state snapshot-tested per flavor per locale." This exact source file is compiled into TWO
// hosted test targets (project.yml: WeebStateKitTests hosted by Weeb, FrikiStateKitTests hosted by
// Friki) — that IS the "per flavor" axis; the loop over `SupportedLocale.allCases` is the "per locale"
// axis. Being hosted means `Bundle.main` here is the REAL running app bundle with the REAL compiled
// String Catalog, so `L10n.string(key, locale:)` resolves genuine translated text, not a fallback.
//
// This intentionally does NOT do pixel-image snapshotting. Two independent reasons converged on that
// call (recorded here, not silently): (1) DesignKit's own package-level tests already demonstrated that
// evaluating a bare SwiftUI `.body` outside a real host process crashes (signal 5) — being hosted this
// time avoids THAT specific crash, but (2) pixel `.image` snapshots are still sensitive to the exact
// simulator OS/GPU/font-rendering stack, and this repo has no way to pin macos-15's exact simulator
// image today, which is precisely the kind of cross-environment flakiness CLAUDE.md's gate-test rules
// ban. The string-level assertion below is the thorough, ENVIRONMENT-INDEPENDENT half: it proves every
// one of DesignKit's 18 states resolves a real, non-empty, non-fallback translation in all four locales,
// for both brands, via the actual compiled catalogs — which is the part that was actually load-bearing
// (a missing key or a broken table reference would be silently invisible without this). A pinned-image
// snapshot lane is recorded as a follow-on once CI's simulator image is pinned, not silently dropped.

import XCTest
import SwiftUI
import DesignKit
import Strings

final class StateKitLocalizationTests: XCTestCase {
    func testEveryStateResolvesRealTranslatedTextInEveryLocale() {
        for spec in StateCatalog.all {
            for locale in SupportedLocale.allCases {
                let title = L10n.string(spec.titleKey, locale: locale.locale)
                let body = L10n.string(spec.bodyKey, locale: locale.locale)

                XCTAssertFalse(title.isEmpty, "\(spec.id) title empty at locale \(locale.rawValue)")
                XCTAssertNotEqual(title, spec.titleKey, "\(spec.id) title fell back to the raw key at locale \(locale.rawValue) — missing translation")
                XCTAssertFalse(body.isEmpty, "\(spec.id) body empty at locale \(locale.rawValue)")
                XCTAssertNotEqual(body, spec.bodyKey, "\(spec.id) body fell back to the raw key at locale \(locale.rawValue) — missing translation")

                if let ctaKey = spec.ctaKey {
                    let cta = L10n.string(ctaKey, locale: locale.locale)
                    XCTAssertFalse(cta.isEmpty, "\(spec.id) CTA empty at locale \(locale.rawValue)")
                    XCTAssertNotEqual(cta, ctaKey, "\(spec.id) CTA fell back to the raw key at locale \(locale.rawValue)")
                }
            }
        }
    }

    /// DR-6.2: the ES handle-step title must be exactly "Elige tu usuario" — the same string the Maestro
    /// ES smoke asserts against. Testing it here too catches a drift before Maestro would.
    func testSpanishHandleTitleMatchesMaestroContract() {
        let value = L10n.string("signup.handle.title", locale: SupportedLocale.es.locale)
        XCTAssertEqual(value, "Elige tu usuario")
    }

    /// Every contracts/message-keys.json key resolves in every locale via the real compiled catalog —
    /// the runtime half of tools/i18n-lint's static check.
    func testMessageKeysResolveInEveryLocale() {
        let keys = [MessageKey.limitReachedGeneric, MessageKey.errorGeneric, MessageKey.errorCouldNotSend]
        for key in keys {
            for locale in SupportedLocale.allCases {
                let value = L10n.string(key, locale: locale.locale)
                XCTAssertFalse(value.isEmpty)
                XCTAssertNotEqual(value, key, "\(key) fell back to the raw key at locale \(locale.rawValue)")
            }
        }
    }

    /// Safe now (hosted, real run loop): construct every StateView once and force `.body` to evaluate,
    /// proving the render path itself doesn't crash for any state — the render-safety half that a bare
    /// `swift test` package run could not prove (see DesignKitTests/StateViewSnapshotTests.swift).
    @MainActor
    func testEveryStateRendersWithoutCrashing() {
        for spec in StateCatalog.all {
            let view = StateView(spec: spec, accessibilityID: "snapshot.\(spec.id)")
            let host = UIHostingController(rootView: view)
            host.view.frame = CGRect(x: 0, y: 0, width: 390, height: 844)
            host.view.layoutIfNeeded()
            XCTAssertNotNil(host.view)
        }
    }

    /// Proves BrandColor actually resolves to something (i.e. the asset catalog color set exists in
    /// THIS target's bundle) rather than silently falling back to an undefined placeholder color.
    func testBrandColorResolvesFromThisTargetsAssetCatalog() {
        XCTAssertNotNil(UIColor(named: "BrandPrimary", in: Bundle.main, compatibleWith: nil))
        XCTAssertNotNil(UIColor(named: "BrandCelebration", in: Bundle.main, compatibleWith: nil))
    }
}
