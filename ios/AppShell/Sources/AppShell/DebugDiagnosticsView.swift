// ios/AppShell/Sources/AppShell/DebugDiagnosticsView.swift — SLICE_S7_CONTRACT.md §1d/§9f.
//
// Debug-build-only diagnostics screen. Renders GET /v1/client-config — the live E2E seam proving
// contract -> codegen -> transport -> running stack end to end (§12 evidence 3). Release builds contain
// no configured backend URL and never compile this screen's network path meaningfully differently, but
// the `#if DEBUG` guard around the whole VIEW is the belt (ReleaseConfigTests is the suspenders — it
// proves the release build config carries no URL at all, independent of whether this file compiles).

#if DEBUG
import SwiftUI
import DesignKit
import ApiKit

struct DebugDiagnosticsView: View {
    @State private var result: BootCheckResult?

    var body: some View {
        List {
            Section("GET /v1/client-config") {
                switch result {
                case nil:
                    Text(verbatim: "Loading…") // dev-only diagnostics screen — never ships, never localized
                case .ok(let apiVersion, let locales, let defaultLocale):
                    Row(title: "apiVersion", caption: apiVersion)
                    Row(title: "locales", caption: locales.joined(separator: ", "))
                    Row(title: "defaultLocale", caption: defaultLocale)
                case .contractMismatch(let serverLocales, let bundledLocales):
                    Text(verbatim: "Contract mismatch — server: \(serverLocales.joined(separator: ",")), bundled: \(bundledLocales.joined(separator: ","))")
                case .limitReached:
                    Text(verbatim: "429 limit reached")
                case .problem:
                    Text(verbatim: "Non-2xx problem")
                case .offline:
                    Text(verbatim: "Offline / transport error")
                case .noBackendConfigured:
                    Text(verbatim: "No backend configured (release posture, or ApiBackendURL unset in Debug.xcconfig)")
                }
            }
        }
        .navigationTitle("Debug Diagnostics")
        .task {
            result = await AppEnvironment.clientConfigService().check()
        }
    }
}
#endif
