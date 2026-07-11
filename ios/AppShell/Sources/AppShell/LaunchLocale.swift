// ios/AppShell/Sources/AppShell/LaunchLocale.swift — SLICE_S7_CONTRACT.md §9g, DR-7.7.
//
// DEBUG-ONLY locale override for the 14A E2E harness. maestro/flows/brand-smoke/smoke.yaml's final leg
// relaunches with `arguments: { appLocale: "es" }` and asserts the ES handle-step title. iOS — unlike
// Android's MainActivity reading the appLocale intent extra — does NOT natively interpret a custom
// `appLocale` launch argument, so the app must read it itself and force the locale.
//
// This whole file is wrapped in `#if DEBUG` with NO `#else`: a release binary contains no symbol from
// it, so there is literally no locale-override code path to ship (DR-7.7: locale follows device only,
// no in-app picker). ClientApp calls `applyOverrideIfPresent()` once at launch, before any view renders.

#if DEBUG
import Foundation
import Strings

public enum LaunchLocale {
    /// The forced locale, if a harness passed one. Read by RootView to also set the SwiftUI
    /// `\.locale` environment (for built-in date/number formatting), alongside L10n's string override.
    public private(set) nonisolated(unsafe) static var overrideLocale: Locale?

    /// Parse the `appLocale` launch argument however the harness passed it and, if present, install it
    /// as L10n's effective default locale + record it for the environment. Idempotent; safe to call on
    /// every (clearState) relaunch.
    public static func applyOverrideIfPresent() {
        guard let code = resolveCode(), !code.isEmpty else {
            overrideLocale = nil
            L10n.debugLocaleOverride = nil
            return
        }
        let locale = Locale(identifier: code)
        overrideLocale = locale
        L10n.debugLocaleOverride = locale
    }

    /// Covers every way `appLocale` might arrive:
    ///  - `UserDefaults` NSArgumentDomain: iOS auto-parses `-appLocale es` (the `-key value` convention)
    ///    into `UserDefaults.standard.string(forKey: "appLocale")`.
    ///  - raw `ProcessInfo.arguments`: `appLocale=es` / `-appLocale=es` single token, OR
    ///    `-appLocale`/`--appLocale`/`appLocale` immediately followed by the code.
    static func resolveCode() -> String? {
        if let code = UserDefaults.standard.string(forKey: "appLocale"), !code.isEmpty {
            return code
        }
        let args = ProcessInfo.processInfo.arguments
        for (index, arg) in args.enumerated() {
            if let eq = arg.range(of: "appLocale=") {
                return String(arg[eq.upperBound...])
            }
            if (arg == "-appLocale" || arg == "--appLocale" || arg == "appLocale"), index + 1 < args.count {
                return args[index + 1]
            }
        }
        return nil
    }
}
#endif
