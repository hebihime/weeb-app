// swift-tools-version:6.0
// ios/DesignKit/Package.swift — SLICE_S7_CONTRACT.md §1b/§9a.
//
// Tokens, component kit, the StateView + 23-state catalog (see StateCatalog.swift for the honest 18/23
// Phase-1 checkpoint note), the ONE LimitReached surface, the ONE generic Problem surface, and the
// Iconography seam (bundled fallback glyph set — Correction 2).
//
// This package's OWN test target deliberately does not depend on swift-snapshot-testing — a headless
// `swift test` run has no live SwiftUI rendering host, so evaluating a View's `.body` here crashes the
// process (see StateViewSnapshotTests.swift's header). The real pixel-level snapshot suite the contract
// calls for ("every state snapshot-tested per flavor per locale") is wired at the project.yml level as
// an App-hosted UI-test target, which runs inside a real iOS Simulator process via `xcodebuild test`.

import PackageDescription

let package = Package(
    name: "DesignKit",
    platforms: [.iOS(.v17), .macOS(.v13)],
    products: [
        .library(name: "DesignKit", targets: ["DesignKit"])
    ],
    dependencies: [
        .package(path: "../Strings")
    ],
    targets: [
        .target(
            name: "DesignKit",
            dependencies: ["Strings"],
            resources: [.process("Resources")]
        ),
        .testTarget(
            name: "DesignKitTests",
            dependencies: ["DesignKit"]
        ),
    ]
)
