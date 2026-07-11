package app.client.features.signup

import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class HandleValidatorTest {
    @Test
    fun `accepts the Maestro fixture handle`() {
        assertTrue(HandleValidator.isValid("nakama_test"))
    }

    @Test
    fun `rejects too short`() {
        assertFalse(HandleValidator.isValid("ab"))
    }

    @Test
    fun `rejects spaces and punctuation`() {
        assertFalse(HandleValidator.isValid("na kama"))
        assertFalse(HandleValidator.isValid("nakama!"))
        assertFalse(HandleValidator.isValid("naka-ma"))
    }

    @Test
    fun `is case-insensitive`() {
        assertTrue(HandleValidator.isValid("NakamaTest"))
    }

    @Test
    fun `rejects too long`() {
        assertFalse(HandleValidator.isValid("a".repeat(21)))
    }
}
