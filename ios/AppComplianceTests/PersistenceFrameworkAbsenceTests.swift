// ios/AppComplianceTests/PersistenceFrameworkAbsenceTests.swift — SLICE_S7_CONTRACT.md §3/§12.
//
// "A client test asserts no persistence framework (CoreData/SwiftData/Room/DataStore) is linked into
// either app target."
//
// SKILLIFY NOTE (a real methodology bug found while building this test, per CLAUDE.md's rule): an
// earlier version scanned dyld's loaded-image list at runtime (`_dyld_image_count`/`_dyld_get_image_name`)
// hoping to catch "is CoreData/SwiftData/CloudKit actually linked." That measures the wrong thing: when
// hosted inside a real iOS Simulator process, CoreData.framework, SwiftData.framework, and
// CloudKit.framework are ALL present in the loaded-image list regardless of what this app explicitly
// imports — the OS/UIKit/XCTest's own runtime plumbing pulls them in incidentally. §3's actual claim is
// about what the APP TARGET links because of code IT wrote, which is a source/build-graph property, not
// a whole-process runtime property. The correct check (this file, now) is the same static-scan technique
// DependencyDirectionTests already uses: grep the real `import` statements across every Swift source
// file this app's own targets/packages compile, and separately confirm no Package.swift/project.yml
// declares CoreData/SwiftData/Realm/GRDB as a dependency. Neither needs a hosted test at all — this
// file stays in AppComplianceTests for now for continuity, but is pure static analysis.

import Foundation
import Testing

private let iosRoot: URL = {
    // ios/AppComplianceTests/PersistenceFrameworkAbsenceTests.swift -> ios/
    URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent() // AppComplianceTests
        .deletingLastPathComponent() // ios
}()

/// Every persistence-framework import that would violate §3's zero-device-persistence law.
private let forbiddenImports = ["CoreData", "SwiftData", "Realm", "RealmSwift", "GRDB", "SQLite"]

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

/// The real source roots the Weeb/Friki app targets actually compile (App itself + every local SPM
/// package they transitively depend on) — matches project.yml's `packages:` list.
private let ownedSourceRoots = [
    "App/Sources",
    "AppShell/Sources",
    "DesignKit/Sources",
    "Strings/Sources",
    "ApiKit/Sources",
    "Features/Signup/Sources",
]

@Suite("Zero device persistence — no persistence framework linked (§3)")
struct PersistenceFrameworkAbsenceTests {
    @Test("no Swift source file this app compiles imports a persistence framework", arguments: ownedSourceRoots)
    func noSourceImportsPersistenceFramework(root: String) {
        let dir = iosRoot.appendingPathComponent(root)
        guard FileManager.default.fileExists(atPath: dir.path) else {
            Issue.record("expected source root to exist: \(dir.path)")
            return
        }
        for file in swiftFiles(under: dir) {
            guard let content = try? String(contentsOf: file, encoding: .utf8) else { continue }
            for forbidden in forbiddenImports {
                let pattern = "(?m)^\\s*import\\s+\(forbidden)\\b"
                let hasImport = content.range(of: pattern, options: .regularExpression) != nil
                #expect(!hasImport, "\(file.path) imports \(forbidden) — §3 requires zero device persistence")
            }
        }
    }

    @Test("no local package declares a persistence-framework dependency in Package.swift", arguments: [
        "ApiKit/Package.swift", "AppShell/Package.swift", "DesignKit/Package.swift",
        "Strings/Package.swift", "Features/Signup/Package.swift",
    ])
    func noPackageDeclaresPersistenceDependency(packageManifest: String) throws {
        let path = iosRoot.appendingPathComponent(packageManifest)
        let content = try String(contentsOf: path, encoding: .utf8)
        for forbidden in forbiddenImports {
            #expect(!content.contains(forbidden), "\(packageManifest) references \(forbidden) — §3 requires zero device persistence")
        }
    }

    @Test("sanity: the scan itself finds real source files (not silently empty)")
    func scanFindsRealFiles() {
        let total = ownedSourceRoots.reduce(0) { count, root in
            count + swiftFiles(under: iosRoot.appendingPathComponent(root)).count
        }
        #expect(total > 10, "expected to find a meaningful number of Swift files across all owned source roots, found \(total)")
    }
}
