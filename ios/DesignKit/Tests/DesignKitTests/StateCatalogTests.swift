// ios/DesignKit/Tests/DesignKitTests/StateCatalogTests.swift — SLICE_S7_CONTRACT.md §9d/§12.

import Foundation
import Testing
@testable import DesignKit

@Suite("StateCatalog")
struct StateCatalogTests {
    @Test("every state has a unique id")
    func idsAreUnique() {
        let ids = StateCatalog.all.map(\.id)
        #expect(Set(ids).count == ids.count)
    }

    @Test("spec(id:) resolves every id in the catalog")
    func lookupResolvesEveryID() {
        for spec in StateCatalog.all {
            #expect(StateCatalog.spec(id: spec.id)?.id == spec.id)
        }
    }

    @Test("spec(id:) returns nil for an unknown id")
    func lookupReturnsNilForUnknown() {
        #expect(StateCatalog.spec(id: "not.a.real.state") == nil)
    }

    @Test("the honest gateway-refusal state is live, neutral register (§9c)")
    func couldNotSendIsLiveAndNeutral() {
        let spec = StateCatalog.spec(id: "error.couldNotSend")
        #expect(spec?.reachability == .live)
        #expect(spec?.register == .neutral)
    }

    @Test("both Correction-1 age-refusal states exist, are live, and are neutral register")
    func ageRefusalStatesAreLiveAndNeutral() {
        for id in ["signup.refusal.under18", "signup.refusal.under13"] {
            let spec = StateCatalog.spec(id: id)
            #expect(spec != nil, "\(id) must exist in the catalog")
            #expect(spec?.reachability == .live)
            #expect(spec?.register == .neutral)
        }
    }

    @Test("every tab empty state is live (the release-shipped honest empties, L6)")
    func tabEmptiesAreLive() {
        let tabEmpties = StateCatalog.all.filter { $0.family == .tabEmpty }
        #expect(tabEmpties.count == 5)
        for spec in tabEmpties {
            #expect(spec.reachability == .live)
        }
    }

    @Test("the three shared error/deny surfaces bind to the exact contracts/message-keys.json keys")
    func errorSurfacesBindToContractKeys() {
        let byID = Dictionary(uniqueKeysWithValues: StateCatalog.all.map { ($0.id, $0) })
        #expect(byID["error.couldNotSend"]?.bodyKey == "error.could_not_send")
        #expect(byID["error.generic"]?.bodyKey == "error.generic")
        #expect(byID["limitReached.generic"]?.bodyKey == "limit_reached.generic")
    }

    @Test("catalog count is named honestly against the ledger's 23 (Phase-1 checkpoint, see file header)")
    func catalogCountIsFlaggedAgainstLedger() {
        // This is deliberately NOT `#expect(StateCatalog.all.count == 23)` — see the file-header comment
        // documenting the Phase-1 checkpoint. Pinning the actual count here means any future addition or
        // removal is a reviewed, intentional diff instead of a silent drift either direction.
        #expect(StateCatalog.all.count == 18)
    }
}
