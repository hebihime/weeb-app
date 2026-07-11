// swift-tools-version:6.0
// ios/Strings/Package.swift — SLICE_S7_CONTRACT.md §1b/§9e.
//
// Pure logic layer: the brand-pack selection + typed message-key constants. The actual String Catalog
// RESOURCE files live natively in the App target (ios/App/Sources/Resources/*.xcstrings) — Xcode's
// per-target String Catalog compiler only runs for a native target's own Resources build phase, not for
// files bundled inside an embedded SPM package's resource bundle. Keeping the catalogs on the App target
// and this package resource-free is what makes the catalogs actually compile and resolve at runtime.
// (tools/i18n-lint reads the .xcstrings files straight off disk by path, so it does not care which
// target owns them; this split is a pure runtime-correctness concern.)

import PackageDescription

let package = Package(
    name: "Strings",
    // macOS is declared too so `swift test` runs on the host mac during local iteration; the shipping
    // target is iOS-only (App/ targets in project.yml only build for iOS).
    platforms: [.iOS(.v17), .macOS(.v13)],
    products: [
        .library(name: "Strings", targets: ["Strings"])
    ],
    targets: [
        .target(name: "Strings"),
        .testTarget(name: "StringsTests", dependencies: ["Strings"]),
    ]
)
