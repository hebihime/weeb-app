// ios/AppShell/Sources/AppShell/DebugStateGalleryView.swift — SLICE_S7_CONTRACT.md §9d.
//
// Debug-build-only state gallery. Renders all states in DesignKit's StateCatalog (see StateCatalog.swift
// for the honest 18/23 Phase-1 checkpoint note) — the L15 posture: design-DISPLAY for reconciliation and
// the UX crawl, never shipped showcase. Never compiled into release (`#if DEBUG`).

#if DEBUG
import SwiftUI
import DesignKit

struct DebugStateGalleryView: View {
    var body: some View {
        List(StateCatalog.all) { spec in
            NavigationLink(spec.id) {
                StateView(spec: spec, accessibilityID: "gallery.\(spec.id)")
                    .navigationTitle(spec.id)
            }
        }
        .navigationTitle("State Gallery (\(StateCatalog.all.count) states)")
    }
}
#endif
