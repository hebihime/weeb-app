// ios/AppShell/Sources/AppShell/SignupHost.swift — SLICE_S7_CONTRACT.md §9c.
//
// AppShell is the ONLY place that composes a Feature into the running app (§1b's dependency-direction
// rule) — this is that composition for Signup. It registers `UnavailableSignupGateway` (the ONLY gateway
// ever registered, in ALL build configurations, per §9c/§13's adoption record) and nothing else.

import SwiftUI
import Signup

struct SignupHost: View {
    @Binding var isPresented: Bool

    var body: some View {
        NavigationStack {
            SignupFlowView(gateway: UnavailableSignupGateway(), isPresented: $isPresented)
        }
    }
}
