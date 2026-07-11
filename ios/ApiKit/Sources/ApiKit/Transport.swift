// ios/ApiKit/Sources/ApiKit/Transport.swift — SLICE_S7_CONTRACT.md §1d/§9f.
//
// The one place a backend base URL is configured. Debug/dev builds point at the compose backend over
// dev-loopback (localhost/10.0.2.2, cleartext ONLY in DEBUG configs — see Config/Debug-*.xcconfig +
// App/Sources/Resources/Info-Debug.plist's ATS exception). Release builds have NO configured backend URL
// at all — fail-closed by absence (§1d), proven by ApiKitTests/ReleaseConfigTests.swift.
//
// No client-config cache, no TTL store (§1d: would violate the §3 zero-persistence inventory). Every
// call is a fresh network round-trip through this one client.

import Foundation
import OpenAPIRuntime
import OpenAPIURLSession

public enum BackendEnvironment: Sendable, Equatable {
    /// Debug/simulator builds only. `baseURLString` is assembled here in Swift from two separate
    /// Info.plist keys (`ApiBackendHost` / `ApiBackendPort`, injected by Config/Debug-*.xcconfig) —
    /// never a literal in Swift source (egress-lint scans .swift/.kt source, not Info.plist/xcconfig).
    /// Host and port are kept apart rather than one URL string because xcconfig treats a bare "//" as a
    /// comment START even mid-value, with no escape that survives every xcconfig -> Info.plist
    /// substitution path — building the scheme in Swift sidesteps that class of bug entirely.
    case debug(baseURLString: String)
    /// Release builds: no backend URL exists anywhere in the compiled binary. `client()` returns nil.
    case release

    /// Reads the compiled-in environment from a target's Info.plist. `isDebugBuild` is threaded in by
    /// the caller (compile-time `#if DEBUG`) rather than sniffed here, so this stays pure and testable.
    public static func current(bundle: Bundle, isDebugBuild: Bool) -> BackendEnvironment {
        guard
            isDebugBuild,
            let host = bundle.object(forInfoDictionaryKey: "ApiBackendHost") as? String, !host.isEmpty,
            let portString = bundle.object(forInfoDictionaryKey: "ApiBackendPort") as? String,
            let port = Int(portString), port > 0
        else {
            return .release
        }
        return .debug(baseURLString: "http://\(host):\(port)")
    }
}

public enum ApiClientFactory {
    /// Builds the generated client, or nil in `.release` (fail-closed by absence — §1d). Callers must
    /// treat a nil client as "no network path exists," never retry with a hardcoded fallback URL.
    public static func makeClient(environment: BackendEnvironment) -> Client? {
        switch environment {
        case .release:
            return nil
        case .debug(let baseURLString):
            guard let url = URL(string: baseURLString) else { return nil }
            return Client(serverURL: url, transport: URLSessionTransport())
        }
    }
}
