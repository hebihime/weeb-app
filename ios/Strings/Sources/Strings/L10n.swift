// ios/Strings/Sources/Strings/L10n.swift — SLICE_S7_CONTRACT.md §9e.
//
// The ONLY place a user-facing string literal is allowed to originate. Every other package/target reads
// through `L10n.string(_:)` (or the typed helpers below), never `Text("literal")`. Locale follows device
// locale with fallback to i18n/locales.json's default ("en") per DR-7.7 — there is no in-app language
// picker at S7, so production call sites never pass `locale:` explicitly and simply get device-locale
// resolution via the `Locale.current` default. The parameter exists so the snapshot suite (§9d: "every
// state snapshot-tested per flavor per locale") can force each of the four locales deterministically
// without touching simulator/device settings.
//
// IMPLEMENTATION NOTE (a real bug found while building the snapshot suite, recorded per CLAUDE.md's
// skillify rule): `String(localized:table:bundle:locale:)`'s `locale:` parameter does NOT reliably
// override which .lproj the runtime pulls from — AppSnapshotTests/StateKitLocalizationTests.swift's
// Spanish-title assertion returned the English fallback even with `locale: Locale(identifier: "es")`
// explicitly passed. The fix is the classic `Bundle.localizedString(forKey:value:table:)` API called on
// a MANUALLY-resolved locale-specific sub-bundle (`Bundle(path: mainBundle.path(forResource: "es",
// ofType: "lproj")!)`) — that API always honors whichever bundle instance you call it on, with no
// locale-negotiation ambiguity. `resolvedBundle(for:in:)` below does that resolution once per call.
//
// The catalogs themselves live in the App target (App/Sources/Resources/*.xcstrings) — see Package.swift
// for why — so every lookup here resolves against `Bundle.main` by default. That default is exactly
// right in the shipping app (Bundle.main IS the app bundle that owns the compiled catalogs) and in any
// XCTest bundle that is *hosted* by the app target (the standard pattern for the snapshot/UI test
// targets that actually need real localized text). A bare `swift test` run of this package alone has no
// app bundle to resolve against, so tests here exercise the SELECTION logic (which table, which pack)
// with an injected bundle rather than asserting real translated values.
//
// Brand voice overlay: `string_pack_id` (from brands/*.json, injected per target via Info.plist at build
// time — never a runtime switch) selects which overlay table a handful of voice-flavored keys resolve
// against. Every other key lives in the one shared `Localizable` table.

import Foundation

public enum BrandStringPack: String, Sendable, CaseIterable {
    case weeb
    case friki

    /// Reads the compiled-in brand pack from the target's Info.plist. Falls back to `.weeb` only if the
    /// key is somehow absent (should never happen — brand-gate verifies STRING_PACK_ID is always set).
    public static func current(bundle: Bundle = .main) -> BrandStringPack {
        guard
            let raw = bundle.object(forInfoDictionaryKey: "StringPackID") as? String,
            let pack = BrandStringPack(rawValue: raw)
        else { return .weeb }
        return pack
    }

    var overlayTable: String {
        switch self {
        case .weeb: return "Brand-Weeb"
        case .friki: return "Brand-Friki"
        }
    }
}

public enum L10n {
    static let commonTable = "Localizable"

    #if DEBUG
    /// DEBUG-ONLY forced locale, installed once at launch by AppShell.LaunchLocale when an E2E harness
    /// passes `appLocale=<code>` (Maestro's `launchApp arguments: { appLocale: "es" }`). iOS does not
    /// natively interpret that custom argument, so this is how the ES-locale smoke leg makes the app
    /// render its `.xcstrings` Spanish values. Compiled out entirely in release (DR-7.7: no
    /// locale-override path ships) — see `effectiveDefaultLocale`. Written once before any UI renders,
    /// then read-only; `nonisolated(unsafe)` is the pragmatic annotation for that one-shot startup set.
    public nonisolated(unsafe) static var debugLocaleOverride: Locale?
    #endif

    /// The locale a lookup uses when the caller does not force one explicitly (`locale: nil`). In
    /// release this is always the device locale; in DEBUG it honors a harness override if one was set.
    static var effectiveDefaultLocale: Locale {
        #if DEBUG
        return debugLocaleOverride ?? .current
        #else
        return .current
        #endif
    }

    /// Resolve a key from the shared common catalog. `key` must exist ×4 locales (i18n-lint enforces).
    /// `locale: nil` (the default at every production call site — there is no in-app language picker,
    /// DR-7.7) resolves to `effectiveDefaultLocale`; the snapshot suite passes an explicit locale to
    /// force each of the four.
    public static func string(_ key: String, locale: Locale? = nil, bundle: Bundle = .main) -> String {
        resolvedBundle(for: locale ?? effectiveDefaultLocale, in: bundle)
            .localizedString(forKey: key, value: nil, table: commonTable)
    }

    /// Resolve a brand-voice-overlay key for the given pack (defaults to the compiled-in target brand).
    public static func brandString(
        _ key: String,
        pack: BrandStringPack = .current(),
        locale: Locale? = nil,
        bundle: Bundle = .main
    ) -> String {
        resolvedBundle(for: locale ?? effectiveDefaultLocale, in: bundle)
            .localizedString(forKey: key, value: nil, table: pack.overlayTable)
    }

    // Memoize resolved `.lproj` sub-bundles. Without this, every single `L10n.string` call did a
    // `Bundle.preferredLocalizations` + `Bundle(path:)` disk load — dozens of main-thread filesystem
    // hits on a screen's worth of strings, worst on a cold-installed app (the state after Maestro's
    // clearState reinstall) where the disk cache is cold. That is exactly the window in which a first
    // frame can be slow enough for an `assertVisible` to time out. Keyed by (bundle path + locale id).
    private nonisolated(unsafe) static var resolvedBundleCache: [String: Bundle] = [:]
    private static let resolvedBundleCacheLock = NSLock()

    /// Finds the best `.lproj` sub-bundle for `locale` among `bundle`'s actual compiled localizations,
    /// via the same negotiation algorithm `Bundle.preferredLocalizations` uses for real device-locale
    /// resolution — falling back to `bundle` itself (e.g. a test-fixture bundle with no `.lproj`
    /// directories at all) rather than crashing when nothing matches. Memoized (see cache above).
    static func resolvedBundle(for locale: Locale, in bundle: Bundle) -> Bundle {
        let cacheKey = "\(bundle.bundlePath)#\(locale.identifier)"
        resolvedBundleCacheLock.lock()
        defer { resolvedBundleCacheLock.unlock() }
        if let cached = resolvedBundleCache[cacheKey] {
            return cached
        }
        let resolved = computeResolvedBundle(for: locale, in: bundle)
        resolvedBundleCache[cacheKey] = resolved
        return resolved
    }

    private static func computeResolvedBundle(for locale: Locale, in bundle: Bundle) -> Bundle {
        let available = bundle.localizations
        guard !available.isEmpty else { return bundle }
        let preferred = Bundle.preferredLocalizations(from: available, forPreferences: [locale.identifier])
        guard
            let best = preferred.first,
            let path = bundle.path(forResource: best, ofType: "lproj"),
            let subBundle = Bundle(path: path)
        else {
            return bundle
        }
        return subBundle
    }
}

/// The four locales this app ships (i18n/locales.json) — a typed handle for the snapshot suite's sweep,
/// so "per locale" in a test loop can never silently drop one.
public enum SupportedLocale: String, Sendable, CaseIterable {
    case en, es, pt
    case zhHans = "zh-Hans"

    public var locale: Locale { Locale(identifier: rawValue) }
}

/// Typed accessors for the contracts/message-keys.json substrate keys — the ONE deny surface and the ONE
/// generic error surface read these, never a raw string literal at the call site.
public enum MessageKey {
    public static let limitReachedGeneric = "limit_reached.generic"
    public static let errorGeneric = "error.generic"
    public static let errorCouldNotSend = "error.could_not_send"

    public static func text(_ key: String, locale: Locale? = nil, bundle: Bundle = .main) -> String {
        L10n.string(key, locale: locale, bundle: bundle)
    }
}
