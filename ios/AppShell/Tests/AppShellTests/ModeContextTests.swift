// ios/AppShell/Tests/AppShellTests/ModeContextTests.swift — SLICE_S7_CONTRACT.md §9b/§12.
//
// "ModeContext closed/sealed type: Online (default, ONLY constructible case at S7)." `.boot()` is the
// one and only way to obtain a ModeContext at S7, and it always returns `.online`.

import Testing
@testable import AppShell

@Suite("ModeContext")
struct ModeContextTests {
    @Test("boot() always returns .online — the only constructible case at S7")
    func bootIsAlwaysOnline() {
        for _ in 0..<10 {
            #expect(ModeContext.boot() == .online)
        }
    }

    @Test(".online never shows the mode chip")
    func onlineNeverShowsChip() {
        #expect(ModeContext.online.showsModeChip == false)
    }

    @Test(".conPresent and .nakama DO show the mode chip (declared for S19/S34, unreachable via .boot())")
    func otherCasesShowChipWhenConstructedDirectly() {
        #expect(ModeContext.conPresent(conRef: "con_123").showsModeChip == true)
        #expect(ModeContext.nakama.showsModeChip == true)
    }
}
