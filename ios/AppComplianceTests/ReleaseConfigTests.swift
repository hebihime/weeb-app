// ios/AppComplianceTests/ReleaseConfigTests.swift — SLICE_S7_CONTRACT.md §1d/§9f/§12.
//
// "A release-config test asserts NO backend URL literal and NO cleartext exception in the release build
// config." This is a static check over the real files the Release configuration actually uses
// (ios/Config/Release-*.xcconfig + ios/App/Sources/Resources/Info-Release.plist) — it does not need to
// be hosted by a running app (unlike PersistenceFrameworkAbsenceTests/PrivacyManifestTests), so it
// reads straight off disk exactly like DependencyDirectionTests does.

import Foundation
import Testing

private let iosRoot: URL = {
    // ios/AppComplianceTests/ReleaseConfigTests.swift -> ios/
    URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent() // AppComplianceTests
        .deletingLastPathComponent() // ios
}()

@Suite("Release build config — fail-closed by absence (§1d/§9f)")
struct ReleaseConfigTests {
    @Test("Release-Weeb.xcconfig and Release-Friki.xcconfig declare no backend host/port", arguments: ["Release-Weeb.xcconfig", "Release-Friki.xcconfig"])
    func releaseXcconfigHasNoBackendHostPort(filename: String) throws {
        let path = iosRoot.appendingPathComponent("Config").appendingPathComponent(filename)
        let content = try String(contentsOf: path, encoding: .utf8)
        #expect(!content.contains("API_BACKEND_HOST"), "\(filename) must not set API_BACKEND_HOST — release is fail-closed by absence")
        #expect(!content.contains("API_BACKEND_PORT"), "\(filename) must not set API_BACKEND_PORT — release is fail-closed by absence")
        #expect(!content.contains("http://"), "\(filename) must not contain a cleartext URL literal")
    }

    @Test("Debug-Weeb.xcconfig and Debug-Friki.xcconfig DO declare a backend host/port (sanity: the absence above is meaningful)", arguments: ["Debug-Weeb.xcconfig", "Debug-Friki.xcconfig"])
    func debugXcconfigHasBackendHostPort(filename: String) throws {
        let path = iosRoot.appendingPathComponent("Config").appendingPathComponent(filename)
        let content = try String(contentsOf: path, encoding: .utf8)
        #expect(content.contains("API_BACKEND_HOST"), "\(filename) should set API_BACKEND_HOST — otherwise the release-absence test above is vacuous")
        #expect(content.contains("API_BACKEND_PORT"), "\(filename) should set API_BACKEND_PORT")
    }

    @Test("Info-Release.plist has no ApiBackendHost/ApiBackendPort key")
    func infoReleasePlistHasNoBackendKeys() throws {
        let plist = try loadPlist("Info-Release.plist")
        #expect(plist["ApiBackendHost"] == nil)
        #expect(plist["ApiBackendPort"] == nil)
    }

    @Test("Info-Release.plist has no NSAppTransportSecurity exceptions dict at all")
    func infoReleasePlistHasNoATSExceptions() throws {
        let plist = try loadPlist("Info-Release.plist")
        #expect(plist["NSAppTransportSecurity"] == nil, "release ATS must be fully enforced by absence of any exceptions dict")
    }

    @Test("Info-Debug.plist DOES have both, as the dev convenience they are (sanity check)")
    func infoDebugPlistHasBoth() throws {
        let plist = try loadPlist("Info-Debug.plist")
        #expect(plist["ApiBackendHost"] != nil)
        #expect(plist["ApiBackendPort"] != nil)
        #expect(plist["NSAppTransportSecurity"] != nil)
    }

    private func loadPlist(_ filename: String) throws -> [String: Any] {
        let path = iosRoot.appendingPathComponent("App/Sources/Resources").appendingPathComponent(filename)
        let data = try Data(contentsOf: path)
        return (try PropertyListSerialization.propertyList(from: data, format: nil) as? [String: Any]) ?? [:]
    }
}
