// swift-tools-version:6.0
// ios/AppShell/Package.swift — SLICE_S7_CONTRACT.md §1b/§9b.
//
// Five fixed tabs, ModeContext, routing, and the signup shell composition. AppShell is the ONLY package
// allowed to import more than one Feature — that is the dependency-direction rule DependencyDirectionTests
// enforces against the actual source tree (Features never import each other; only AppShell composes).

import PackageDescription

let package = Package(
    name: "AppShell",
    platforms: [.iOS(.v17), .macOS(.v13)],
    products: [
        .library(name: "AppShell", targets: ["AppShell"])
    ],
    dependencies: [
        .package(path: "../DesignKit"),
        .package(path: "../Strings"),
        .package(path: "../ApiKit"),
        .package(path: "../Features/Signup"),
    ],
    targets: [
        .target(
            name: "AppShell",
            dependencies: ["DesignKit", "Strings", "ApiKit", "Signup"]
        ),
        .testTarget(
            name: "AppShellTests",
            dependencies: ["AppShell"]
        ),
    ]
)
