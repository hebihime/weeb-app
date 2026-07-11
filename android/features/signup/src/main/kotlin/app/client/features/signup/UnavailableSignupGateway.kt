package app.client.features.signup

import java.time.LocalDate

/**
 * SLICE_S7_CONTRACT.md §9c, adoption record P1 — "S7 registers only UnavailableSignupGateway, in ALL
 * build configurations: every submit resolves to the one generic could-not-send pattern
 * (error.could_not_send). No debug mock success path exists." Every single member — not just [submit]
 * — fails honestly; the seam has no code path anywhere that could accidentally produce a fabricated
 * success (L6), in debug, in release, or in a test double swapped in by a future slice's staging config.
 */
class UnavailableSignupGateway : SignupGateway {
    override suspend fun checkHandleAvailability(handle: String): SignupStepResult =
        SignupStepResult.Failed(SignupMessageKeys.COULD_NOT_SEND)

    override suspend fun requestEmailVerification(email: String): SignupStepResult =
        SignupStepResult.Failed(SignupMessageKeys.COULD_NOT_SEND)

    override suspend fun attestBirthdate(birthdate: LocalDate): SignupStepResult =
        SignupStepResult.Failed(SignupMessageKeys.COULD_NOT_SEND)

    override suspend fun uploadAvatar(avatarRef: String?): SignupStepResult =
        SignupStepResult.Failed(SignupMessageKeys.COULD_NOT_SEND)

    override suspend fun submit(
        handle: String,
        email: String,
        birthdate: LocalDate,
        avatarRef: String?,
        fandomTag: String,
    ): SignupStepResult = SignupStepResult.Failed(SignupMessageKeys.COULD_NOT_SEND)
}
