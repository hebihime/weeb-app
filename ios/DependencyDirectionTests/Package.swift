// swift-tools-version:6.0
// ios/DependencyDirectionTests/Package.swift — SLICE_S7_CONTRACT.md §1b/§10/§12.
//
// The 1A module-isolation discipline in client idiom, made a real (not aspirational) test: Features
// never import each other; only AppShell composes. This package has no library target and no
// dependency on any of the other packages — it is a pure filesystem/text scan over the real `ios/`
// source tree, so it can never go stale relative to what actually got built (unlike a hand-maintained
// list of "allowed" imports).

import PackageDescription

let package = Package(
    name: "DependencyDirectionTests",
    platforms: [.iOS(.v17), .macOS(.v13)],
    targets: [
        .testTarget(name: "DependencyDirectionTests")
    ]
)
