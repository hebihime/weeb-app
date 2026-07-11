// ios/Features/Signup/Sources/Signup/SignupGateway.swift — SLICE_S7_CONTRACT.md §9c/§11.
//
// The S3 seam. ~5 members mirroring the 5.14a fields (handle, verified email, birthdate attestation,
// avatar, fandom tag) — a real S3 implementation posts each field to its own backend verb (handle
// availability, email verification send, age-attestation record, avatar upload, fandom tag write).
// S7 registers ONLY `UnavailableSignupGateway`, in ALL build configurations (AppShell composes it, never
// a mock success stub) — every one of these calls resolves to `.couldNotSend`. Activation for S3 is a
// contract regen + one new conforming type; this protocol and the flow UI do not change.

import Foundation

public enum SignupStepResult: Sendable, Equatable {
    /// The ONE outcome this seam can produce at S7 (§9c: no fake success path exists to remove later).
    case couldNotSend
}

public protocol SignupGateway: Sendable {
    func checkHandleAvailability(_ handle: String) async -> SignupStepResult
    func sendEmailVerification(_ email: String) async -> SignupStepResult
    func recordBirthdateAttestation(_ birthdate: Date) async -> SignupStepResult
    func uploadAvatar(_ imageData: Data?) async -> SignupStepResult
    func submitFandomTag(_ tag: String) async -> SignupStepResult
}

/// The ONLY gateway registered in any build configuration at S7. Every member returns `.couldNotSend`
/// immediately — no network call is attempted, because there is nothing real to call yet (S3 is a
/// seam-now dependency, §11). This is what makes "no fake success path ever exists to remove later" true
/// by construction rather than by review.
public struct UnavailableSignupGateway: SignupGateway {
    public init() {}

    public func checkHandleAvailability(_ handle: String) async -> SignupStepResult { .couldNotSend }
    public func sendEmailVerification(_ email: String) async -> SignupStepResult { .couldNotSend }
    public func recordBirthdateAttestation(_ birthdate: Date) async -> SignupStepResult { .couldNotSend }
    public func uploadAvatar(_ imageData: Data?) async -> SignupStepResult { .couldNotSend }
    public func submitFandomTag(_ tag: String) async -> SignupStepResult { .couldNotSend }
}
