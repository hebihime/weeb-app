// ios/Features/Signup/Sources/Signup/SignupFlow.swift — SLICE_S7_CONTRACT.md §9c.
//
// Handle -> verified-email step -> birthdate attest (neutral-plain register, Correction 1's 18+/13
// floors) -> avatar-or-skip -> one fandom tag -> submit. Fully navigable with real client-side
// validation; submission crosses the SignupGateway seam and — because only UnavailableSignupGateway is
// ever registered — always ends at the honest could-not-send state. Signup form state is in-memory only
// (§3: no persistence of any kind, dies with the process/view).
//
// Every accessibility identifier below is exactly what maestro/README.md's contract +
// maestro/flows/brand-smoke/subflows/signup-walk.yaml assert against.

import SwiftUI
import DesignKit
import Strings

/// UIKit-only text-input modifiers (no software-keyboard concept on macOS). This package declares a
/// macOS platform too so `swift test` runs locally without a simulator (see Package.swift) — the
/// shipping target is iOS-only, where these apply for real.
extension View {
    @ViewBuilder
    fileprivate func signupTextFieldStyle(keyboard: SignupKeyboardHint = .default) -> some View {
        #if os(iOS)
        self
            .autocorrectionDisabled()
            .textInputAutocapitalization(.never)
            .keyboardType(keyboard.uiKeyboardType)
        #else
        self
        #endif
    }
}

fileprivate enum SignupKeyboardHint {
    case `default`
    case email
    case numbersAndPunctuation

    #if os(iOS)
    var uiKeyboardType: UIKeyboardType {
        switch self {
        case .default: return .default
        case .email: return .emailAddress
        case .numbersAndPunctuation: return .numbersAndPunctuation
        }
    }
    #endif
}

enum SignupStep: Equatable {
    case handle
    case email
    case birthdate
    case avatar
    case fandom
    case refusedUnder18
    case refusedUnder13
    case couldNotSend
}

@MainActor
public struct SignupFlowView: View {
    private let gateway: any SignupGateway
    @Binding var isPresented: Bool

    @State private var step: SignupStep = .handle
    @State private var handle: String = ""
    @State private var handleError: String?
    @State private var email: String = ""
    @State private var birthdateText: String = ""
    @State private var birthdateError: String?
    @State private var selectedFandomIndex: Int?
    @State private var isSubmitting = false

    public init(gateway: any SignupGateway, isPresented: Binding<Bool>) {
        self.gateway = gateway
        self._isPresented = isPresented
    }

    public var body: some View {
        VStack {
            switch step {
            case .handle: handleStep
            case .email: emailStep
            case .birthdate: birthdateStep
            case .avatar: avatarStep
            case .fandom: fandomStep
            case .refusedUnder18:
                StateView(spec: StateCatalog.spec(id: "signup.refusal.under18")!, accessibilityID: "state.signup.refusal.under18")
            case .refusedUnder13:
                StateView(spec: StateCatalog.spec(id: "signup.refusal.under13")!, accessibilityID: "state.signup.refusal.under13")
            case .couldNotSend:
                ProblemView(variant: .couldNotSend)
            }
        }
        .padding(Tokens.Spacing.xl)
        .background(Tokens.Light.groundColor)
    }

    // MARK: - Step 1: handle

    private var handleStep: some View {
        VStack(alignment: .leading, spacing: Tokens.Spacing.lg) {
            Text(L10n.string("signup.start.title"))
                .font(.token(Tokens.TypeScale.title))
            Text(L10n.string("signup.start.body"))
                .font(.token(Tokens.TypeScale.body))
                .foregroundStyle(Tokens.Light.dimColor)

            Text(L10n.string("signup.handle.title"))
                .font(.token(Tokens.TypeScale.heading))

            TextField(L10n.string("signup.handle.placeholder"), text: $handle)
                .textFieldStyle(.roundedBorder)
                .signupTextFieldStyle()
                .accessibilityIdentifier("signup.handle")

            if let handleError {
                Text(handleError).font(.token(Tokens.TypeScale.caption)).foregroundStyle(Tokens.Semantic.dangerColor)
            }

            Button(L10n.string("signup.handle.next")) {
                if HandleValidation.isValid(handle) {
                    handleError = nil
                    step = .email
                } else {
                    handleError = L10n.string("signup.handle.error.charset")
                }
            }
            .buttonStyle(PillButtonStyle(register: .playful))
            .accessibilityIdentifier("signup.handle.next")
        }
    }

    // MARK: - Step 2: verified email

    private var emailStep: some View {
        VStack(alignment: .leading, spacing: Tokens.Spacing.lg) {
            Text(L10n.string("signup.email.title")).font(.token(Tokens.TypeScale.heading))
            TextField(L10n.string("signup.email.placeholder"), text: $email)
                .textFieldStyle(.roundedBorder)
                .signupTextFieldStyle(keyboard: .email)
                .accessibilityIdentifier("signup.email")

            Button(L10n.string("signup.email.next")) {
                step = .birthdate
            }
            .buttonStyle(PillButtonStyle(register: .playful))
            .accessibilityIdentifier("signup.email.next")
        }
    }

    // MARK: - Step 3: birthdate attest (neutral-plain register — Correction 1)

    private var birthdateStep: some View {
        VStack(alignment: .leading, spacing: Tokens.Spacing.lg) {
            Text(L10n.string("signup.birthdate.title")).font(.token(Tokens.TypeScale.heading))
            TextField(L10n.string("signup.birthdate.placeholder"), text: $birthdateText)
                .textFieldStyle(.roundedBorder)
                .signupTextFieldStyle(keyboard: .numbersAndPunctuation)
                .accessibilityIdentifier("signup.birthdate")

            if let birthdateError {
                Text(birthdateError).font(.token(Tokens.TypeScale.caption)).foregroundStyle(Tokens.Semantic.dangerColor)
            }

            Button(L10n.string("signup.birthdate.next")) {
                switch BirthdateValidation.evaluate(text: birthdateText) {
                case .ok:
                    birthdateError = nil
                    step = .avatar
                case .refusedUnder18:
                    step = .refusedUnder18
                case .refusedUnder13COPPA:
                    step = .refusedUnder13
                case .invalidFormat:
                    birthdateError = L10n.string("signup.birthdate.error.invalidFormat")
                }
            }
            .buttonStyle(PillButtonStyle(register: .playful))
            .accessibilityIdentifier("signup.birthdate.next")
        }
    }

    // MARK: - Step 4: avatar or skip (DR-7.1: no OS permission asked; skip is a live action)

    private var avatarStep: some View {
        VStack(alignment: .leading, spacing: Tokens.Spacing.lg) {
            Text(L10n.string("signup.avatar.title")).font(.token(Tokens.TypeScale.heading))
            Text(L10n.string("signup.avatar.body")).font(.token(Tokens.TypeScale.body)).foregroundStyle(Tokens.Light.dimColor)

            Button(L10n.string("signup.avatar.skip")) {
                step = .fandom
            }
            .buttonStyle(PillButtonStyle(register: .neutral))
            .accessibilityIdentifier("signup.avatar.skip")
        }
    }

    // MARK: - Step 5: one fandom tag + final submit

    private var fandomStep: some View {
        VStack(alignment: .leading, spacing: Tokens.Spacing.lg) {
            Text(L10n.string("signup.fandom.title"))
                .font(.token(Tokens.TypeScale.heading))
                .accessibilityIdentifier("signup.fandom")

            Chip(label: L10n.string("signup.fandom.option.0.label"), isSelected: selectedFandomIndex == 0, register: .playful)
                .accessibilityIdentifier("signup.fandom.option.0")
                .onTapGesture { selectedFandomIndex = 0 }

            // Absence, not disablement (law 3): the submit action simply does not exist until a fandom
            // tag is picked, rather than rendering a grayed-out button. Once submitting, showing a
            // spinner IN PLACE of the button (not a disabled variant of it) keeps the same law honored.
            if isSubmitting {
                ProgressView()
            } else if selectedFandomIndex != nil {
                Button(action: { Task { await submit() } }) {
                    Text(L10n.string("signup.fandom.submit"))
                }
                .buttonStyle(PillButtonStyle(register: .playful))
                .accessibilityIdentifier("signup.submit")
            }
        }
    }

    /// Crosses the SignupGateway seam. Because only `UnavailableSignupGateway` is ever registered, this
    /// always resolves to `.couldNotSend` — there is no fake success branch to fall into.
    private func submit() async {
        isSubmitting = true
        defer { isSubmitting = false }
        _ = await gateway.checkHandleAvailability(handle)
        _ = await gateway.sendEmailVerification(email)
        if let birthdate = BirthdateValidation.parse(birthdateText) {
            _ = await gateway.recordBirthdateAttestation(birthdate)
        }
        _ = await gateway.uploadAvatar(nil)
        _ = await gateway.submitFandomTag(L10n.string("signup.fandom.option.0.label"))
        step = .couldNotSend
    }
}
