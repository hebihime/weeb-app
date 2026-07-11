// ios/AppShell/Sources/AppShell/AppEnvironment.swift — SLICE_S7_CONTRACT.md §1d/§9f.
//
// The one place `#if DEBUG` decides the build configuration for network/diagnostics purposes. Every
// other file that needs to know takes this as an explicit parameter (ApiKit.BackendEnvironment.current
// (bundle:isDebugBuild:)) rather than re-testing the compiler flag itself, which is what keeps that
// logic unit-testable (BackendEnvironmentTests exercises both branches directly).

import Foundation
import ApiKit

public enum AppEnvironment {
    public static var isDebugBuild: Bool {
        #if DEBUG
        true
        #else
        false
        #endif
    }

    /// `bundledLocales` — i18n/locales.json's set, baked in at compile time (never fetched) — is what the
    /// boot check (§1d) compares the server's advertised locales against.
    public static let bundledLocales = ["en", "es", "pt", "zh-Hans"]

    public static func backendEnvironment(bundle: Bundle = .main) -> BackendEnvironment {
        .current(bundle: bundle, isDebugBuild: isDebugBuild)
    }

    public static func clientConfigService(bundle: Bundle = .main) -> ClientConfigService {
        ClientConfigService(environment: backendEnvironment(bundle: bundle), bundledLocales: bundledLocales)
    }
}
