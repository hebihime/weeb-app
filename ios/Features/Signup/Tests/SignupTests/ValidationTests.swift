// ios/Features/Signup/Tests/SignupTests/ValidationTests.swift — SLICE_S7_CONTRACT.md §9c/§12, Correction 1.
//
// Unit-tests the validation boundary exactly as the contract names it: 17 -> refuse, 18 -> pass,
// 12 -> COPPA copy. `now` is injected so these are deterministic regardless of what day CI runs on.

import Foundation
import Testing
@testable import Signup

@Suite("HandleValidation")
struct HandleValidationTests {
    @Test("valid handles: letters, numbers, underscores, 3-20 chars")
    func validHandles() {
        for handle in ["abc", "nakama_forever", "user123", "AAAAAAAAAAAAAAAAAAAA"] {
            #expect(HandleValidation.isValid(handle), "\(handle) should be valid")
        }
    }

    @Test("invalid handles: too short, too long, or forbidden characters")
    func invalidHandles() {
        for handle in ["ab", "has space", "has-dash", "has.dot", "emoji🎉", "twentyOneCharactersXX", ""] {
            #expect(!HandleValidation.isValid(handle), "\(handle) should be invalid")
        }
    }
}

@Suite("BirthdateValidation — the 17/18/12 boundary (Correction 1)")
struct BirthdateValidationTests {
    private let referenceNow = ISO8601DateFormatter().date(from: "2026-07-11T00:00:00Z")!

    private func birthdate(yearsBeforeReference years: Int, monthDay: (month: Int, day: Int) = (7, 11)) -> Date {
        var components = Calendar(identifier: .gregorian).dateComponents([.year], from: referenceNow)
        components.year = (components.year ?? 2026) - years
        components.month = monthDay.month
        components.day = monthDay.day
        components.timeZone = TimeZone(identifier: "UTC")
        return Calendar(identifier: .gregorian).date(from: components)!
    }

    @Test("exactly 17 years old -> refusedUnder18")
    func seventeenIsRefused() {
        let outcome = BirthdateValidation.evaluate(birthdate: birthdate(yearsBeforeReference: 17), now: referenceNow)
        #expect(outcome == .refusedUnder18)
    }

    @Test("exactly 18 years old (birthday today) -> ok")
    func eighteenPasses() {
        let outcome = BirthdateValidation.evaluate(birthdate: birthdate(yearsBeforeReference: 18), now: referenceNow)
        #expect(outcome == .ok)
    }

    @Test("17 years old, one day before their 18th birthday -> refusedUnder18 (not yet 18)")
    func almostEighteenIsStillRefused() {
        // Reference is July 11; birthday is July 12 — hasn't happened yet this "year" of counting.
        let notYetEighteen = birthdate(yearsBeforeReference: 18, monthDay: (7, 12))
        #expect(BirthdateValidation.evaluate(birthdate: notYetEighteen, now: referenceNow) == .refusedUnder18)
    }

    @Test("exactly 12 years old -> refusedUnder13COPPA (the distinct hard floor, not the 18+ copy)")
    func twelveIsCOPPARefused() {
        let outcome = BirthdateValidation.evaluate(birthdate: birthdate(yearsBeforeReference: 12), now: referenceNow)
        #expect(outcome == .refusedUnder13COPPA)
    }

    @Test("exactly 13 years old -> refusedUnder18 (COPPA floor cleared, 18+ floor still applies)")
    func thirteenClearsCOPPAButStillUnder18() {
        let outcome = BirthdateValidation.evaluate(birthdate: birthdate(yearsBeforeReference: 13), now: referenceNow)
        #expect(outcome == .refusedUnder18)
    }

    @Test("well over 18 -> ok")
    func adultPasses() {
        let outcome = BirthdateValidation.evaluate(birthdate: birthdate(yearsBeforeReference: 30), now: referenceNow)
        #expect(outcome == .ok)
    }

    @Test("a future birthdate is invalidFormat, never a COPPA/age verdict (SEC-S7-F1)")
    func futureDateIsInvalidNotCOPPA() {
        // year = 2026 - (-5) = 2031, same month/day -> 5 years in the FUTURE.
        let future = birthdate(yearsBeforeReference: -5)
        #expect(BirthdateValidation.evaluate(birthdate: future, now: referenceNow) == .invalidFormat)
        // And through the text path (the shipping entry point).
        #expect(BirthdateValidation.evaluate(text: "2031-07-11", now: referenceNow) == .invalidFormat)
        // Boundary: a birthdate of exactly `now` is age 0, refused as under-13 — NOT invalid (it is a
        // real, if absurd, past-or-present date). Proves the guard fires only strictly in the future.
        #expect(BirthdateValidation.evaluate(birthdate: referenceNow, now: referenceNow) == .refusedUnder13COPPA)
    }

    @Test("malformed date text -> invalidFormat, never silently treated as any age")
    func malformedTextIsInvalidFormat() {
        #expect(BirthdateValidation.evaluate(text: "not-a-date", now: referenceNow) == .invalidFormat)
        #expect(BirthdateValidation.evaluate(text: "2026/07/11", now: referenceNow) == .invalidFormat)
        #expect(BirthdateValidation.evaluate(text: "", now: referenceNow) == .invalidFormat)
    }

    @Test("well-formed text at the boundary round-trips through evaluate(text:) identically to evaluate(birthdate:)")
    func textEvaluationMatchesDateEvaluation() {
        let text17 = "2009-07-11" // 17 years before 2026-07-11
        #expect(BirthdateValidation.evaluate(text: text17, now: referenceNow) == .refusedUnder18)
    }
}

@Suite("UnavailableSignupGateway")
struct UnavailableSignupGatewayTests {
    @Test("every member resolves to .couldNotSend — no fake success path exists")
    func everyMemberResolvesToCouldNotSend() async {
        let gateway = UnavailableSignupGateway()
        #expect(await gateway.checkHandleAvailability("nakama") == .couldNotSend)
        #expect(await gateway.sendEmailVerification("a@b.com") == .couldNotSend)
        #expect(await gateway.recordBirthdateAttestation(Date()) == .couldNotSend)
        #expect(await gateway.uploadAvatar(nil) == .couldNotSend)
        #expect(await gateway.submitFandomTag("Shonen") == .couldNotSend)
    }
}
