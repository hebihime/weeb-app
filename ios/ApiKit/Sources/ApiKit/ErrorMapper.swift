// ios/ApiKit/Sources/ApiKit/ErrorMapper.swift — SLICE_S7_CONTRACT.md §1e/§9d/§9f.
//
// The ONE error mapper. Total consumer error taxonomy, a single choke point:
//   2xx            -> .rendered(Success)
//   429             -> .limitReached (the ONE LimitReached surface, limit_reached.generic)
//   everything else -> .problem (the ONE generic Problem surface, error.generic — 404-uniformity:
//                       403/404/410/500/... all fall into this same case, and nothing decoded from the
//                       response body ever escapes it, by construction, not by review)
//   transport throw -> .offline (5.13b connectivity state)
//
// This type is plain data — no DesignKit import, no rendering. AppShell is the only place a
// `MappedOutcome` becomes a `StateView`/`LimitReachedView`/`ProblemView` (keeps this layer UI-free,
// §1b's module-isolation discipline).

import Foundation
import OpenAPIRuntime

public struct LimitReachedInfo: Sendable, Equatable {
    public let quotaKey: String
    public let resetsAt: Date?
    public let premiumExtends: Bool
}

public enum MappedOutcome<Success: Sendable>: Sendable {
    case rendered(Success)
    case limitReached(LimitReachedInfo)
    /// Deliberately carries NOTHING from the server response — no status code, no decoded detail, no
    /// messageKey. That absence is what makes 403/404/410 byte-identical without a test having to prove
    /// three call sites all remembered to discard the same fields.
    case problem
    case offline
}

public enum ErrorMapper {
    private static let maxUndocumentedBodyBytes = 1_000_000

    /// The generated types' `date-time` fields (e.g. `LimitReached.resetsAt`) are plain `Foundation.Date`
    /// properties whose Decodable conformance expects the SAME date strategy the generator's own runtime
    /// `Converter` uses when decoding a documented response — ISO 8601, not Foundation's raw-timestamp
    /// default. Decoding an undocumented body ourselves (there is no generated Converter call for a
    /// status code the spec never declared) means we must configure that strategy explicitly here.
    static let responseDecoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return decoder
    }()

    /// Runs a throwing operation call and converts a transport-level throw (offline, DNS failure, TLS
    /// failure, timeout — anything that never got as far as an HTTP status) into `.offline`, so callers
    /// never need their own try/catch around a generated client call.
    public static func run<Success: Sendable>(
        _ operation: () async throws -> MappedOutcome<Success>
    ) async -> MappedOutcome<Success> {
        do {
            return try await operation()
        } catch {
            return .offline
        }
    }

    public static func map(health output: Operations.GetHealth.Output) async -> MappedOutcome<Components.Schemas.HealthStatus> {
        switch output {
        case .ok(let ok):
            switch ok.body {
            case .json(let value): return .rendered(value)
            }
        case .undocumented(let statusCode, let payload):
            return await mapNonSuccess(statusCode: statusCode, payload: payload)
        }
    }

    public static func map(clientConfig output: Operations.GetClientConfig.Output) async -> MappedOutcome<Components.Schemas.ClientConfigResponse> {
        switch output {
        case .ok(let ok):
            switch ok.body {
            case .json(let value): return .rendered(value)
            }
        case .undocumented(let statusCode, let payload):
            return await mapNonSuccess(statusCode: statusCode, payload: payload)
        }
    }

    /// The shared core every operation's non-2xx path funnels through — this single function body is
    /// what makes 404-uniformity a structural property instead of a per-call-site convention.
    private static func mapNonSuccess<Success: Sendable>(
        statusCode: Int,
        payload: OpenAPIRuntime.UndocumentedPayload
    ) async -> MappedOutcome<Success> {
        let data = await collectedBody(payload)

        guard statusCode == 429 else {
            // 403/404/410/500/anything-else-non-2xx-non-429: byte-identical, nothing server-specific
            // escapes this line, regardless of what `data` decodes to.
            return .problem
        }

        guard let limitReached = try? Self.responseDecoder.decode(Components.Schemas.LimitReached.self, from: data) else {
            // Even a malformed 429 body still renders the ONE limit-reached surface (DR-7.3) — a
            // decode failure here is never allowed to fall through to the generic Problem surface,
            // which would leak the fact that decoding failed.
            return .limitReached(LimitReachedInfo(quotaKey: "", resetsAt: nil, premiumExtends: false))
        }
        return .limitReached(
            LimitReachedInfo(
                quotaKey: limitReached.quotaKey,
                resetsAt: limitReached.resetsAt,
                premiumExtends: limitReached.premiumExtends
            )
        )
    }

    private static func collectedBody(_ payload: OpenAPIRuntime.UndocumentedPayload) async -> Data {
        guard let body = payload.body else { return Data() }
        return (try? await Data(collecting: body, upTo: maxUndocumentedBodyBytes)) ?? Data()
    }
}
