package app.client.features.signup

/**
 * Every string the 5.14a shell displays, resolved by the caller (:app's composition root, which owns
 * all message-keys.json + i18n catalogs — see SLICE_S7_CONTRACT.md §9e) and threaded down as plain
 * data. This module never resolves an Android string resource itself, so it stays a pure Kotlin/Compose
 * unit testable without Robolectric for its validation logic.
 */
data class SignupStrings(
    val startTitle: String,
    val startCta: String,
    val handleTitle: String,
    val handleHint: String,
    val handleInvalid: String,
    val handleNextCta: String,
    val emailTitle: String,
    val emailHint: String,
    val emailNextCta: String,
    val birthdateTitle: String,
    val birthdateHint: String,
    val birthdateNextCta: String,
    val ageRefusalNeutralTitle: String,
    val ageRefusalNeutralBody: String,
    val ageRefusalCoppaTitle: String,
    val ageRefusalCoppaBody: String,
    val avatarTitle: String,
    val avatarSkipCta: String,
    val fandomTitle: String,
    val fandomOptionLabels: List<String>,
    val submitCta: String,
    val couldNotSendTitle: String,
    val couldNotSendBody: String,
)
