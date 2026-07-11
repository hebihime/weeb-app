// ios/ApiKit/Tests/ApiKitTests/ClientConfigServiceTests.swift — SLICE_S7_CONTRACT.md §1d/§12.
//
// Boot check: bundled catalogs ⊇ server `locales`; a mismatch renders the neutral contract-mismatch
// state, never a crash. `evaluateLocaleCoverage` is pure (no I/O), so the coverage rule is fully
// unit-testable without standing up a fake server.

import Testing
@testable import ApiKit

@Suite("ClientConfigService — locale coverage boot check")
struct ClientConfigServiceTests {
    @Test("server locales fully covered by the bundle -> ok")
    func fullCoverageIsOK() {
        let result = ClientConfigService.evaluateLocaleCoverage(
            serverLocales: ["en", "es"],
            bundledLocales: ["en", "es", "pt", "zh-Hans"],
            apiVersion: "1.0.0",
            defaultLocale: "en"
        )
        guard case .ok(let apiVersion, let locales, let defaultLocale) = result else {
            Issue.record("expected .ok, got \(result)")
            return
        }
        #expect(apiVersion == "1.0.0")
        #expect(locales == ["en", "es"])
        #expect(defaultLocale == "en")
    }

    @Test("server locale missing from the bundle -> contractMismatch, never a crash")
    func missingBundledLocaleIsMismatch() {
        let result = ClientConfigService.evaluateLocaleCoverage(
            serverLocales: ["en", "fr"], // fr not bundled
            bundledLocales: ["en", "es", "pt", "zh-Hans"],
            apiVersion: "1.0.0",
            defaultLocale: "en"
        )
        guard case .contractMismatch(let serverLocales, let bundledLocales) = result else {
            Issue.record("expected .contractMismatch, got \(result)")
            return
        }
        #expect(serverLocales == ["en", "fr"])
        #expect(bundledLocales == ["en", "es", "pt", "zh-Hans"])
    }

    @Test("exact match (server == bundle) -> ok")
    func exactMatchIsOK() {
        let result = ClientConfigService.evaluateLocaleCoverage(
            serverLocales: ["en", "es", "pt", "zh-Hans"],
            bundledLocales: ["en", "es", "pt", "zh-Hans"],
            apiVersion: "1.0.0",
            defaultLocale: "en"
        )
        #expect({ if case .ok = result { return true } else { return false } }())
    }

    @Test("empty server locales list is trivially covered -> ok")
    func emptyServerLocalesIsOK() {
        let result = ClientConfigService.evaluateLocaleCoverage(
            serverLocales: [],
            bundledLocales: ["en"],
            apiVersion: "1.0.0",
            defaultLocale: "en"
        )
        #expect({ if case .ok = result { return true } else { return false } }())
    }
}
