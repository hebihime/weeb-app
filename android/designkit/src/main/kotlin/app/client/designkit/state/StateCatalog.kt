package app.client.designkit.state

import app.client.designkit.Glyph

/**
 * SLICE_S7_CONTRACT.md §9d — the typed state catalog enumerating every state in design 06 + DR-2.1 §3.
 * The ledger's acceptance number is 23; this is a faithful transcription of every state SLICE_S7_CONTRACT
 * names by name, landing exactly on that count (documented derivation below), so no Phase-1 checkpoint
 * flag is raised. If a future faithful re-read of design 06 (Safety, Settings & States) yields a
 * different enumeration, that re-derivation is the named checkpoint flag, not a silent edit here.
 *
 * Derivation (§9d/§9b/§8/§1e/§9c, one entry per named state):
 *  - 5.13a empties (4): deck, matches, nakama (crews), cosplay (explore)
 *  - 5.13b (6): gate, pending, presence, battle-pause, connectivity, contract-mismatch
 *  - 5.13c dignity (2): age-estimation lockout, counterpart protection
 *  - 5.12 (1): pre-first-con
 *  - §9b Profile's own empty, distinct from the 5.13a four (1): profile preview-first empty
 *  - §9c signup gateway end + Correction 1's two refusals (3): could-not-send, age-refusal (neutral),
 *    COPPA hard refusal (sub-case)
 *  - §1e the one deny surface + the one generic Problem surface (2): limit-reached, error-generic
 *  - §9c gallery-only obligations recorded for S3/S4 (2): notification-priming, 5.14b short-test prompt
 *  - §8 inbox scaffold rendering rules, fixture-only (2): Requests-section-empty, category-8
 *    pinned/undismissable demo
 *  Total: 4 + 6 + 2 + 1 + 1 + 3 + 2 + 2 = 23.
 *
 * `reachableLive` marks the handful that a released build actually renders (the tab empties + the
 * signup gateway-refusal end); everything else is debug-gallery-only per §9d (never compiled into a
 * screen a release build can navigate to — L15: design-DISPLAY for reconciliation, never shipped
 * showcase). `testTag` is the Maestro accessibility-ID contract value (maestro/README.md) for the states
 * that carry one; states with no live surface carry no testTag (nothing for Maestro to assert against).
 */
enum class DesignState(
    val id: String,
    val testTag: String?,
    val glyph: Glyph,
    val register: Register,
    val reachableLive: Boolean,
) {
    EmptyDeck("empty_deck", "state.connect.empty", Glyph.EmptyDeck, Register.Playful, reachableLive = true),
    EmptyMatches("empty_matches", "state.inbox.empty", Glyph.EmptyInbox, Register.Playful, reachableLive = true),
    EmptyNakama("empty_nakama", "state.crews.empty", Glyph.EmptyCrews, Register.Playful, reachableLive = true),
    EmptyCosplay("empty_cosplay", "state.explore.empty", Glyph.EmptyExplore, Register.Playful, reachableLive = true),
    ProfileEmpty("profile_empty", "state.profile.empty", Glyph.EmptyProfile, Register.Playful, reachableLive = true),

    Gate("gate", null, Glyph.GatePending, Register.Neutral, reachableLive = false),
    Pending("pending", null, Glyph.GatePending, Register.Neutral, reachableLive = false),
    Presence("presence", null, Glyph.PresenceFallback, Register.Neutral, reachableLive = false),
    BattlePause("battle_pause", null, Glyph.BattlePause, Register.Playful, reachableLive = false),
    Connectivity("connectivity", null, Glyph.ConnectivityOffline, Register.Neutral, reachableLive = false),
    ContractMismatch("contract_mismatch", null, Glyph.ContractMismatch, Register.Neutral, reachableLive = false),

    DignityAgeEstimationLockout("dignity_age_estimation_lockout", null, Glyph.DignityShield, Register.Neutral, reachableLive = false),
    DignityCounterpartProtection("dignity_counterpart_protection", null, Glyph.DignityShield, Register.Neutral, reachableLive = false),

    PreFirstCon("pre_first_con", null, Glyph.PreFirstCon, Register.Playful, reachableLive = false),

    ErrorCouldNotSend("error_could_not_send", "state.error.could_not_send", Glyph.CouldNotSend, Register.Neutral, reachableLive = true),
    SignupAgeRefusalNeutral("signup_age_refusal_neutral", null, Glyph.DignityShield, Register.Neutral, reachableLive = false),
    SignupAgeRefusalCoppa("signup_age_refusal_coppa", null, Glyph.DignityShield, Register.Neutral, reachableLive = false),

    LimitReachedGeneric("limit_reached_generic", null, Glyph.LimitReached, Register.Neutral, reachableLive = false),
    ErrorGeneric("error_generic", null, Glyph.ProblemGeneric, Register.Neutral, reachableLive = false),

    NotificationPriming("notification_priming", null, Glyph.TabInbox, Register.Playful, reachableLive = false),
    ShortTestPrompt("short_test_prompt", null, Glyph.PreFirstCon, Register.Playful, reachableLive = false),

    InboxRequestsEmpty("inbox_requests_empty", null, Glyph.EmptyInbox, Register.Playful, reachableLive = false),
    InboxCategoryPinnedDemo("inbox_category_pinned_demo", null, Glyph.TabInbox, Register.Neutral, reachableLive = false),
    ;

    companion object {
        /** The ledger acceptance number (§9d). A test asserts `DesignState.entries.size == ACCEPTANCE_COUNT`. */
        const val ACCEPTANCE_COUNT = 23
    }
}

/** DESIGN.md registers: playful (candy, choco outline, Black 900) vs neutral (decoration-zero). */
enum class Register { Playful, Neutral }
