// swift-tools-version:6.0
// ios/ApiKit/Package.swift — SLICE_S7_CONTRACT.md §1b/§1d/§1e/§9f.
//
// The generated client (swift-openapi-generator SPM build plugin, generating from the checked-in
// canonical contracts/openapi.v0.json — copied in by scripts/bootstrap.sh, never committed), the
// transport, and the ONE error mapper. Deliberately depends on NEITHER DesignKit NOR Strings: the
// mapper's output is plain data (`MappedOutcome`), not a rendered view — AppShell is the only place that
// turns an outcome into a DesignKit view, which is what keeps this layer a pure, swappable, UI-free
// network seam (1A module-isolation discipline in client idiom, §1b).

import PackageDescription

let package = Package(
    name: "ApiKit",
    platforms: [.iOS(.v17), .macOS(.v13)],
    products: [
        .library(name: "ApiKit", targets: ["ApiKit"])
    ],
    dependencies: [
        .package(url: "https://github.com/apple/swift-openapi-generator", from: "1.6.0"),
        .package(url: "https://github.com/apple/swift-openapi-runtime", from: "1.6.0"),
        .package(url: "https://github.com/apple/swift-openapi-urlsession", from: "1.0.0"),
    ],
    targets: [
        .target(
            name: "ApiKit",
            dependencies: [
                .product(name: "OpenAPIRuntime", package: "swift-openapi-runtime"),
                .product(name: "OpenAPIURLSession", package: "swift-openapi-urlsession"),
            ],
            plugins: [
                .plugin(name: "OpenAPIGenerator", package: "swift-openapi-generator")
            ]
        ),
        .testTarget(
            name: "ApiKitTests",
            dependencies: ["ApiKit"]
        ),
    ]
)
