// ios/DependencyDirectionTests/Tests/DependencyDirectionTests/DependencyDirectionTests.swift
// SLICE_S7_CONTRACT.md §1b/§10/§12.
//
// "Features never import each other; only AppShell composes." Enforced by scanning the REAL `ios/`
// source tree for `import <Name>` statements, not by trusting each Package.swift's declared
// dependencies (a Package.swift could declare a dependency it never actually imports, or — the failure
// mode this test exists to catch — a Feature could add a dependency on another Feature and this would
// be the only thing standing in the way).

import Foundation
import Testing

private let iosRoot: URL = {
    // ios/DependencyDirectionTests/Tests/DependencyDirectionTests/DependencyDirectionTests.swift -> ios/
    URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent() // DependencyDirectionTests (dir)
        .deletingLastPathComponent() // Tests
        .deletingLastPathComponent() // DependencyDirectionTests (package root)
        .deletingLastPathComponent() // ios
}()

private let featuresRoot = iosRoot.appendingPathComponent("Features")

/// Every top-level Feature name, derived from `ios/Features/*` directory names — never hardcoded, so a
/// newly added empty Features/* directory is picked up automatically.
private func featureNames() -> [String] {
    guard let entries = try? FileManager.default.contentsOfDirectory(at: featuresRoot, includingPropertiesForKeys: nil) else {
        return []
    }
    return entries.filter { (try? $0.resourceValues(forKeys: [.isDirectoryKey]))?.isDirectory == true }.map(\.lastPathComponent)
}

private func swiftFiles(under root: URL) -> [URL] {
    guard let enumerator = FileManager.default.enumerator(at: root, includingPropertiesForKeys: [.isDirectoryKey]) else { return [] }
    var files: [URL] = []
    for case let url as URL in enumerator {
        let isDir = (try? url.resourceValues(forKeys: [.isDirectoryKey]))?.isDirectory ?? false
        if isDir, [".build", ".git"].contains(url.lastPathComponent) {
            enumerator.skipDescendants()
            continue
        }
        if !isDir, url.pathExtension == "swift" {
            files.append(url)
        }
    }
    return files
}

/// `import Foo`, `@testable import Foo`, `import struct Foo.Bar` (rare, but valid Swift) all start the
/// imported module name right after the word "import".
private func importedModuleNames(in fileContent: String) -> Set<String> {
    var names: Set<String> = []
    // Horizontal whitespace ONLY between tokens ([ \t], never \s) — \s matches newlines too, which let
    // an earlier version of this regex "eat" past the end of one import line and mis-capture the next
    // line's `import` keyword as if it were a module name. That bug hid a real "AppShell imports Signup"
    // false negative during development; this comment (and the fix) is the skillify note for it.
    let pattern = #"(?m)^[ \t]*(?:@testable[ \t]+)?import[ \t]+(?:\w+[ \t]+)?([A-Za-z_][A-Za-z0-9_]*)"#
    let regex = try! NSRegularExpression(pattern: pattern)
    let range = NSRange(fileContent.startIndex..., in: fileContent)
    regex.enumerateMatches(in: fileContent, range: range) { match, _, _ in
        guard let match, let r = Range(match.range(at: 1), in: fileContent) else { return }
        names.insert(String(fileContent[r]))
    }
    return names
}

@Suite("Dependency direction: Features never import each other")
struct DependencyDirectionTests {
    @Test("ios/Features/* exists and is discoverable (sanity check the scan itself isn't silently empty)")
    func featuresDirectoryIsDiscoverable() {
        let names = featureNames()
        #expect(names.contains("Signup"), "expected to find the Signup feature under \(featuresRoot.path)")
        #expect(names.count >= 5, "expected Connect/Explore/Crews/Inbox/Profile/Signup placeholders, found: \(names)")
    }

    @Test("no Feature's Sources import another Feature's module name")
    func noFeatureImportsAnotherFeature() {
        let names = Set(featureNames())
        for feature in names {
            let sourcesDir = featuresRoot.appendingPathComponent(feature).appendingPathComponent("Sources")
            guard FileManager.default.fileExists(atPath: sourcesDir.path) else { continue } // empty placeholder Features
            for file in swiftFiles(under: sourcesDir) {
                guard let content = try? String(contentsOf: file, encoding: .utf8) else { continue }
                let imported = importedModuleNames(in: content)
                let otherFeaturesImported = imported.intersection(names.subtracting([feature]))
                #expect(
                    otherFeaturesImported.isEmpty,
                    "\(file.path) (Feature '\(feature)') imports other Feature module(s): \(otherFeaturesImported) — only AppShell may compose Features"
                )
            }
        }
    }

    @Test("only AppShell imports the Signup feature module")
    func onlyAppShellImportsSignup() {
        let packagesToCheck = ["DesignKit", "Strings", "ApiKit"]
        for package in packagesToCheck {
            let sourcesDir = iosRoot.appendingPathComponent(package).appendingPathComponent("Sources")
            guard FileManager.default.fileExists(atPath: sourcesDir.path) else { continue }
            for file in swiftFiles(under: sourcesDir) {
                guard let content = try? String(contentsOf: file, encoding: .utf8) else { continue }
                #expect(
                    !importedModuleNames(in: content).contains("Signup"),
                    "\(file.path) (package '\(package)') imports Signup — only AppShell may compose a Feature"
                )
            }
        }
    }

    @Test("AppShell does import Signup (the composition this whole rule exists to allow)")
    func appShellDoesImportSignup() {
        let sourcesDir = iosRoot.appendingPathComponent("AppShell").appendingPathComponent("Sources")
        let anyFileImportsSignup = swiftFiles(under: sourcesDir).contains { file in
            guard let content = try? String(contentsOf: file, encoding: .utf8) else { return false }
            return importedModuleNames(in: content).contains("Signup")
        }
        #expect(anyFileImportsSignup, "expected AppShell to compose Signup somewhere — if this ever fails, the app has no signup entry point")
    }
}
