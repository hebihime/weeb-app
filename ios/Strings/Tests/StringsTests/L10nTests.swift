// ios/Strings/Tests/StringsTests/L10nTests.swift
//
// The catalogs themselves live on the App target (see Package.swift), so a bare `swift test` of this
// package has no compiled String Catalog to resolve against. These tests exercise the SELECTION logic
// (Info.plist -> BrandStringPack, the message-key constants) with a real-but-minimal Info.plist bundle
// built on the fly — no mocking framework, just a temp directory Foundation can load as a Bundle.
// Full ×4-locale VALUE correctness is tools/i18n-lint's job (static parse of the .xcstrings files) plus
// the app-hosted snapshot suite, which runs with the real compiled catalogs via `xcodebuild test`.

import Foundation
import Testing
@testable import Strings

@Suite("L10n")
struct L10nTests {
    /// Builds a throwaway `.bundle` on disk with only an Info.plist so `Bundle.object(forInfoDictionaryKey:)`
    /// has something real to read, without needing a compiled String Catalog.
    private func makeBundle(stringPackID: String?) throws -> Bundle {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("L10nTests-\(UUID().uuidString).bundle")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        var plist: [String: Any] = ["CFBundleIdentifier": "app.test.l10n"]
        if let stringPackID { plist["StringPackID"] = stringPackID }
        let data = try PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0)
        try data.write(to: dir.appendingPathComponent("Info.plist"))
        guard let bundle = Bundle(path: dir.path) else {
            throw NSError(domain: "L10nTests", code: 1, userInfo: [NSLocalizedDescriptionKey: "could not load fixture bundle"])
        }
        return bundle
    }

    @Test("current() reads a valid StringPackID from Info.plist")
    func currentReadsValidPack() throws {
        let bundle = try makeBundle(stringPackID: "friki")
        #expect(BrandStringPack.current(bundle: bundle) == .friki)
    }

    @Test("current() falls back to weeb when the key is absent")
    func currentFallsBackWhenAbsent() throws {
        let bundle = try makeBundle(stringPackID: nil)
        #expect(BrandStringPack.current(bundle: bundle) == .weeb)
    }

    @Test("current() falls back to weeb on an unrecognized value")
    func currentFallsBackOnGarbage() throws {
        let bundle = try makeBundle(stringPackID: "not-a-real-brand")
        #expect(BrandStringPack.current(bundle: bundle) == .weeb)
    }

    @Test("message key constants match contracts/message-keys.json exactly")
    func messageKeyConstantsMatchContract() {
        #expect(MessageKey.limitReachedGeneric == "limit_reached.generic")
        #expect(MessageKey.errorGeneric == "error.generic")
        #expect(MessageKey.errorCouldNotSend == "error.could_not_send")
    }
}
