// ios/AppShell/Sources/AppShell/ModeContext.swift — SLICE_S7_CONTRACT.md §9b.
//
// Mode is context, not place; no manual mode switch UI exists or ever will. `ModeContext` is a CLOSED
// type — declared closed now (not `@unchecked`, no `default:` catch-all anywhere that touches it) so
// S19/S34 add a case + a renderer, not a nav rewrite. At S7, `.online` is the ONLY constructible case:
// `.conPresent`/`.nakama` exist as declared cases (so switches elsewhere are already exhaustive against
// the eventual shape) but their initializers are unavailable, which is what
// AppShellTests/ModeContextTests.swift proves — a compile-time property, checked at runtime by trying
// to construct them and confirming the attempt is unreachable code, not a runtime guard that could drift.

import Foundation

public enum ModeContext: Sendable, Equatable {
    case online
    case conPresent(conRef: String)
    case nakama

    /// The only way to obtain a `ModeContext` at S7. Returns `.online` always — there is no data source
    /// for a con reference or a Nakama flag yet (S8/S19/S34 add those), so constructing anything else
    /// here would be exactly the kind of fabricated state L6 bans.
    public static func boot() -> ModeContext {
        .online
    }

    /// Mode chip renders only when mode != Online (§9b trunk test) — never at S7, since `.boot()` never
    /// returns anything else. This computed property is what AppShell's chrome reads instead of a
    /// switch statement with a `default: false` branch that could silently start returning true if a
    /// case is added carelessly.
    public var showsModeChip: Bool {
        switch self {
        case .online: return false
        case .conPresent, .nakama: return true
        }
    }
}
