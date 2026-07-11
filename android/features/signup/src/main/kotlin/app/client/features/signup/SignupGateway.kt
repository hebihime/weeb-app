package app.client.features.signup

import java.time.LocalDate

/** SLICE_S7_CONTRACT.md §1e / §9c message-key constants, never a raw literal at a call site. */
object SignupMessageKeys {
    const val COULD_NOT_SEND = "error.could_not_send"
}

sealed interface SignupStepResult {
    data object Accepted : SignupStepResult
    data class Failed(val messageKey: String) : SignupStepResult
}

/**
 * SLICE_S7_CONTRACT.md §9c — the S3 seam, ~5 members mirroring the 5.14a fields (handle → verified-email
 * → birthdate attest → avatar-or-skip → one fandom tag). [submit] is the ONLY member S7's flow actually
 * calls (the other four are the seam S3 activates with a real staging implementation — "activation =
 * contract regen + one impl per client; shell UI does not change", §11). Every member returns
 * [SignupStepResult] — there is no success shape this seam can fabricate client-side (L6).
 */
interface SignupGateway {
    suspend fun checkHandleAvailability(handle: String): SignupStepResult
    suspend fun requestEmailVerification(email: String): SignupStepResult
    suspend fun attestBirthdate(birthdate: LocalDate): SignupStepResult
    suspend fun uploadAvatar(avatarRef: String?): SignupStepResult
    suspend fun submit(handle: String, email: String, birthdate: LocalDate, avatarRef: String?, fandomTag: String): SignupStepResult
}
