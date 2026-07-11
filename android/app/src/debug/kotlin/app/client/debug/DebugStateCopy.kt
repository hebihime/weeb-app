package app.client.debug

import app.client.designkit.state.DesignState

/**
 * English-only descriptive copy for the debug-build-only state gallery (SLICE_S7_CONTRACT.md §9d).
 * This is a QA/UX-coherence-crawl tool for Julien, never shipped in release, so it does not need to
 * satisfy the ×4-locale i18n contract the way live-reachable strings do (contracts/message-keys.json's
 * 3 substrate keys are still real, translated resource strings in :app's main res — see the
 * per-locale strings.xml files under android/app/src/main/res — this file only supplies the GALLERY's
 * own English-only walkthrough labels for the states that have no other reachable surface yet).
 */
object DebugStateCopy {
    data class Copy(val title: String, val body: String)

    fun describe(state: DesignState): Copy = when (state) {
        DesignState.EmptyDeck -> Copy("Connect — pre-first-deck", "Live in the Connect tab. See main res strings.")
        DesignState.EmptyMatches -> Copy("Inbox — matches empty", "Live in the Inbox tab. See main res strings.")
        DesignState.EmptyNakama -> Copy("Crews — get invited", "Live in the Crews tab. See main res strings.")
        DesignState.EmptyCosplay -> Copy("Explore — between contests", "Live in the Explore tab. See main res strings.")
        DesignState.ProfileEmpty -> Copy("Profile — preview-first", "Live in the Profile tab. See main res strings.")
        DesignState.Gate -> Copy("Gate (5.13b)", "A gate is pending resolution. Not reachable at S7 — no gated action exists yet.")
        DesignState.Pending -> Copy("Pending (5.13b)", "A generic pending state. No pending-chrome component ships live (§1e) — this is the design reference only.")
        DesignState.Presence -> Copy("Presence fallback (5.13b)", "Presence signal degraded. Lands with S19/S34's real presence detection.")
        DesignState.BattlePause -> Copy("Battle pause (5.13b)", "Playful-register pause chrome for the battle feature. Lands with S8/S19.")
        DesignState.Connectivity -> Copy("Connectivity offline (5.13b)", "Transport-offline rendering. Exercised live by ClientResult.Offline once a screen calls Transport.")
        DesignState.ContractMismatch -> Copy("Contract mismatch (5.13b)", "Bundled locales don't cover a server-declared locale. See §1d boot check.")
        DesignState.DignityAgeEstimationLockout -> Copy("Dignity — age-estimation lockout (5.13c)", "Neutral register. Lands with S18's estimation-first verification.")
        DesignState.DignityCounterpartProtection -> Copy("Dignity — counterpart protection (5.13c)", "Neutral register, anti-humiliation posture. Lands with the reporting/safety slice.")
        DesignState.PreFirstCon -> Copy("Pre-first-con (5.12)", "Shown before a user's first convention. Lands with S8/S19's con registry.")
        DesignState.ErrorCouldNotSend -> Copy("Signup gateway refusal — LIVE", "Reachable now via Profile → Start → walk to submit. See features/signup.")
        DesignState.SignupAgeRefusalNeutral -> Copy("Signup — under-18 refusal — LIVE", "Reachable now: enter a birthdate under 18 in the signup shell.")
        DesignState.SignupAgeRefusalCoppa -> Copy("Signup — under-13 COPPA refusal — LIVE", "Reachable now: enter a birthdate under 13 in the signup shell.")
        DesignState.LimitReachedGeneric -> Copy("Limit reached (10A deny)", "The one deny surface. No quota-consuming action exists yet at S7 to trigger it live.")
        DesignState.ErrorGeneric -> Copy("Generic problem", "The one generic Problem surface. Exercised live by ErrorMapper's non-2xx/non-429 branch.")
        DesignState.NotificationPriming -> Copy("Notification priming (DR-7.1)", "Pre-permission priming copy. Unused until S4; never asked at first launch.")
        DesignState.ShortTestPrompt -> Copy("5.14b short-test prompt", "Unreachable in the release flow until S3.")
        DesignState.InboxRequestsEmpty -> Copy("Inbox — Requests section (DR-2.2)", "Rendered inline in the live Inbox tab, always empty at S7.")
        DesignState.InboxCategoryPinnedDemo -> Copy("Inbox — category-8 pinned demo", "Fixture-only rendering rule demo; never live data (L6). Lands with S4's notification taxonomy.")
    }
}
