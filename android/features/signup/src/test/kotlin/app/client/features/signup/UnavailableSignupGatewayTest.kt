package app.client.features.signup

import java.time.LocalDate
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs

/**
 * SLICE_S7_CONTRACT.md §9c — "No debug mock success path exists ... every submit resolves to the one
 * generic could-not-send pattern." Every one of the five members is exercised here, not just [submit],
 * because the contract is explicit that the honest refusal applies to the whole seam, in ALL configs.
 */
class UnavailableSignupGatewayTest {
    private val gateway = UnavailableSignupGateway()

    @Test
    fun `checkHandleAvailability always fails honestly`() = runTest {
        val result = gateway.checkHandleAvailability("nakama_test")
        val failed = assertIs<SignupStepResult.Failed>(result)
        assertEquals(SignupMessageKeys.COULD_NOT_SEND, failed.messageKey)
    }

    @Test
    fun `requestEmailVerification always fails honestly`() = runTest {
        val failed = assertIs<SignupStepResult.Failed>(gateway.requestEmailVerification("test@example.com"))
        assertEquals(SignupMessageKeys.COULD_NOT_SEND, failed.messageKey)
    }

    @Test
    fun `attestBirthdate always fails honestly`() = runTest {
        val failed = assertIs<SignupStepResult.Failed>(gateway.attestBirthdate(LocalDate.of(2000, 1, 1)))
        assertEquals(SignupMessageKeys.COULD_NOT_SEND, failed.messageKey)
    }

    @Test
    fun `uploadAvatar always fails honestly, including the skip path`() = runTest {
        val failed = assertIs<SignupStepResult.Failed>(gateway.uploadAvatar(avatarRef = null))
        assertEquals(SignupMessageKeys.COULD_NOT_SEND, failed.messageKey)
    }

    @Test
    fun `submit always fails honestly - no fake success path exists`() = runTest {
        val failed = assertIs<SignupStepResult.Failed>(
            gateway.submit(
                handle = "nakama_test",
                email = "test@example.com",
                birthdate = LocalDate.of(2000, 1, 1),
                avatarRef = null,
                fandomTag = "shonen",
            ),
        )
        assertEquals(SignupMessageKeys.COULD_NOT_SEND, failed.messageKey)
    }

    @Test
    fun `repeated calls never produce Accepted, ever`() = runTest {
        repeat(20) {
            val result = gateway.submit("h", "e@example.com", LocalDate.of(1990, 1, 1), null, "f")
            assertIs<SignupStepResult.Failed>(result)
        }
    }
}
