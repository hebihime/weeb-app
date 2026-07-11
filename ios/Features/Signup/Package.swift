// swift-tools-version:6.0
// ios/Features/Signup/Package.swift — SLICE_S7_CONTRACT.md §1b/§9c.
//
// The 5.14a shell: handle -> verified-email -> birthdate attest -> avatar-or-skip -> one fandom tag.
// Depends on DesignKit + Strings for rendering and the shared deny surface, but NOT on ApiKit or any
// other Feature — Features never import each other; only AppShell composes (the dependency-direction
// rule DependencyDirectionTests enforces against the real source tree).

import PackageDescription

let package = Package(
    name: "Signup",
    platforms: [.iOS(.v17), .macOS(.v13)],
    products: [
        .library(name: "Signup", targets: ["Signup"])
    ],
    dependencies: [
        .package(path: "../../DesignKit"),
        .package(path: "../../Strings"),
    ],
    targets: [
        .target(
            name: "Signup",
            dependencies: ["DesignKit", "Strings"]
        ),
        .testTarget(
            name: "SignupTests",
            dependencies: ["Signup"]
        ),
    ]
)
