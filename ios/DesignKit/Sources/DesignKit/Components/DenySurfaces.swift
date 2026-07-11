// ios/DesignKit/Sources/DesignKit/Components/DenySurfaces.swift — SLICE_S7_CONTRACT.md §1e/§6/§9c.
//
// The two named, singular deny/error surfaces every consumer of ApiKit's error mapper renders into.
// 429 -> LimitReachedView (bound to limit_reached.generic). Every other non-2xx status -> ProblemView
// (bound to error.generic, or error.could_not_send for the signup gateway specifically). There is no
// second deny UI anywhere in this kit — DesignKit ships no "pending..." indicator for deny/void-class
// operations (the leak-shaped component does not exist for a later slice to misuse).

import SwiftUI

public struct LimitReachedView: View {
    public init() {}

    public var body: some View {
        StateView(
            spec: StateCatalog.spec(id: "limitReached.generic")!,
            accessibilityID: "state.limit_reached.generic"
        )
    }
}

public struct ProblemView: View {
    private let variant: Variant

    public enum Variant: Sendable {
        /// The generic could-not-send pattern (§9c: UnavailableSignupGateway's only outcome, all
        /// configs). This is the exact a11y ID maestro/README.md's contract mandates.
        case couldNotSend
        /// Every other non-2xx, non-429 status (§1e, 404-uniformity: 403/404/410 render byte-identical).
        case generic

        var stateID: String {
            switch self {
            case .couldNotSend: return "error.couldNotSend"
            case .generic: return "error.generic"
            }
        }

        var accessibilityID: String {
            switch self {
            case .couldNotSend: return "state.error.could_not_send"
            case .generic: return "state.error.generic"
            }
        }
    }

    public init(variant: Variant = .generic) {
        self.variant = variant
    }

    public var body: some View {
        StateView(
            spec: StateCatalog.spec(id: variant.stateID)!,
            accessibilityID: variant.accessibilityID
        )
    }
}
