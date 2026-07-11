// ios/AppComplianceTests/PrivacyManifestTests.swift — SLICE_S7_CONTRACT.md §9f/§12.
//
// "A test parses it and asserts tracking=false + empty collection [types]." Hosted by the Weeb app
// (project.yml TEST_HOST), so `Bundle.main` here IS the real running Weeb.app — the exact bundle whose
// PrivacyInfo.xcprivacy ships to the store. Parses the real compiled resource, not the source file on
// disk, so a build step that silently dropped the manifest would fail this test too.

import Foundation
import Testing

@Suite("PrivacyInfo.xcprivacy — truthful because there is nothing to declare (§9f)")
struct PrivacyManifestTests {
    private func loadManifest() throws -> [String: Any] {
        guard let url = Bundle.main.url(forResource: "PrivacyInfo", withExtension: "xcprivacy") else {
            Issue.record("PrivacyInfo.xcprivacy not found in the app bundle")
            return [:]
        }
        let data = try Data(contentsOf: url)
        guard let plist = try PropertyListSerialization.propertyList(from: data, format: nil) as? [String: Any] else {
            Issue.record("PrivacyInfo.xcprivacy did not parse as a dictionary")
            return [:]
        }
        return plist
    }

    @Test("NSPrivacyTracking is false")
    func trackingIsFalse() throws {
        let manifest = try loadManifest()
        #expect((manifest["NSPrivacyTracking"] as? Bool) == false)
    }

    @Test("NSPrivacyTrackingDomains is empty")
    func trackingDomainsEmpty() throws {
        let manifest = try loadManifest()
        #expect((manifest["NSPrivacyTrackingDomains"] as? [Any])?.isEmpty == true)
    }

    @Test("NSPrivacyCollectedDataTypes is empty — the §3 zero-inventory, declared as code")
    func collectedDataTypesEmpty() throws {
        let manifest = try loadManifest()
        #expect((manifest["NSPrivacyCollectedDataTypes"] as? [Any])?.isEmpty == true)
    }

    @Test("NSPrivacyAccessedAPITypes is empty — no required-reason API is used at S7")
    func accessedAPITypesEmpty() throws {
        let manifest = try loadManifest()
        #expect((manifest["NSPrivacyAccessedAPITypes"] as? [Any])?.isEmpty == true)
    }
}
