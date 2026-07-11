// ios/ApiKit/Tests/ApiKitTests/BackendEnvironmentTests.swift — SLICE_S7_CONTRACT.md §1d/§9f/§12.
//
// Release network posture: "release builds contain no configured backend URL and make zero network
// calls — fail-closed by absence." `isDebugBuild` is threaded in explicitly (compile-time `#if DEBUG` at
// the call site, AppShell.AppEnvironment) rather than sniffed here, which is exactly what makes this
// testable without needing two separate build configurations to exercise both branches.

import Foundation
import Testing
@testable import ApiKit

@Suite("BackendEnvironment — release fail-closed by absence")
struct BackendEnvironmentTests {
    private func fixtureBundle(host: String?, port: String?) throws -> Bundle {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("BackendEnvTests-\(UUID().uuidString).bundle")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        var plist: [String: Any] = ["CFBundleIdentifier": "app.test.env"]
        if let host { plist["ApiBackendHost"] = host }
        if let port { plist["ApiBackendPort"] = port }
        let data = try PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0)
        try data.write(to: dir.appendingPathComponent("Info.plist"))
        return Bundle(path: dir.path)!
    }

    @Test("release build config ignores host/port even if the keys are somehow present")
    func releaseIgnoresConfiguredHostPort() throws {
        let bundle = try fixtureBundle(host: "10.0.2.2", port: "8080")
        let env = BackendEnvironment.current(bundle: bundle, isDebugBuild: false)
        #expect(env == .release)
    }

    @Test("release build config with no host/port at all is also .release")
    func releaseWithNoHostPortIsRelease() throws {
        let bundle = try fixtureBundle(host: nil, port: nil)
        let env = BackendEnvironment.current(bundle: bundle, isDebugBuild: false)
        #expect(env == .release)
    }

    @Test("debug build config with a configured host/port assembles the http URL in Swift")
    func debugWithHostPortResolvesToDebug() throws {
        let bundle = try fixtureBundle(host: "127.0.0.1", port: "8090")
        let env = BackendEnvironment.current(bundle: bundle, isDebugBuild: true)
        #expect(env == .debug(baseURLString: "http://127.0.0.1:8090"))
    }

    @Test("debug build config with no host/port configured still resolves to .release (nothing to connect to)")
    func debugWithNoHostPortIsStillRelease() throws {
        let bundle = try fixtureBundle(host: nil, port: nil)
        let env = BackendEnvironment.current(bundle: bundle, isDebugBuild: true)
        #expect(env == .release)
    }

    @Test("debug build config with a non-numeric port still resolves to .release (fails closed, not open)")
    func debugWithNonNumericPortIsRelease() throws {
        let bundle = try fixtureBundle(host: "127.0.0.1", port: "not-a-port")
        let env = BackendEnvironment.current(bundle: bundle, isDebugBuild: true)
        #expect(env == .release)
    }

    @Test("ApiClientFactory.makeClient returns nil for .release — no network path exists to misconfigure")
    func makeClientReturnsNilForRelease() {
        #expect(ApiClientFactory.makeClient(environment: .release) == nil)
    }

    @Test("ApiClientFactory.makeClient returns a client for a valid debug URL")
    func makeClientReturnsClientForDebug() {
        #expect(ApiClientFactory.makeClient(environment: .debug(baseURLString: "http://127.0.0.1:8080")) != nil)
    }

    @Test("ApiClientFactory.makeClient returns nil for an empty debug URL string (fails closed, not open)")
    func makeClientReturnsNilForEmptyURL() {
        #expect(ApiClientFactory.makeClient(environment: .debug(baseURLString: "")) == nil)
    }
}
