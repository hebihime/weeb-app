// ios/Features/Signup/Sources/Signup/Validation.swift — SLICE_S7_CONTRACT.md §9c, Correction 1.
//
// Real client-side validation (handle charset, birthdate). Correction 1: the birthdate step refuses any
// attested age UNDER 18 (routes to the neutral-plain refusal state), with attested UNDER 13 rendering
// the distinct hard COPPA-refusal copy as a sub-case. Client-side attestation only — server-side 18+
// enforcement + estimation-first verification are S3/S18's job; this is the honest floor a native app
// with a signup shell must not let a self-attested minor walk past today.

import Foundation

public enum HandleValidation {
    /// Letters, numbers, underscores only; 3-20 characters (DESIGN.md doesn't specify exact bounds —
    /// this is the conservative, uncontroversial floor every real handle system uses).
    private static let pattern = "^[A-Za-z0-9_]{3,20}$"

    public static func isValid(_ handle: String) -> Bool {
        handle.range(of: pattern, options: .regularExpression) != nil
    }
}

public enum BirthdateValidation {
    public enum Outcome: Sendable, Equatable {
        case ok
        /// Correction 1's main floor: this app is actor A2 (18+); the web funnel is the ONLY
        /// under-18-visible surface (A1, T10-A). Routes to the neutral-plain refusal state.
        case refusedUnder18
        /// The distinct hard COPPA sub-case (US law absolute floor at 13) — different copy, same
        /// neutral-plain register, still refused before under18 would even apply.
        case refusedUnder13COPPA
        case invalidFormat
    }

    public static let inputFormat = "yyyy-MM-dd"

    private static func formatter() -> DateFormatter {
        let formatter = DateFormatter()
        formatter.dateFormat = inputFormat
        formatter.calendar = Calendar(identifier: .gregorian)
        formatter.timeZone = TimeZone(identifier: "UTC")
        formatter.locale = Locale(identifier: "en_US_POSIX") // parsing a fixed machine format, not display
        formatter.isLenient = false // "2026/07/11" must NOT silently parse as 2026-07-11
        return formatter
    }

    /// Belt-and-suspenders on top of `isLenient = false`: DateFormatter's leniency flag is not always
    /// enough to reject a slash-separated date on every OS version, so this also checks the exact
    /// character shape before trusting the formatter's result.
    private static let strictShape = "^[0-9]{4}-[0-9]{2}-[0-9]{2}$"

    public static func parse(_ text: String) -> Date? {
        guard text.range(of: strictShape, options: .regularExpression) != nil else { return nil }
        return formatter().date(from: text)
    }

    /// `now` is injectable so the 17->refuse / 18->pass / 12->COPPA boundary is exactly, deterministically
    /// testable instead of depending on the wall clock the test happens to run on.
    public static func evaluate(birthdate: Date, now: Date = Date()) -> Outcome {
        // A birthdate in the future is invalid INPUT (a typo / bad year), never an age verdict. Without
        // this guard, `completeElapsedYears` returns a negative number, which trips `years < 13` and
        // mis-renders the hard COPPA-under-13 refusal on what is really a data error — wrong copy, and a
        // minor-protection verdict fabricated from a fat-fingered date. Treat it as invalidFormat.
        if birthdate > now { return .invalidFormat }
        let years = completeElapsedYears(from: birthdate, to: now)
        if years < 13 { return .refusedUnder13COPPA }
        if years < 18 { return .refusedUnder18 }
        return .ok
    }

    public static func evaluate(text: String, now: Date = Date()) -> Outcome {
        guard let date = parse(text) else { return .invalidFormat }
        return evaluate(birthdate: date, now: now)
    }

    /// Complete elapsed years (not a naive year-subtraction) — correctly handles "birthday hasn't
    /// happened yet this year," which is the difference between someone turning 18 tomorrow and someone
    /// who turned 18 yesterday.
    static func completeElapsedYears(from birthdate: Date, to now: Date) -> Int {
        Calendar(identifier: .gregorian).dateComponents([.year], from: birthdate, to: now).year ?? 0
    }
}
