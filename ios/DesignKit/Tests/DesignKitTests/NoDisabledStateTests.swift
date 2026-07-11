// ios/DesignKit/Tests/DesignKitTests/NoDisabledStateTests.swift — SLICE_S7_CONTRACT.md §9a/§12.
//
// Token-layer law 3 (absence, not disablement) and law 6 (time only adds value) are enforced at the
// TYPE level — there is no `case disabled` anywhere in this package. That is a compile-time property,
// not a runtime flag that could be flipped back on. This test is the deterministic proof: it walks every
// CaseIterable enum this package exports and asserts none of its case names spell a forbidden concept.
// It also cross-checks against design/tokens.v1.json's own `forbidden_token_groups` list so a manifest
// edit and this test can never silently drift apart (a real repo-relative file read, not a duplicated
// literal list).

import Foundation
import Testing
@testable import DesignKit

@Suite("No disabled/locked/decay state exists")
struct NoDisabledStateTests {
    // "grays" would false-positive-match if we ever added a color named e.g. "grayscale" — the forbidden
    // groups are specifically about STATE semantics, so match on the exact forbidden words from the
    // manifest, not arbitrary substrings.
    static let forbiddenWords = ["disabled", "locked", "grayed", "decay", "expiry", "streak_loss"]

    @Test("Tokens.Register has no disabled/locked case")
    func registerHasNoForbiddenCase() {
        for register in Tokens.Register.allCases {
            let name = String(describing: register)
            assertClean(name, context: "Tokens.Register")
        }
    }

    @Test("Glyph has no disabled/locked case")
    func glyphHasNoForbiddenCase() {
        for glyph in Glyph.allCases {
            assertClean(String(describing: glyph), context: "Glyph")
        }
    }

    @Test("StateFamily has no disabled/locked/decay case")
    func stateFamilyHasNoForbiddenCase() {
        for family in StateFamily.allCases {
            assertClean(family.rawValue, context: "StateFamily")
        }
    }

    @Test("StateReachability has no disabled/locked case")
    func reachabilityHasNoForbiddenCase() {
        for reachability in StateReachability.allCases {
            assertClean(String(describing: reachability), context: "StateReachability")
        }
    }

    @Test("StateCatalog carries no state whose id/family spells a forbidden concept")
    func stateCatalogHasNoForbiddenState() {
        for spec in StateCatalog.all {
            assertClean(spec.id, context: "StateCatalog id")
        }
    }

    @Test("this test's own forbidden-word list matches design/tokens.v1.json exactly")
    func forbiddenWordListMatchesManifest() throws {
        // Walk up from this test file's known repo-relative location to find design/tokens.v1.json,
        // rather than hardcoding an absolute path — keeps the test portable across checkouts/CI.
        let thisFile = URL(fileURLWithPath: #filePath)
        // ios/DesignKit/Tests/DesignKitTests/NoDisabledStateTests.swift -> repo root is 4 levels up.
        let repoRoot = thisFile
            .deletingLastPathComponent() // DesignKitTests
            .deletingLastPathComponent() // Tests
            .deletingLastPathComponent() // DesignKit
            .deletingLastPathComponent() // ios
            .deletingLastPathComponent() // repo root
        let manifestURL = repoRoot.appendingPathComponent("design/tokens.v1.json")
        let data = try Data(contentsOf: manifestURL)
        let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        let manifestGroups = json?["forbidden_token_groups"] as? [String] ?? []
        #expect(Set(manifestGroups) == Set(Self.forbiddenWords), "design/tokens.v1.json forbidden_token_groups drifted from this test's list")
    }

    private func assertClean(_ name: String, context: String) {
        let lowered = name.lowercased()
        for word in Self.forbiddenWords {
            let bare = word.replacingOccurrences(of: "_", with: "")
            #expect(!lowered.contains(bare), "\(context) case \"\(name)\" contains forbidden concept \"\(word)\" (law 3/6)")
        }
    }
}
