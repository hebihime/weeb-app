// ios/UITests/ShellLayoutUITests.swift — SLICE_S7_CONTRACT.md §9b/§9g.
//
// An in-repo XCUITest mirror of maestro/flows/brand-smoke. ONE shared source, compiled into BOTH a Weeb
// and a Friki UI-test target (project.yml), so every assertion — the tab-bar layout, the crews-CTA
// scroll, the signup walk, and the ES-locale relaunch — is proved on BOTH flavors. Maestro remains the
// CI acceptance gate (14A shared harness); this exists because the Maestro CLI is not installable in
// every dev sandbox, and it is what let us reproduce the Friki-flavor ES-relaunch failure CI caught.
//
// It is NOT a substitute for the Maestro brand-smoke matrix (that stays the ×4 required gate); it is the
// belt to Maestro's suspenders for the regressions that motivated it.

import XCTest

final class ShellLayoutUITests: XCTestCase {
    override func setUp() {
        continueAfterFailure = false
    }

    private func launch(appLocale: String? = nil) -> XCUIApplication {
        let app = XCUIApplication()
        if let appLocale {
            // Both forms the real harness might use: the `-key value` argument-domain pair AND a single
            // `appLocale=<code>` token — LaunchLocale.resolveCode handles either.
            app.launchArguments += ["-appLocale", appLocale, "appLocale=\(appLocale)"]
        }
        app.launch()
        return app
    }

    /// The five tab.* ids are all present at once (the custom-tab-bar fix), and Quests/mode.chip are absent.
    func testFiveTabsAlwaysPresentQuestsAndModeChipAbsent() {
        let app = launch()
        for tab in ["connect", "explore", "crews", "inbox", "profile"] {
            XCTAssertTrue(app.buttons["tab.\(tab)"].waitForExistence(timeout: 10), "tab.\(tab) must be visible")
        }
        XCTAssertFalse(app.buttons["tab.quests"].exists, "tab.quests must not exist (pre-G4 absence)")
        XCTAssertFalse(app.otherElements["mode.chip"].exists, "mode.chip must not render at S7 (only Online is constructible)")
        XCTAssertTrue(app.staticTexts["brand.wordmark"].exists || app.otherElements["brand.wordmark"].exists, "brand.wordmark must be visible")
    }

    /// Each tab navigates to its honest empty state.
    func testEachTabShowsItsEmptyState() {
        let app = launch()
        for tab in ["connect", "explore", "crews", "inbox", "profile"] {
            app.buttons["tab.\(tab)"].tap()
            XCTAssertTrue(
                app.descendants(matching: .any)["state.\(tab).empty"].waitForExistence(timeout: 10),
                "tapping tab.\(tab) must show state.\(tab).empty"
            )
        }
    }

    /// THE fix under test: the crews secondary CTA must be reachable — scrollable to hittable — despite
    /// the always-present bottom tab bar. Before the safeAreaInset fix, the bar obscured it and this
    /// (like the Maestro "scroll until 100% visible") could not be satisfied.
    func testCrewsPremiumCTAScrollsClearOfTabBar() {
        let app = launch()
        app.buttons["tab.crews"].tap()
        XCTAssertTrue(app.descendants(matching: .any)["state.crews.empty"].waitForExistence(timeout: 10))

        let cta = app.buttons["crews.create.premium.cta"]

        // Scroll the crews content until the CTA becomes hittable (not clipped/obscured by the tab bar).
        // A fully-off-screen scroll descendant may not even register as existent until scrolled toward,
        // so this loop drives the scroll the same way Maestro's "scroll until visible" does.
        var attempts = 0
        while !cta.isHittable && attempts < 8 {
            app.swipeUp()
            attempts += 1
        }

        if !cta.isHittable {
            let dump = app.debugDescription
            try? dump.write(toFile: "/tmp/weeb_crews_hierarchy.txt", atomically: true, encoding: .utf8)
        }
        XCTAssertTrue(cta.exists, "crews.create.premium.cta must exist in the crews empty state")
        XCTAssertTrue(cta.isHittable, "crews.create.premium.cta must scroll fully clear of the bottom tab bar and be hittable")

        // And it must sit ABOVE the tab bar frame, proving it isn't merely reported hittable while
        // visually overlapping the bar.
        let barTop = app.buttons["tab.crews"].frame.minY
        XCTAssertLessThanOrEqual(cta.frame.maxY, barTop + 1, "the CTA's bottom must be at or above the tab bar's top edge")
    }

    /// The ES-locale smoke leg (DR-6.2 / smoke.yaml final assertion): launching with the harness
    /// `appLocale=es` argument must render the handle-step title's Spanish .xcstrings value, exactly
    /// "Elige tu usuario". This drives the SAME debug-gated launch-argument path Maestro exercises.
    func testSpanishLaunchArgumentRendersSpanishHandleTitle() {
        let app = launch(appLocale: "es")

        app.buttons["signup.start"].tap()
        XCTAssertTrue(
            app.staticTexts["Elige tu usuario"].waitForExistence(timeout: 10),
            "with appLocale=es the handle-step title must render the ES value 'Elige tu usuario'"
        )
        // And the EN value must NOT be what's shown (proves the override actually took effect).
        XCTAssertFalse(app.staticTexts["Choose your handle"].exists, "ES launch arg must not still render the EN title")
    }

    /// The FULL Maestro brand-smoke sequence in order, ENDING with the ES-locale relaunch — the exact
    /// scenario CI's Friki leg failed (assert-shell + signup-walk pass, then the ES relaunch's
    /// `brand.wordmark` assertion fails). Reproducing it here on the Friki target is what surfaced the
    /// root cause; keeping it guards both flavors against regressing that relaunch.
    func testFullSmokeSequenceThenESRelaunchRendersWordmark() {
        // Leg 1: EN launch — trunk test + full shell + signup walk to the honest refusal.
        let app = launch()
        XCTAssertTrue(app.descendants(matching: .any)["brand.wordmark"].waitForExistence(timeout: 10))
        for tab in ["connect", "explore", "crews", "inbox", "profile"] {
            XCTAssertTrue(app.buttons["tab.\(tab)"].waitForExistence(timeout: 10))
        }
        walkSignupToRefusal(app)

        // Leg 2: the ES relaunch (Maestro: `launchApp clearState arguments {forceLightMode, appLocale:es}`).
        // XCUIApplication.launch() terminates the running instance and relaunches fresh; the app persists
        // nothing (§3), so this is equivalent to clearState. This is the assertion that failed on Friki.
        app.launchArguments += ["-forceLightMode", "true", "-appLocale", "es", "appLocale=es"]
        app.launch()

        XCTAssertTrue(
            app.descendants(matching: .any)["brand.wordmark"].waitForExistence(timeout: 15),
            "after the ES clearState relaunch the app must render brand.wordmark (the Friki leg's failure)"
        )
        app.buttons["signup.start"].tap()
        XCTAssertTrue(
            app.staticTexts["Elige tu usuario"].waitForExistence(timeout: 10),
            "the ES handle-step title must render 'Elige tu usuario' after the relaunch"
        )
    }

    /// Sanity counterpart: with no locale argument the app renders its default (EN on the CI runner),
    /// so the override is genuinely opt-in and does not leak into normal launches.
    func testNoLocaleArgumentRendersDefaultEnglishHandleTitle() {
        let app = launch()
        app.buttons["signup.start"].tap()
        XCTAssertTrue(app.staticTexts["Choose your handle"].waitForExistence(timeout: 10))
    }

    /// The signup shell walks to the honest gateway-refusal terminus (no fake success path).
    func testSignupWalkReachesCouldNotSend() {
        let app = launch()
        walkSignupToRefusal(app)
    }

    // MARK: - helpers

    /// The 5.14a walk: start -> handle -> email -> birthdate -> avatar-skip -> fandom -> submit, ending
    /// at the honest could-not-send refusal. Shared by the standalone walk test and the full-sequence one.
    private func walkSignupToRefusal(_ app: XCUIApplication) {
        app.buttons["signup.start"].tap()

        typeIfPresent(app, id: "signup.handle", text: "nakama_test")
        tap(app, id: "signup.handle.next")

        typeIfPresent(app, id: "signup.email", text: "test@example.com")
        tap(app, id: "signup.email.next")

        typeIfPresent(app, id: "signup.birthdate", text: "2000-01-01")
        tap(app, id: "signup.birthdate.next")

        tap(app, id: "signup.avatar.skip")

        XCTAssertTrue(app.descendants(matching: .any)["signup.fandom"].waitForExistence(timeout: 10))
        tap(app, id: "signup.fandom.option.0")
        tap(app, id: "signup.submit")

        XCTAssertTrue(
            app.descendants(matching: .any)["state.error.could_not_send"].waitForExistence(timeout: 10),
            "the signup walk must end at the honest could-not-send refusal"
        )
    }

    private func tap(_ app: XCUIApplication, id: String) {
        let el = app.descendants(matching: .any)[id]
        XCTAssertTrue(el.waitForExistence(timeout: 10), "\(id) must exist")
        el.tap()
    }

    private func typeIfPresent(_ app: XCUIApplication, id: String, text: String) {
        let field = app.textFields[id]
        if field.waitForExistence(timeout: 10) {
            field.tap()
            field.typeText(text)
        }
    }
}
