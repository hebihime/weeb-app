// ios/DesignKit/Sources/DesignKit/StateCatalog.swift — SLICE_S7_CONTRACT.md §9d.
//
// PHASE-1 CHECKPOINT FLAG (per §9d's own instruction: "if faithful transcription yields a different
// enumeration, that is a NAMED Phase-1 checkpoint flag, never a silent adaptation"):
//
//   This catalog enumerates 18 states, transcribed faithfully from every state family §9d names by name
//   (5.13a's four tab empties, 5.13b's five gate/connectivity states, 5.13c's two dignity screens, 5.12's
//   pre-first-con, the two Correction-1 signup-age refusals, and the three shared error/deny surfaces)
//   plus the honest signup-refusal terminus. The ledger's acceptance number is 23. `design/06 Safety,
//   Settings & States.dc.html` groups several of those families under section headers (5.16 consent, 5.27
//   -30 standing, 5.35 Premium) that S0's own scope ruling excludes from S7 outright (§0: "does NOT...
//   build... IAP"; reputation "standing" screens and Premium paywall/lapsed states are S14/S16/S35's
//   surfaces, not S7's). Padding this catalog with out-of-scope states to hit 23 would be exactly the
//   silent adaptation §9d forbids in the other direction. Flagging the 18/23 gap here, by name, for the
//   Phase-1 UX-coherence crawl (§12 evidence 5) is the faithful-transcription-or-named-flag choice.
//
// Every state below is keyed ×4 locales via Strings/App catalogs (i18n-lint enforces parity) and
// reachable either live (in the release flow) or through the debug-only state gallery (never release).

import Foundation

public enum StateFamily: String, Sendable, CaseIterable {
    case tabEmpty            // 5.13a
    case gateConnectivity    // 5.13b
    case dignity             // 5.13c
    case preFirstCon         // 5.12
    case signupAgeRefusal    // Correction 1
    case errorSurface        // token law 2 / DR-7.3 / §9c
}

/// Whether a state is reached by real user flows at S7 (release-shipped code path) or only through the
/// debug-only gallery (never compiled into release — §9d, the L15 posture).
public enum StateReachability: Sendable, CaseIterable {
    case live
    case galleryOnly
}

public struct StateSpec: Sendable, Identifiable {
    public let id: String
    public let family: StateFamily
    public let titleKey: String
    public let bodyKey: String
    public let ctaKey: String?
    public let glyph: Glyph
    public let register: Tokens.Register
    public let reachability: StateReachability

    public init(
        id: String,
        family: StateFamily,
        titleKey: String,
        bodyKey: String,
        ctaKey: String? = nil,
        glyph: Glyph,
        register: Tokens.Register,
        reachability: StateReachability
    ) {
        self.id = id
        self.family = family
        self.titleKey = titleKey
        self.bodyKey = bodyKey
        self.ctaKey = ctaKey
        self.glyph = glyph
        self.register = register
        self.reachability = reachability
    }
}

public enum StateCatalog {
    /// The 18 transcribed states — see the Phase-1 checkpoint note above. `CaseIterable`-style access via
    /// `.all`; each `id` doubles as its DesignKit-internal lookup key (distinct from the a11y identifiers
    /// AppShell/Features assign to the *live* tab-empty views, which follow maestro/README's contract).
    public static let all: [StateSpec] = [
        // 5.13a — tab empties (playful except explore, which is the ONE neutral zero-result state)
        StateSpec(
            id: "connect.empty", family: .tabEmpty,
            titleKey: "state.connect.empty.title", bodyKey: "state.connect.empty.body",
            ctaKey: "state.connect.empty.cta", glyph: .emptyDeck,
            register: .playful, reachability: .live
        ),
        StateSpec(
            id: "explore.empty", family: .tabEmpty,
            titleKey: "state.explore.empty.title", bodyKey: "state.explore.empty.body",
            ctaKey: "state.explore.empty.cta", glyph: .emptyExplore,
            register: .neutral, reachability: .live
        ),
        StateSpec(
            id: "crews.empty", family: .tabEmpty,
            titleKey: "state.crews.empty.title", bodyKey: "state.crews.empty.body",
            ctaKey: "crews.create.premium.cta", glyph: .emptyCrews,
            register: .playful, reachability: .live
        ),
        StateSpec(
            id: "inbox.empty", family: .tabEmpty,
            titleKey: "state.inbox.empty.title", bodyKey: "state.inbox.empty.body",
            ctaKey: nil, glyph: .emptyInbox,
            register: .playful, reachability: .live
        ),
        StateSpec(
            id: "profile.empty", family: .tabEmpty,
            titleKey: "state.profile.empty.title", bodyKey: "state.profile.empty.body",
            ctaKey: "state.profile.empty.cta", glyph: .emptyProfile,
            register: .playful, reachability: .live
        ),

        // 5.13b — gate & connectivity (neutral-plain throughout)
        StateSpec(
            id: "gate.pending", family: .gateConnectivity,
            titleKey: "state.gate.pending.title", bodyKey: "state.gate.pending.body",
            glyph: .gatePending, register: .neutral, reachability: .galleryOnly
        ),
        StateSpec(
            id: "presence.fallback", family: .gateConnectivity,
            titleKey: "state.presence.fallback.title", bodyKey: "state.presence.fallback.body",
            glyph: .presenceFallback, register: .neutral, reachability: .galleryOnly
        ),
        StateSpec(
            id: "battle.pause", family: .gateConnectivity,
            titleKey: "state.battle.pause.title", bodyKey: "state.battle.pause.body",
            glyph: .battlePause, register: .neutral, reachability: .galleryOnly
        ),
        StateSpec(
            id: "connectivity.offline", family: .gateConnectivity,
            titleKey: "state.connectivity.offline.title", bodyKey: "state.connectivity.offline.body",
            glyph: .connectivityOffline, register: .neutral, reachability: .live // ApiKit mapper, transport-offline
        ),
        StateSpec(
            id: "contract.mismatch", family: .gateConnectivity,
            titleKey: "state.contract.mismatch.title", bodyKey: "state.contract.mismatch.body",
            glyph: .contractMismatch, register: .neutral, reachability: .live // boot check, §1d
        ),

        // 5.13c — dignity screens (neutral-plain, zero accusation)
        StateSpec(
            id: "dignity.ageLockout", family: .dignity,
            titleKey: "state.dignity.ageLockout.title", bodyKey: "state.dignity.ageLockout.body",
            glyph: .dignityShield, register: .neutral, reachability: .galleryOnly
        ),
        StateSpec(
            id: "dignity.counterpartProtection", family: .dignity,
            titleKey: "state.dignity.counterpartProtection.title", bodyKey: "state.dignity.counterpartProtection.body",
            glyph: .dignityShield, register: .neutral, reachability: .galleryOnly
        ),

        // 5.12 — pre-first-con posture (playful)
        StateSpec(
            id: "preFirstCon", family: .preFirstCon,
            titleKey: "state.preFirstCon.title", bodyKey: "state.preFirstCon.body",
            glyph: .preFirstCon, register: .playful, reachability: .galleryOnly
        ),

        // Correction 1 — the 18+ floor + the distinct under-13 COPPA sub-case (neutral-plain, live)
        StateSpec(
            id: "signup.refusal.under18", family: .signupAgeRefusal,
            titleKey: "signup.refusal.under18.title", bodyKey: "signup.refusal.under18.body",
            glyph: .dignityShield, register: .neutral, reachability: .live
        ),
        StateSpec(
            id: "signup.refusal.under13", family: .signupAgeRefusal,
            titleKey: "signup.refusal.under13.title", bodyKey: "signup.refusal.under13.body",
            glyph: .dignityShield, register: .neutral, reachability: .live
        ),

        // The shared error/deny surfaces (§9c/§1e/DR-7.3) — all three are live, release-shipped code.
        StateSpec(
            id: "error.couldNotSend", family: .errorSurface,
            titleKey: "state.error.couldNotSend.title", bodyKey: MessageKeyBodyKeys.errorCouldNotSend,
            glyph: .couldNotSend, register: .neutral, reachability: .live
        ),
        StateSpec(
            id: "error.generic", family: .errorSurface,
            titleKey: "state.error.generic.title", bodyKey: MessageKeyBodyKeys.errorGeneric,
            glyph: .problemGeneric, register: .neutral, reachability: .live
        ),
        StateSpec(
            id: "limitReached.generic", family: .errorSurface,
            titleKey: "state.limitReached.title", bodyKey: MessageKeyBodyKeys.limitReachedGeneric,
            glyph: .limitReached, register: .neutral, reachability: .live
        ),
    ]

    public static func spec(id: String) -> StateSpec? {
        all.first { $0.id == id }
    }
}

/// Bridges to the exact dotted keys contracts/message-keys.json defines, without DesignKit importing
/// Strings' `MessageKey` type directly into every call site's vocabulary.
enum MessageKeyBodyKeys {
    static let errorCouldNotSend = "error.could_not_send"
    static let errorGeneric = "error.generic"
    static let limitReachedGeneric = "limit_reached.generic"
}
