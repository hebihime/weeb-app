// ios/ApiKit/Sources/ApiKit/ClientConfigService.swift — SLICE_S7_CONTRACT.md §1d.
//
// The one live call debug/dev builds make: GET /v1/client-config, rendered on the debug-build-only
// diagnostics screen — the smallest thing proving contract -> codegen -> transport -> running stack end
// to end. Boot check: bundled catalogs ⊇ server `locales`; a mismatch renders the neutral
// contract-mismatch state (5.13b family), never a crash (§1d).

import Foundation

public enum BootCheckResult: Sendable, Equatable {
    case ok(apiVersion: String, locales: [String], defaultLocale: String)
    case contractMismatch(serverLocales: [String], bundledLocales: [String])
    case limitReached
    case problem
    case offline
    /// Release builds never call the network at all (§1d fail-closed by absence) — this is what a
    /// release-config test asserts is the ONLY reachable case in that build configuration.
    case noBackendConfigured
}

public struct ClientConfigService: Sendable {
    private let environment: BackendEnvironment
    private let bundledLocales: [String]

    public init(environment: BackendEnvironment, bundledLocales: [String]) {
        self.environment = environment
        self.bundledLocales = bundledLocales
    }

    public func check() async -> BootCheckResult {
        guard let client = ApiClientFactory.makeClient(environment: environment) else {
            return .noBackendConfigured
        }

        let outcome = await ErrorMapper.run {
            await ErrorMapper.map(clientConfig: try await client.GetClientConfig())
        }

        switch outcome {
        case .rendered(let config):
            return Self.evaluateLocaleCoverage(
                serverLocales: config.locales,
                bundledLocales: bundledLocales,
                apiVersion: config.apiVersion,
                defaultLocale: config.defaultLocale
            )
        case .limitReached:
            return .limitReached
        case .problem:
            return .problem
        case .offline:
            return .offline
        }
    }

    /// GET /health — debug diagnostics only (§1d). No boot-check logic rides on this; it exists purely
    /// so the diagnostics screen can show the compose backend is alive.
    public func checkHealth() async -> MappedOutcome<Components.Schemas.HealthStatus> {
        guard let client = ApiClientFactory.makeClient(environment: environment) else {
            return .offline
        }
        return await ErrorMapper.run {
            await ErrorMapper.map(health: try await client.GetHealth())
        }
    }

    /// Pure function (no I/O) so the coverage rule itself — bundled catalogs must be a SUPERSET of what
    /// the server advertises — is unit-testable without a network stack.
    static func evaluateLocaleCoverage(
        serverLocales: [String],
        bundledLocales: [String],
        apiVersion: String,
        defaultLocale: String
    ) -> BootCheckResult {
        let bundled = Set(bundledLocales)
        let server = Set(serverLocales)
        guard server.isSubset(of: bundled) else {
            return .contractMismatch(serverLocales: serverLocales, bundledLocales: bundledLocales)
        }
        return .ok(apiVersion: apiVersion, locales: serverLocales, defaultLocale: defaultLocale)
    }
}
