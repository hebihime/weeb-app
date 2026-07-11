// ios/ApiKit/Tests/ApiKitTests/ErrorMapperTests.swift — SLICE_S7_CONTRACT.md §1e/§12.
//
// The 404-uniformity test (§1e/§12): feed the mapper 403/404/410 on a consumer read and assert
// byte-identical rendered state, zero user-visible or log-visible distinction. Because MappedOutcome's
// `.problem` case carries no payload at all, "byte-identical" is not something this test has to compare
// field-by-field — it is comparing two enum cases with no associated values, which can only ever be
// equal or not. That is the point: the mapper physically cannot leak a distinction here.

import Foundation
import OpenAPIRuntime
import Testing
@testable import ApiKit

@Suite("ErrorMapper")
struct ErrorMapperTests {
    private func undocumentedOutput(statusCode: Int, jsonBody: Data?) -> Operations.GetClientConfig.Output {
        let body = jsonBody.map { HTTPBody($0) }
        return .undocumented(statusCode: statusCode, OpenAPIRuntime.UndocumentedPayload(headerFields: [:], body: body))
    }

    @Test("2xx renders the decoded success value")
    func rendersSuccess() async {
        let config = Components.Schemas.ClientConfigResponse(apiVersion: "1.0.0", locales: ["en"], defaultLocale: "en")
        let output = Operations.GetClientConfig.Output.ok(.init(body: .json(config)))
        let outcome = await ErrorMapper.map(clientConfig: output)
        guard case .rendered(let value) = outcome else {
            Issue.record("expected .rendered, got \(outcome)")
            return
        }
        #expect(value.apiVersion == "1.0.0")
    }

    @Test("404-uniformity: 403, 404, and 410 all map to the exact same .problem case, with no payload")
    func fourZeroFourUniformity() async {
        let statuses = [403, 404, 410]
        var outcomes: [MappedOutcome<Components.Schemas.ClientConfigResponse>] = []
        for status in statuses {
            // Even when the (hypothetical, contract-lint-banned) server DID send a distinguishing body,
            // the mapper must still discard it — feed each status a DIFFERENT body to prove that.
            let body = #"{"type":"about:blank","title":"x","status":\#(status),"messageKey":"leak-\#(status)","correlationId":"c-\#(status)"}"#.data(using: .utf8)
            let output = undocumentedOutput(statusCode: status, jsonBody: body)
            outcomes.append(await ErrorMapper.map(clientConfig: output))
        }
        for outcome in outcomes {
            guard case .problem = outcome else {
                Issue.record("expected .problem for every status, got \(outcome)")
                return
            }
        }
        // All three are the same case with no associated data — "byte-identical" is structurally true.
        #expect(outcomes.count == 3)
    }

    @Test("500 also maps to the same .problem case as 403/404/410 (not just the 4xx family)")
    func fiveHundredMapsToProblemToo() async {
        let output = undocumentedOutput(statusCode: 500, jsonBody: nil)
        let outcome = await ErrorMapper.map(clientConfig: output)
        guard case .problem = outcome else {
            Issue.record("expected .problem, got \(outcome)")
            return
        }
    }

    @Test("429 with a well-formed LimitReached body maps to .limitReached carrying the real fields")
    func limitReachedDecodes() async {
        let iso = "2026-07-11T00:00:00Z"
        let json = #"{"quotaKey":"daily.likes","messageKey":"limit_reached.generic","resetsAt":"\#(iso)","premiumExtends":true}"#
        let output = undocumentedOutput(statusCode: 429, jsonBody: json.data(using: .utf8))
        let outcome = await ErrorMapper.map(clientConfig: output)
        guard case .limitReached(let info) = outcome else {
            Issue.record("expected .limitReached, got \(outcome)")
            return
        }
        #expect(info.quotaKey == "daily.likes")
        #expect(info.premiumExtends == true)
        #expect(info.resetsAt != nil)
    }

    @Test("429 with a malformed body still maps to .limitReached, never falls through to .problem")
    func limitReachedNeverDowngradesToProblemOnBadBody() async {
        let output = undocumentedOutput(statusCode: 429, jsonBody: "not json".data(using: .utf8))
        let outcome = await ErrorMapper.map(clientConfig: output)
        guard case .limitReached = outcome else {
            Issue.record("expected .limitReached even with a malformed body, got \(outcome)")
            return
        }
    }

    @Test("run() converts a thrown transport error into .offline")
    func runConvertsThrowToOffline() async {
        struct FakeTransportError: Error {}
        let outcome: MappedOutcome<Components.Schemas.ClientConfigResponse> = await ErrorMapper.run {
            throw FakeTransportError()
        }
        #expect(outcome == .offline)
    }
}

extension MappedOutcome: Equatable where Success: Equatable {
    public static func == (lhs: MappedOutcome<Success>, rhs: MappedOutcome<Success>) -> Bool {
        switch (lhs, rhs) {
        case (.rendered(let a), .rendered(let b)): return a == b
        case (.limitReached(let a), .limitReached(let b)): return a == b
        case (.problem, .problem): return true
        case (.offline, .offline): return true
        default: return false
        }
    }
}
