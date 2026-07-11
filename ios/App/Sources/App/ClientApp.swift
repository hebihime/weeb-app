// ios/App/Sources/App/ClientApp.swift — SLICE_S7_CONTRACT.md §9b.
//
// The SwiftUI App entry point. Both the Weeb and Friki targets compile this exact same file — brand is
// a build-time flavor (different target, different Info.plist/Assets.xcassets), never a runtime branch
// here (§1c: "no runtime brand switch exists, ever"). The wordmark display name comes straight from
// Info.plist's CFBundleDisplayName, which each target's Brand-*.xcconfig sets to a string containing the
// brand name — that's what makes `assertVisible: ".*${WORDMARK}.*"` in the Maestro smoke pass.

import SwiftUI
import AppShell

@main
struct ClientApp: App {
    init() {
        #if DEBUG
        // Install the E2E harness locale override (Maestro `appLocale=<code>`) before any view renders.
        // Compiled out of release entirely — LaunchLocale does not exist there (DR-7.7).
        LaunchLocale.applyOverrideIfPresent()
        #endif
    }

    var body: some Scene {
        WindowGroup {
            RootView(wordmarkDisplayName: Self.wordmarkDisplayName)
        }
    }

    private static var wordmarkDisplayName: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleDisplayName") as? String ?? "Weeb App"
    }
}
