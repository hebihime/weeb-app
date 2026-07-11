// ios/ApiKit/Tests/ApiKitTests/TolerantReaderTests.swift — SLICE_S7_CONTRACT.md §1d/§12.
//
// Tolerant readers, asserted: the generated client wrapper ignores unknown response fields. Named
// beneficiary (§1d): a future additive `min_supported_client_version` on ClientConfigResponse — this
// test proves that landing that field server-side tomorrow does not break today's compiled client, by
// decoding a payload that already has an extra field no one asked for.

import Foundation
import Testing
@testable import ApiKit

@Suite("Tolerant readers")
struct TolerantReaderTests {
    @Test("ClientConfigResponse decodes successfully with an unknown extra field present")
    func clientConfigTolerantOfExtraField() throws {
        let json = """
        {
            "apiVersion": "1.0.0",
            "locales": ["en", "es", "pt", "zh-Hans"],
            "defaultLocale": "en",
            "minSupportedClientVersion": "1.0.0",
            "somethingNobodyToldUsAbout": { "nested": true }
        }
        """
        let decoded = try JSONDecoder().decode(Components.Schemas.ClientConfigResponse.self, from: Data(json.utf8))
        #expect(decoded.apiVersion == "1.0.0")
        #expect(decoded.locales == ["en", "es", "pt", "zh-Hans"])
        #expect(decoded.defaultLocale == "en")
    }

    @Test("LimitReached decodes successfully with an unknown extra field present")
    func limitReachedTolerantOfExtraField() throws {
        let json = """
        {
            "quotaKey": "daily.likes",
            "messageKey": "limit_reached.generic",
            "resetsAt": "2026-07-11T00:00:00Z",
            "premiumExtends": true,
            "futureField": "whatever this becomes later"
        }
        """
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        let decoded = try decoder.decode(Components.Schemas.LimitReached.self, from: Data(json.utf8))
        #expect(decoded.quotaKey == "daily.likes")
        #expect(decoded.premiumExtends == true)
    }

    @Test("Problem decodes successfully with an unknown extra field present")
    func problemTolerantOfExtraField() throws {
        let json = """
        {
            "type": "about:blank",
            "title": "Something went wrong",
            "status": 500,
            "messageKey": "error.generic",
            "correlationId": "corr-123",
            "aFieldThatDoesNotExistYet": [1, 2, 3]
        }
        """
        let decoded = try JSONDecoder().decode(Components.Schemas.Problem.self, from: Data(json.utf8))
        #expect(decoded.messageKey == "error.generic")
        #expect(decoded.correlationId == "corr-123")
    }
}
